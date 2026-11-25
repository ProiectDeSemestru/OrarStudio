using System.Globalization;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.Api;
using OrarStudio.Import;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

// ═════════════════════════════ Import ═════════════════════════════

/// <summary>Previzualizarea importului sarcinii didactice: rânduri editabile, avertismente, doleanțe.</summary>
public sealed partial class ImportViewModel : ObservableObject
{
    public ImportPreview Preview { get; }
    public ObservableCollection<ImportRow> Rows { get; }
    public ListCollectionView RowsView { get; }
    public IReadOnlyList<LessonType> Types { get; } = new[] { LessonType.Curs, LessonType.Seminar, LessonType.Laborator };
    public List<string> Messages { get; }
    public List<string> Wishes { get; }
    public string WishSlots { get; }
    public bool HasWishes => Wishes.Count > 0;
    public bool HasMessages => Messages.Count > 0;
    public string FilesText { get; }

    [ObservableProperty] private bool _replace;
    [ObservableProperty] private bool _applyWishes = true;
    [ObservableProperty] private bool _updateSettings;
    [ObservableProperty] private bool _onlyWarnings;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _settingsInfo = "";

    public event Action<bool>? CloseRequested;

    public ImportViewModel(ImportPreview preview, Dataset current)
    {
        Preview = preview;
        Rows = new ObservableCollection<ImportRow>(preview.Rows);
        RowsView = new ListCollectionView(Rows) { Filter = o => !OnlyWarnings || (o is ImportRow r && r.Warning.Length > 0) };
        Messages = preview.Messages;
        Wishes = preview.Wishes;
        WishSlots = string.Join(", ", preview.SuggestedUnavailable.Select(k => $"{Ro.Day(k.Day).ToLowerInvariant()}, perechea {Ro.Roman(k.Slot)}"));
        FilesText = string.Join(" · ", preview.Files);

        // grupe noi (inexistente în proiect) — semnalate pentru verificare
        var known = current.Groups.Select(g => g.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var codeCount = Rows.SelectMany(r => Audience.ParseList(r.Groups)).Select(Audience.BaseCode)
                            .GroupBy(c => c, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        if (known.Count > 0)
        {
            foreach (var row in Rows)
            {
                var fresh = Audience.ParseList(row.Groups).Select(Audience.BaseCode).Where(c => !known.Contains(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (fresh.Count > 0)
                    row.Warning = (row.Warning + $" Grupă nouă: {string.Join(", ", fresh)}" + (fresh.Any(c => codeCount[c] == 1) ? " (apare o singură dată — posibilă greșeală de tipar)" : "") + ".").Trim();
            }
        }
        _updateSettings = current.Activities.Count == 0;
        SettingsInfo = preview.AcademicYear != null || preview.SemesterStart != null
            ? $"Din fișiere: anul universitar {preview.AcademicYear ?? "?"}" +
              (preview.SemesterStart is DateTime d ? $", început {d:dd.MM.yyyy}" : "") +
              (preview.Weeks is int w ? $", {w} săptămâni" : "")
            : "Fișierele nu conțin informații despre semestru.";
        foreach (var r in Rows) r.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ImportRow.Include)) UpdateSummary(); };
        UpdateSummary();
    }

    partial void OnOnlyWarningsChanged(bool value) => RowsView.Refresh();

    private void UpdateSummary()
    {
        int included = Rows.Count(r => r.Include);
        int warnings = Rows.Count(r => r.Warning.Length > 0);
        int hours = Rows.Where(r => r.Include).Sum(r => r.Hours);
        Summary = $"{Ro.Count(Rows.Count, "rând recunoscut", "rânduri recunoscute")} · {included} selectate ({hours} ore/săpt.) · " +
                  (warnings > 0 ? $"{Ro.Count(warnings, "avertisment", "avertismente")}" : "fără avertismente");
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var r in Rows) r.Include = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var r in Rows) r.Include = false;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!Rows.Any(r => r.Include))
        {
            Dialogs.Info("Import", "Nu ați selectat niciun rând pentru import.");
            return;
        }
        if (Replace && !Dialogs.Confirm("Înlocuire sarcină didactică",
                "Toate activitățile și lecțiile existente vor fi șterse și înlocuite cu datele importate. Continuați?", "Înlocuiește", danger: true))
            return;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}

