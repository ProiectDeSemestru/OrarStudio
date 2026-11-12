using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.Api;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

public sealed class EndpointInfo
{
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "";
    public string Description { get; init; } = "";
    public string Sample { get; init; } = "";
}

/// <summary>Publicarea orarului pe web: server API integrat, pagini web, webhook, site static.</summary>
public sealed partial class WebApiViewModel : PageViewModel
{
    public override string Title => "Web & API";
    public override string Icon => "";
    public override string Subtitle => Api.IsRunning ? $"Serverul rulează · {Api.Url}" : "Serverul este oprit";

    public ApiHost Api => AppServices.Api;

    [ObservableProperty] private int _port;
    [ObservableProperty] private bool _lan;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _webhookUrl = "";
    [ObservableProperty] private string _webhookToken = "";
    [ObservableProperty] private string _publishResult = "";
    [ObservableProperty] private bool _publishOk;
    [ObservableProperty] private bool _isPublishing;
    [ObservableProperty] private string _sampleGroup = "I2301";

    public List<EndpointInfo> Endpoints { get; private set; } = new();

    public string BaseUrl => Api.Url ?? $"http://localhost:{Port}";

    public string EmbedSnippet =>
        $"<iframe src=\"{BaseUrl}/?group={SampleGroup}&embed=1\" width=\"100%\" height=\"720\" style=\"border:0\"></iframe>";

    public string FetchSnippet =>
        $"fetch('{BaseUrl}/api/schedule/group/{SampleGroup}')\n  .then(r => r.json())\n  .then(data => console.log(data.lessons));";

