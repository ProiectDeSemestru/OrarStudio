using System.Text;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.Api;
using OrarStudio.Export;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

public sealed class FormatOption
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Description { get; init; } = "";
    public string Extension { get; init; } = "";
}

public sealed class RecentExport
{
    public string Path { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public string When { get; init; } = "";
    public string Icon { get; init; } = "";
}

/// <summary>Export în PDF, Excel, CSV, iCalendar, JSON și site static, cu previzualizare PDF.</summary>
public sealed partial class ExportViewModel : PageViewModel
{
    private readonly DispatcherTimer _previewTimer;
    private int _previewVersion;

    public override string Title => "Export & publicare";
    public override string Icon => "";
    public override string Subtitle => "PDF pentru tipar, Excel pentru prelucrare, iCalendar pentru telefon, JSON / site pentru web";

    public IReadOnlyList<Option<string>> Scopes { get; } = new[]
    {
        new Option<string>("group", "O grupă", ""),
        new Option<string>("teacher", "Un profesor", ""),
        new Option<string>("room", "Un auditoriu", ""),
        new Option<string>("allGroups", "Toate grupele", ""),
        new Option<string>("allTeachers", "Toți profesorii", ""),
        new Option<string>("allRooms", "Toate auditoriile", ""),
        new Option<string>("overview", "Orarul general", ""),
    };

    public IReadOnlyList<FormatOption> Formats { get; } = new[]
    {
        new FormatOption { Key = "pdf", Label = "PDF", Icon = "", Extension = "pdf", Description = "Document pentru tipar și afișare pe panou (A4/A3, peisaj)." },
        new FormatOption { Key = "xlsx", Label = "Excel", Icon = "", Extension = "xlsx", Description = "Registru .xlsx: orare pe foi, lista lecțiilor și sarcina didactică." },
        new FormatOption { Key = "csv", Label = "CSV", Icon = "", Extension = "csv", Description = "Tabel simplu cu lecțiile (separator „;”, UTF-8)." },
        new FormatOption { Key = "ics", Label = "iCalendar", Icon = "", Extension = "ics", Description = "Calendar recurent pentru Google Calendar / Outlook / telefon." },
        new FormatOption { Key = "json", Label = "JSON", Icon = "", Extension = "json", Description = "Toate datele în formatul API-ului (pentru site sau alte aplicații)." },
        new FormatOption { Key = "site", Label = "Site web", Icon = "", Extension = "", Description = "Folder cu index.html + data.json, gata de urcat pe orice hosting." },
    };

    public ObservableCollection<EntityOption> Entities { get; } = new();
    public ObservableCollection<RecentExport> Recent { get; } = new();

    [ObservableProperty] private Option<string>? _scope;
    [ObservableProperty] private FormatOption? _format;
    [ObservableProperty] private EntityOption? _entity;
    [ObservableProperty] private bool _needsEntity;
    [ObservableProperty] private bool _isSupported = true;
    [ObservableProperty] private string _supportNote = "";
    [ObservableProperty] private BitmapImage? _preview;
    [ObservableProperty] private bool _isPreviewBusy;
    [ObservableProperty] private string _previewInfo = "";
    [ObservableProperty] private bool _openAfter;

    public ExportViewModel()
    {
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewTimer.Tick += async (_, _) =>
        {
            _previewTimer.Stop();
            await RenderPreview();
        };
        _openAfter = AppServices.Settings.OpenAfterExport;
        _scope = Scopes[0];
        _format = Formats[0];
    }

    partial void OnScopeChanged(Option<string>? value)
    {
        LoadEntities();
        UpdateSupport();
        SchedulePreview();
    }

    partial void OnFormatChanged(FormatOption? value)
    {
        UpdateSupport();
        SchedulePreview();
    }

    partial void OnEntityChanged(EntityOption? value) => SchedulePreview();

    partial void OnOpenAfterChanged(bool value)
    {
        AppServices.Settings.OpenAfterExport = value;
        AppServices.Settings.Save();
    }

    public override void Refresh()
    {
        LoadEntities();
        UpdateSupport();
        SchedulePreview();
    }

