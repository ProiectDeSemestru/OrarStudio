using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.Import;
using OrarStudio.UI;
using OrarStudio.Views;

namespace OrarStudio.ViewModels;

/// <summary>Fereastra principală: navigare, comenzi globale (fișier, Undo/Redo, import, generare, panou, API), bara de stare.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly PageViewModel[] _pages;
    private object? _pendingParameter;

    public DashboardViewModel Dashboard { get; } = new();
    public TimetableViewModel Timetable { get; } = new();
    public OverviewViewModel Overview { get; } = new();
    public ActivitiesViewModel Activities { get; } = new();
    public GroupsViewModel Groups { get; } = new();
    public TeachersViewModel Teachers { get; } = new();
    public SubjectsViewModel Subjects { get; } = new();
    public RoomsViewModel Rooms { get; } = new();
    public ExportViewModel Export { get; } = new();
    public WebApiViewModel WebApi { get; } = new();
    public SettingsViewModel SettingsPage { get; } = new();

    public ObservableCollection<NavItem> NavItems { get; }

    public DataStore Store => AppServices.Store;
    public ApiHost Api => AppServices.Api;
    public ToastService Toasts => AppServices.Toasts;

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private bool _isDark;
    [ObservableProperty] private string _fileLabel = "";
    [ObservableProperty] private string _fileTooltip = "";
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private double _coverage;
    [ObservableProperty] private string _coverageText = "";
    [ObservableProperty] private string _weekText = "";

    public MainViewModel()
    {
        _pages = new PageViewModel[] { Dashboard, Timetable, Overview, Activities, Groups, Teachers, Subjects, Rooms, Export, WebApi, SettingsPage };
        NavItems = new ObservableCollection<NavItem>
        {
            Header("PRINCIPAL"),
            Item(Dashboard, "dashboard"),
            Item(Timetable, "orar"),
            Item(Overview, "general"),
            Header("DATE"),
            Item(Activities, "sarcina"),
            Item(Groups, "grupe"),
            Item(Teachers, "profesori"),
            Item(Subjects, "discipline"),
            Item(Rooms, "auditorii"),
            Header("PUBLICARE"),
            Item(Export, "export"),
            Item(WebApi, "api"),
            Header("SISTEM"),
            Item(SettingsPage, "setari"),
        };
        Store.Changed += OnStoreChanged;
        Store.PropertyChanged += OnStorePropertyChanged;
        IsDark = ThemeService.Current == "Dark";
        UpdateStatus();
        UpdateFileInfo();
        SelectedNav = NavItems.First(n => n.Page == Dashboard);
    }

    private static NavItem Header(string title) => new() { Title = title, IsHeader = true };
    private static NavItem Item(PageViewModel page, string key) => new() { Title = page.Title, Icon = page.Icon, Page = page, Key = key };

    partial void OnSelectedNavChanged(NavItem? oldValue, NavItem? newValue)
    {
        if (newValue?.Page == null)
        {
            if (newValue != null) SelectedNav = oldValue;
            return;
        }
        if (CurrentPage == newValue.Page) return;
        CurrentPage?.Deactivate();
        CurrentPage = newValue.Page;
        var parameter = _pendingParameter;
        _pendingParameter = null;
        CurrentPage.Activate(parameter);
    }

    /// <summary>Navighează la o pagină, cu un parametru opțional (ex. TimetableTarget, Guid).</summary>
    public void Navigate(PageViewModel page, object? parameter = null)
    {
        if (CurrentPage == page)
        {
            page.Activate(parameter);
            return;
        }
        _pendingParameter = parameter;
        SelectedNav = NavItems.FirstOrDefault(n => n.Page == page);
    }

    private void OnStoreChanged(object? sender, EventArgs e)
    {
        foreach (var p in _pages) p.OnDataChanged();
        UpdateStatus();
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DataStore.LastSaved) or nameof(DataStore.FilePath) or nameof(DataStore.LastError))
            UpdateFileInfo();
    }

    private void UpdateStatus()
    {
        var d = Store.Data;
        var r = Store.Report;
        ErrorCount = r.ConflictingLessons;
        WarningCount = r.Warnings;
        int planned = d.Activities.Sum(a => a.HoursPerWeek);
        int placed = d.Activities.Sum(a => Math.Min(a.HoursPerWeek, Store.Lk.PlacedHoursOf(a.Id)));
        Coverage = planned == 0 ? 0 : placed / (double)planned;
        CoverageText = planned == 0 ? "Sarcina didactică este goală" : $"{Coverage:P0} din sarcină plasată";
        StatsText = $"{Ro.Count(d.Lessons.Count, "lecție", "lecții")} · {Ro.Count(d.Groups.Count, "grupă", "grupe")} · {Ro.Count(d.Teachers.Count, "profesor", "profesori")} · {Ro.Count(d.Rooms.Count, "auditoriu", "auditorii")}";
        int week = SemesterCalendar.WeekNumber(d.Settings, DateTime.Today);
        var parity = SemesterCalendar.Parity(d.Settings, week);
        WeekText = SemesterCalendar.InSemester(d.Settings, week)
            ? $"Săpt. {week} · {(parity == WeekParity.Impara ? "impară" : "pară")}"
            : $"Săpt. {(parity == WeekParity.Impara ? "impară" : "pară")}";

        var orar = NavItems.FirstOrDefault(n => n.Page == Timetable);
        if (orar != null)
        {
            orar.Badge = r.ConflictingLessons > 0 ? r.ConflictingLessons.ToString() : null;
            orar.BadgeIsError = true;
        }
        var sarcina = NavItems.FirstOrDefault(n => n.Page == Activities);
        if (sarcina != null)
        {
            int pending = d.Activities.Count(a => Store.Lk.PlacedHoursOf(a.Id) < a.HoursPerWeek);
            sarcina.Badge = pending > 0 ? pending.ToString() : null;
            sarcina.BadgeIsError = false;
        }
    }

    private void UpdateFileInfo()
    {
        var path = Store.EffectivePath;
        FileLabel = Store.FilePath == null ? "Spațiu de lucru (salvare automată)" : Path.GetFileName(path);
        FileTooltip = path;
        SaveStatus = Store.LastError != null
            ? "⚠ Eroare la salvare: " + Store.LastError
            : Store.LastSaved is DateTime t ? $"Salvat automat la {t:HH:mm:ss}" : "Salvare automată";
    }

    /// <summary>Rulează o operațiune lungă afișând indicatorul de încărcare.</summary>
    public async Task RunBusy(string text, Func<Task> work)
    {
        BusyText = text;
        IsBusy = true;
        try { await work(); }
        finally { IsBusy = false; }
    }

    // ───────────────────────── Comenzi ─────────────────────────

    [RelayCommand]
    private void Undo()
    {
        var label = Store.Undo();
        if (label != null) Toasts.Show($"Anulat: {label}", ToastKind.Info, "Refă", () => Redo(), 4);
    }

    [RelayCommand]
    private void Redo()
    {
        var label = Store.Redo();
        if (label != null) Toasts.Show($"Refăcut: {label}", ToastKind.Info, seconds: 3);
    }

    [RelayCommand]
    private void Save()
    {
        Store.SaveNow();
        if (Store.LastError == null) Toasts.Show($"Salvat: {Store.EffectivePath}", ToastKind.Success, seconds: 3);
        else Toasts.Show("Eroare la salvare: " + Store.LastError, ToastKind.Error);
    }

    [RelayCommand]
    private void SaveAs()
    {
        var s = Store.Data.Settings;
        var path = Dialogs.SaveFile("Proiect orar (*.orar)|*.orar|Fișier JSON (*.json)|*.json",
            $"Orar {s.AcademicYear} sem {s.Semester}.orar", "Salvează proiectul ca…");
        if (path == null) return;
        Store.SaveAs(path);
        RememberFile(path);
        Toasts.Show($"Proiectul a fost salvat: {Path.GetFileName(path)}", ToastKind.Success, "Arată", () => Dialogs.ShowInFolder(path));
    }

    [RelayCommand]
    private void Open()
    {
        var files = Dialogs.OpenFiles("Proiect orar (*.orar;*.json)|*.orar;*.json|Toate fișierele|*.*", false, "Deschide un proiect de orar");
        if (files == null || files.Length == 0) return;
        OpenFile(files[0]);
    }

    public void OpenFile(string path)
    {
        try
        {
            Store.Open(path);
            RememberFile(path);
            Navigate(Dashboard);
            Toasts.Show($"S-a deschis: {Path.GetFileName(path)}", ToastKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deschidere " + path);
            Dialogs.Error("Fișierul nu a putut fi deschis", ex.GetBaseException().Message);
        }
    }

    private static void RememberFile(string path)
    {
        AppServices.Settings.LastFile = path;
        AppServices.Settings.AddRecent(path);
        AppServices.Settings.Save();
    }

    [RelayCommand]
    private void NewProject()
    {
        if (!Dialogs.Confirm("Proiect nou",
                "Se creează un orar gol. Auditoriile și setările (instituție, perechi) se păstrează; grupele, profesorii, sarcina didactică și lecțiile se golesc.\n\nProiectul curent rămâne salvat în fișierul său.",
                "Creează proiect nou"))
            return;
        var path = Dialogs.SaveFile("Proiect orar (*.orar)|*.orar", "Orar nou.orar", "Unde salvăm noul proiect?");
        if (path == null) return;
        var d = new Dataset { Settings = Json.Clone(Store.Data.Settings) };
        foreach (var r in Store.Data.Rooms) d.Rooms.Add(Json.Clone(r));
        Store.Load(d, path, saveNow: true);
        RememberFile(path);
        Navigate(Activities);
        Toasts.Show("Proiect nou creat. Importați sarcina didactică pentru a începe.", ToastKind.Success, "Importă", () => ImportCommand.Execute(null), 8);
    }

    [RelayCommand]
    private async Task Import()
    {
        var files = Dialogs.OpenFiles(
            "Sarcina didactică (*.xlsx;*.docx;*.csv)|*.xlsx;*.docx;*.csv|Excel (*.xlsx)|*.xlsx|Word (*.docx)|*.docx|CSV (*.csv)|*.csv",
            true, "Importă sarcina didactică (fișierele departamentelor)");
        if (files == null || files.Length == 0) return;
        await ImportFiles(files);
    }

    public async Task ImportFiles(string[] files)
    {
        var settings = Json.Clone(Store.Data.Settings);
        ImportPreview? preview = null;
        await RunBusy($"Se citesc {Ro.Count(files.Length, "fișier", "fișiere")}…", async () => preview = await Task.Run(() => LoadImporter.Parse(files, settings)));
        if (preview == null) return;
        if (preview.Rows.Count == 0)
        {
            Dialogs.Error("Nimic de importat",
                "Nu s-a recunoscut niciun rând de sarcină didactică.\n\n" +
                "Fișierul trebuie să conțină coloanele: Disciplina · Nr. de ore și tipul lecției (ex. „2 prel.”, „3 lab.”) · Grupa/seria · Profesorul.\n\n" +
                string.Join("\n", preview.Messages.Take(5)));
            return;
        }
        var vm = new ImportViewModel(preview, Store.Data);
        if (Dialogs.ShowModal(new ImportWindow { DataContext = vm }) != true) return;

        ImportResult? result = null;
        Store.Mutate("Import sarcină didactică", d => result = LoadImporter.Apply(preview, d, vm.Replace, vm.ApplyWishes, vm.UpdateSettings));
        Navigate(Activities);
        var text = $"Import reușit: {result}." + (result!.WishSlots > 0 ? $" Doleanțe aplicate: {result.WishSlots}." : "");
        Toasts.Show(text, ToastKind.Success, "Generează orarul", () => AutoScheduleCommand.Execute(null), 10);
    }

    [RelayCommand]
    private async Task AutoSchedule() => await RunAutoSchedule(null, ask: true);

    public async Task RunAutoSchedule(AutoScheduleOptions? options, bool ask)
    {
        if (ask || options == null)
        {
            var vm = new AutoScheduleViewModel();
            if (Dialogs.ShowModal(new AutoScheduleWindow { DataContext = vm }) != true) return;
            options = vm.ToOptions();
        }
        var clone = Json.Clone(Store.Data);
        AutoScheduleResult? res = null;
        await RunBusy("Se generează orarul…", async () => res = await Task.Run(() => AutoScheduler.Run(clone, options!)));
        if (res == null) return;
        if (res.Added.Count == 0 && res.Removed.Count == 0)
        {
            Toasts.Show(res.Unplaced.Count > 0
                ? $"Nu s-a găsit niciun interval liber pentru {Ro.Count(res.Unplaced.Count, "activitate", "activități")}."
                : "Toate activitățile selectate sunt deja plasate.", res.Unplaced.Count > 0 ? ToastKind.Warning : ToastKind.Info);
            return;
        }
        Store.Mutate("Generare automată", d =>
        {
            if (res.Removed.Count > 0)
            {
                var removed = res.Removed.ToHashSet();
                foreach (var l in d.Lessons.Where(l => removed.Contains(l.Id)).ToList()) d.Lessons.Remove(l);
            }
            foreach (var l in res.Added) d.Lessons.Add(l);
        });
        var msg = $"Generare automată: {Ro.Count(res.Added.Count, "lecție plasată", "lecții plasate")} în {res.Duration.TotalSeconds:0.00} s";
        if (res.Unplaced.Count > 0)
            Toasts.Show($"{msg}; {Ro.Count(res.Unplaced.Count, "activitate", "activități")} fără interval liber.", ToastKind.Warning, "Detalii",
                () => Dialogs.Info("Activități neplasate", string.Join("\n", res.Unplaced.Take(25).Select(u => "• " + u)) +
                                                            (res.Unplaced.Count > 25 ? $"\n… și încă {res.Unplaced.Count - 25}" : "") +
                                                            "\n\nPlasați-le manual (trageți din panoul „De plasat”) sau relaxați restricțiile."), 10);
        else
            Toasts.Show(msg + ", fără conflicte.", ToastKind.Success, "Anulează", () => Store.Undo(), 6);
    }

    [RelayCommand]
    private void OpenBoard()
    {
        var win = new BoardWindow { DataContext = new BoardViewModel() };
        win.Show();
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var theme = IsDark ? "Light" : "Dark";
        ThemeService.Apply(theme);
        IsDark = theme == "Dark";
        AppServices.Settings.Theme = theme;
        AppServices.Settings.Save();
    }

    [RelayCommand]
    private async Task ToggleApi()
    {
        var s = AppServices.Settings;
        if (Api.IsRunning) await Api.StopAsync();
        else if (await Api.StartAsync(s.ApiPort, s.ApiLan))
            Toasts.Show($"Serverul API rulează: {Api.Url}", ToastKind.Success, "Deschide site-ul", () => Dialogs.OpenPath(Api.Url!));
    }

    [RelayCommand]
    private void GoTo(string? key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item?.Page != null) Navigate(item.Page);
    }

    [RelayCommand]
    private void ShowConflicts()
    {
        if (Store.Report.Errors == 0)
        {
            Toasts.Show("Nu există conflicte în orar. ✓", ToastKind.Success, seconds: 3);
            return;
        }
        Navigate(Timetable, "conflicts");
    }

    [RelayCommand]
    private void About() =>
        Dialogs.Info("Despre Orar Studio",
            "Orar Studio 1.0 — planificator de orar universitar.\n\n" +
            "• Auditorii, discipline, profesori, grupe și sarcina didactică\n" +
            "• Import din fișierele departamentelor (.xlsx, .docx, .csv)\n" +
            "• Editor flexibil: drag & drop, dublu-clic, meniu contextual, Ctrl+Z / Ctrl+Y\n" +
            "• Verificarea conflictelor, săptămâni pare/impare, subgrupe, generare automată\n" +
            "• Export PDF, Excel, CSV, iCalendar, JSON, site static\n" +
            "• Server API REST + site + panou informativ\n\n" +
            "Tehnologii: C# · .NET 10 · WPF (MVVM) · ASP.NET Core · QuestPDF · ClosedXML.");
}