    public WebApiViewModel()
    {
        var s = AppServices.Settings;
        _port = s.ApiPort;
        _lan = s.ApiLan;
        _autoStart = s.ApiAutoStart;
        _webhookUrl = s.WebhookUrl;
        _webhookToken = s.WebhookToken;
        Api.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ApiHost.IsRunning) or nameof(ApiHost.Url))
            {
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(BaseUrl));
                OnPropertyChanged(nameof(EmbedSnippet));
                OnPropertyChanged(nameof(FetchSnippet));
                BuildEndpoints();
            }
        };
        BuildEndpoints();
    }

    partial void OnPortChanged(int value) { SaveSettings(); BuildEndpoints(); OnPropertyChanged(nameof(BaseUrl)); }
    partial void OnLanChanged(bool value) => SaveSettings();
    partial void OnAutoStartChanged(bool value) => SaveSettings();
    partial void OnWebhookUrlChanged(string value) => SaveSettings();
    partial void OnWebhookTokenChanged(string value) => SaveSettings();

    private void SaveSettings()
    {
        var s = AppServices.Settings;
        s.ApiPort = Math.Clamp(Port, 1024, 65535);
        s.ApiLan = Lan;
        s.ApiAutoStart = AutoStart;
        s.WebhookUrl = WebhookUrl.Trim();
        s.WebhookToken = WebhookToken.Trim();
        s.Save();
    }

    public override void Refresh()
    {
        SampleGroup = TimetableBuilder.OrderedGroups(Store.Data).FirstOrDefault()?.Code ?? "I2301";
        BuildEndpoints();
        OnPropertyChanged(nameof(EmbedSnippet));
        OnPropertyChanged(nameof(FetchSnippet));
    }

    private void BuildEndpoints()
    {
        var g = SampleGroup;
        var room = Store.Data.Rooms.FirstOrDefault()?.Name ?? "101";
        var teacher = Store.Data.Teachers.FirstOrDefault()?.Name.Replace(' ', '-') ?? "V.-Arnaut";
        Endpoints = new List<EndpointInfo>
        {
            new() { Path = "/", Description = "Site-ul public al orarului (grupe, profesori, auditorii)", Sample = $"/?group={g}" },
            new() { Path = "/board", Description = "Panoul informativ web (ce are loc acum / urmează)", Sample = "/board" },
            new() { Path = "/docs", Description = "Documentația API-ului", Sample = "/docs" },
            new() { Path = "/api/info", Description = "Instituția, semestrul, perechile, săptămâna curentă", Sample = "/api/info" },
            new() { Path = "/api/bundle", Description = "Toate datele într-un singur răspuns (pentru site-uri)", Sample = "/api/bundle" },
            new() { Path = "/api/groups", Description = "Lista grupelor", Sample = "/api/groups" },
            new() { Path = "/api/teachers", Description = "Lista profesorilor", Sample = "/api/teachers" },
            new() { Path = "/api/rooms", Description = "Auditoriile (destinație, capacitate, dotări)", Sample = "/api/rooms" },
            new() { Path = "/api/lessons?group=&teacher=&room=&day=", Description = "Lecțiile, cu filtre", Sample = $"/api/lessons?group={g}&day=0" },
            new() { Path = "/api/schedule/group/{cod}", Description = "Orarul săptămânal al unei grupe", Sample = $"/api/schedule/group/{g}" },
            new() { Path = "/api/schedule/teacher/{nume}", Description = "Orarul unui profesor", Sample = $"/api/schedule/teacher/{teacher}" },
            new() { Path = "/api/schedule/room/{nume}", Description = "Orarul unui auditoriu", Sample = $"/api/schedule/room/{room}" },
            new() { Path = "/api/now?mode=rooms|groups", Description = "Starea curentă pentru panou (acum / urmează)", Sample = "/api/now?mode=rooms" },
            new() { Path = "/api/pdf/group/{cod}", Description = "Orarul grupei în PDF (generat la cerere)", Sample = $"/api/pdf/group/{g}" },
            new() { Path = "/api/pdf/overview", Description = "Orarul general în PDF", Sample = "/api/pdf/overview" },
            new() { Path = "/api/ics/group/{cod}", Description = "Calendar iCalendar al grupei", Sample = $"/api/ics/group/{g}" },
        };
        OnPropertyChanged(nameof(Endpoints));
    }

    [RelayCommand]
    private async Task ToggleServer()
    {
        SaveSettings();
        if (Api.IsRunning) await Api.StopAsync();
        else if (await Api.StartAsync(Math.Clamp(Port, 1024, 65535), Lan))
            Toasts.Show($"Serverul API rulează: {Api.Url}", ToastKind.Success, "Deschide site-ul", () => Dialogs.OpenPath(Api.Url!));
    }

    private async Task<bool> EnsureRunning()
    {
        if (Api.IsRunning) return true;
        return await Api.StartAsync(Math.Clamp(Port, 1024, 65535), Lan);
    }

    [RelayCommand]
    private async Task OpenEndpoint(EndpointInfo? e)
    {
        if (e == null || !await EnsureRunning()) return;
        Dialogs.OpenPath(BaseUrl + e.Sample);
    }

    [RelayCommand]
    private void CopyEndpoint(EndpointInfo? e)
    {
        if (e != null) Dialogs.CopyText(BaseUrl + e.Sample, "Adresa");
    }

    [RelayCommand]
    private async Task OpenSite()
    {
        if (await EnsureRunning()) Dialogs.OpenPath(BaseUrl + "/");
    }

    [RelayCommand]
    private async Task OpenWebBoard()
    {
        if (await EnsureRunning()) Dialogs.OpenPath(BaseUrl + "/board");
    }

    [RelayCommand]
    private async Task OpenDocs()
    {
        if (await EnsureRunning()) Dialogs.OpenPath(BaseUrl + "/docs");
    }

    [RelayCommand]
    private void CopyEmbed() => Dialogs.CopyText(EmbedSnippet, "Codul de încorporare");

    [RelayCommand]
    private void CopyFetch() => Dialogs.CopyText(FetchSnippet, "Exemplul JavaScript");

    [RelayCommand]
    private async Task Publish()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            PublishOk = false;
            PublishResult = "Introduceți adresa endpoint-ului site-ului (ex. https://fmi.usm.md/api/orar).";
            return;
        }
        IsPublishing = true;
        PublishResult = "Se trimite…";
        try
        {
            var copy = Json.Clone(Store.Data);
            var json = await Task.Run(() => System.Text.Json.JsonSerializer.Serialize(ApiSnapshot.BuildBundle(copy, new Lookups(copy)), Json.Compact));
            var (ok, message) = await WebPublisher.PublishAsync(WebhookUrl.Trim(), WebhookToken, json);
            PublishOk = ok;
            PublishResult = (ok ? "✓ Publicat cu succes · " : "✗ Eroare · ") + message + $" · {DateTime.Now:HH:mm:ss}";
            Toasts.Show(ok ? "Orarul a fost publicat pe site." : "Publicarea a eșuat: " + message, ok ? ToastKind.Success : ToastKind.Error);
        }
        catch (Exception ex)
        {
            PublishOk = false;
            PublishResult = "✗ " + ex.GetBaseException().Message;
        }
        finally
        {
            IsPublishing = false;
        }
    }

    [RelayCommand]
    private void ExportSite()
    {
        var main = AppServices.Main;
        if (main == null) return;
        main.Navigate(main.Export);
        main.Export.Format = main.Export.Formats.First(f => f.Key == "site");
    }
}
