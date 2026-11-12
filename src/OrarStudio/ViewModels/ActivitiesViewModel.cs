using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

public sealed class ActivityRow
{
    public TeachingActivity Model { get; init; } = null!;
    public Guid Id => Model.Id;
    public string Subject { get; init; } = "";
    public string SubjectColor { get; init; } = "#6366F1";
    public LessonType Type { get; init; }
    public string TypeLabel { get; init; } = "";
    public int Hours { get; init; }
    public string Groups { get; init; } = "";
    public int Students { get; init; }
    public string Teacher { get; init; } = "";
    public string Language { get; init; } = "";
    public int Placed { get; init; }
    public double Progress => Hours <= 0 ? 0 : Math.Min(1, Placed / (double)Hours);
    public string ProgressText => $"{Placed}/{Hours}";
    public string Status => Placed == 0 ? "Neplasat" : Placed < Hours ? "Parțial" : Placed == Hours ? "Complet" : "Peste plan";
    public int StatusKind => Placed == 0 ? 0 : Placed < Hours ? 1 : Placed == Hours ? 2 : 3;
    public string Source { get; init; } = "";
}

public sealed partial class ActivityDraft : ObservableObject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    [ObservableProperty] private Subject? _subject;
    [ObservableProperty] private LessonType _type;
    [ObservableProperty] private int _hours = 2;
    [ObservableProperty] private TeacherOption? _teacher;
    [ObservableProperty] private string _groupsText = "";
    [ObservableProperty] private int _students;
    [ObservableProperty] private TeachingLanguage _language = TeachingLanguage.RO;
    [ObservableProperty] private string _notes = "";
    public string Source { get; init; } = "";
}

/// <summary>Sarcina didactică: lista activităților (ce trebuie plasat în orar) cu progresul plasării.</summary>
public sealed partial class ActivitiesViewModel : PageViewModel
{
    private bool _reselecting;

    public override string Title => "Sarcina didactică";
    public override string Icon => "";
    public override string Subtitle
    {
        get
        {
            var acts = Store.Data.Activities;
            int planned = acts.Sum(a => a.HoursPerWeek);
            int placed = acts.Sum(a => Math.Min(a.HoursPerWeek, Store.Lk.PlacedHoursOf(a.Id)));
            return $"{Ro.Count(acts.Count, "activitate", "activități")} · {placed} din {planned} ore/săpt. plasate";
        }
    }

    public ObservableCollection<ActivityRow> Rows { get; } = new();
    public ListCollectionView RowsView { get; }

    public IReadOnlyList<Option<string>> TypeFilters { get; } = new[]
    {
        new Option<string>("all", "Toate"), new Option<string>("Curs", "Cursuri"),
        new Option<string>("Seminar", "Seminare"), new Option<string>("Laborator", "Laboratoare"),
    };
    public IReadOnlyList<Option<string>> StatusFilters { get; } = new[]
    {
        new Option<string>("all", "Toate"), new Option<string>("todo", "De plasat"), new Option<string>("done", "Complete"),
    };

    public List<Subject> Subjects { get; private set; } = new();
    public List<TeacherOption> Teachers { get; private set; } = new();
    public IReadOnlyList<LessonType> Types { get; } = new[] { LessonType.Curs, LessonType.Seminar, LessonType.Laborator };
    public IReadOnlyList<int> HourOptions { get; } = new[] { 1, 2, 3, 4, 5, 6, 8 };
    public IReadOnlyList<TeachingLanguage> Languages { get; } = Enum.GetValues<TeachingLanguage>();

    [ObservableProperty] private ActivityRow? _selected;
    [ObservableProperty] private ActivityDraft? _draft;
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private Option<string>? _typeFilter;
    [ObservableProperty] private Option<string>? _statusFilter;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private double _coverage;
    [ObservableProperty] private string _coverageText = "";
    [ObservableProperty] private string _linkedInfo = "";

    public bool HasDraft => Draft != null;

    public ActivitiesViewModel()
    {
        RowsView = new ListCollectionView(Rows) { Filter = FilterRow };
        RowsView.SortDescriptions.Add(new SortDescription(nameof(ActivityRow.Subject), ListSortDirection.Ascending));
        RowsView.SortDescriptions.Add(new SortDescription(nameof(ActivityRow.Type), ListSortDirection.Ascending));
        TypeFilter = TypeFilters[0];
        StatusFilter = StatusFilters[0];
    }

    private bool FilterRow(object o)
    {
        if (o is not ActivityRow r) return false;
        if (TypeFilter?.Value is string t && t != "all" && r.Type.ToString() != t) return false;
        if (StatusFilter?.Value == "todo" && r.Placed >= r.Hours) return false;
        if (StatusFilter?.Value == "done" && r.Placed < r.Hours) return false;
        return Ro.Matches(r.Subject, Search) || Ro.Matches(r.Groups, Search) || Ro.Matches(r.Teacher, Search);
    }

