using System.Globalization;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrarStudio.Export;

namespace OrarStudio.Api;

/// <summary>
/// Server HTTP integrat (ASP.NET Core Minimal API, Kestrel): API REST JSON pentru site-ul facultății,
/// pagina publică a orarului, panoul web și exporturi PDF/iCal la cerere. Doar citire (GET), CORS deschis.
/// </summary>
public sealed class ApiServer
{
    private readonly Func<ApiSnapshot> _snapshot;
    private WebApplication? _app;
    private long _requests;

    public ApiServer(Func<ApiSnapshot> snapshot) => _snapshot = snapshot;

    public bool IsRunning => _app != null;
    public int Port { get; private set; }
    public bool Lan { get; private set; }
    public long Requests => Interlocked.Read(ref _requests);
    public string BaseUrl => $"http://localhost:{Port}";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public async Task StartAsync(int port, bool lan)
    {
        if (_app != null) await StopAsync();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = Array.Empty<string>(),
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            if (lan) o.ListenAnyIP(port);
            else o.ListenLocalhost(port);
        });
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.WriteIndented = true;
        });
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().WithMethods("GET")));

        var app = builder.Build();
        app.UseCors();
        app.Use(async (ctx, next) =>
        {
            Interlocked.Increment(ref _requests);
            ctx.Response.Headers["X-Powered-By"] = "Orar Studio";
            await next();
        });
        Map(app);
        await app.StartAsync();
        _app = app;
        Port = port;
        Lan = lan;
    }

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        if (app == null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await app.StopAsync(cts.Token);
            await app.DisposeAsync();
        }
        catch (Exception ex) { Log.Error(ex, "Oprirea serverului API"); }
    }

    private static IResult Page(string name)
    {
        var text = WebAssets.Text(name);
        return text == null ? Results.NotFound() : Results.Content(text, WebAssets.ContentType(name), Encoding.UTF8);
    }

    private void Map(WebApplication app)
    {
        ApiSnapshot S() => _snapshot();

        app.MapGet("/", () => Page("index.html"));
        app.MapGet("/board", () => Page("board.html"));
        app.MapGet("/docs", () => Page("docs.html"));
        app.MapGet("/api", () => Results.Redirect("/docs"));
        app.MapGet("/assets/{name}", (string name) =>
            WebAssets.SiteFiles.Contains(name) ? Page(name) : Results.NotFound());

        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            app = "Orar Studio",
            version = Version,
            lessons = S().Data.Lessons.Count,
            snapshot = S().BuiltAt.ToString("yyyy-MM-ddTHH:mm:ss"),
        }));
        app.MapGet("/api/info", () => Results.Ok(S().Bundle.Info));
        app.MapGet("/api/bundle", () => Results.Ok(S().Bundle));
        app.MapGet("/api/groups", () => Results.Ok(S().Bundle.Groups));
        app.MapGet("/api/teachers", () => Results.Ok(S().Bundle.Teachers));
        app.MapGet("/api/rooms", () => Results.Ok(S().Bundle.Rooms));
        app.MapGet("/api/subjects", () => Results.Ok(S().Bundle.Subjects));

        app.MapGet("/api/lessons", (string? group, string? teacher, string? room, int? day, string? type, string? week) =>
        {
            var s = S();
            IEnumerable<Lesson> q = s.Data.Lessons;
            if (!string.IsNullOrWhiteSpace(group)) q = q.Where(l => Audience.Concerns(l.Groups, group.Trim()));
            if (!string.IsNullOrWhiteSpace(teacher))
            {
                var t = FindTeacher(s, teacher);
                if (t == null) return Results.NotFound(new { error = $"Profesorul „{teacher}” nu a fost găsit." });
                q = q.Where(l => l.TeacherId == t.Id);
            }
            if (!string.IsNullOrWhiteSpace(room))
            {
                var r = FindRoom(s, room);
                if (r == null) return Results.NotFound(new { error = $"Auditoriul „{room}” nu a fost găsit." });
                q = q.Where(l => l.RoomId == r.Id);
            }
            if (day is int dd) q = q.Where(l => l.Day == dd);
            if (!string.IsNullOrWhiteSpace(type)) q = q.Where(l => ApiSnapshot.TypeCode(l.Type) == type.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(week))
            {
                var w = week.Trim().ToLowerInvariant();
                q = q.Where(l => l.Week == WeekParity.Saptamanal || ApiSnapshot.WeekCode(l.Week) == w);
            }
            return Results.Ok(q.OrderBy(l => l.Day).ThenBy(l => l.Slot).Select(l => ApiSnapshot.ToDto(l, s.Data, s.Lk)).ToArray());
        });

        app.MapGet("/api/schedule/group/{code}", (string code) =>
        {
            var s = S();
            var g = s.Lk.Group(code);
            return g == null
                ? Results.NotFound(new { error = $"Grupa „{code}” nu există." })
                : Results.Ok(Grid(s, "group", g.Code, TimetableBuilder.ForGroup(s.Data, s.Lk, g), l => Audience.Concerns(l.Groups, g.Code)));
        });
        app.MapGet("/api/schedule/teacher/{key}", (string key) =>
        {
            var s = S();
            var t = FindTeacher(s, key);
            return t == null
                ? Results.NotFound(new { error = $"Profesorul „{key}” nu a fost găsit." })
                : Results.Ok(Grid(s, "teacher", t.Id.ToString(), TimetableBuilder.ForTeacher(s.Data, s.Lk, t), l => l.TeacherId == t.Id));
        });
        app.MapGet("/api/schedule/room/{key}", (string key) =>
        {
            var s = S();
            var r = FindRoom(s, key);
            return r == null
                ? Results.NotFound(new { error = $"Auditoriul „{key}” nu a fost găsit." })
                : Results.Ok(Grid(s, "room", r.Id.ToString(), TimetableBuilder.ForRoom(s.Data, s.Lk, r), l => l.RoomId == r.Id));
        });

        app.MapGet("/api/now", (string? mode, string? at) =>
        {
            var s = S();
            var when = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(at) &&
                DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
                when = parsed;
            return Results.Ok(BoardLogic.Compute(s.Data, s.Lk, when, mode ?? "rooms"));
        });

        app.MapGet("/api/pdf/group/{code}", (string code, int? download) =>
        {
            var s = S();
            var g = s.Lk.Group(code);
            if (g == null) return Results.NotFound(new { error = $"Grupa „{code}” nu există." });
            return Pdf(PdfExporter.Timetables(s.Data, new[] { TimetableBuilder.ForGroup(s.Data, s.Lk, g) }, $"Orar {g.Code}"), $"orar-{g.Code}.pdf", download);
        });
        app.MapGet("/api/pdf/teacher/{key}", (string key, int? download) =>
        {
            var s = S();
            var t = FindTeacher(s, key);
            if (t == null) return Results.NotFound(new { error = $"Profesorul „{key}” nu a fost găsit." });
            return Pdf(PdfExporter.Timetables(s.Data, new[] { TimetableBuilder.ForTeacher(s.Data, s.Lk, t) }, $"Orar {t.Name}"),
                $"orar-{TimetableBuilder.SafeFileName(t.Name)}.pdf", download);
        });
        app.MapGet("/api/pdf/room/{key}", (string key, int? download) =>
        {
            var s = S();
            var r = FindRoom(s, key);
            if (r == null) return Results.NotFound(new { error = $"Auditoriul „{key}” nu a fost găsit." });
            return Pdf(PdfExporter.Timetables(s.Data, new[] { TimetableBuilder.ForRoom(s.Data, s.Lk, r) }, $"Orar aud. {r.Name}"),
                $"orar-aud-{TimetableBuilder.SafeFileName(r.Name)}.pdf", download);
        });
        app.MapGet("/api/pdf/overview", (int? download) =>
        {
            var s = S();
            return Pdf(PdfExporter.Overview(s.Data, s.Lk, TimetableBuilder.OrderedGroups(s.Data).ToList()), "orar-general.pdf", download);
        });
        app.MapGet("/api/ics/group/{code}", (string code) =>
        {
            var s = S();
            var g = s.Lk.Group(code);
            if (g == null) return Results.NotFound(new { error = $"Grupa „{code}” nu există." });
            var ics = IcsExporter.Build(s.Data, TimetableBuilder.ForGroup(s.Data, s.Lk, g));
            return Results.File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", $"orar-{g.Code}.ics");
        });
    }

    private static IResult Pdf(byte[] bytes, string fileName, int? download) =>
        download == 1 ? Results.File(bytes, "application/pdf", fileName) : Results.File(bytes, "application/pdf");

    private static object Grid(ApiSnapshot s, string kind, string key, EntityTimetable t, Func<Lesson, bool> filter) => new
    {
        kind,
        key,
        title = t.Title,
        subtitle = t.Subtitle,
        days = s.Bundle.Info.DayNames,
        slots = s.Bundle.Info.Slots,
        lessons = s.Data.Lessons.Where(filter).OrderBy(l => l.Day).ThenBy(l => l.Slot)
                    .Select(l => ApiSnapshot.ToDto(l, s.Data, s.Lk)).ToArray(),
    };

    private static Teacher? FindTeacher(ApiSnapshot s, string key)
    {
        if (Guid.TryParse(key, out var id)) return s.Lk.Teacher(id);
        var k = Ro.Key(key.Replace('-', ' ').Replace('_', ' '));
        return s.Data.Teachers.FirstOrDefault(t => Ro.Key(t.Name) == k)
            ?? s.Data.Teachers.FirstOrDefault(t => Ro.Key(t.Name).Contains(k));
    }

    private static Room? FindRoom(ApiSnapshot s, string key)
    {
        if (Guid.TryParse(key, out var id)) return s.Lk.Room(id);
        return s.Data.Rooms.FirstOrDefault(r => string.Equals(r.Name, key.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Publicare „push": trimite orarul (JSON) către un endpoint al site-ului (webhook).</summary>
public static class WebPublisher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    public static async Task<(bool Ok, string Message)> PublishAsync(string url, string token, string json, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return (false, "Adresa URL nu este validă (trebuie să înceapă cu http:// sau https://).");
        using var req = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        req.Headers.UserAgent.ParseAdd($"OrarStudio/{ApiServer.Version}");
        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (body.Length > 200) body = body[..200] + "…";
        return (resp.IsSuccessStatusCode, $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}{(body.Length > 0 ? " · " + body : "")}");
    }
}

/// <summary>Exportă un site static (index.html + data.json) care poate fi urcat pe orice hosting.</summary>
public static class StaticSiteExporter
{
    public static void Export(BundleDto bundle, string folder)
    {
        Directory.CreateDirectory(folder);
        var assets = Path.Combine(folder, "assets");
        Directory.CreateDirectory(assets);
        foreach (var name in new[] { "site.css", "site.js" })
        {
            var text = WebAssets.Text(name);
            if (text != null) File.WriteAllText(Path.Combine(assets, name), text, new UTF8Encoding(false));
        }
        // site static: doar pagina orarului (panoul și API-ul necesită serverul din aplicație)
        var html = WebAssets.Text("index.html") ?? "";
        html = html.Replace("<!--STATIC-->", "<script>window.ORAR_STATIC_DATA = 'data.json';</script>");
        File.WriteAllText(Path.Combine(folder, "index.html"), html, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "data.json"), JsonSerializer.Serialize(bundle, Json.Options), new UTF8Encoding(false));
    }
}
