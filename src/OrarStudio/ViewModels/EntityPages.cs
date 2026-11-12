using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

/// <summary>
/// Bază pentru paginile de date (grupe, profesori, discipline, auditorii): listă filtrabilă + formular
/// care editează o COPIE (ciornă); „Salvează" aplică modificarea ca pas anulabil (Ctrl+Z).
/// </summary>
public abstract partial class EntityPageViewModel<T> : PageViewModel where T : ObservableObject, IEntity
{
    private bool _reselecting;

    protected abstract ObservableCollection<T> Source { get; }
    protected abstract string Noun { get; }
    protected abstract T CreateNew();
    protected abstract bool Matches(T item, string search);
    protected virtual string? Validate(T draft) => null;
    protected virtual void BeforeReplace(Dataset d, T original, T updated) { }
    protected virtual (string Message, bool Proceed) DescribeDelete(T item) => ($"Ștergeți {Noun} „{item}”?", true);
    protected virtual void CascadeDelete(Dataset d, T item) { }
    protected virtual void UpdateStats() { }
    protected virtual void OnDraftLoaded(T? draft) { }
    protected virtual bool ExtraFilter(T item) => true;

    public ListCollectionView ItemsView { get; }

    [ObservableProperty] private T? _selected;
    [ObservableProperty] private T? _draft;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string _countText = "";

    protected EntityPageViewModel()
    {
        ItemsView = new ListCollectionView(Source) { Filter = o => o is T t && ExtraFilter(t) && Matches(t, Search) };
    }

    public bool HasDraft => Draft != null;
    public string FormTitle => Draft == null ? "" : IsNew ? $"Adăugare {Noun}" : Draft.ToString() ?? "";

    partial void OnSearchChanged(string value) => RefreshView();

    partial void OnSelectedChanged(T? value)
    {
        if (value != null)
        {
            IsNew = false;
            Draft = Json.Clone(value);
        }
        else if (!IsNew && !_reselecting)
        {
            Draft = null;
        }
        ValidationError = null;
    }

    partial void OnDraftChanged(T? value)
    {
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(FormTitle));
        OnDraftLoaded(value);
    }

    partial void OnIsNewChanged(bool value) => OnPropertyChanged(nameof(FormTitle));

    protected void RefreshView()
    {
        ItemsView.Refresh();
        CountText = Ro.Count(ItemsView.Count, "element", "elemente") + (ItemsView.Count != Source.Count ? $" din {Source.Count}" : "");
    }

    public override void Refresh()
    {
        UpdateStats();
        var id = Selected?.Id ?? (IsNew ? null : Draft?.Id);
        RefreshView();
        if (id is Guid g)
        {
            _reselecting = true;
            Selected = Source.FirstOrDefault(x => x.Id == g);
            _reselecting = false;
        }
    }

    protected override void OnNavigatedTo(object? parameter)
    {
        if (parameter is Guid id) SelectById(id);
    }

    public void SelectById(Guid id)
    {
        var item = Source.FirstOrDefault(x => x.Id == id);
        if (item == null) return;
        if (!ItemsView.Contains(item))
        {
            Search = "";
            ResetFilters();
        }
        Selected = item;
    }

    protected virtual void ResetFilters() { }

    [RelayCommand]
    private void Add()
    {
        Selected = null;
        IsNew = true;
        Draft = CreateNew();
        ValidationError = null;
    }

    [RelayCommand]
    private void Save()
    {
        if (Draft == null) return;
        var error = Validate(Draft);
        if (error != null)
        {
            ValidationError = error;
            return;
        }
        var copy = Json.Clone(Draft);
        bool isNew = IsNew;
        Store.Mutate(isNew ? $"Adăugare {Noun}" : $"Modificare {Noun}", d =>
        {
            if (isNew)
            {
                Source.Add(copy);
            }
            else
            {
                var original = Source.FirstOrDefault(x => x.Id == copy.Id);
                if (original == null) { Source.Add(copy); return; }
                BeforeReplace(d, original, copy);
                Source[Source.IndexOf(original)] = copy;
            }
        });
        IsNew = false;
        SelectById(copy.Id);
        Toasts.Show(isNew ? $"S-a adăugat: {copy}" : $"Modificările au fost salvate ({copy}).", ToastKind.Success, "Anulează", () => Store.Undo(), 4);
    }

    [RelayCommand]
    private void Delete()
    {
        var target = Selected;
        if (target == null) return;
        var (message, proceed) = DescribeDelete(target);
        if (!proceed) return;
        if (!Dialogs.Confirm($"Ștergere {Noun}", message, "Șterge", danger: true)) return;
        Store.Mutate($"Ștergere {Noun} „{target}”", d =>
        {
            CascadeDelete(d, target);
            var item = Source.FirstOrDefault(x => x.Id == target.Id);
            if (item != null) Source.Remove(item);
        });
        Selected = null;
        Draft = null;
        Toasts.Show($"„{target}” a fost șters.", ToastKind.Info, "Anulează", () => Store.Undo());
    }

    [RelayCommand]
    private void Cancel()
    {
        ValidationError = null;
        if (IsNew)
        {
            IsNew = false;
            Draft = null;
        }
        else if (Selected != null)
        {
            Draft = Json.Clone(Selected);
        }
    }
}