    partial void OnSearchChanged(string value) => RefreshView();
    partial void OnTypeFilterChanged(Option<string>? value) => RefreshView();
    partial void OnStatusFilterChanged(Option<string>? value) => RefreshView();
    partial void OnDraftChanged(ActivityDraft? value) => OnPropertyChanged(nameof(HasDraft));

    partial void OnSelectedChanged(ActivityRow? value)
    {
        Error = null;
        if (value != null)
        {
            IsNew = false;
            Draft = ToDraft(value.Model);
            int lessons = Store.Data.Lessons.Count(l => l.ActivityId == value.Id);
            LinkedInfo = $"Plasat: {value.Placed} din {value.Hours} ore ({Ro.Count(lessons, "lecție", "lecții")} în orar)" +
                         (string.IsNullOrWhiteSpace(value.Source) ? "" : $"\nSursa: {value.Source}");
        }
        else if (!IsNew && !_reselecting)
        {
            Draft = null;
            LinkedInfo = "";
        }
    }

    private void RefreshView()
    {
        RowsView.Refresh();
        CountText = Ro.Count(RowsView.Count, "activitate", "activități") + (RowsView.Count != Rows.Count ? $" din {Rows.Count}" : "");
    }

    public override void Refresh()
    {
        var d = Store.Data;
        var lk = Store.Lk;
        Subjects = d.Subjects.OrderBy(s => s.Name, StringComparer.Create(Ro.Culture, true)).ToList();
        Teachers = new List<TeacherOption> { new() { Model = null, Label = "— fără profesor —" } };
        Teachers.AddRange(d.Teachers.OrderBy(t => t.Name, StringComparer.Create(Ro.Culture, true)).Select(t => new TeacherOption { Model = t, Label = t.Name, Detail = t.Title }));
        OnPropertyChanged(nameof(Subjects));
        OnPropertyChanged(nameof(Teachers));

        var keep = Selected?.Id;
        _reselecting = true;
        Rows.Clear();
        foreach (var a in d.Activities)
        {
            var subject = lk.Subject(a.SubjectId);
            Rows.Add(new ActivityRow
            {
                Model = a,
                Subject = subject?.Name ?? "(disciplină ștearsă)",
                SubjectColor = subject?.Color ?? "#94A3B8",
                Type = a.Type,
                TypeLabel = Ro.TypeLabel(a.Type),
                Hours = a.HoursPerWeek,
                Groups = Audience.Format(a.Groups),
                Students = a.Students > 0 ? a.Students : lk.Students(a.Groups),
                Teacher = lk.TeacherName(a.TeacherId),
                Language = Ro.LangShort(a.Language),
                Placed = lk.PlacedHoursOf(a.Id),
                Source = a.Source,
            });
        }
        RefreshView();
        int planned = d.Activities.Sum(a => a.HoursPerWeek);
        int placed = d.Activities.Sum(a => Math.Min(a.HoursPerWeek, lk.PlacedHoursOf(a.Id)));
        Coverage = planned == 0 ? 0 : placed / (double)planned;
        CoverageText = $"{placed} din {planned} ore plasate ({Coverage:P0})";
        Selected = keep is Guid id ? Rows.FirstOrDefault(r => r.Id == id) : null;
        _reselecting = false;
    }

    protected override void OnNavigatedTo(object? parameter)
    {
        if (parameter is Guid id)
        {
            var row = Rows.FirstOrDefault(r => r.Id == id);
            if (row == null) return;
            if (!RowsView.Contains(row))
            {
                Search = "";
                TypeFilter = TypeFilters[0];
                StatusFilter = StatusFilters[0];
            }
            Selected = row;
        }
    }

    private ActivityDraft ToDraft(TeachingActivity a) => new()
    {
        Id = a.Id,
        Subject = Subjects.FirstOrDefault(s => s.Id == a.SubjectId),
        Type = a.Type,
        Hours = a.HoursPerWeek,
        Teacher = Teachers.FirstOrDefault(t => t.Model?.Id == a.TeacherId) ?? Teachers.FirstOrDefault(),
        GroupsText = Audience.Format(a.Groups),
        Students = a.Students,
        Language = a.Language,
        Notes = a.Notes,
        Source = a.Source,
    };

    [RelayCommand]
    private void Add()
    {
        Selected = null;
        IsNew = true;
        LinkedInfo = "";
        Error = null;
        Draft = new ActivityDraft { Teacher = Teachers.FirstOrDefault(), Type = LessonType.Curs, Hours = 2 };
    }

    [RelayCommand]
    private void Cancel()
    {
        Error = null;
        if (IsNew) { IsNew = false; Draft = null; }
        else if (Selected != null) Draft = ToDraft(Selected.Model);
    }

