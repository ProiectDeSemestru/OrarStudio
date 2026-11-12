namespace OrarStudio.Services;

public sealed record ScheduleIssue(IssueSeverity Severity, IssueKind Kind, Guid LessonId, Guid? OtherLessonId, string Message);

public sealed class ConflictReport
{
    public static readonly ConflictReport Empty = new(new List<ScheduleIssue>());

    public IReadOnlyList<ScheduleIssue> Issues { get; }
    public ILookup<Guid, ScheduleIssue> ByLesson { get; }
    public int Errors { get; }
    public int Warnings { get; }
    public int ConflictingLessons { get; }

    public ConflictReport(List<ScheduleIssue> issues)
    {
        Issues = issues.OrderByDescending(i => i.Severity).ToList();
        ByLesson = issues.ToLookup(i => i.LessonId);
        Errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        Warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
        ConflictingLessons = issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.LessonId).Distinct().Count();
    }

    public IssueSeverity? SeverityOf(Guid lessonId)
    {
        var list = ByLesson[lessonId];
        return list.Any() ? list.Max(i => i.Severity) : null;
    }
}

/// <summary>
/// Detectarea conflictelor: profesor/grupă/auditoriu ocupat în același interval (ținând cont de săptămânile
/// pare/impare și de subgrupe), capacitatea și destinația sălii, doleanțele profesorilor.
/// </summary>
public static class ConflictService
{
    public static bool WeeksOverlap(WeekParity a, WeekParity b) =>
        a == WeekParity.Saptamanal || b == WeekParity.Saptamanal || a == b;

    public static ConflictReport Analyze(Dataset d, Lookups lk)
    {
        var issues = new List<ScheduleIssue>();
        foreach (var bucket in d.Lessons.GroupBy(l => (l.Day, l.Slot)))
        {
            var arr = bucket.ToArray();
            for (int i = 0; i < arr.Length; i++)
                for (int j = i + 1; j < arr.Length; j++)
                    CheckPair(arr[i], arr[j], lk, issues, both: true);
        }
        foreach (var l in d.Lessons) CheckSingle(l, d, lk, issues);
        return new ConflictReport(issues);
    }

    /// <summary>Evaluează o lecție candidată (încă neaplicată) față de restul orarului.</summary>
    public static List<ScheduleIssue> Evaluate(Dataset d, Lookups lk, Lesson candidate, ICollection<Guid>? ignore = null)
    {
        var issues = new List<ScheduleIssue>();
        foreach (var other in d.Lessons)
        {
            if (other.Id == candidate.Id || other.Day != candidate.Day || other.Slot != candidate.Slot) continue;
            if (ignore != null && ignore.Contains(other.Id)) continue;
            CheckPair(candidate, other, lk, issues, both: false);
        }
        CheckSingle(candidate, d, lk, issues);
        return issues;
    }

    /// <summary>Verifică rapid dacă profesorul sau grupele sunt ocupate (pentru evidențierea celulelor la drag &amp; drop).</summary>
    public static IssueSeverity? QuickCheck(Dataset d, Lookups lk, Guid? teacherId, IReadOnlyList<string> groups,
        int day, int slot, WeekParity week, ICollection<Guid>? ignore)
    {
        IssueSeverity? worst = null;
        foreach (var other in d.Lessons)
        {
            if (other.Day != day || other.Slot != slot) continue;
            if (ignore != null && ignore.Contains(other.Id)) continue;
            if (!WeeksOverlap(week, other.Week)) continue;
            if (teacherId != null && other.TeacherId == teacherId) return IssueSeverity.Error;
            if (Audience.FirstOverlap(groups, other.Groups) != null) return IssueSeverity.Error;
        }
        if (lk.Teacher(teacherId)?.IsUnavailable(day, slot) == true) worst = IssueSeverity.Warning;
        return worst;
    }