// ═════════════════════════════ Grupe ═════════════════════════════

public sealed partial class GroupsViewModel : EntityPageViewModel<StudentGroup>
{
    public override string Title => "Grupe";
    public override string Icon => "";
    public override string Subtitle => $"{Ro.Count(Store.Data.Groups.Count, "grupă", "grupe")} · {Store.Data.Groups.Sum(g => g.Students)} studenți";
    protected override ObservableCollection<StudentGroup> Source => Store.Data.Groups;
    protected override string Noun => "grupă";

    public IReadOnlyList<TeachingLanguage> Languages { get; } = Enum.GetValues<TeachingLanguage>();
    public IReadOnlyList<int> Years { get; } = new[] { 1, 2, 3, 4, 5, 6 };
    public IReadOnlyList<string> Cycles { get; } = new[] { "Licență", "Master", "Doctorat" };

    public GroupsViewModel()
    {
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(StudentGroup.Year), ListSortDirection.Ascending));
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(StudentGroup.Code), ListSortDirection.Ascending));
    }

    protected override StudentGroup CreateNew() => new() { Code = "", Year = 1, Students = 25, Language = TeachingLanguage.RO };

    protected override bool Matches(StudentGroup g, string s) => Ro.Matches(g.Code, s) || Ro.Matches(g.Specialty, s);

    protected override string? Validate(StudentGroup g)
    {
        g.Code = (g.Code ?? "").Trim().ToUpperInvariant();
        if (g.Code.Length == 0) return "Introduceți codul grupei (ex. I2301).";
        if (!Regex.IsMatch(g.Code, @"^[A-Z]{1,8}\d{3,5}$")) return "Codul trebuie să conțină litere urmate de cifre (ex. I2301, IA2302).";
        if (Store.Data.Groups.Any(x => x.Id != g.Id && string.Equals(x.Code, g.Code, StringComparison.OrdinalIgnoreCase)))
            return $"Există deja grupa {g.Code}.";
        foreach (var sg in g.Subgroups)
        {
            sg.Code = (sg.Code ?? "").Trim().ToUpperInvariant();
            if (!sg.Code.StartsWith(g.Code + "-", StringComparison.OrdinalIgnoreCase))
                return $"Subgrupa „{sg.Code}” trebuie să înceapă cu „{g.Code}-” (ex. {g.Code}-1).";
        }
        if (g.Subgroups.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != g.Subgroups.Count)
            return "Codurile subgrupelor trebuie să fie unice.";
        return null;
    }

    protected override void BeforeReplace(Dataset d, StudentGroup original, StudentGroup updated)
    {
        if (string.Equals(original.Code, updated.Code, StringComparison.OrdinalIgnoreCase)) return;
        string Rename(string code)
        {
            if (string.Equals(code, original.Code, StringComparison.OrdinalIgnoreCase)) return updated.Code;
            if (code.StartsWith(original.Code + "-", StringComparison.OrdinalIgnoreCase)) return updated.Code + code[original.Code.Length..];
            return code;
        }
        foreach (var a in d.Activities) a.Groups = a.Groups.Select(Rename).ToList();
        foreach (var l in d.Lessons) l.Groups = l.Groups.Select(Rename).ToList();
    }

    protected override (string Message, bool Proceed) DescribeDelete(StudentGroup g)
    {
        int acts = Store.Data.Activities.Count(a => Audience.Concerns(a.Groups, g.Code));
        int lessons = Store.Data.Lessons.Count(l => Audience.Concerns(l.Groups, g.Code));
        return ($"Ștergeți grupa {g.Code}?\n\nGrupa va fi eliminată din {Ro.Count(acts, "activitate", "activități")} și {Ro.Count(lessons, "lecție", "lecții")}. " +
                "Activitățile/lecțiile care rămân fără grupe vor fi șterse.", true);
    }

    protected override void CascadeDelete(Dataset d, StudentGroup g)
    {
        bool Keep(string c) => !string.Equals(Audience.BaseCode(c), g.Code, StringComparison.OrdinalIgnoreCase);
        foreach (var a in d.Activities.ToList())
        {
            a.Groups = a.Groups.Where(Keep).ToList();
            if (a.Groups.Count == 0) d.Activities.Remove(a);
        }
        foreach (var l in d.Lessons.ToList())
        {
            l.Groups = l.Groups.Where(Keep).ToList();
            if (l.Groups.Count == 0) d.Lessons.Remove(l);
        }
    }

    protected override void UpdateStats()
    {
        foreach (var g in Store.Data.Groups)
        {
            var occupied = new HashSet<(int, int, int)>();
            foreach (var l in Store.Data.Lessons.Where(l => Audience.Concerns(l.Groups, g.Code)))
            {
                if (l.Week != WeekParity.Para) occupied.Add((l.Day, l.Slot, 1));
                if (l.Week != WeekParity.Impara) occupied.Add((l.Day, l.Slot, 2));
            }
            g.HoursScheduled = occupied.Count;
        }
    }

    [RelayCommand]
    private void AddSubgroup()
    {
        if (Draft == null) return;
        var code = (Draft.Code ?? "").Trim().ToUpperInvariant();
        int n = Draft.Subgroups.Count + 1;
        while (Draft.Subgroups.Any(s => s.Code.EndsWith("-" + n))) n++;
        Draft.Subgroups.Add(new Subgroup { Code = $"{code}-{n}", Students = Draft.Students > 0 ? (int)Math.Ceiling(Draft.Students / 2.0) : 0 });
    }

    [RelayCommand]
    private void RemoveSubgroup(Subgroup? sg)
    {
        if (Draft != null && sg != null) Draft.Subgroups.Remove(sg);
    }

    [RelayCommand]
    private void OpenSchedule()
    {
        if (Selected == null || AppServices.Main == null) return;
        AppServices.Main.Navigate(AppServices.Main.Timetable, new TimetableTarget(ScheduleViewMode.Group, Selected.Code));
    }
}