    private void LoadEntities()
    {
        var keep = Entity?.Key;
        var d = Store.Data;
        Entities.Clear();
        switch (Scope?.Value)
        {
            case "group":
                foreach (var g in TimetableBuilder.OrderedGroups(d))
                    Entities.Add(new EntityOption { Key = g.Code, Label = g.Code, Detail = g.Specialty, Model = g });
                break;
            case "teacher":
                foreach (var t in d.Teachers.OrderBy(t => t.Name, StringComparer.Create(Ro.Culture, true)))
                    Entities.Add(new EntityOption { Key = t.Id.ToString(), Label = t.Name, Detail = t.Title, Model = t });
                break;
            case "room":
                foreach (var r in d.Rooms.OrderBy(r => r.Name, StringComparer.Create(Ro.Culture, true)))
                    Entities.Add(new EntityOption { Key = r.Id.ToString(), Label = "Aud. " + r.Name, Detail = r.KindLabel, Model = r });
                break;
        }
        NeedsEntity = Entities.Count > 0;
        Entity = Entities.FirstOrDefault(e => e.Key == keep) ?? Entities.FirstOrDefault();
    }

    private void UpdateSupport()
    {
        var scope = Scope?.Value ?? "";
        var fmt = Format?.Key ?? "";
        (IsSupported, SupportNote) = fmt switch
        {
            "ics" when scope is not ("group" or "teacher" or "room") => (false, "Formatul iCalendar se generează pentru o singură grupă, un profesor sau un auditoriu."),
            "json" => (true, "Fișierul JSON conține tot orarul (toate grupele, profesorii și auditoriile)."),
            "site" => (true, "Se creează un folder cu site-ul complet al orarului (independent de selecție)."),
            "xlsx" when scope == "overview" => (true, "Registrul conține orarul general, câte o foaie pentru fiecare grupă, lista lecțiilor și sarcina didactică."),
            _ => (true, ""),
        };
    }

    private List<EntityTimetable> BuildTables(Dataset d, Lookups lk, string scope, string? key)
    {
        switch (scope)
        {
            case "group":
                return d.Groups.Where(g => g.Code == key).Select(g => TimetableBuilder.ForGroup(d, lk, g)).ToList();
            case "teacher":
                return d.Teachers.Where(t => t.Id.ToString() == key).Select(t => TimetableBuilder.ForTeacher(d, lk, t)).ToList();
            case "room":
                return d.Rooms.Where(r => r.Id.ToString() == key).Select(r => TimetableBuilder.ForRoom(d, lk, r)).ToList();
            case "allGroups":
                return TimetableBuilder.OrderedGroups(d).Select(g => TimetableBuilder.ForGroup(d, lk, g)).ToList();
            case "allTeachers":
                return d.Teachers.Where(t => d.Lessons.Any(l => l.TeacherId == t.Id))
                                 .OrderBy(t => t.Name, StringComparer.Create(Ro.Culture, true))
                                 .Select(t => TimetableBuilder.ForTeacher(d, lk, t)).ToList();
            case "allRooms":
                return d.Rooms.OrderBy(r => r.Name, StringComparer.Create(Ro.Culture, true)).Select(r => TimetableBuilder.ForRoom(d, lk, r)).ToList();
            default:
                return new List<EntityTimetable>();
        }
    }

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        if (!IsActive) return;
        _previewTimer.Start();
    }

