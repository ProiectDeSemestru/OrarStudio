using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OrarStudio.Models;

public interface IEntity
{
    Guid Id { get; set; }
}

/// <summary>Un interval (zi + pereche) din grila săptămânală. Day: 0 = luni.</summary>
public readonly record struct SlotKey(int Day, int Slot);

/// <summary>Auditoriu / sală: unde se țin cursurile, seminarele și laboratoarele.</summary>
public partial class Room : ObservableObject, IEntity
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _building = "";
    [ObservableProperty] private int _floor;
    [ObservableProperty] private int _capacity = 30;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(KindLabel))] private RoomKind _kind = RoomKind.SalaSeminar;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UsageLabel))] private bool _allowsCurs = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UsageLabel))] private bool _allowsSeminar = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UsageLabel))] private bool _allowsLaborator;
    [ObservableProperty] private bool _hasProjector;
    [ObservableProperty] private bool _hasWhiteboard = true;
    [ObservableProperty] private int _computers;
    [ObservableProperty] private string _notes = "";

    // statistici calculate la afișare (nu se salvează)
    [ObservableProperty] [property: JsonIgnore] private double _occupancy;
    [ObservableProperty] [property: JsonIgnore] private int _lessonCount;

    [JsonIgnore] public string KindLabel => Ro.KindLabel(Kind);

    [JsonIgnore]
    public string UsageLabel
    {
        get
        {
            var parts = new List<string>();
            if (AllowsCurs) parts.Add("Curs");
            if (AllowsSeminar) parts.Add("Seminar");
            if (AllowsLaborator) parts.Add("Laborator");
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }

    public bool Allows(LessonType type) => type switch
    {
        LessonType.Curs => AllowsCurs,
        LessonType.Seminar => AllowsSeminar,
        _ => AllowsLaborator,
    };

    /// <summary>Utilizarea implicită pentru o categorie de sală.</summary>
    public static (bool Curs, bool Seminar, bool Laborator) DefaultUsage(RoomKind kind) => kind switch
    {
        RoomKind.Amfiteatru => (true, true, false),
        RoomKind.SalaSeminar => (true, true, false),
        RoomKind.LaboratorInformatica => (false, true, true),
        RoomKind.LaboratorSpecializat => (false, false, true),
        _ => (true, true, false),
    };

    public override string ToString() => Name;
}

/// <summary>Disciplină (obiect de studiu).</summary>
public partial class Subject : ObservableObject, IEntity
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Short))] private string _name = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Short))] private string _shortName = "";
    [ObservableProperty] private string _color = "#6366F1";
    [ObservableProperty] private string _evaluation = "Examen";
    [ObservableProperty] private string _department = "";
    [ObservableProperty] private string _notes = "";

    [ObservableProperty] [property: JsonIgnore] private int _activityCount;
    [ObservableProperty] [property: JsonIgnore] private int _hoursScheduled;

    [JsonIgnore] public string Short => string.IsNullOrWhiteSpace(ShortName) ? Abbrev.Make(Name) : ShortName;

    public override string ToString() => Name;
}

/// <summary>Cadru didactic.</summary>
public partial class Teacher : ObservableObject, IEntity
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FullLabel))] private string _name = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FullLabel))] private string _title = "";
    [ObservableProperty] private string _department = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _notes = "";

    [ObservableProperty] [property: JsonIgnore] private int _hoursScheduled;
    [ObservableProperty] [property: JsonIgnore] private int _hoursPlanned;

    /// <summary>Doleanțe: intervalele în care profesorul nu poate avea ore.</summary>
    public List<SlotKey> Unavailable { get; set; } = new();

    [JsonIgnore] public string FullLabel => string.IsNullOrWhiteSpace(Title) ? Name : $"{Name}, {Title}";

    public bool IsUnavailable(int day, int slot) => Unavailable.Contains(new SlotKey(day, slot));

    public override string ToString() => Name;
}

/// <summary>Grupă academică (ex. I2301), cu subgrupe opționale (ex. I2301-1).</summary>
public partial class StudentGroup : ObservableObject, IEntity
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _specialty = "";
    [ObservableProperty] private int _year = 1;
    [ObservableProperty] private string _cycle = "Licență";
    [ObservableProperty] private TeachingLanguage _language = TeachingLanguage.RO;
    [ObservableProperty] private int _students;
    [ObservableProperty] private string _notes = "";

    [ObservableProperty] [property: JsonIgnore] private int _hoursScheduled;
    [ObservableProperty] [property: JsonIgnore] private int _hoursPlanned;

    public ObservableCollection<Subgroup> Subgroups { get; set; } = new();

    [JsonIgnore] public string SubgroupsLabel => Subgroups.Count == 0 ? "—" : string.Join(", ", Subgroups.Select(s => Audience.SubgroupSuffix(s.Code)));

    public override string ToString() => Code;
}

public partial class Subgroup : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private int _students;

    public override string ToString() => Code;
}