    private static void CheckPair(Lesson a, Lesson b, Lookups lk, List<ScheduleIssue> issues, bool both)
    {
        if (!WeeksOverlap(a.Week, b.Week)) return;

        if (a.TeacherId != null && a.TeacherId == b.TeacherId)
        {
            var t = lk.TeacherName(a.TeacherId);
            Add(issues, IssueSeverity.Error, IssueKind.TeacherConflict, a, b,
                $"Profesorul {t} are simultan „{lk.SubjectName(b.SubjectId)}” ({Audience.Format(b.Groups)}).", both,
                $"Profesorul {t} are simultan „{lk.SubjectName(a.SubjectId)}” ({Audience.Format(a.Groups)}).");
        }

        var overlap = Audience.FirstOverlap(a.Groups, b.Groups);
        if (overlap != null)
        {
            Add(issues, IssueSeverity.Error, IssueKind.GroupConflict, a, b,
                $"Grupa {overlap} are simultan „{lk.SubjectName(b.SubjectId)}” ({Ro.TypeLabel(b.Type).ToLowerInvariant()}).", both,
                $"Grupa {overlap} are simultan „{lk.SubjectName(a.SubjectId)}” ({Ro.TypeLabel(a.Type).ToLowerInvariant()}).");
        }

        if (a.RoomId != null && a.RoomId == b.RoomId)
        {
            var r = lk.RoomName(a.RoomId);
            Add(issues, IssueSeverity.Error, IssueKind.RoomConflict, a, b,
                $"Auditoriul {r} este ocupat de „{lk.SubjectName(b.SubjectId)}” ({Audience.Format(b.Groups)}).", both,
                $"Auditoriul {r} este ocupat de „{lk.SubjectName(a.SubjectId)}” ({Audience.Format(a.Groups)}).");
        }
    }

    private static void Add(List<ScheduleIssue> issues, IssueSeverity sev, IssueKind kind, Lesson a, Lesson b,
        string msgForA, bool both, string msgForB)
    {
        issues.Add(new ScheduleIssue(sev, kind, a.Id, b.Id, msgForA));
        if (both) issues.Add(new ScheduleIssue(sev, kind, b.Id, a.Id, msgForB));
    }

    private static void CheckSingle(Lesson l, Dataset d, Lookups lk, List<ScheduleIssue> issues)
    {
        var room = lk.Room(l.RoomId);
        if (l.RoomId == null || room == null)
        {
            issues.Add(new ScheduleIssue(IssueSeverity.Info, IssueKind.NoRoom, l.Id, null, "Lecția nu are auditoriu atribuit."));
        }
        else
        {
            if (!room.Allows(l.Type))
                issues.Add(new ScheduleIssue(IssueSeverity.Warning, IssueKind.RoomType, l.Id, null,
                    $"Auditoriul {room.Name} ({room.KindLabel.ToLowerInvariant()}) nu este destinat pentru {Ro.TypeLabel(l.Type).ToLowerInvariant()}."));
            var students = lk.StudentsOf(l);
            if (students > 0 && room.Capacity > 0 && students > room.Capacity)
                issues.Add(new ScheduleIssue(IssueSeverity.Warning, IssueKind.RoomCapacity, l.Id, null,
                    $"Auditoriul {room.Name} are {room.Capacity} locuri, dar participă {students} studenți."));
        }

        var teacher = lk.Teacher(l.TeacherId);
        if (teacher == null)
            issues.Add(new ScheduleIssue(IssueSeverity.Info, IssueKind.NoTeacher, l.Id, null, "Lecția nu are profesor atribuit."));
        else if (teacher.IsUnavailable(l.Day, l.Slot))
            issues.Add(new ScheduleIssue(IssueSeverity.Warning, IssueKind.TeacherUnavailable, l.Id, null,
                $"{teacher.Name} a solicitat să nu aibă ore {Ro.Day(l.Day).ToLowerInvariant()}, perechea {Ro.Roman(l.Slot)}."));
    }
}