// ═════════════════════════════ Profesori ═════════════════════════════

public sealed partial class AvailabilityCell : ObservableObject
{
    private readonly Action<AvailabilityCell> _changed;
    public AvailabilityCell(Action<AvailabilityCell> changed) => _changed = changed;
    public int Day { get; init; }
    public int Slot { get; init; }
    public string Tooltip { get; init; } = "";
    [ObservableProperty] private bool _isBlocked;
    partial void OnIsBlockedChanged(bool value) => _changed(this);
}

public sealed class AvailabilityRow
{
    public string Label { get; init; } = "";
    public string Time { get; init; } = "";
    public List<AvailabilityCell> Cells { get; init; } = new();
}

public sealed partial class TeachersViewModel : EntityPageViewModel<Teacher>
{
    public override string Title => "Profesori";
    public override string Icon => "";
    public override string Subtitle => $"{Ro.Count(Store.Data.Teachers.Count, "cadru didactic", "cadre didactice")} · doleanțe și încărcare";
    protected override ObservableCollection<Teacher> Source => Store.Data.Teachers;
    protected override string Noun => "profesor";

    public ObservableCollection<AvailabilityRow> Availability { get; } = new();
    public List<string> DayHeaders { get; private set; } = new();
    [ObservableProperty] private string _teacherInfo = "";
    public ObservableCollection<string> TeacherActivities { get; } = new();

