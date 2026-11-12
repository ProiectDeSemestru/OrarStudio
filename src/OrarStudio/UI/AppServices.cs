using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.Api;
using OrarStudio.ViewModels;

namespace OrarStudio.UI;

/// <summary>Rădăcina de compoziție: serviciile partajate ale aplicației.</summary>
public static class AppServices
{
    private static DataStore? _store;
    private static ApiHost? _api;

    public static DataStore Store => _store ??= new DataStore();
    public static ApiHost Api => _api ??= new ApiHost();
    public static ToastService Toasts { get; } = new();
    public static AppSettings Settings { get; set; } = new();
    public static MainViewModel? Main { get; set; }
}

/// <summary>Găzduiește serverul API și îi furnizează instantanee (copii) ale datelor, actualizate la fiecare modificare.</summary>
public sealed partial class ApiHost : ObservableObject
{
    private ApiSnapshot _snapshot;
    private readonly ApiServer _server;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _stats;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Oprit";
    [ObservableProperty] private string? _url;
    [ObservableProperty] private string? _lanUrl;
    [ObservableProperty] private long _requests;
    [ObservableProperty] private DateTime? _snapshotTime;

    public ApiHost()
    {
        _snapshot = ApiSnapshot.FromCopy(new Dataset());
        _server = new ApiServer(() => Volatile.Read(ref _snapshot));
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshSnapshot(); };
        _stats = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _stats.Tick += (_, _) => Requests = _server.Requests;
        AppServices.Store.Changed += (_, _) =>
        {
            if (!IsRunning) return;
            _debounce.Stop();
            _debounce.Start();
        };
    }

    public ApiSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public void RefreshSnapshot()
    {
        var copy = Json.Clone(AppServices.Store.Data);
        Volatile.Write(ref _snapshot, ApiSnapshot.FromCopy(copy));
        SnapshotTime = DateTime.Now;
    }

    public async Task<bool> StartAsync(int port, bool lan)
    {
        if (IsBusy) return false;
        IsBusy = true;
        try
        {
            RefreshSnapshot();
            await _server.StartAsync(port, lan);
            IsRunning = true;
            Url = $"http://localhost:{port}";
            LanUrl = lan ? $"http://{LocalIp() ?? Environment.MachineName}:{port}" : null;
            Status = lan ? "Pornit · accesibil în rețeaua locală" : "Pornit · doar pe acest calculator";
            _stats.Start();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Pornire API");
            IsRunning = false;
            Status = "Eroare la pornire";
            AppServices.Toasts.Show($"Serverul nu a putut porni pe portul {port}: {ex.GetBaseException().Message}", ToastKind.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;
        IsBusy = true;
        try
        {
            await _server.StopAsync();
        }
        finally
        {
            _stats.Stop();
            IsRunning = false;
            Url = null;
            LanUrl = null;
            Status = "Oprit";
            IsBusy = false;
        }
    }

    private static string? LocalIp()
    {
        try
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))?.ToString();
        }
        catch { return null; }
    }
}

public enum ToastKind { Info, Success, Warning, Error }

public sealed partial class ToastItem : ObservableObject
{
    public string Message { get; init; } = "";
    public ToastKind Kind { get; init; }
    public string? ActionText { get; init; }
    public Action? Action { get; init; }
    public string Icon => Kind switch
    {
        ToastKind.Success => "",
        ToastKind.Warning => "",
        ToastKind.Error => "",
        _ => "",
    };
}

/// <summary>Notificări scurte (colțul din dreapta-jos), cu acțiune opțională.</summary>
public sealed partial class ToastService : ObservableObject
{
    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(string message, ToastKind kind = ToastKind.Info, string? actionText = null, Action? action = null, int seconds = 5)
    {
        void Add()
        {
            var item = new ToastItem { Message = message, Kind = kind, ActionText = actionText, Action = action };
            Items.Add(item);
            while (Items.Count > 4) Items.RemoveAt(0);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds + (action != null ? 3 : 0)) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Items.Remove(item);
            };
            timer.Start();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) Add();
        else dispatcher.BeginInvoke(Add);
    }

    [RelayCommand]
    private void Dismiss(ToastItem? item)
    {
        if (item != null) Items.Remove(item);
    }

    [RelayCommand]
    private void Run(ToastItem? item)
    {
        if (item == null) return;
        Items.Remove(item);
        try { item.Action?.Invoke(); }
        catch (Exception ex) { Log.Error(ex, "Acțiune notificare"); }
    }
}
