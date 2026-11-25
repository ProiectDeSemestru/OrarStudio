using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using OrarStudio.UI;
using OrarStudio.ViewModels;
using OrarStudio.Views;

namespace OrarStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Eroare UI");
            ex.Handled = true;
            try
            {
                AppServices.Toasts.Show("A apărut o eroare neașteptată: " + ex.Exception.GetBaseException().Message, ToastKind.Error, "Jurnal",
                    () => Dialogs.OpenPath(AppPaths.LogFile), 8);
            }
            catch { /* ignorat */ }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Log.Error(ex.ExceptionObject as Exception ?? new Exception("Eroare necunoscută"), "Eroare fatală");
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Task");
            ex.SetObserved();
        };

        // formatare românească (date, procente) în toată interfața
        CultureInfo.DefaultThreadCurrentCulture = Ro.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Ro.Culture;
        Thread.CurrentThread.CurrentCulture = Ro.Culture;
        Thread.CurrentThread.CurrentUICulture = Ro.Culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Ro.Culture.IetfLanguageTag)));

        var settings = AppSettings.Load();
        AppServices.Settings = settings;
        ThemeService.Apply(settings.Theme);

        // Linia de comandă: OrarStudio.exe [proiect.orar] [--snapshot <dosar> [--import <fișier>]]
        var (fileArg, options) = ParseArgs(e.Args);
        options.TryGetValue("--snapshot", out var snapshotDir);
        options.TryGetValue("--import", out var importSample);

        var store = AppServices.Store;
        bool fresh = false;
        try
        {
            if (snapshotDir != null)
            {
                // diagnostic: se lucrează pe o copie în dosarul capturilor — nici fișierul original,
                // nici spațiul de lucru al utilizatorului nu sunt modificate
                Directory.CreateDirectory(snapshotDir);
                var data = fileArg != null ? DataStore.ReadFile(fileArg) : new Dataset();
                store.Load(data, Path.Combine(snapshotDir, fileArg != null ? Path.GetFileName(fileArg) : "Proiect nou.orar"), saveNow: false);
                fresh = fileArg == null;
            }
            else if (fileArg != null)
            {
                store.Open(fileArg);
                settings.LastFile = fileArg;
            }
            else if (snapshotDir == null && settings.LastFile != null && File.Exists(settings.LastFile))
                store.Open(settings.LastFile);
            else if (snapshotDir == null && File.Exists(AppPaths.WorkspaceFile))
                store.Load(DataStore.ReadFile(AppPaths.WorkspaceFile), null);
            else
                fresh = true;   // prima pornire: proiect gol
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Încărcarea datelor la pornire");
            fresh = true;
        }

        var main = new MainViewModel();
        AppServices.Main = main;
        var window = new MainWindow { DataContext = main };
        MainWindow = window;
        window.Show();

        _ = StartAsync(main, fresh, snapshotDir, importSample);
    }

    private static async Task StartAsync(MainViewModel main, bool fresh, string? snapshotDir, string? importSample)
    {
        if (fresh && snapshotDir == null)
            AppServices.Toasts.Show("Bun venit în Orar Studio! Începeți prin importul sarcinii didactice a departamentelor (.xlsx, .docx, .csv).",
                ToastKind.Info, "Importă", () => main.ImportCommand.Execute(null), 10);

        var settings = AppServices.Settings;
        if (settings.ApiAutoStart && snapshotDir == null)
            await AppServices.Api.StartAsync(settings.ApiPort, settings.ApiLan);

        if (snapshotDir != null)
            await SnapshotRunner.RunAsync(main, snapshotDir, importSample);
    }

    /// <summary>Separă fișierul de proiect de opțiunile de forma „--nume valoare".</summary>
    private static (string? File, Dictionary<string, string> Options) ParseArgs(string[] args)
    {
        string? file = null;
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--"))
            {
                options[a] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "";
            }
            else if (file == null && File.Exists(a))
            {
                file = a;
            }
        }
        return (file, options);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppServices.Store.SaveNow(); } catch { /* ignorat */ }
        try { AppServices.Api.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { /* ignorat */ }
        base.OnExit(e);
    }
}