/// <summary>
/// Activitate didactică din „sarcina didactică": disciplină + tip + ore/săptămână + grupe + profesor.
/// Din ea se creează una sau mai multe lecții plasate în orar.
/// </summary>
public partial class TeachingActivity : ObservableObject, IEntity
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private Guid _subjectId;
    [ObservableProperty] private LessonType _type;
    [ObservableProperty] private int _hoursPerWeek = 2;
    [ObservableProperty] private Guid? _teacherId;
    [ObservableProperty] private TeachingLanguage _language = TeachingLanguage.RO;
    [ObservableProperty] private int _students;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _source = "";

    /// <summary>Coduri de grupe/subgrupe (ex. „I2301", „I2301-1").</summary>
    public List<string> Groups { get; set; } = new();
}

/// <summary>O lecție plasată în orar (zi, pereche, auditoriu, periodicitate).</summary>
public partial class Lesson : ObservableObject, IEntity
{
    [ObservableProperty] private Guid _id = Guid.NewGuid();
    [ObservableProperty] private Guid? _activityId;
    [ObservableProperty] private Guid _subjectId;
    [ObservableProperty] private Guid? _teacherId;
    [ObservableProperty] private LessonType _type;
    [ObservableProperty] private Guid? _roomId;
    [ObservableProperty] private int _day;
    [ObservableProperty] private int _slot = 1;
    [ObservableProperty] private WeekParity _week;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private bool _locked;

    public List<string> Groups { get; set; } = new();

    /// <summary>Ore academice pe săptămână acoperite de lecție (o pereche = 2 ore).</summary>
    [JsonIgnore] public int Hours => Week == WeekParity.Saptamanal ? 2 : 1;
}

/// <summary>Perechea (intervalul orar) cu numărul ei și orele de început/sfârșit.</summary>
public partial class TimeSlot : ObservableObject
{
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Label))] private int _number;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Label))] private string _start = "08:00";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Label))] private string _end = "09:30";

    [JsonIgnore] public string Label => $"{Start}–{End}";
    [JsonIgnore] public TimeSpan StartTime => ParseTime(Start);
    [JsonIgnore] public TimeSpan EndTime => ParseTime(End);

    public static TimeSpan ParseTime(string s) =>
        TimeSpan.TryParse(s?.Trim().Replace('.', ':'), out var t) ? t : TimeSpan.Zero;
}

/// <summary>Setările orarului (instituție, semestru, perechi).</summary>
public partial class ScheduleSettings : ObservableObject
{
    [ObservableProperty] private string _institution = "Universitatea de Stat din Moldova";
    [ObservableProperty] private string _faculty = "Facultatea de Matematică și Informatică";
    [ObservableProperty] private string _department = "Departamentul Informatică";
    [ObservableProperty] private string _academicYear = "2023-2024";
    [ObservableProperty] private string _semester = "I";
    [ObservableProperty] private DateTime _semesterStart = new(2023, 9, 1);
    [ObservableProperty] private int _weeks = 15;
    [ObservableProperty] private int _daysPerWeek = 5;
    [ObservableProperty] private bool _firstWeekOdd = true;
    [ObservableProperty] private string _boardMessage = "Bine ați venit! Orarul actualizat este disponibil și pe site-ul facultății.";

    public ObservableCollection<TimeSlot> TimeSlots { get; set; } = DefaultSlots();

    public static ObservableCollection<TimeSlot> DefaultSlots() => new()
    {
        new TimeSlot { Number = 1, Start = "08:00", End = "09:30" },
        new TimeSlot { Number = 2, Start = "09:45", End = "11:15" },
        new TimeSlot { Number = 3, Start = "11:30", End = "13:00" },
        new TimeSlot { Number = 4, Start = "13:30", End = "15:00" },
        new TimeSlot { Number = 5, Start = "15:15", End = "16:45" },
        new TimeSlot { Number = 6, Start = "17:00", End = "18:30" },
        new TimeSlot { Number = 7, Start = "18:45", End = "20:15" },
    };

    public TimeSlot? Slot(int number) => TimeSlots.FirstOrDefault(s => s.Number == number);
}

/// <summary>Tot proiectul de orar (se salvează ca fișier JSON *.orar).</summary>
public sealed class Dataset
{
    public string Format { get; set; } = "OrarStudio/1";
    public DateTime Modified { get; set; } = DateTime.Now;
    public ScheduleSettings Settings { get; set; } = new();
    public ObservableCollection<Room> Rooms { get; set; } = new();
    public ObservableCollection<Subject> Subjects { get; set; } = new();
    public ObservableCollection<Teacher> Teachers { get; set; } = new();
    public ObservableCollection<StudentGroup> Groups { get; set; } = new();
    public ObservableCollection<TeachingActivity> Activities { get; set; } = new();
    public ObservableCollection<Lesson> Lessons { get; set; } = new();

    /// <summary>Înlocuiește conținutul păstrând aceleași instanțe de colecții (legăturile UI rămân valide).</summary>
    public void ReplaceContents(Dataset other)
    {
        Format = other.Format;
        Modified = other.Modified;
        Settings = other.Settings;
        Replace(Rooms, other.Rooms);
        Replace(Subjects, other.Subjects);
        Replace(Teachers, other.Teachers);
        Replace(Groups, other.Groups);
        Replace(Activities, other.Activities);
        Replace(Lessons, other.Lessons);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        var items = source.ToList();
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
