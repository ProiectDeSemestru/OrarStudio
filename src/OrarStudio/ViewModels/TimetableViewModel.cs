using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.Export;
using OrarStudio.UI;
using OrarStudio.Views;

namespace OrarStudio.ViewModels;

public enum DropState { None, Ok, Warn, Bad }

/// <summary>Țintă de navigare către editor (din panou, orarul general etc.).</summary>
public sealed record TimetableTarget(ScheduleViewMode Mode, string Key, Guid? LessonId = null);

public abstract class GridItemVM : ObservableObject
{
    public int Row { get; init; }
    public int Col { get; init; }
    public int RowSpan { get; init; } = 1;
    public int ColSpan { get; init; } = 1;
}

public sealed class CornerVM : GridItemVM { }

/// <summary>Antetul unei coloane în vizualizarea „Toate …": o grupă, un profesor sau un auditoriu.</summary>
public sealed class EntityHeaderVM : GridItemVM
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Tooltip { get; init; } = "";
}

/// <summary>Eticheta (verticală) a zilei în vizualizarea „Toate …".</summary>
public sealed class MatrixDayVM : GridItemVM
{
    public string Name { get; init; } = "";
    public bool IsToday { get; init; }
}

/// <summary>Eticheta perechii în vizualizarea „Toate …".</summary>
public sealed class MatrixSlotVM : GridItemVM
{
    public string Roman { get; init; } = "";
    public string Time { get; init; } = "";
    public bool LastOfDay { get; init; }
}

public sealed partial class DayHeaderVM : GridItemVM
{
    public int Day { get; init; }
    public string Name { get; init; } = "";
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private string _load = "";
}

public sealed partial class SlotHeaderVM : GridItemVM
{
    public int Slot { get; init; }
    public string Roman { get; init; } = "";
    public string Start { get; init; } = "";
    public string End { get; init; } = "";
    [ObservableProperty] private bool _isNow;
}

/// <summary>O „coloană" din celulă: o lecție săptămânală sau o pereche impar/par (fracție).</summary>
public sealed class CellColumnVM
{
    public LessonCardVM? Full { get; init; }
    public LessonCardVM? Odd { get; init; }
    public LessonCardVM? Even { get; init; }
    public bool IsSplit { get; init; }
}

public sealed partial class CellVM : GridItemVM
{
    public int Day { get; init; }
    public int Slot { get; init; }
    /// <summary>În vizualizarea „Toate …": grupa, profesorul sau auditoriul coloanei (altfel null).</summary>
    public object? Entity { get; init; }
    /// <summary>Celulă mică, din vizualizarea „Toate …".</summary>
    public bool Compact { get; init; }
    public ObservableCollection<CellColumnVM> Columns { get; } = new();
    [ObservableProperty] private DropState _dropState;
    [ObservableProperty] private bool _isDropTarget;
    [ObservableProperty] private bool _isToday;
    [ObservableProperty] private bool _isUnavailable;
    [ObservableProperty] private bool _hasLessons;
    [ObservableProperty] private int _columnCount = 1;

    public IEnumerable<LessonCardVM> Cards =>
        Columns.SelectMany(c => new[] { c.Full, c.Odd, c.Even }).Where(x => x != null)!;

    public void SetLessons(List<LessonCardVM> cards)
    {
        Columns.Clear();
        var ordered = cards.OrderBy(c => c.Badge, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Title).ToList();
        foreach (var w in ordered.Where(c => c.Week == WeekParity.Saptamanal))
            Columns.Add(new CellColumnVM { Full = w });
        var odd = ordered.Where(c => c.Week == WeekParity.Impara).ToList();
        var even = ordered.Where(c => c.Week == WeekParity.Para).ToList();
        for (int i = 0; i < Math.Max(odd.Count, even.Count); i++)
            Columns.Add(new CellColumnVM { Odd = i < odd.Count ? odd[i] : null, Even = i < even.Count ? even[i] : null, IsSplit = true });
        foreach (var c in Columns)
        {
            if (c.Full != null) c.Full.Compact = Compact || Columns.Count > 1;
            if (c.Odd != null) c.Odd.Compact = true;
            if (c.Even != null) c.Even.Compact = true;
        }
        ColumnCount = Math.Max(1, Columns.Count);
        HasLessons = cards.Count > 0;
    }
}

/// <summary>Cartonașul unei lecții în grila editorului.</summary>
public sealed partial class LessonCardVM : ObservableObject
{
    public LessonCardVM(Lesson model) => Model = model;

