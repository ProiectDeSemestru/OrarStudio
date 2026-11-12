using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

public sealed class TeacherOption
{
    public Teacher? Model { get; init; }
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed partial class RoomOption : ObservableObject
{
    public Room? Model { get; init; }
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _free = true;
    [ObservableProperty] private bool _suitable = true;
}

public sealed partial class AudienceOption : ObservableObject
{
    private readonly Action _changed;
    public AudienceOption(Action changed) => _changed = changed;
    public string Code { get; init; } = "";
    public string Detail { get; init; } = "";
    public bool IsGroup { get; init; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible = true;
    partial void OnIsSelectedChanged(bool value) => _changed();
}

public sealed class ActivityOption
{
    public TeachingActivity? Model { get; init; }
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>Dialogul de editare/creare a unei lecții (deschis prin dublu-clic).</summary>
public sealed partial class LessonEditorViewModel : ObservableObject
{
    private readonly Lesson? _original;
    private bool _loading;

    public bool IsNew => _original == null;
    public string Title => IsNew ? "Lecție nouă" : "Editare lecție";
    public string Subtitle => IsNew
        ? "Completați detaliile sau alegeți o activitate din sarcina didactică"
        : $"{Ro.Day(_original!.Day)}, perechea {Ro.Roman(_original.Slot)} · {Store.Lk.SubjectName(_original.SubjectId)}";

    private static DataStore Store => AppServices.Store;

    public List<Subject> Subjects { get; }
    public List<TeacherOption> Teachers { get; }
    public IReadOnlyList<LessonType> Types { get; } = new[] { LessonType.Curs, LessonType.Seminar, LessonType.Laborator };
    public IReadOnlyList<Option<WeekParity>> Weeks { get; } = new[]
    {
        new Option<WeekParity>(WeekParity.Saptamanal, "Săptămânal"),
        new Option<WeekParity>(WeekParity.Impara, "Săpt. impară"),
        new Option<WeekParity>(WeekParity.Para, "Săpt. pară"),
    };
    public List<Option<int>> Days { get; }
    public List<Option<int>> Slots { get; }
    public ObservableCollection<RoomOption> Rooms { get; } = new();
    public ObservableCollection<AudienceOption> Audiences { get; } = new();
    public List<ActivityOption> Activities { get; }
    public ObservableCollection<IssueVM> Issues { get; } = new();

    [ObservableProperty] private Subject? _subject;
    [ObservableProperty] private TeacherOption? _teacher;
    [ObservableProperty] private LessonType _type;
    [ObservableProperty] private Option<int>? _day;
    [ObservableProperty] private Option<int>? _slot;
    [ObservableProperty] private Option<WeekParity>? _week;
    [ObservableProperty] private RoomOption? _room;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private ActivityOption? _activity;
    [ObservableProperty] private string _audienceSearch = "";
    [ObservableProperty] private string _groupsSummary = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string _studentsInfo = "";

    public Guid? SavedLessonId { get; private set; }
    public event Action<bool>? CloseRequested;

    private LessonEditorViewModel(Lesson? original)
    {
        _original = original;
        var d = Store.Data;
        Subjects = d.Subjects.OrderBy(s => s.Name, StringComparer.Create(Ro.Culture, true)).ToList();
        Teachers = new List<TeacherOption> { new() { Model = null, Label = "— fără profesor —" } };
        Teachers.AddRange(d.Teachers.OrderBy(t => t.Name, StringComparer.Create(Ro.Culture, true))
                                    .Select(t => new TeacherOption { Model = t, Label = t.Name, Detail = t.Title }));
        Days = Enumerable.Range(0, TimetableBuilder.Days(d)).Select(i => new Option<int>(i, Ro.Day(i))).ToList();
        Slots = d.Settings.TimeSlots.OrderBy(s => s.Number).Select(s => new Option<int>(s.Number, $"{Ro.Roman(s.Number)} · {s.Label}")).ToList();

        Rooms.Add(new RoomOption { Model = null, Label = "— fără auditoriu —" });
        foreach (var r in d.Rooms.OrderBy(r => r.Name, StringComparer.Create(Ro.Culture, true)))
            Rooms.Add(new RoomOption { Model = r, Label = r.Name, Detail = $"{r.KindLabel} · {r.Capacity} locuri" });

        foreach (var g in TimetableBuilder.OrderedGroups(d))
        {
            Audiences.Add(new AudienceOption(OnAudienceChanged) { Code = g.Code, IsGroup = true, Detail = g.Students > 0 ? $"{g.Students} st." : "" });
            foreach (var sg in g.Subgroups)
                Audiences.Add(new AudienceOption(OnAudienceChanged) { Code = sg.Code, IsGroup = false, Detail = sg.Students > 0 ? $"{sg.Students} st." : "" });
        }

        Activities = new List<ActivityOption> { new() { Model = null, Label = "— lecție independentă (fără legătură) —" } };
    }

