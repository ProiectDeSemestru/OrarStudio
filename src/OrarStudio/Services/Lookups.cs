namespace OrarStudio.Services;

/// <summary>Indexuri rapide peste un set de date (reconstruite după fiecare modificare).</summary>
public sealed class Lookups
{
    private readonly Dictionary<string, int> _students = new(StringComparer.OrdinalIgnoreCase);

    public Dataset Data { get; }
    public IReadOnlyDictionary<Guid, Subject> Subjects { get; }
    public IReadOnlyDictionary<Guid, Teacher> Teachers { get; }
    public IReadOnlyDictionary<Guid, Room> Rooms { get; }
    public IReadOnlyDictionary<Guid, TeachingActivity> Activities { get; }
    public IReadOnlyDictionary<string, StudentGroup> Groups { get; }
    public IReadOnlyDictionary<Guid, int> PlacedHours { get; }

    public Lookups(Dataset d)
    {
        Data = d;
        Subjects = d.Subjects.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        Teachers = d.Teachers.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        Rooms = d.Rooms.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        Activities = d.Activities.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var groups = new Dictionary<string, StudentGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in d.Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Code)) continue;
            groups.TryAdd(g.Code, g);
            _students[g.Code] = g.Students;
            foreach (var sg in g.Subgroups)
                if (!string.IsNullOrWhiteSpace(sg.Code)) _students[sg.Code] = sg.Students;
        }
        Groups = groups;
        PlacedHours = d.Lessons.Where(l => l.ActivityId != null)
                               .GroupBy(l => l.ActivityId!.Value)
                               .ToDictionary(g => g.Key, g => g.Sum(l => l.Hours));
    }

    public Subject? Subject(Guid id) => Subjects.TryGetValue(id, out var s) ? s : null;
    public Teacher? Teacher(Guid? id) => id is Guid g && Teachers.TryGetValue(g, out var t) ? t : null;
    public Room? Room(Guid? id) => id is Guid g && Rooms.TryGetValue(g, out var r) ? r : null;
    public TeachingActivity? Activity(Guid? id) => id is Guid g && Activities.TryGetValue(g, out var a) ? a : null;
    public StudentGroup? Group(string code) => Groups.TryGetValue(code, out var g) ? g : null;

    public string SubjectName(Guid id) => Subject(id)?.Name ?? "(disciplină necunoscută)";
    public string SubjectShort(Guid id) => Subject(id)?.Short ?? "?";
    public string SubjectColor(Guid id) => Subject(id)?.Color ?? "#6366F1";
    public string TeacherName(Guid? id) => Teacher(id)?.Name ?? "—";
    public string RoomName(Guid? id) => Room(id)?.Name ?? "—";

    public int PlacedHoursOf(Guid activityId) => PlacedHours.TryGetValue(activityId, out var h) ? h : 0;

    /// <summary>Numărul de studenți pentru un cod de grupă sau subgrupă.</summary>
    public int AudienceSize(string code)
    {
        if (_students.TryGetValue(code, out var n) && n > 0) return n;
        var baseCode = Audience.BaseCode(code);
        if (!string.Equals(baseCode, code, StringComparison.OrdinalIgnoreCase)
            && _students.TryGetValue(baseCode, out var whole) && whole > 0)
        {
            var parts = Math.Max(2, Group(baseCode)?.Subgroups.Count ?? 2);
            return (int)Math.Ceiling(whole / (double)parts);
        }
        return 0;
    }

    public int Students(IEnumerable<string> codes) => codes.Sum(AudienceSize);

    public int StudentsOf(Lesson l)
    {
        var a = Activity(l.ActivityId);
        if (a is { Students: > 0 } && a.Groups.Count == l.Groups.Count) return a.Students;
        return Students(l.Groups);
    }
}