    public Lesson Model { get; }
    public Guid Id => Model.Id;
    public WeekParity Week => Model.Week;
    public LessonType Type => Model.Type;
    public bool Locked => Model.Locked;
    public string Title { get; init; } = "";
    public string ShortTitle { get; init; } = "";
    public string Line2 { get; init; } = "";
    public string Room { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public string Badge { get; init; } = "";
    public string WeekBadge => Week == WeekParity.Impara ? "impar" : Week == WeekParity.Para ? "par" : "";
    public string SubjectColor { get; init; } = "#6366F1";
    public string Tooltip { get; init; } = "";
    public IssueSeverity? Severity { get; init; }
    public bool HasError => Severity == IssueSeverity.Error;
    public bool HasWarning => Severity == IssueSeverity.Warning;
    public bool ColorBySubject { get; init; }
    /// <summary>Cartonaș mic (vizualizarea „Toate …"): titlu scurt + un rând, fără etichete.</summary>
    public bool Mini { get; init; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _compact;
}

/// <summary>Activitate din sarcina didactică afișată în panoul „De plasat".</summary>
public sealed class ActivityItemVM
{
    public TeachingActivity Model { get; init; } = null!;
    public string Title { get; init; } = "";
    public LessonType Type { get; init; }
    public string TypeLabel { get; init; } = "";
    public string Groups { get; init; } = "";
    public string Teacher { get; init; } = "";
    public int Hours { get; init; }
    public int Placed { get; init; }
    public double Progress => Hours <= 0 ? 0 : Math.Min(1.0, Placed / (double)Hours);
    public string ProgressText => $"{Placed}/{Hours} ore";
    public bool IsComplete => Placed >= Hours;
    public bool IsOver => Placed > Hours;
    public string Color { get; init; } = "#6366F1";
    public string Tooltip { get; init; } = "";
}

public sealed class IssueVM
{
    public ScheduleIssue Issue { get; init; } = null!;
    public string Message => Issue.Message;
    public bool IsError => Issue.Severity == IssueSeverity.Error;
    public string Where { get; init; } = "";
}

/// <summary>
/// Editorul de orar: grilă zile × perechi pentru o grupă / un profesor / un auditoriu, cu drag &amp; drop,
/// dublu-clic pentru editare, meniu contextual, verificarea conflictelor în timp real.
/// </summary>
public sealed partial class TimetableViewModel : PageViewModel
{
    private readonly Dictionary<(int Day, int Slot), CellVM> _cells = new();
    private readonly List<CellVM> _matrixCells = new();
    private string _structureKey = "";
    private string _matrixKey = "";
    private bool _suppress;
    private Guid? _selectedId;

    private const string AllKey = "*";
    private const int MatrixColWidth = 156, MatrixRowHeight = 72;

    public override string Title => "Editor orar";
    public override string Icon => "";

    public override string Subtitle => SelectedEntity == null
        ? "Selectați o grupă, un profesor sau un auditoriu"
        : SelectedEntity.IsAll
            ? $"{SelectedEntity.Label} · {Summary}"
            : $"{ModeNoun} {SelectedEntity.Label} · {Summary}";

    private string ModeNoun => Mode switch
    {
        ScheduleViewMode.Group => "Grupa",
        ScheduleViewMode.Teacher => "Profesorul",
        _ => "Auditoriul",
    };

    /// <summary>Este aleasă opțiunea „Toate grupele / Toți profesorii / Toate auditoriile".</summary>
    private bool IsAll => SelectedEntity?.IsAll == true;

    /// <summary>Celulele grilei afișate acum (o entitate sau vizualizarea „Toate …").</summary>
    private IEnumerable<CellVM> AllCells => IsMatrix ? _matrixCells : _cells.Values;

    /// <summary>Entitatea căreia îi aparține celula: coloana în „Toate …", altfel entitatea selectată.</summary>
    private object? EntityOf(CellVM cell) => cell.Entity ?? SelectedEntity?.Model;

    public IReadOnlyList<Option<ScheduleViewMode>> Modes { get; } = new[]
    {
        new Option<ScheduleViewMode>(ScheduleViewMode.Group, "Grupe", ""),
        new Option<ScheduleViewMode>(ScheduleViewMode.Teacher, "Profesori", ""),
        new Option<ScheduleViewMode>(ScheduleViewMode.Room, "Auditorii", ""),
    };

    [ObservableProperty] private ScheduleViewMode _mode = ScheduleViewMode.Group;
    public ObservableCollection<EntityOption> Entities { get; } = new();
    [ObservableProperty] private EntityOption? _selectedEntity;

    // vizualizarea „Toate …": rânduri = zi × pereche, coloane = grupe / profesori / auditorii
    [ObservableProperty] private bool _isMatrix;
    [ObservableProperty] private IReadOnlyList<GridItemVM> _matrixHeaderItems = Array.Empty<GridItemVM>();
    [ObservableProperty] private IReadOnlyList<GridItemVM> _matrixLeftItems = Array.Empty<GridItemVM>();
    [ObservableProperty] private IReadOnlyList<GridItemVM> _matrixCellItems = Array.Empty<GridItemVM>();
    [ObservableProperty] private string _matrixColumnSpec = "";
    [ObservableProperty] private string _matrixRowSpec = "";

    public ObservableCollection<GridItemVM> GridItems { get; } = new();
    [ObservableProperty] private string _columnSpec = "";
    [ObservableProperty] private string _rowSpec = "";
    [ObservableProperty] private double _gridMinWidth = 840;

    public ObservableCollection<ActivityItemVM> Pending { get; } = new();
    [ObservableProperty] private string _pendingSearch = "";
    [ObservableProperty] private bool _showCompleted;
    [ObservableProperty] private string _pendingSummary = "";

    public ObservableCollection<IssueVM> Issues { get; } = new();
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private LessonCardVM? _selectedLesson;
    [ObservableProperty] private bool _isDragging;
    [ObservableProperty] private string _dragHint = "";

    public bool ColorBySubject => AppServices.Settings.ColorBySubject;

    partial void OnModeChanged(ScheduleViewMode value)
    {
        if (_suppress) return;
        LoadEntities(null);
    }

    partial void OnSelectedEntityChanged(EntityOption? value)
    {
        if (_suppress) return;
        _selectedId = null;
        BuildContent();
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnPendingSearchChanged(string value) => BuildPending();
    partial void OnShowCompletedChanged(bool value) => BuildPending();

    public override void Refresh() => LoadEntities(SelectedEntity?.Key);

    protected override void OnNavigatedTo(object? parameter)
    {
        if (parameter is TimetableTarget t)
        {
            _suppress = true;
            Mode = t.Mode;
            _suppress = false;
            LoadEntities(t.Key);
            if (t.LessonId is Guid id) SelectLessonById(id);
        }
        else if (parameter is string s && s == "conflicts")
        {
            var first = Store.Report.Issues.FirstOrDefault(i => i.Severity == IssueSeverity.Error);
            if (first != null) FocusLesson(first.LessonId);
        }
    }

    // ───────────────────────── Încărcare ─────────────────────────

    private void LoadEntities(string? keepKey)
    {
        var d = Store.Data;
        _suppress = true;
        Entities.Clear();
        switch (Mode)
        {
            case ScheduleViewMode.Group:
                foreach (var g in TimetableBuilder.OrderedGroups(d))
                    Entities.Add(new EntityOption { Key = g.Code, Label = g.Code, Detail = $"{g.Specialty} · anul {g.Year}", Model = g });
                break;
            case ScheduleViewMode.Teacher:
                foreach (var t in d.Teachers.OrderBy(x => x.Name, StringComparer.Create(Ro.Culture, true)))
                    Entities.Add(new EntityOption { Key = t.Id.ToString(), Label = t.Name, Detail = t.Title, Model = t });
                break;
            default:
                foreach (var r in d.Rooms.OrderBy(x => x.Name, StringComparer.Create(Ro.Culture, true)))
                    Entities.Add(new EntityOption { Key = r.Id.ToString(), Label = r.Name, Detail = $"{r.KindLabel} · {r.Capacity} locuri", Model = r });
                break;
        }
        int count = Entities.Count;
        if (count > 0)
        {
            var (label, detail) = Mode switch
            {
                ScheduleViewMode.Group => ("Toate grupele", Ro.Count(count, "grupă", "grupe")),
                ScheduleViewMode.Teacher => ("Toți profesorii", Ro.Count(count, "profesor", "profesori")),
                _ => ("Toate auditoriile", Ro.Count(count, "auditoriu", "auditorii")),
            };
            Entities.Insert(0, new EntityOption { Key = AllKey, Label = label, Detail = detail, IsAll = true });
        }
        SelectedEntity = Entities.FirstOrDefault(e => e.Key == keepKey)
                         ?? Entities.FirstOrDefault(e => !e.IsAll)
                         ?? Entities.FirstOrDefault();
        _suppress = false;
        BuildContent();
        OnPropertyChanged(nameof(Subtitle));
    }

    private void BuildContent()
    {
        IsMatrix = IsAll;
        if (IsMatrix)
        {
            EnsureMatrix();
            FillMatrix();
        }
        else
        {
            EnsureStructure();
            FillCells();
        }
        BuildPending();
        BuildIssues();
    }

    /// <summary>Deschide o singură grupă / un profesor / un auditoriu (dublu-clic pe antetul coloanei).</summary>
    public void OpenEntity(string key)
    {
        var option = Entities.FirstOrDefault(e => e.Key == key);
        if (option != null) SelectedEntity = option;
    }

    private static bool Concerns(Lesson l, object? entity) => entity switch
    {
        StudentGroup g => Audience.Concerns(l.Groups, g.Code),
        Teacher t => l.TeacherId == t.Id,
        Room r => l.RoomId == r.Id,
        _ => false,
    };

    private void EnsureMatrix()
    {
        var d = Store.Data;
        int days = TimetableBuilder.Days(d);
        var slots = d.Settings.TimeSlots.OrderBy(x => x.Number).ToList();
        var columns = Entities.Where(e => !e.IsAll).ToList();
        var key = $"{Mode}|{days}|{string.Join(",", slots.Select(x => $"{x.Number}:{x.Start}-{x.End}"))}|" +
                  string.Join(",", columns.Select(c => $"{c.Key}={c.Label}={c.Detail}"));
        if (key == _matrixKey) return;
        _matrixKey = key;

        var headers = new List<GridItemVM>();
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            headers.Add(new EntityHeaderVM
            {
                Row = 0, Col = i, Key = c.Key, Label = c.Label, Detail = c.Detail,
                Tooltip = $"{c.Label} — {c.Detail}\nDublu-clic: deschide doar această coloană",
            });
        }

        int today = Ro.DayIndex(DateTime.Today);
        var left = new List<GridItemVM>();
        var cells = new List<GridItemVM>();
        _matrixCells.Clear();
        int row = 0;
        for (int day = 0; day < days; day++)
        {
            left.Add(new MatrixDayVM { Row = row, Col = 0, RowSpan = slots.Count, Name = Ro.Day(day).ToUpperInvariant(), IsToday = day == today });
            for (int si = 0; si < slots.Count; si++)
            {
                var slot = slots[si];
                left.Add(new MatrixSlotVM { Row = row, Col = 1, Roman = Ro.Roman(slot.Number), Time = slot.Label, LastOfDay = si == slots.Count - 1 });
                for (int c = 0; c < columns.Count; c++)
                {
                    var cell = new CellVM { Row = row, Col = c, Day = day, Slot = slot.Number, Entity = columns[c].Model, IsToday = day == today, Compact = true };
                    _matrixCells.Add(cell);
                    cells.Add(cell);
                }
                row++;
            }
        }

        MatrixColumnSpec = string.Join(" ", Enumerable.Repeat(MatrixColWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), columns.Count));
        MatrixRowSpec = string.Join(" ", Enumerable.Repeat(MatrixRowHeight.ToString(System.Globalization.CultureInfo.InvariantCulture), row));
        MatrixHeaderItems = headers;
        MatrixLeftItems = left;
        MatrixCellItems = cells;
    }

    private void FillMatrix()
    {
        var d = Store.Data;
        var byCell = d.Lessons.GroupBy(l => (l.Day, l.Slot)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var cell in _matrixCells)
        {
            var cards = byCell.TryGetValue((cell.Day, cell.Slot), out var here)
                ? here.Where(l => Concerns(l, cell.Entity)).Select(l => MakeCard(l, cell.Entity, mini: true)).ToList()
                : new List<LessonCardVM>();
            cell.SetLessons(cards);
            cell.IsUnavailable = cell.Entity is Teacher t && t.IsUnavailable(cell.Day, cell.Slot);
        }

        int hours = d.Lessons.Sum(l => l.Hours);
        int errors = Store.Report.ConflictingLessons;
        Summary = $"{Ro.Count(d.Lessons.Count, "lecție", "lecții")} · {hours} ore/săpt." +
                  (errors > 0 ? $" · {Ro.Count(errors, "lecție", "lecții")} în conflict" : " · fără conflicte");

        SelectedLesson = null;
        if (_selectedId is Guid id) SelectLessonById(id);
    }

    private void EnsureStructure()
    {
        var s = Store.Data.Settings;
        int days = TimetableBuilder.Days(Store.Data);
        var slots = s.TimeSlots.OrderBy(x => x.Number).ToList();
        var key = days + "|" + string.Join(",", slots.Select(x => $"{x.Number}:{x.Start}-{x.End}"));
        int today = Ro.DayIndex(DateTime.Today);
        if (key == _structureKey)
        {
            foreach (var h in GridItems.OfType<DayHeaderVM>()) h.IsToday = h.Day == today;
            return;
        }
        _structureKey = key;
        GridItems.Clear();
        _cells.Clear();
        ColumnSpec = "84 " + string.Join(" ", Enumerable.Repeat("*150", days));
        GridMinWidth = 84 + days * 150 + 4;
        RowSpec = "auto " + string.Join(" ", Enumerable.Repeat("*98", slots.Count));
        GridItems.Add(new CornerVM { Row = 0, Col = 0 });
        for (int day = 0; day < days; day++)
            GridItems.Add(new DayHeaderVM { Row = 0, Col = day + 1, Day = day, Name = Ro.Day(day), IsToday = day == today });
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            GridItems.Add(new SlotHeaderVM { Row = i + 1, Col = 0, Slot = slot.Number, Roman = Ro.Roman(slot.Number), Start = slot.Start, End = slot.End });
            for (int day = 0; day < days; day++)
            {
                var cell = new CellVM { Row = i + 1, Col = day + 1, Day = day, Slot = slot.Number, IsToday = day == today };
                _cells[(day, slot.Number)] = cell;
                GridItems.Add(cell);
            }
        }
    }