    public static LessonEditorViewModel ForExisting(Lesson lesson)
    {
        var vm = new LessonEditorViewModel(lesson);
        vm.AddActivities(Store.Data.Activities.Where(a => a.Id == lesson.ActivityId
                                                          || a.Groups.Any(c => lesson.Groups.Any(x => Audience.Overlaps(x, c)))));
        vm.LoadFrom(lesson);
        return vm;
    }

    public static LessonEditorViewModel ForNew(int day, int slot, ScheduleViewMode mode, object? entity)
    {
        var lesson = new Lesson { Day = day, Slot = slot, Week = WeekParity.Saptamanal, Type = LessonType.Curs };
        IEnumerable<TeachingActivity> relevant = Store.Data.Activities;
        switch (entity)
        {
            case StudentGroup g:
                lesson.Groups = new List<string> { g.Code };
                relevant = relevant.Where(a => Audience.Concerns(a.Groups, g.Code));
                break;
            case Teacher t:
                lesson.TeacherId = t.Id;
                relevant = relevant.Where(a => a.TeacherId == t.Id);
                break;
            case Room r:
                lesson.RoomId = r.Id;
                relevant = relevant.Where(a => r.Allows(a.Type));
                break;
        }
        var vm = new LessonEditorViewModel(null);
        vm.AddActivities(relevant.Where(a => Store.Lk.PlacedHoursOf(a.Id) < a.HoursPerWeek).Take(200));
        vm.LoadFrom(lesson);
        return vm;
    }

    private void AddActivities(IEnumerable<TeachingActivity> items)
    {
        var lk = Store.Lk;
        foreach (var a in items.OrderBy(a => lk.SubjectName(a.SubjectId), StringComparer.Create(Ro.Culture, true)).ThenBy(a => a.Type))
        {
            int placed = lk.PlacedHoursOf(a.Id);
            Activities.Add(new ActivityOption
            {
                Model = a,
                Label = $"{lk.SubjectName(a.SubjectId)} — {Ro.TypeLabel(a.Type).ToLowerInvariant()}",
                Detail = $"{Audience.Format(a.Groups)} · {lk.TeacherName(a.TeacherId)} · plasat {placed}/{a.HoursPerWeek} ore",
            });
        }
    }

    private void LoadFrom(Lesson l)
    {
        _loading = true;
        Subject = Subjects.FirstOrDefault(s => s.Id == l.SubjectId);
        Teacher = Teachers.FirstOrDefault(t => t.Model?.Id == l.TeacherId) ?? Teachers[0];
        Type = l.Type;
        Day = Days.FirstOrDefault(x => x.Value == l.Day) ?? Days.FirstOrDefault();
        Slot = Slots.FirstOrDefault(x => x.Value == l.Slot) ?? Slots.FirstOrDefault();
        Week = Weeks.First(x => x.Value == l.Week);
        Room = Rooms.FirstOrDefault(r => r.Model?.Id == l.RoomId) ?? Rooms[0];
        Note = l.Note;
        Locked = l.Locked;
        Activity = Activities.FirstOrDefault(a => a.Model?.Id == l.ActivityId) ?? Activities[0];
        foreach (var a in Audiences) a.IsSelected = l.Groups.Contains(a.Code, StringComparer.OrdinalIgnoreCase);
        _loading = false;
        Recompute();
    }

    partial void OnActivityChanged(ActivityOption? value)
    {
        if (_loading || value?.Model == null) return;
        var a = value.Model;
        _loading = true;
        Subject = Subjects.FirstOrDefault(s => s.Id == a.SubjectId) ?? Subject;
        Type = a.Type;
        Teacher = Teachers.FirstOrDefault(t => t.Model?.Id == a.TeacherId) ?? Teachers[0];
        foreach (var o in Audiences) o.IsSelected = a.Groups.Contains(o.Code, StringComparer.OrdinalIgnoreCase);
        int remaining = a.HoursPerWeek - Store.Lk.PlacedHoursOf(a.Id);
        if (IsNew) Week = Weeks.First(x => x.Value == (remaining == 1 ? WeekParity.Impara : WeekParity.Saptamanal));
        _loading = false;
        Recompute();
        if (IsNew && (Room == null || Room.Model == null || !Room.Free || !Room.Suitable)) AutoRoom();
    }

    partial void OnSubjectChanged(Subject? value) => Recompute();
    partial void OnTeacherChanged(TeacherOption? value) => Recompute();
    partial void OnTypeChanged(LessonType value) => Recompute();
    partial void OnDayChanged(Option<int>? value) => Recompute();
    partial void OnSlotChanged(Option<int>? value) => Recompute();
    partial void OnWeekChanged(Option<WeekParity>? value) => Recompute();
    partial void OnRoomChanged(RoomOption? value) => Recompute();