    public TeachersViewModel()
    {
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(Teacher.Name), ListSortDirection.Ascending));
    }

    protected override Teacher CreateNew() => new() { Name = "" };

    protected override bool Matches(Teacher t, string s) =>
        Ro.Matches(t.Name, s) || Ro.Matches(t.Title, s) || Ro.Matches(t.Department, s);

    protected override string? Validate(Teacher t)
    {
        t.Name = Ro.CollapseSpaces(t.Name ?? "");
        if (t.Name.Length < 2) return "Introduceți numele profesorului (ex. V. Arnaut).";
        if (Store.Data.Teachers.Any(x => x.Id != t.Id && Ro.Key(x.Name) == Ro.Key(t.Name)))
            return $"Profesorul „{t.Name}” există deja.";
        if (!string.IsNullOrWhiteSpace(t.Email) && !Regex.IsMatch(t.Email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            return "Adresa de e-mail nu este validă.";
        return null;
    }

    protected override (string Message, bool Proceed) DescribeDelete(Teacher t)
    {
        int acts = Store.Data.Activities.Count(a => a.TeacherId == t.Id);
        int lessons = Store.Data.Lessons.Count(l => l.TeacherId == t.Id);
        return ($"Ștergeți profesorul {t.Name}?\n\n{Ro.Count(acts, "activitate", "activități")} și {Ro.Count(lessons, "lecție", "lecții")} vor rămâne fără profesor.", true);
    }

    protected override void CascadeDelete(Dataset d, Teacher t)
    {
        foreach (var a in d.Activities.Where(a => a.TeacherId == t.Id)) a.TeacherId = null;
        foreach (var l in d.Lessons.Where(l => l.TeacherId == t.Id)) l.TeacherId = null;
    }

    protected override void UpdateStats()
    {
        foreach (var t in Store.Data.Teachers)
        {
            t.HoursScheduled = Store.Data.Lessons.Where(l => l.TeacherId == t.Id).Sum(l => l.Hours);
            t.HoursPlanned = Store.Data.Activities.Where(a => a.TeacherId == t.Id).Sum(a => a.HoursPerWeek);
        }
    }

    protected override void OnDraftLoaded(Teacher? draft)
    {
        Availability.Clear();
        TeacherActivities.Clear();
        TeacherInfo = "";
        if (draft == null) return;
        var s = Store.Data.Settings;
        int days = TimetableBuilder.Days(Store.Data);
        DayHeaders = Enumerable.Range(0, days).Select(Ro.DayShort).ToList();
        OnPropertyChanged(nameof(DayHeaders));
        foreach (var slot in s.TimeSlots.OrderBy(x => x.Number))
        {
            var row = new AvailabilityRow { Label = Ro.Roman(slot.Number), Time = slot.Start };
            for (int day = 0; day < days; day++)
            {
                var cell = new AvailabilityCell(OnAvailabilityChanged)
                {
                    Day = day, Slot = slot.Number,
                    Tooltip = $"{Ro.Day(day)}, perechea {Ro.Roman(slot.Number)} ({slot.Label}) — clic pentru a marca indisponibil",
                };
                cell.IsBlocked = draft.IsUnavailable(day, slot.Number);
                row.Cells.Add(cell);
            }
            Availability.Add(row);
        }

        if (!IsNew)
        {
            var lk = Store.Lk;
            var acts = Store.Data.Activities.Where(a => a.TeacherId == draft.Id).ToList();
            foreach (var a in acts.OrderBy(a => lk.SubjectName(a.SubjectId)).Take(40))
                TeacherActivities.Add($"{lk.SubjectName(a.SubjectId)} — {Ro.TypeLabel(a.Type).ToLowerInvariant()}, {Audience.Format(a.Groups)} · {lk.PlacedHoursOf(a.Id)}/{a.HoursPerWeek} ore");
            var orig = Store.Data.Teachers.FirstOrDefault(t => t.Id == draft.Id);
            TeacherInfo = orig == null ? "" : $"{orig.HoursScheduled} ore/săpt. în orar din {orig.HoursPlanned} planificate · {Ro.Count(acts.Count, "activitate", "activități")}";
        }
    }

    private void OnAvailabilityChanged(AvailabilityCell cell)
    {
        if (Draft == null) return;
        var key = new SlotKey(cell.Day, cell.Slot);
        if (cell.IsBlocked && !Draft.Unavailable.Contains(key)) Draft.Unavailable.Add(key);
        if (!cell.IsBlocked) Draft.Unavailable.Remove(key);
    }

    [RelayCommand]
    private void ClearAvailability()
    {
        foreach (var c in Availability.SelectMany(r => r.Cells)) c.IsBlocked = false;
    }

    [RelayCommand]
    private void OpenSchedule()
    {
        if (Selected == null || AppServices.Main == null) return;
        AppServices.Main.Navigate(AppServices.Main.Timetable, new TimetableTarget(ScheduleViewMode.Teacher, Selected.Id.ToString()));
    }
}

// ═════════════════════════════ Discipline ═════════════════════════════

public sealed partial class SubjectsViewModel : EntityPageViewModel<Subject>
{
    public override string Title => "Discipline";
    public override string Icon => "";
    public override string Subtitle => $"{Ro.Count(Store.Data.Subjects.Count, "disciplină", "discipline")} (obiecte de studiu)";
    protected override ObservableCollection<Subject> Source => Store.Data.Subjects;
    protected override string Noun => "disciplină";

    public IReadOnlyList<string> Colors { get; } = Palette.Subjects;
    public IReadOnlyList<string> Evaluations { get; } = new[] { "Examen", "Colocviu", "Evaluare curentă", "" };

    public SubjectsViewModel()
    {
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(Subject.Name), ListSortDirection.Ascending));
    }

    protected override Subject CreateNew() => new() { Name = "", Color = Palette.SubjectColor(Store.Data.Subjects.Count), Evaluation = "Examen" };

    protected override bool Matches(Subject s, string q) => Ro.Matches(s.Name, q) || Ro.Matches(s.Short, q) || Ro.Matches(s.Department, q);

    protected override string? Validate(Subject s)
    {
        s.Name = Ro.CollapseSpaces(s.Name ?? "");
        if (s.Name.Length < 2) return "Introduceți denumirea disciplinei.";
        if (Store.Data.Subjects.Any(x => x.Id != s.Id && Ro.Key(x.Name) == Ro.Key(s.Name)))
            return $"Disciplina „{s.Name}” există deja.";
        if (string.IsNullOrWhiteSpace(s.ShortName)) s.ShortName = Abbrev.Make(s.Name);
        return null;
    }

    protected override (string Message, bool Proceed) DescribeDelete(Subject s)
    {
        int acts = Store.Data.Activities.Count(a => a.SubjectId == s.Id);
        int lessons = Store.Data.Lessons.Count(l => l.SubjectId == s.Id);
        return ($"Ștergeți disciplina „{s.Name}”?\n\nSe vor șterge și {Ro.Count(acts, "activitate", "activități")} din sarcina didactică și {Ro.Count(lessons, "lecție", "lecții")} din orar.", true);
    }

    protected override void CascadeDelete(Dataset d, Subject s)
    {
        foreach (var a in d.Activities.Where(a => a.SubjectId == s.Id).ToList()) d.Activities.Remove(a);
        foreach (var l in d.Lessons.Where(l => l.SubjectId == s.Id).ToList()) d.Lessons.Remove(l);
    }

    protected override void UpdateStats()
    {
        foreach (var s in Store.Data.Subjects)
        {
            s.ActivityCount = Store.Data.Activities.Count(a => a.SubjectId == s.Id);
            s.HoursScheduled = Store.Data.Lessons.Where(l => l.SubjectId == s.Id).Sum(l => l.Hours);
        }
    }

    [RelayCommand]
    private void AutoShort()
    {
        if (Draft != null) Draft.ShortName = Abbrev.Make(Draft.Name);
    }
}