    private List<Lesson> EntityLessons()
    {
        var d = Store.Data;
        if (IsAll) return d.Lessons.ToList();
        return SelectedEntity?.Model switch
        {
            StudentGroup g => d.Lessons.Where(l => Audience.Concerns(l.Groups, g.Code)).ToList(),
            Teacher t => d.Lessons.Where(l => l.TeacherId == t.Id).ToList(),
            Room r => d.Lessons.Where(l => l.RoomId == r.Id).ToList(),
            _ => new List<Lesson>(),
        };
    }

    private void FillCells()
    {
        var lessons = EntityLessons();
        var teacher = SelectedEntity?.Model as Teacher;
        foreach (var cell in _cells.Values)
        {
            var cards = lessons.Where(l => l.Day == cell.Day && l.Slot == cell.Slot).Select(l => MakeCard(l)).ToList();
            cell.SetLessons(cards);
            cell.IsUnavailable = teacher?.IsUnavailable(cell.Day, cell.Slot) == true;
        }

        foreach (var h in GridItems.OfType<DayHeaderVM>())
        {
            int count = lessons.Count(l => l.Day == h.Day);
            h.Load = count == 0 ? "liber" : Ro.Count(count, "lecție", "lecții");
        }

        int hours = lessons.Sum(l => l.Hours);
        int errors = lessons.Count(l => Store.Report.SeverityOf(l.Id) == IssueSeverity.Error);
        Summary = $"{Ro.Count(lessons.Count, "lecție", "lecții")} · {hours} ore/săpt." +
                  (errors > 0 ? $" · {Ro.Count(errors, "conflict", "conflicte")}" : " · fără conflicte");

        SelectedLesson = null;
        if (_selectedId is Guid id) SelectLessonById(id);
    }

