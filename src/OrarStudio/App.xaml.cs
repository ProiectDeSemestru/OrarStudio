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

        string? snapshotDir = ArgValue(e.Args, "--snapshot");
        bool forceDemo = e.Args.Contains("--demo") || snapshotDir != null;
        string? fileArg = e.Args.FirstOrDefault(a => !a.StartsWith("--") && File.Exists(a));

        var store = AppServices.Store;
        bool needDemo = false;
        try
        {
            if (fileArg != null && !forceDemo)
            {
                store.Open(fileArg);
                settings.LastFile = fileArg;
            }
            else if (!forceDemo && settings.LastFile != null && File.Exists(settings.LastFile))
                store.Open(settings.LastFile);
            else if (!forceDemo && File.Exists(AppPaths.WorkspaceFile))
                store.Load(DataStore.ReadFile(AppPaths.WorkspaceFile), null);
            else
                needDemo = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Încărcarea datelor la pornire");
            needDemo = true;
        }

        var main = new MainViewModel();
        AppServices.Main = main;
        var window = new MainWindow { DataContext = main };
        MainWindow = window;
        window.Show();

        _ = StartAsync(main, needDemo, snapshotDir);
    }

    private static async Task StartAsync(MainViewModel main, bool needDemo, string? snapshotDir)
    {
        var store = AppServices.Store;
        if (needDemo)
        {
            await main.RunBusy("Se pregătesc datele demonstrative din fișierele-exemplu…", async () =>
            {
                var demo = await Task.Run(() => SampleData.Create());
                store.Load(demo, snapshotDir != null ? Path.Combine(snapshotDir, "demo.orar") : null, saveNow: snapshotDir == null);
            });
            if (snapshotDir == null)
                AppServices.Toasts.Show("Bun venit! S-au încărcat datele demonstrative: sarcina didactică din fișierele-exemplu și un orar generat automat.",
                    ToastKind.Info, "Import propriu", () => main.ImportCommand.Execute(null), 9);
        }

        var settings = AppServices.Settings;
        if (settings.ApiAutoStart && snapshotDir == null)
            await AppServices.Api.StartAsync(settings.ApiPort, settings.ApiLan);

        if (snapshotDir != null)
            await SnapshotRunner.RunAsync(main, snapshotDir);
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppServices.Store.SaveNow(); } catch { /* ignorat */ }
        try { AppServices.Api.StopAsync().Wait(TimeSpan.FromSeconds(2)); } catch { /* ignorat */ }
        base.OnExit(e);
    }
}