// ═════════════════════════════ Auditorii ═════════════════════════════

public sealed partial class RoomsViewModel : EntityPageViewModel<Room>
{
    private Room? _watched;

    public override string Title => "Auditorii";
    public override string Icon => "";
    public override string Subtitle =>
        $"{Ro.Count(Store.Data.Rooms.Count, "sală", "săli")} · {Store.Data.Rooms.Count(r => r.AllowsCurs)} pentru cursuri · " +
        $"{Store.Data.Rooms.Count(r => r.AllowsSeminar)} pentru seminare · {Store.Data.Rooms.Count(r => r.AllowsLaborator)} pentru laboratoare";
    protected override ObservableCollection<Room> Source => Store.Data.Rooms;
    protected override string Noun => "auditoriu";

    public IReadOnlyList<RoomKind> Kinds { get; } = Enum.GetValues<RoomKind>();
    public IReadOnlyList<Option<string>> Filters { get; } = new[]
    {
        new Option<string>("all", "Toate"),
        new Option<string>("curs", "Cursuri"),
        new Option<string>("seminar", "Seminare"),
        new Option<string>("lab", "Laboratoare"),
    };
    [ObservableProperty] private Option<string>? _filter;
    [ObservableProperty] private string _roomInfo = "";

    public RoomsViewModel()
    {
        Filter = Filters[0];
        ItemsView.SortDescriptions.Add(new SortDescription(nameof(Room.Name), ListSortDirection.Ascending));
    }