    partial void OnAudienceSearchChanged(string value)
    {
        foreach (var a in Audiences)
            a.IsVisible = string.IsNullOrWhiteSpace(value) || a.IsSelected || a.Code.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void OnAudienceChanged()
    {
        if (_loading) return;
        Recompute();
    }

    private Lesson BuildLesson() => new()
    {
        Id = _original?.Id ?? Guid.NewGuid(),
        ActivityId = Activity?.Model?.Id,
        SubjectId = Subject?.Id ?? Guid.Empty,
        TeacherId = Teacher?.Model?.Id,
        Type = Type,
        Groups = Audiences.Where(a => a.IsSelected).Select(a => a.Code).ToList(),
        RoomId = Room?.Model?.Id,
        Day = Day?.Value ?? 0,
        Slot = Slot?.Value ?? 1,
        Week = Week?.Value ?? WeekParity.Saptamanal,
        Note = Note.Trim(),
        Locked = Locked,
    };

    private void Recompute()
    {
        if (_loading) return;
        Error = null;
        var candidate = BuildLesson();
        GroupsSummary = candidate.Groups.Count == 0 ? "Nicio grupă selectată" : Audience.Format(candidate.Groups);
        int students = Store.Lk.Students(candidate.Groups);
        if (Activity?.Model is { Students: > 0 } act) students = act.Students;
        StudentsInfo = students > 0 ? $"{students} studenți" : "";

        // starea auditoriilor pentru intervalul ales
        var busy = Store.Data.Lessons
            .Where(o => o.Id != candidate.Id && o.Day == candidate.Day && o.Slot == candidate.Slot && o.RoomId != null
                        && ConflictService.WeeksOverlap(o.Week, candidate.Week))
            .GroupBy(o => o.RoomId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var r in Rooms)
        {
            if (r.Model == null) { r.Status = ""; r.Free = true; r.Suitable = true; continue; }
            bool suitable = r.Model.Allows(candidate.Type) && (students <= 0 || r.Model.Capacity >= students);
            r.Suitable = suitable;
            if (busy.TryGetValue(r.Model.Id, out var other))
            {
                r.Free = false;
                r.Status = $"ocupat: {Store.Lk.SubjectShort(other.SubjectId)} ({Audience.Format(other.Groups)})";
            }
            else
            {
                r.Free = true;
                r.Status = suitable ? "liber" : (!r.Model.Allows(candidate.Type) ? "liber · altă destinație" : "liber · prea mic");
            }
        }

        Issues.Clear();
        if (candidate.SubjectId == Guid.Empty || candidate.Groups.Count == 0) return;
        foreach (var issue in ConflictService.Evaluate(Store.Data, Store.Lk, candidate).Where(i => i.Severity != IssueSeverity.Info || i.Kind == IssueKind.NoRoom))
            Issues.Add(new IssueVM { Issue = issue });
    }

    [RelayCommand]
    private void AutoRoom()
    {
        var candidate = BuildLesson();
        var room = AutoScheduler.SuggestRoom(Store.Data, Store.Lk, candidate, Room?.Model?.Id);
        if (room != null) Room = Rooms.FirstOrDefault(r => r.Model?.Id == room.Id) ?? Room;
        else Error = "Nu există un auditoriu potrivit liber în acest interval.";
    }

    [RelayCommand]
    private void Save()
    {
        var candidate = BuildLesson();
        if (candidate.SubjectId == Guid.Empty) { Error = "Alegeți disciplina."; return; }
        if (candidate.Groups.Count == 0) { Error = "Selectați cel puțin o grupă sau subgrupă."; return; }
        var errors = Issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0 &&
            !Dialogs.Confirm("Lecția are conflicte",
                string.Join("\n", errors.Select(e => "• " + e.Message)) + "\n\nSalvați totuși lecția?", "Salvează oricum", "Înapoi"))
            return;

        Store.Mutate(IsNew ? $"Adăugare „{Subject?.Name}”" : $"Editare „{Subject?.Name}”", d =>
        {
            if (IsNew)
            {
                d.Lessons.Add(candidate);
            }
            else
            {
                var index = d.Lessons.IndexOf(d.Lessons.First(x => x.Id == candidate.Id));
                d.Lessons[index] = candidate;
            }
        });
        SavedLessonId = candidate.Id;
        AppServices.Toasts.Show(IsNew ? "Lecția a fost adăugată în orar." : "Modificările au fost salvate.", ToastKind.Success, "Anulează", () => Store.Undo(), 4);
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Delete()
    {
        if (_original == null) return;
        if (!Dialogs.Confirm("Ștergere lecție", $"Scoateți „{Store.Lk.SubjectName(_original.SubjectId)}” din orar?", "Șterge", danger: true))
            return;
        Store.Mutate("Ștergere lecție", d =>
        {
            var l = d.Lessons.FirstOrDefault(x => x.Id == _original.Id);
            if (l != null) d.Lessons.Remove(l);
        });
        AppServices.Toasts.Show("Lecția a fost ștearsă.", ToastKind.Info, "Anulează", () => Store.Undo());
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