// ═════════════════════════════ Generare automată ═════════════════════════════

public sealed partial class AutoScheduleViewModel : ObservableObject
{
    [ObservableProperty] private bool _rebuildAll;
    [ObservableProperty] private int _maxPairsPerDay = 4;
    [ObservableProperty] private int _lastComfortSlot = 5;
    [ObservableProperty] private bool _avoidGaps = true;
    [ObservableProperty] private bool _respectWishes = true;

    public IReadOnlyList<int> PairOptions { get; } = new[] { 3, 4, 5, 6 };
    public IReadOnlyList<int> SlotOptions { get; } = new[] { 4, 5, 6, 7 };
    public string Info { get; }
    public string LockedInfo { get; }

    public event Action<bool>? CloseRequested;

    public AutoScheduleViewModel()
    {
        var d = AppServices.Store.Data;
        var lk = AppServices.Store.Lk;
        int pending = d.Activities.Count(a => lk.PlacedHoursOf(a.Id) < a.HoursPerWeek);
        int hours = d.Activities.Sum(a => Math.Max(0, a.HoursPerWeek - lk.PlacedHoursOf(a.Id)));
        Info = $"{Ro.Count(pending, "activitate", "activități")} nu sunt plasate complet ({hours} ore/săpt.). În orar există {Ro.Count(d.Lessons.Count, "lecție", "lecții")}.";
        int locked = d.Lessons.Count(l => l.Locked);
        LockedInfo = locked > 0 ? $"{Ro.Count(locked, "lecție blocată", "lecții blocate")} vor rămâne neschimbate." : "Puteți bloca lecțiile importante (meniul contextual → Blochează) pentru a le păstra.";
    }

    public AutoScheduleOptions ToOptions() => new()
    {
        RebuildAll = RebuildAll,
        MaxPairsPerDay = MaxPairsPerDay,
        LastComfortSlot = LastComfortSlot,
        AvoidGaps = AvoidGaps,
        RespectUnavailability = RespectWishes,
    };

    [RelayCommand]
    private void Run()
    {
        if (RebuildAll && !Dialogs.Confirm("Regenerare completă",
                "Toate lecțiile neblocate vor fi șterse și orarul va fi refăcut de la zero. Puteți reveni cu Ctrl+Z. Continuați?", "Regenerează", danger: true))
            return;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}

// ═════════════════════════════ Panou informativ ═════════════════════════════

/// <summary>Panoul informativ (ecran pe hol / la ușa sălii): ce are loc acum și ce urmează.</summary>
public sealed partial class BoardViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _rotate;
    private List<BoardRowDto> _allRows = new();
    private string _lastMinute = "";

    public IReadOnlyList<Option<string>> Modes { get; } = new[]
    {
        new Option<string>("rooms", "Auditorii", ""),
        new Option<string>("groups", "Grupe", ""),
        new Option<string>("teachers", "Profesori", ""),
    };

    public ObservableCollection<BoardRowDto> Rows { get; } = new();

    [ObservableProperty] private Option<string>? _mode;
    [ObservableProperty] private string _time = "";
    [ObservableProperty] private string _seconds = "";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _weekText = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _currentSlot = "";
    [ObservableProperty] private double _slotProgress;
    [ObservableProperty] private string _institution = "";
    [ObservableProperty] private string _faculty = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private int _page;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private string _pageText = "";
    [ObservableProperty] private bool _onlyActive = true;
    [ObservableProperty] private bool _simulate;
    [ObservableProperty] private DateTime _simDate = DateTime.Today;
    [ObservableProperty] private string _simTime = "10:00";
    [ObservableProperty] private int _rowsPerPage = 8;
    [ObservableProperty] private string _updated = "";
    [ObservableProperty] private bool _isEmpty;