    partial void OnFilterChanged(Option<string>? value) => RefreshView();

    protected override void ResetFilters() => Filter = Filters[0];

    protected override bool ExtraFilter(Room r) => Filter?.Value switch
    {
        "curs" => r.AllowsCurs,
        "seminar" => r.AllowsSeminar,
        "lab" => r.AllowsLaborator,
        _ => true,
    };

    protected override Room CreateNew()
    {
        var r = new Room { Name = "", Building = Store.Data.Rooms.FirstOrDefault()?.Building ?? "", Capacity = 30, Kind = RoomKind.SalaSeminar };
        var (c, s, l) = Room.DefaultUsage(r.Kind);
        r.AllowsCurs = c;
        r.AllowsSeminar = s;
        r.AllowsLaborator = l;
        return r;
    }

    protected override bool Matches(Room r, string s) =>
        Ro.Matches(r.Name, s) || Ro.Matches(r.Building, s) || Ro.Matches(r.KindLabel, s) || Ro.Matches(r.Notes, s);

    protected override string? Validate(Room r)
    {
        r.Name = (r.Name ?? "").Trim();
        if (r.Name.Length == 0) return "Introduceți numărul/denumirea auditoriului.";
        if (Store.Data.Rooms.Any(x => x.Id != r.Id && string.Equals(x.Name, r.Name, StringComparison.OrdinalIgnoreCase)))
            return $"Auditoriul {r.Name} există deja.";
        if (r.Capacity <= 0) return "Capacitatea trebuie să fie un număr pozitiv.";
        if (!r.AllowsCurs && !r.AllowsSeminar && !r.AllowsLaborator) return "Bifați cel puțin un tip de lecții pentru care este destinată sala.";
        return null;
    }

    protected override (string Message, bool Proceed) DescribeDelete(Room r)
    {
        int lessons = Store.Data.Lessons.Count(l => l.RoomId == r.Id);
        return ($"Ștergeți auditoriul {r.Name}?\n\n{Ro.Count(lessons, "lecție", "lecții")} vor rămâne fără auditoriu.", true);
    }

    protected override void CascadeDelete(Dataset d, Room r)
    {
        foreach (var l in d.Lessons.Where(l => l.RoomId == r.Id)) l.RoomId = null;
    }

    protected override void UpdateStats()
    {
        foreach (var r in Store.Data.Rooms)
        {
            r.Occupancy = TimetableBuilder.RoomOccupancy(Store.Data, r);
            r.LessonCount = Store.Data.Lessons.Count(l => l.RoomId == r.Id);
        }
    }

    protected override void OnDraftLoaded(Room? draft)
    {
        if (_watched != null) _watched.PropertyChanged -= DraftPropertyChanged;
        _watched = draft;
        if (draft != null) draft.PropertyChanged += DraftPropertyChanged;
        var orig = draft == null ? null : Store.Data.Rooms.FirstOrDefault(r => r.Id == draft.Id);
        RoomInfo = orig == null ? "" : $"{Ro.Count(orig.LessonCount, "lecție", "lecții")} pe săptămână · grad de ocupare {orig.Occupancy:P0}";
    }

    private void DraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Room.Kind) || sender is not Room r) return;
        var (c, s, l) = Room.DefaultUsage(r.Kind);
        r.AllowsCurs = c;
        r.AllowsSeminar = s;
        r.AllowsLaborator = l;
        if (r.Kind is RoomKind.LaboratorInformatica or RoomKind.LaboratorSpecializat && r.Computers == 0)
            r.Computers = Math.Max(0, r.Capacity - 2);
    }

    [RelayCommand]
    private void OpenSchedule()
    {
        if (Selected == null || AppServices.Main == null) return;
        AppServices.Main.Navigate(AppServices.Main.Timetable, new TimetableTarget(ScheduleViewMode.Room, Selected.Id.ToString()));
    }
}