    /// <param name="entity">Entitatea în contextul căreia se afișează lecția (implicit: cea selectată).</param>
    /// <param name="mini">Cartonaș mic pentru vizualizarea „Toate …".</param>
    private LessonCardVM MakeCard(Lesson l, object? entity = null, bool mini = false)
    {
        var lk = Store.Lk;
        var subject = lk.Subject(l.SubjectId);
        var teacher = lk.Teacher(l.TeacherId);
        var room = lk.Room(l.RoomId);
        entity ??= SelectedEntity?.Model;
        string line2, badge = "";
        switch (entity)
        {
            case StudentGroup g:
                line2 = teacher?.Name ?? "fără profesor";
                var own = l.Groups.Where(c => string.Equals(Audience.BaseCode(c), g.Code, StringComparison.OrdinalIgnoreCase)).ToList();
                if (own.Count > 0 && own.All(Audience.IsSubgroup))
                    badge = string.Join("+", own.Select(c => "sg." + Audience.SubgroupSuffix(c)));
                else if (l.Groups.Select(Audience.BaseCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    badge = "serie";
                break;
            case Teacher:
                line2 = Audience.Format(l.Groups);
                break;
            default:
                line2 = $"{Audience.Format(l.Groups)} · {teacher?.Name ?? "—"}";
                break;
        }

        var issues = Store.Report.ByLesson[l.Id].Where(i => i.Severity != IssueSeverity.Info).ToList();
        var slot = Store.Data.Settings.Slot(l.Slot);
        var tip = new StringBuilder();
        tip.Append(subject?.Name ?? "?").Append(" — ").AppendLine(Ro.TypeLabel(l.Type));
        tip.Append("Profesor: ").AppendLine(teacher?.FullLabel ?? "—");
        int students = lk.StudentsOf(l);
        tip.Append("Grupe: ").Append(Audience.Format(l.Groups)).AppendLine(students > 0 ? $" ({students} studenți)" : "");
        tip.Append("Auditoriu: ").AppendLine(room != null ? $"{room.Name} ({room.KindLabel.ToLowerInvariant()}, {room.Capacity} locuri)" : "neatribuit");
        tip.Append(Ro.Day(l.Day)).Append(", perechea ").Append(Ro.Roman(l.Slot));
        if (slot != null) tip.Append($" ({slot.Label})");
        tip.Append(" · ").AppendLine(Ro.WeekLabel(l.Week).ToLowerInvariant());
        if (!string.IsNullOrWhiteSpace(l.Note)) tip.Append("Notă: ").AppendLine(l.Note);
        if (l.Locked) tip.AppendLine("🔒 Blocată (nu este modificată de generatorul automat)");
        foreach (var i in issues) tip.Append(i.Severity == IssueSeverity.Error ? "⛔ " : "⚠ ").AppendLine(i.Message);
        tip.Append("Dublu-clic: editare · trageți pentru a muta (Ctrl = copiere) · clic dreapta: meniu");

        if (mini)
        {
            var parts = entity switch
            {
                StudentGroup => new[] { room?.Name ?? "fără sală", badge, teacher?.Name ?? "" },
                Teacher => new[] { room?.Name ?? "fără sală", Audience.Format(l.Groups) },
                _ => new[] { Audience.Format(l.Groups), teacher?.Name ?? "" },
            };
            line2 = string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        return new LessonCardVM(l)
        {
            Title = mini ? $"{subject?.Short ?? "?"} ({Ro.TypeShort(l.Type)})" : subject?.Name ?? "(disciplină ștearsă)",
            Mini = mini,
            ShortTitle = subject?.Short ?? "?",
            Line2 = line2,
            Room = room?.Name ?? "—",
            TypeLabel = Ro.TypeLabel(l.Type),
            Badge = badge,
            SubjectColor = subject?.Color ?? "#6366F1",
            Tooltip = tip.ToString(),
            Severity = issues.Count == 0 ? null : issues.Max(i => i.Severity),
            ColorBySubject = ColorBySubject,
        };
    }

    private void BuildPending()
    {
        Pending.Clear();
        var d = Store.Data;
        var lk = Store.Lk;
        IEnumerable<TeachingActivity> q = IsAll ? d.Activities : SelectedEntity?.Model switch
        {
            StudentGroup g => d.Activities.Where(a => Audience.Concerns(a.Groups, g.Code)),
            Teacher t => d.Activities.Where(a => a.TeacherId == t.Id),
            Room r => d.Activities.Where(a => r.Allows(a.Type)),
            _ => Enumerable.Empty<TeachingActivity>(),
        };
        var items = q.Select(a =>
        {
            var subject = lk.Subject(a.SubjectId);
            int placed = lk.PlacedHoursOf(a.Id);
            return new ActivityItemVM
            {
                Model = a,
                Title = subject?.Name ?? "?",
                Type = a.Type,
                TypeLabel = Ro.TypeLabel(a.Type),
                Groups = Audience.Format(a.Groups),
                Teacher = lk.TeacherName(a.TeacherId),
                Hours = a.HoursPerWeek,
                Placed = placed,
                Color = subject?.Color ?? "#6366F1",
                Tooltip = $"{subject?.Name} — {Ro.TypeLabel(a.Type)}, {a.HoursPerWeek} ore/săptămână\n" +
                          $"Grupe: {Audience.Format(a.Groups)}{(a.Students > 0 ? $" ({a.Students} studenți)" : "")}\n" +
                          $"Profesor: {lk.Teacher(a.TeacherId)?.FullLabel ?? "—"}\nPlasat: {placed} din {a.HoursPerWeek} ore\n" +
                          "Trageți în grilă pentru a plasa · dublu-clic: plasare automată",
            };
        }).ToList();

        int toPlace = items.Where(i => !i.IsComplete).Sum(i => i.Hours - i.Placed);
        int incomplete = items.Count(i => !i.IsComplete);
        PendingSummary = incomplete == 0
            ? "Toată sarcina este plasată ✓"
            : $"{Ro.Count(incomplete, "activitate", "activități")} · {toPlace} ore de plasat";

        foreach (var i in items
                     .Where(i => ShowCompleted || !i.IsComplete)
                     .Where(i => Ro.Matches(i.Title, PendingSearch) || Ro.Matches(i.Groups, PendingSearch) || Ro.Matches(i.Teacher, PendingSearch))
                     .OrderBy(i => i.IsComplete)
                     .ThenBy(i => i.Title, StringComparer.Create(Ro.Culture, true))
                     .ThenBy(i => i.Type)
                     .Take(300))
            Pending.Add(i);
    }

    private void BuildIssues()
    {
        Issues.Clear();
        var ids = EntityLessons().Select(l => l.Id).ToHashSet();
        foreach (var issue in Store.Report.Issues.Where(i => i.Severity != IssueSeverity.Info && ids.Contains(i.LessonId)).Take(50))
        {
            var l = Store.Data.Lessons.FirstOrDefault(x => x.Id == issue.LessonId);
            Issues.Add(new IssueVM
            {
                Issue = issue,
                Where = l == null ? "" : $"{Ro.Day(l.Day)}, perechea {Ro.Roman(l.Slot)} · {Store.Lk.SubjectShort(l.SubjectId)}",
            });
        }
    }

    // ───────────────────────── Selecție ─────────────────────────

    public void Select(LessonCardVM? card)
    {
        // o lecție pe serie apare în mai multe coloane (Toate grupele) — se evidențiază peste tot
        foreach (var c in AllCells.SelectMany(x => x.Cards)) c.IsSelected = card != null && c.Id == card.Id;
        SelectedLesson = card;
        _selectedId = card?.Id;
    }

    private void SelectLessonById(Guid id)
    {
        var card = AllCells.SelectMany(c => c.Cards).FirstOrDefault(c => c.Id == id);
        Select(card);
    }

    /// <summary>Deschide perspectiva potrivită și selectează lecția (ex. dintr-o problemă din listă).</summary>
    public void FocusLesson(Guid lessonId)
    {
        var l = Store.Data.Lessons.FirstOrDefault(x => x.Id == lessonId);
        if (l == null) return;
        bool visible = EntityLessons().Any(x => x.Id == lessonId);
        if (!visible && l.Groups.Count > 0)
        {
            _suppress = true;
            Mode = ScheduleViewMode.Group;
            _suppress = false;
            LoadEntities(Audience.BaseCode(l.Groups[0]));
        }
        SelectLessonById(lessonId);
    }

    [RelayCommand]
    private void IssueClick(IssueVM? issue)
    {
        if (issue != null) FocusLesson(issue.Issue.LessonId);
    }

    [RelayCommand]
    private void PrevEntity() => StepEntity(-1);

    [RelayCommand]
    private void NextEntity() => StepEntity(1);

    private void StepEntity(int delta)
    {
        if (Entities.Count == 0) return;
        int i = SelectedEntity == null ? 0 : Entities.IndexOf(SelectedEntity);
        SelectedEntity = Entities[(i + delta + Entities.Count) % Entities.Count];
    }

    // ───────────────────────── Drag & drop ─────────────────────────

    private (Guid? Teacher, List<string> Groups, LessonType Type, HashSet<Guid> Ignore, WeekParity Week, Guid? PreferredRoom, Guid? ActivityId) Describe(object payload)
    {
        switch (payload)
        {
            case ActivityItemVM a:
            {
                int remaining = a.Hours - a.Placed;
                return (a.Model.TeacherId, a.Model.Groups, a.Model.Type, new HashSet<Guid>(),
                    remaining == 1 ? WeekParity.Impara : WeekParity.Saptamanal, PreferredRoomFor(a.Model), a.Model.Id);
            }
            case LessonCardVM c:
                return (c.Model.TeacherId, c.Model.Groups, c.Model.Type, new HashSet<Guid> { c.Id }, c.Model.Week, c.Model.RoomId, c.Model.ActivityId);
            default:
                return (null, new List<string>(), LessonType.Curs, new HashSet<Guid>(), WeekParity.Saptamanal, null, null);
        }
    }

    /// <summary>
    /// În vizualizarea „Toate …" o lecție rămâne în coloana ei: la grupe — una dintre grupele lecției,
    /// la profesori — profesorul lecției; la auditorii se poate alege orice coloană (lecția primește acea sală).
    /// </summary>
    private static bool FitsColumn(Guid? teacher, List<string> groups, CellVM cell) => cell.Entity switch
    {
        StudentGroup g => Audience.Concerns(groups, g.Code),
        Teacher t => teacher == t.Id,
        _ => true,
    };

    public void BeginDrag(object payload)
    {
        IsDragging = true;
        var info = Describe(payload);
        var d = Store.Data;
        var quick = new Dictionary<(int Day, int Slot), IssueSeverity?>();
        foreach (var cell in AllCells)
        {
            if (!FitsColumn(info.Teacher, info.Groups, cell))
            {
                cell.DropState = DropState.Bad;
                continue;
            }
            if (!quick.TryGetValue((cell.Day, cell.Slot), out var sev))
                quick[(cell.Day, cell.Slot)] = sev = ConflictService.QuickCheck(d, Store.Lk, info.Teacher, info.Groups, cell.Day, cell.Slot, info.Week, info.Ignore);
            var room = EntityOf(cell) as Room;
            DropState state;
            if (sev == IssueSeverity.Error) state = DropState.Bad;
            else if (room != null)
            {
                bool busy = d.Lessons.Any(o => !info.Ignore.Contains(o.Id) && o.RoomId == room.Id && o.Day == cell.Day && o.Slot == cell.Slot
                                               && ConflictService.WeeksOverlap(o.Week, info.Week));
                state = busy ? DropState.Bad : (!room.Allows(info.Type) || sev == IssueSeverity.Warning ? DropState.Warn : DropState.Ok);
            }
            else
            {
                var probe = new Lesson
                {
                    Id = info.Ignore.FirstOrDefault(), ActivityId = info.ActivityId, TeacherId = info.Teacher,
                    Groups = info.Groups, Type = info.Type, Day = cell.Day, Slot = cell.Slot, Week = info.Week,
                };
                var suggested = AutoScheduler.SuggestRoom(d, Store.Lk, probe, info.PreferredRoom, info.Ignore);
                state = suggested == null || sev == IssueSeverity.Warning ? DropState.Warn : DropState.Ok;
            }
            cell.DropState = state;
        }
        DragHint = IsMatrix
            ? Mode switch
            {
                ScheduleViewMode.Group => "Mutați în coloana uneia dintre grupele lecției · verde = liber · galben = fără sală potrivită / doleanță · roșu = ocupat sau altă grupă.",
                ScheduleViewMode.Teacher => "Mutați în coloana aceluiași profesor · verde = liber · galben = fără sală potrivită / doleanță · roșu = ocupat sau alt profesor.",
                _ => "Eliberați în coloana auditoriului dorit — lecția primește acea sală · verde = liber · galben = sală nepotrivită / doleanță · roșu = ocupat.",
            }
            : payload is LessonCardVM
                ? "Verde = liber · galben = fără sală potrivită / doleanță · roșu = conflict. Ctrl = copiere. Trageți în lista din stânga pentru a scoate din orar."
                : "Verde = liber · galben = fără sală potrivită / doleanță · roșu = profesorul sau grupa sunt ocupați.";
    }

    public void EndDrag()
    {
        IsDragging = false;
        DragHint = "";
        foreach (var cell in AllCells)
        {
            cell.DropState = DropState.None;
            cell.IsDropTarget = false;
        }
    }

    public void HoverCell(CellVM? target)
    {
        foreach (var cell in AllCells) cell.IsDropTarget = cell == target;
    }

    public void Drop(object payload, CellVM cell, bool copy)
    {
        var info = Describe(payload);
        if (!FitsColumn(info.Teacher, info.Groups, cell))
        {
            Toasts.Show(Mode == ScheduleViewMode.Teacher
                    ? "Lecția se poate muta doar în coloana profesorului ei."
                    : "Lecția se poate muta doar în coloanele grupelor ei.",
                ToastKind.Warning, seconds: 4);
            return;
        }
        var room = cell.Entity as Room;
        switch (payload)
        {
            case ActivityItemVM a:
                PlaceActivity(a.Model, cell.Day, cell.Slot, room);
                break;
            case LessonCardVM c when copy:
                CopyLesson(c.Model, cell.Day, cell.Slot, room);
                break;
            case LessonCardVM c:
                MoveLesson(c.Model.Id, cell.Day, cell.Slot, room);
                break;
        }
    }

    public void DropOnPending(object payload)
    {
        if (payload is LessonCardVM c) DeleteLesson(c, confirm: false);
    }

    private Guid? PreferredRoomFor(TeachingActivity a) =>
        Store.Data.Lessons.Where(l => l.ActivityId == a.Id && l.RoomId != null).Select(l => l.RoomId).FirstOrDefault();

    /// <param name="targetRoom">Sala impusă (coloana din „Toate auditoriile"); altfel se alege automat.</param>
    public void PlaceActivity(TeachingActivity a, int day, int slot, Room? targetRoom = null)
    {
        var lk = Store.Lk;
        int remaining = a.HoursPerWeek - lk.PlacedHoursOf(a.Id);
        var week = WeekParity.Saptamanal;
        if (remaining == 1)
        {
            bool oddTaken = Store.Data.Lessons.Any(l => l.Day == day && l.Slot == slot && l.Week == WeekParity.Impara
                                                        && Audience.FirstOverlap(a.Groups, l.Groups) != null);
            week = oddTaken ? WeekParity.Para : WeekParity.Impara;
        }
        var lesson = new Lesson
        {
            ActivityId = a.Id, SubjectId = a.SubjectId, TeacherId = a.TeacherId, Type = a.Type,
            Groups = a.Groups.ToList(), Day = day, Slot = slot, Week = week,
        };
        var fixedRoom = targetRoom ?? (SelectedEntity?.Model as Room);
        lesson.RoomId = fixedRoom != null
            ? fixedRoom.Id
            : AutoScheduler.SuggestRoom(Store.Data, lk, lesson, PreferredRoomFor(a))?.Id;

        var subject = lk.SubjectName(a.SubjectId);
        _selectedId = lesson.Id;
        Store.Mutate($"Plasare „{subject}”", d => d.Lessons.Add(lesson));

        var where = $"{Ro.Day(day)}, perechea {Ro.Roman(slot)}" + (week != WeekParity.Saptamanal ? $" ({Ro.WeekLabel(week).ToLowerInvariant()})" : "");
        var errors = Store.Report.ByLesson[lesson.Id].Where(i => i.Severity == IssueSeverity.Error).ToList();
        if (errors.Count > 0)
            Toasts.Show($"„{subject}” plasată cu conflict: {errors[0].Message}", ToastKind.Warning, "Anulează", () => Store.Undo());
        else if (remaining <= 0)
            Toasts.Show($"„{subject}” era deja plasată integral — s-a adăugat o lecție suplimentară ({where}).", ToastKind.Warning, "Anulează", () => Store.Undo());
        else
            Toasts.Show($"„{subject}” → {where}, aud. {lk.RoomName(lesson.RoomId)}", ToastKind.Success, "Anulează", () => Store.Undo(), 4);
    }

    /// <param name="targetRoom">Sala impusă (coloana din „Toate auditoriile"); altfel se păstrează sau se alege alta liberă.</param>
    public void MoveLesson(Guid lessonId, int day, int slot, Room? targetRoom = null)
    {
        var lesson = Store.Data.Lessons.FirstOrDefault(l => l.Id == lessonId);
        if (lesson == null) return;
        bool sameTime = lesson.Day == day && lesson.Slot == slot;
        if (sameTime && (targetRoom == null || lesson.RoomId == targetRoom.Id)) return;
        var fixedRoom = targetRoom ?? (SelectedEntity?.Model as Room);
        string? roomChange = null;
        _selectedId = lessonId;
        Store.Mutate($"Mutare „{Store.Lk.SubjectShort(lesson.SubjectId)}”", d =>
        {
            var l = d.Lessons.First(x => x.Id == lessonId);
            l.Day = day;
            l.Slot = slot;
            if (fixedRoom != null)
            {
                l.RoomId = fixedRoom.Id;
            }
            else if (l.RoomId != null)
            {
                bool busy = d.Lessons.Any(o => o.Id != l.Id && o.RoomId == l.RoomId && o.Day == day && o.Slot == slot
                                               && ConflictService.WeeksOverlap(o.Week, l.Week));
                if (busy)
                {
                    var old = Store.Lk.RoomName(l.RoomId);
                    var suggestion = AutoScheduler.SuggestRoom(d, Store.Lk, l);
                    if (suggestion != null)
                    {
                        l.RoomId = suggestion.Id;
                        roomChange = $"auditoriul {old} era ocupat — s-a ales {suggestion.Name}";
                    }
                }
            }
            else
            {
                l.RoomId = AutoScheduler.SuggestRoom(d, Store.Lk, l)?.Id;
            }
        });
        var errors = Store.Report.ByLesson[lessonId].Where(i => i.Severity == IssueSeverity.Error).ToList();
        var msg = (sameTime ? "Mutată" : $"Mutată în {Ro.Day(day).ToLowerInvariant()}, perechea {Ro.Roman(slot)}") +
                  (targetRoom != null ? (sameTime ? $" în auditoriul {targetRoom.Name}" : $", auditoriul {targetRoom.Name}") : "") +
                  (roomChange != null ? $" ({roomChange})" : "");
        if (errors.Count > 0) Toasts.Show(msg + ". Atenție: " + errors[0].Message, ToastKind.Warning, "Anulează", () => Store.Undo());
        else Toasts.Show(msg, ToastKind.Success, "Anulează", () => Store.Undo(), 3);
    }

    public void CopyLesson(Lesson source, int day, int slot, Room? targetRoom = null)
    {
        var copy = Json.Clone(source);
        copy.Id = Guid.NewGuid();
        copy.Day = day;
        copy.Slot = slot;
        copy.Locked = false;
        var fixedRoom = targetRoom ?? (SelectedEntity?.Model as Room);
        if (fixedRoom != null) copy.RoomId = fixedRoom.Id;
        else copy.RoomId = AutoScheduler.SuggestRoom(Store.Data, Store.Lk, copy, source.RoomId)?.Id;
        _selectedId = copy.Id;
        Store.Mutate($"Copiere „{Store.Lk.SubjectShort(source.SubjectId)}”", d => d.Lessons.Add(copy));
        Toasts.Show($"Lecția a fost copiată în {Ro.Day(day).ToLowerInvariant()}, perechea {Ro.Roman(slot)}.", ToastKind.Success, "Anulează", () => Store.Undo(), 3);
    }

    /// <summary>Mutare cu tastatura (Alt + săgeți).</summary>
    public void MoveSelectedBy(int dDay, int dSlot)
    {
        if (SelectedLesson == null) return;
        var l = SelectedLesson.Model;
        int days = TimetableBuilder.Days(Store.Data);
        var slots = Store.Data.Settings.TimeSlots.Select(s => s.Number).OrderBy(x => x).ToList();
        int day = Math.Clamp(l.Day + dDay, 0, days - 1);
        int idx = Math.Clamp(slots.IndexOf(l.Slot) + dSlot, 0, slots.Count - 1);
        MoveLesson(l.Id, day, slots[idx]);
    }

    // ───────────────────────── Acțiuni asupra lecțiilor ─────────────────────────

    public void EditLesson(LessonCardVM card)
    {
        Select(card);
        var vm = LessonEditorViewModel.ForExisting(card.Model);
        Dialogs.ShowModal(new LessonEditorWindow { DataContext = vm });
        if (vm.SavedLessonId is Guid id) _selectedId = id;
    }

    public void NewLessonAt(CellVM cell)
    {
        var vm = LessonEditorViewModel.ForNew(cell.Day, cell.Slot, Mode, EntityOf(cell));
        Dialogs.ShowModal(new LessonEditorWindow { DataContext = vm });
        if (vm.SavedLessonId is Guid id)
        {
            _selectedId = id;
            SelectLessonById(id);
        }
    }

    public void DeleteLesson(LessonCardVM card, bool confirm = true)
    {
        var name = card.Title;
        if (confirm && card.Locked &&
            !Dialogs.Confirm("Ștergere lecție blocată", $"Lecția „{name}” este blocată. Sigur doriți să o ștergeți?", "Șterge", danger: true))
            return;
        Store.Mutate($"Ștergere „{card.ShortTitle}”", d =>
        {
            var l = d.Lessons.FirstOrDefault(x => x.Id == card.Id);
            if (l != null) d.Lessons.Remove(l);
        });
        Toasts.Show($"Lecția „{name}” a fost scoasă din orar.", ToastKind.Info, "Anulează", () => Store.Undo());
    }

    public void SetWeek(LessonCardVM card, WeekParity week)
    {
        if (card.Week == week) return;
        Store.Mutate("Schimbare periodicitate", d =>
        {
            var l = d.Lessons.First(x => x.Id == card.Id);
            l.Week = week;
        });
    }

    public void SetRoom(LessonCardVM card, Guid? roomId)
    {
        Store.Mutate("Schimbare auditoriu", d =>
        {
            var l = d.Lessons.First(x => x.Id == card.Id);
            l.RoomId = roomId;
        });
        Toasts.Show($"Auditoriu schimbat: {Store.Lk.RoomName(roomId)}", ToastKind.Success, seconds: 3);
    }

    public void ToggleLock(LessonCardVM card)
    {
        Store.Mutate(card.Locked ? "Deblocare lecție" : "Blocare lecție", d =>
        {
            var l = d.Lessons.First(x => x.Id == card.Id);
            l.Locked = !l.Locked;
        });
    }

    public void ToggleUnavailable(CellVM cell)
    {
        if (EntityOf(cell) is not Teacher t) return;
        var key = new SlotKey(cell.Day, cell.Slot);
        bool now = t.Unavailable.Contains(key);
        Store.Mutate(now ? "Eliminare doleanță" : "Adăugare doleanță", d =>
        {
            var teacher = d.Teachers.First(x => x.Id == t.Id);
            if (now) teacher.Unavailable.Remove(key);
            else teacher.Unavailable.Add(key);
        });
    }

    /// <summary>Lista auditoriilor potrivite pentru meniul „Schimbă auditoriul".</summary>
    public List<(Room Room, bool Free, bool Suitable)> RoomChoices(LessonCardVM card)
    {
        var l = card.Model;
        int students = Store.Lk.StudentsOf(l);
        var busy = Store.Data.Lessons.Where(o => o.Id != l.Id && o.Day == l.Day && o.Slot == l.Slot && o.RoomId != null
                                                 && ConflictService.WeeksOverlap(o.Week, l.Week))
                                     .Select(o => o.RoomId!.Value).ToHashSet();
        return Store.Data.Rooms
            .Select(r => (Room: r, Free: !busy.Contains(r.Id), Suitable: r.Allows(l.Type) && (students <= 0 || r.Capacity >= students)))
            .OrderByDescending(x => x.Free && x.Suitable)
            .ThenBy(x => x.Room.Capacity)
            .ToList();
    }

    public void OpenRelated(ScheduleViewMode mode, string key) =>
        AppServices.Main?.Navigate(this, new TimetableTarget(mode, key, SelectedLesson?.Id));

    public async void AutoPlace(ActivityItemVM item)
    {
        if (AppServices.Main == null) return;
        await AppServices.Main.RunAutoSchedule(new AutoScheduleOptions { OnlyActivities = new HashSet<Guid> { item.Model.Id } }, ask: false);
    }

    [RelayCommand]
    private async Task AutoPlaceEntity()
    {
        if (AppServices.Main == null || SelectedEntity == null) return;
        var ids = Pending.Where(p => !p.IsComplete).Select(p => p.Model.Id).ToHashSet();
        if (ids.Count == 0)
        {
            Toasts.Show("Nu există activități nealocate pentru această selecție.", ToastKind.Info);
            return;
        }
        await AppServices.Main.RunAutoSchedule(new AutoScheduleOptions { OnlyActivities = ids }, ask: false);
    }

    [RelayCommand]
    private void ClearEntity()
    {
        var lessons = EntityLessons().Where(l => !l.Locked).ToList();
        if (lessons.Count == 0 || SelectedEntity == null) return;
        if (!Dialogs.Confirm("Golire orar", $"Se vor scoate din orar {Ro.Count(lessons.Count, "lecție", "lecții")} ({SelectedEntity.Label}). Lecțiile blocate rămân. Continuați?", "Golește", danger: true))
            return;
        var ids = lessons.Select(l => l.Id).ToHashSet();
        Store.Mutate($"Golire orar {SelectedEntity.Label}", d =>
        {
            foreach (var l in d.Lessons.Where(x => ids.Contains(x.Id)).ToList()) d.Lessons.Remove(l);
        });
        Toasts.Show("Orarul a fost golit.", ToastKind.Info, "Anulează", () => Store.Undo());
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        if (SelectedEntity == null) return;
        var d = Store.Data;
        var lk = Store.Lk;
        EntityTimetable Table(object? model) => model switch
        {
            StudentGroup g => TimetableBuilder.ForGroup(d, lk, g),
            Teacher t => TimetableBuilder.ForTeacher(d, lk, t),
            Room r => TimetableBuilder.ForRoom(d, lk, r),
            _ => throw new InvalidOperationException(),
        };
        // „Toate …" → un singur PDF cu orarul fiecărei grupe / fiecărui profesor / auditoriu
        var tables = IsAll
            ? Entities.Where(e => !e.IsAll).Select(e => Table(e.Model)).ToList()
            : new List<EntityTimetable> { Table(SelectedEntity.Model) };
        if (tables.Count == 0) return;
        var title = IsAll ? SelectedEntity.Label : tables[0].Title;
        var fileName = IsAll ? $"Orar - {SelectedEntity.Label}.pdf" : $"Orar {TimetableBuilder.SafeFileName(tables[0].FileKey)}.pdf";
        var path = Dialogs.SaveFile("Document PDF (*.pdf)|*.pdf", fileName, "Exportă orarul în PDF");
        if (path == null) return;
        var copy = Json.Clone(d);
        await AppServices.Main!.RunBusy("Se generează PDF…", () => Task.Run(() => File.WriteAllBytes(path, PdfExporter.Timetables(copy, tables, title))));
        Toasts.Show($"PDF salvat: {Path.GetFileName(path)}", ToastKind.Success, "Deschide", () => Dialogs.OpenPath(path));
    }

    public void EditActivity(ActivityItemVM item)
    {
        var main = AppServices.Main;
        main?.Navigate(main.Activities, item.Model.Id);
    }
}