    private async Task RenderPreview()
    {
        int version = ++_previewVersion;
        if (Format?.Key != "pdf" || !IsSupported || (NeedsEntity && Entity == null))
        {
            Preview = null;
            PreviewInfo = Format?.Key == "pdf" ? "" : Format?.Description ?? "";
            return;
        }
        IsPreviewBusy = true;
        var copy = Json.Clone(Store.Data);
        var scope = Scope?.Value ?? "group";
        var key = Entity?.Key;
        try
        {
            var (png, pages) = await Task.Run(() =>
            {
                var lk = new Lookups(copy);
                var doc = scope == "overview"
                    ? PdfExporter.BuildOverview(copy, lk, TimetableBuilder.OrderedGroups(copy).ToList())
                    : PdfExporter.BuildTimetables(copy, BuildTables(copy, lk, scope, key).Take(1).ToList(), "Previzualizare");
                var images = PdfExporter.RenderImages(doc, 72);
                int count = scope switch
                {
                    "overview" => -1,
                    "allGroups" => copy.Groups.Count,
                    "allTeachers" => copy.Teachers.Count(t => copy.Lessons.Any(l => l.TeacherId == t.Id)),
                    "allRooms" => copy.Rooms.Count,
                    _ => images.Count,
                };
                return (images.FirstOrDefault(), count);
            });
            if (version != _previewVersion) return;
            if (png == null)
            {
                Preview = null;
                PreviewInfo = "Nu există date pentru previzualizare.";
                return;
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(png);
            bmp.EndInit();
            bmp.Freeze();
            Preview = bmp;
            PreviewInfo = pages switch
            {
                -1 => "Previzualizare: prima pagină a orarului general (A3 peisaj)",
                1 => "Previzualizare: 1 pagină A4 peisaj",
                _ => $"Previzualizare: prima pagină din {pages} (câte o pagină A4 pentru fiecare)",
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Previzualizare PDF");
            PreviewInfo = "Previzualizarea nu a putut fi generată: " + ex.Message;
        }
        finally
        {
            if (version == _previewVersion) IsPreviewBusy = false;
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        var main = AppServices.Main;
        if (main == null || Format == null || Scope == null) return;
        if (!IsSupported)
        {
            Toasts.Show(SupportNote, ToastKind.Warning);
            return;
        }
        var scope = Scope.Value;
        var fmt = Format.Key;
        var key = Entity?.Key;
        string baseName = scope switch
        {
            "group" or "teacher" or "room" => "Orar " + TimetableBuilder.SafeFileName(Entity?.Label ?? ""),
            "allGroups" => "Orar - toate grupele",
            "allTeachers" => "Orar - toti profesorii",
            "allRooms" => "Orar - auditorii",
            _ => "Orar general",
        };

        string? target;
        if (fmt == "site")
        {
            target = Dialogs.PickFolder("Alegeți folderul în care se creează site-ul orarului");
            if (target == null) return;
            target = Path.Combine(target, "orar-web");
        }
        else
        {
            var filter = fmt switch
            {
                "pdf" => "Document PDF (*.pdf)|*.pdf",
                "xlsx" => "Registru Excel (*.xlsx)|*.xlsx",
                "csv" => "Fișier CSV (*.csv)|*.csv",
                "ics" => "Calendar iCalendar (*.ics)|*.ics",
                _ => "Fișier JSON (*.json)|*.json",
            };
            target = Dialogs.SaveFile(filter, $"{baseName}.{Format.Extension}", "Exportă orarul");
            if (target == null) return;
        }

        var copy = Json.Clone(Store.Data);
        var path = target;
        try
        {
            await main.RunBusy("Se exportă…", () => Task.Run(() =>
            {
                var lk = new Lookups(copy);
                var tables = BuildTables(copy, lk, scope, key);
                switch (fmt)
                {
                    case "pdf":
                        var bytes = scope == "overview"
                            ? PdfExporter.Overview(copy, lk, TimetableBuilder.OrderedGroups(copy).ToList())
                            : PdfExporter.Timetables(copy, tables, baseName);
                        File.WriteAllBytes(path, bytes);
                        break;
                    case "xlsx":
                        if (scope == "overview") ExcelExporter.ExportWorkbook(copy, lk, path);
                        else ExcelExporter.ExportTimetables(copy, tables, path);
                        break;
                    case "csv":
                        var ids = tables.SelectMany(t => t.Lessons.Select(l => l.Id)).ToHashSet();
                        var lessons = scope == "overview" ? copy.Lessons.ToList() : copy.Lessons.Where(l => ids.Contains(l.Id)).ToList();
                        CsvExporter.Save(CsvExporter.Build(copy, lk, lessons), path);
                        break;
                    case "ics":
                        File.WriteAllText(path, IcsExporter.Build(copy, tables.First()), new UTF8Encoding(false));
                        break;
                    case "json":
                        File.WriteAllText(path, Json.Serialize(ApiSnapshot.BuildBundle(copy, lk)), new UTF8Encoding(false));
                        break;
                    case "site":
                        StaticSiteExporter.Export(ApiSnapshot.BuildBundle(copy, lk), path);
                        break;
                }
            }));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export");
            Dialogs.Error("Exportul a eșuat", ex.GetBaseException().Message);
            return;
        }

        Recent.Insert(0, new RecentExport { Path = path, When = DateTime.Now.ToString("HH:mm"), Icon = Format.Icon });
        while (Recent.Count > 6) Recent.RemoveAt(Recent.Count - 1);
        var open = fmt == "site" ? Path.Combine(path, "index.html") : path;
        Toasts.Show($"Export reușit: {Path.GetFileName(path)}", ToastKind.Success, "Deschide", () => Dialogs.OpenPath(open), 6);
        if (OpenAfter) Dialogs.OpenPath(open);
    }

    [RelayCommand]
    private void OpenRecent(RecentExport? item)
    {
        if (item == null) return;
        var path = Directory.Exists(item.Path) ? Path.Combine(item.Path, "index.html") : item.Path;
        Dialogs.OpenPath(path);
    }

    [RelayCommand]
    private void ShowRecent(RecentExport? item)
    {
        if (item != null) Dialogs.ShowInFolder(item.Path);
    }
}
