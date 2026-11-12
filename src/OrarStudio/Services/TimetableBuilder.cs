namespace OrarStudio.Services;

/// <summary>Reprezentarea „de afișare" a unei lecții (folosită la export, API și panou).</summary>
public sealed record LessonView(
    Guid Id, int Day, int Slot, WeekParity Week, LessonType Type,
    string Subject, string SubjectShort, string TypeLabel,
    string Teacher, string TeacherFull, string Room, string Groups,
    string Color, string Note, string Subgroup);

public sealed class EntityTimetable
{
    public ScheduleViewMode Mode { get; init; }
    public string Kind { get; init; } = "";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string FileKey { get; init; } = "";
    public List<LessonView> Lessons { get; init; } = new();

    public IEnumerable<LessonView> At(int day, int slot) =>
        Lessons.Where(l => l.Day == day && l.Slot == slot)
               .OrderBy(l => l.Week)
               .ThenBy(l => l.Subgroup, StringComparer.OrdinalIgnoreCase);
}

public static class TimetableBuilder
{
    public static int Days(Dataset d) => Math.Clamp(d.Settings.DaysPerWeek, 1, 7);

    public static LessonView View(Lesson l, Lookups lk, string? contextGroup = null)
    {
        var teacher = lk.Teacher(l.TeacherId);
        string subgroup = "";
        if (contextGroup != null)
        {
            var own = l.Groups.Where(c => string.Equals(Audience.BaseCode(c), contextGroup, StringComparison.OrdinalIgnoreCase)).ToList();
            if (own.Count > 0 && own.All(Audience.IsSubgroup))
                subgroup = string.Join("+", own.Select(c => "sg. " + Audience.SubgroupSuffix(c)));
        }
        return new LessonView(
            l.Id, l.Day, l.Slot, l.Week, l.Type,
            lk.SubjectName(l.SubjectId), lk.SubjectShort(l.SubjectId), Ro.TypeLabel(l.Type),
            teacher?.Name ?? "—", teacher?.FullLabel ?? "—", lk.RoomName(l.RoomId),
            Audience.Format(l.Groups), lk.SubjectColor(l.SubjectId), l.Note, subgroup);
    }

    public static IEnumerable<StudentGroup> OrderedGroups(Dataset d) =>
        d.Groups.OrderBy(g => g.Cycle == "Licență" ? 0 : 1)
                .ThenBy(g => g.Year)
                .ThenBy(g => new string(g.Code.TakeWhile(char.IsLetter).ToArray()))
                .ThenBy(g => g.Code, StringComparer.OrdinalIgnoreCase);

    public static EntityTimetable ForGroup(Dataset d, Lookups lk, StudentGroup g)
    {
        var lessons = d.Lessons.Where(l => Audience.Concerns(l.Groups, g.Code)).Select(l => View(l, lk, g.Code)).ToList();
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(g.Specialty)) parts.Add(g.Specialty);
        parts.Add($"anul {g.Year}");
        if (g.Students > 0) parts.Add(Ro.Count(g.Students, "student", "studenți"));
        parts.Add("limba " + Ro.LangLabel(g.Language).ToLowerInvariant());
        return new EntityTimetable
        {
            Mode = ScheduleViewMode.Group, Kind = "Grupa", Title = $"Grupa {g.Code}",
            Subtitle = string.Join(" · ", parts), FileKey = g.Code, Lessons = lessons,
        };
    }

    public static EntityTimetable ForTeacher(Dataset d, Lookups lk, Teacher t)
    {
        var lessons = d.Lessons.Where(l => l.TeacherId == t.Id).Select(l => View(l, lk)).ToList();
        int hours = d.Lessons.Where(l => l.TeacherId == t.Id).Sum(l => l.Hours);
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.Title)) parts.Add(t.Title);
        if (!string.IsNullOrWhiteSpace(t.Department)) parts.Add(t.Department);
        parts.Add($"{hours} ore/săptămână");
        return new EntityTimetable
        {
            Mode = ScheduleViewMode.Teacher, Kind = "Profesor", Title = t.Name,
            Subtitle = string.Join(" · ", parts), FileKey = t.Name, Lessons = lessons,
        };
    }

    public static EntityTimetable ForRoom(Dataset d, Lookups lk, Room r)
    {
        var lessons = d.Lessons.Where(l => l.RoomId == r.Id).Select(l => View(l, lk)).ToList();
        var parts = new List<string> { r.KindLabel, $"{r.Capacity} locuri" };
        if (!string.IsNullOrWhiteSpace(r.Building)) parts.Add(r.Building + (r.Floor > 0 ? $", etajul {r.Floor}" : ""));
        if (r.Computers > 0) parts.Add($"{r.Computers} calculatoare");
        return new EntityTimetable
        {
            Mode = ScheduleViewMode.Room, Kind = "Auditoriul", Title = $"Auditoriul {r.Name}",
            Subtitle = string.Join(" · ", parts), FileKey = "aud-" + r.Name, Lessons = lessons,
        };
    }

    /// <summary>Gradul de ocupare al unei săli (ore ocupate / ore disponibile).</summary>
    public static double RoomOccupancy(Dataset d, Room r)
    {
        int capacityHours = Days(d) * d.Settings.TimeSlots.Count * 2;
        if (capacityHours == 0) return 0;
        return Math.Min(1.0, d.Lessons.Where(l => l.RoomId == r.Id).Sum(l => l.Hours) / (double)capacityHours);
    }

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "orar" : cleaned;
    }
}