    [RelayCommand]
    private void Save()
    {
        if (Draft == null) return;
        if (Draft.Subject == null) { Error = "Alegeți disciplina."; return; }
        var codes = Audience.ParseList(Draft.GroupsText);
        if (codes.Count == 0) { Error = "Introduceți grupele (ex. I2301, IA2301 sau I2301-1)."; return; }
        var bad = codes.Where(c => !Regex.IsMatch(c, @"^[A-Z]{1,8}\d{3,5}(-[A-Z0-9]{1,4}(-[A-Z0-9]{1,4})*)?$")).ToList();
        if (bad.Count > 0) { Error = $"Cod de grupă invalid: {string.Join(", ", bad)}"; return; }
        if (Draft.Hours is < 1 or > 12) { Error = "Numărul de ore trebuie să fie între 1 și 12."; return; }

        var unknown = codes.Where(c => Store.Lk.Group(Audience.BaseCode(c)) == null).Select(Audience.BaseCode)
                           .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0 &&
            !Dialogs.Confirm("Grupe noi", $"Grupele {string.Join(", ", unknown)} nu există încă. Le creăm automat?", "Creează"))
            return;

        var draft = Draft;
        var activity = new TeachingActivity
        {
            Id = draft.Id,
            SubjectId = draft.Subject.Id,
            Type = draft.Type,
            HoursPerWeek = draft.Hours,
            TeacherId = draft.Teacher?.Model?.Id,
            Groups = codes,
            Students = Math.Max(0, draft.Students),
            Language = draft.Language,
            Notes = draft.Notes.Trim(),
            Source = string.IsNullOrEmpty(draft.Source) ? "introdus manual" : draft.Source,
        };
        bool isNew = IsNew;
        int updatedLessons = 0;
        Store.Mutate(isNew ? "Adăugare activitate" : "Modificare activitate", d =>
        {
            foreach (var code in codes)
            {
                var baseCode = Audience.BaseCode(code);
                var g = d.Groups.FirstOrDefault(x => string.Equals(x.Code, baseCode, StringComparison.OrdinalIgnoreCase));
                if (g == null)
                {
                    g = new StudentGroup { Code = baseCode, Language = activity.Language, Year = 1 };
                    d.Groups.Add(g);
                }
                if (Audience.IsSubgroup(code) && !g.Subgroups.Any(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)))
                    g.Subgroups.Add(new Subgroup { Code = code });
            }
            if (isNew)
            {
                d.Activities.Add(activity);
                return;
            }
            var index = d.Activities.IndexOf(d.Activities.First(a => a.Id == activity.Id));
            d.Activities[index] = activity;
            foreach (var l in d.Lessons.Where(l => l.ActivityId == activity.Id))
            {
                l.SubjectId = activity.SubjectId;
                l.TeacherId = activity.TeacherId;
                l.Type = activity.Type;
                l.Groups = activity.Groups.ToList();
                updatedLessons++;
            }
        });
        IsNew = false;
        Selected = Rows.FirstOrDefault(r => r.Id == activity.Id);
        Toasts.Show(isNew ? "Activitatea a fost adăugată în sarcina didactică." :
            $"Activitatea a fost actualizată{(updatedLessons > 0 ? $" (și {Ro.Count(updatedLessons, "lecție plasată", "lecții plasate")})" : "")}.",
            ToastKind.Success, "Anulează", () => Store.Undo(), 4);
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected == null) return;
        var id = Selected.Id;
        int lessons = Store.Data.Lessons.Count(l => l.ActivityId == id);
        if (!Dialogs.Confirm("Ștergere activitate",
                $"Ștergeți „{Selected.Subject}” ({Selected.TypeLabel.ToLowerInvariant()}, {Selected.Groups})?" +
                (lessons > 0 ? $"\n\nSe vor șterge și {Ro.Count(lessons, "lecție plasată", "lecții plasate")} în orar." : ""), "Șterge", danger: true))
            return;
        Store.Mutate("Ștergere activitate", d =>
        {
            foreach (var l in d.Lessons.Where(l => l.ActivityId == id).ToList()) d.Lessons.Remove(l);
            var a = d.Activities.FirstOrDefault(x => x.Id == id);
            if (a != null) d.Activities.Remove(a);
        });
        Selected = null;
        Draft = null;
        Toasts.Show("Activitatea a fost ștearsă.", ToastKind.Info, "Anulează", () => Store.Undo());
    }

    [RelayCommand]
    private async Task PlaceSelected()
    {
        if (Selected == null || AppServices.Main == null) return;
        await AppServices.Main.RunAutoSchedule(new AutoScheduleOptions { OnlyActivities = new HashSet<Guid> { Selected.Id } }, ask: false);
    }

    [RelayCommand]
    private void OpenInEditor()
    {
        if (Selected == null || AppServices.Main == null) return;
        var a = Selected.Model;
        var lesson = Store.Data.Lessons.FirstOrDefault(l => l.ActivityId == a.Id);
        var target = a.Groups.Count > 0
            ? new TimetableTarget(ScheduleViewMode.Group, Audience.BaseCode(a.Groups[0]), lesson?.Id)
            : new TimetableTarget(ScheduleViewMode.Teacher, a.TeacherId?.ToString() ?? "", lesson?.Id);
        AppServices.Main.Navigate(AppServices.Main.Timetable, target);
    }

    [RelayCommand]
    private void Import() => AppServices.Main?.ImportCommand.Execute(null);

    [RelayCommand]
    private void Generate() => AppServices.Main?.AutoScheduleCommand.Execute(null);
}