    public BoardViewModel()
    {
        _mode = Modes.FirstOrDefault(m => m.Value == AppServices.Settings.BoardMode) ?? Modes[0];
        var s = AppServices.Store.Data.Settings;
        // pentru demonstrație: dacă azi e în afara semestrului, propunem o zi lucrătoare din semestru
        int week = SemesterCalendar.WeekNumber(s, DateTime.Today);
        if (!SemesterCalendar.InSemester(s, week) || Ro.DayIndex(DateTime.Today) >= TimetableBuilder.Days(AppServices.Store.Data))
        {
            // simulare implicită: o zi lucrătoare din săptămâna a 5-a a semestrului
            var simDay = SemesterCalendar.MondayOf(s.SemesterStart).AddDays(7 * 4 + Math.Min(2, TimetableBuilder.Days(AppServices.Store.Data) - 1));
            _simDate = simDay;
        }
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => Tick(false);
        _rotate = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _rotate.Tick += (_, _) => NextPage();
        AppServices.Store.Changed += OnStoreChanged;
        Tick(true);
        _clock.Start();
        _rotate.Start();
    }

    private void OnStoreChanged(object? sender, EventArgs e) => Tick(true);

    partial void OnModeChanged(Option<string>? value)
    {
        if (value != null)
        {
            AppServices.Settings.BoardMode = value.Value;
            AppServices.Settings.Save();
        }
        Page = 0;
        Tick(true);
    }

    partial void OnOnlyActiveChanged(bool value) { Page = 0; Tick(true); }
    partial void OnSimulateChanged(bool value) => Tick(true);
    partial void OnSimDateChanged(DateTime value) => Tick(true);
    partial void OnSimTimeChanged(string value) => Tick(true);
    partial void OnRowsPerPageChanged(int value) => ShowPage();

    private DateTime Now
    {
        get
        {
            if (!Simulate) return DateTime.Now;
            var t = TimeSpan.TryParse(SimTime.Replace('.', ':'), CultureInfo.InvariantCulture, out var parsed) ? parsed : new TimeSpan(10, 0, 0);
            return SimDate.Date + t + TimeSpan.FromSeconds(DateTime.Now.Second);
        }
    }

    private void Tick(bool force)
    {
        var now = Now;
        Time = now.ToString("HH:mm");
        Seconds = now.ToString(":ss");
        var minute = now.ToString("yyyyMMddHHmm") + (Mode?.Value ?? "");
        if (!force && minute == _lastMinute) return;
        _lastMinute = minute;

        var store = AppServices.Store;
        var state = BoardLogic.Compute(store.Data, store.Lk, now, Mode?.Value ?? "rooms");
        var s = store.Data.Settings;
        Institution = s.Institution;
        Faculty = s.Faculty;
        Message = string.IsNullOrWhiteSpace(state.Message) ? "Orar Studio" : state.Message;
        Date = state.Date;
        WeekText = state.InSemester ? $"Săptămâna {state.Week} · {state.ParityLabel}" : $"{char.ToUpper(state.ParityLabel[0])}{state.ParityLabel[1..]} · în afara perioadei semestrului";
        Status = state.Status;
        CurrentSlot = state.CurrentSlotLabel ?? (state.NextSlot is int n ? $"Următoarea: perechea {Ro.Roman(n)}" : "—");
        var slot = state.CurrentSlot is int cs ? s.Slot(cs) : null;
        SlotProgress = slot == null ? 0 : Math.Clamp((now.TimeOfDay - slot.StartTime).TotalMinutes / Math.Max(1, (slot.EndTime - slot.StartTime).TotalMinutes), 0, 1);
        _allRows = state.Rows.Where(r => !OnlyActive || r.Now.Length > 0 || r.Next.Length > 0).ToList();
        IsEmpty = _allRows.Count == 0;
        Updated = $"Actualizat la {DateTime.Now:HH:mm:ss}" + (Simulate ? " · simulare" : "");
        ShowPage();
    }

    private void ShowPage()
    {
        int per = Math.Max(1, RowsPerPage);
        PageCount = Math.Max(1, (int)Math.Ceiling(_allRows.Count / (double)per));
        if (Page >= PageCount) Page = 0;
        Rows.Clear();
        foreach (var r in _allRows.Skip(Page * per).Take(per)) Rows.Add(r);
        PageText = PageCount > 1 ? $"Pagina {Page + 1} din {PageCount}" : "";
    }

    [RelayCommand]
    private void NextPage()
    {
        if (PageCount <= 1) return;
        Page = (Page + 1) % PageCount;
        ShowPage();
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (PageCount <= 1) return;
        Page = (Page - 1 + PageCount) % PageCount;
        ShowPage();
    }

    public void Dispose()
    {
        _clock.Stop();
        _rotate.Stop();
        AppServices.Store.Changed -= OnStoreChanged;
    }
}
