using System.Reflection;

namespace OrarStudio.Api;

public sealed record SlotDto(int Number, string Roman, string Start, string End);

public sealed record InfoDto(
    string Institution, string Faculty, string Department, string AcademicYear, string Semester,
    string SemesterStart, string SemesterEnd, int Weeks, int Days, string[] DayNames, SlotDto[] Slots,
    int CurrentWeek, string CurrentParity, bool InSemester, string GeneratedAt, string BoardMessage);

public sealed record GroupDto(string Code, string Specialty, int Year, string Cycle, string Language, int Students, string[] Subgroups);

public sealed record TeacherDto(string Id, string Name, string Title, string Department);

public sealed record RoomDto(string Id, string Name, string Building, int Floor, int Capacity, string Kind, string KindLabel,
    string[] Usage, bool Projector, int Computers, string Notes);

public sealed record SubjectDto(string Id, string Name, string Short, string Color, string Evaluation);

public sealed record LessonDto(
    string Id, int Day, string DayName, int Slot, string SlotRoman, string Start, string End,
    string Week, string WeekLabel, string Type, string TypeLabel,
    string SubjectId, string Subject, string SubjectShort, string Color,
    string? TeacherId, string Teacher, string TeacherTitle, string? RoomId, string Room, string[] Groups, string Note);

public sealed record BundleDto(InfoDto Info, GroupDto[] Groups, TeacherDto[] Teachers, RoomDto[] Rooms, SubjectDto[] Subjects, LessonDto[] Lessons);

/// <summary>Instantaneu imuabil al datelor, citit în siguranță de serverul HTTP (alt fir de execuție).</summary>
public sealed class ApiSnapshot
{
    public Dataset Data { get; }
    public Lookups Lk { get; }
    public BundleDto Bundle { get; }
    public DateTime BuiltAt { get; } = DateTime.Now;

    private ApiSnapshot(Dataset data)
    {
        Data = data;
        Lk = new Lookups(data);
        Bundle = BuildBundle(data, Lk);
    }

    /// <summary>Creează un instantaneu dintr-o COPIE a datelor (nu din obiectele legate la UI).</summary>
    public static ApiSnapshot FromCopy(Dataset copy) => new(copy);

    public static string WeekCode(WeekParity w) => w switch
    {
        WeekParity.Impara => "impar",
        WeekParity.Para => "par",
        _ => "saptamanal",
    };

    public static string TypeCode(LessonType t) => t switch
    {
        LessonType.Curs => "curs",
        LessonType.Seminar => "seminar",
        _ => "laborator",
    };

    public static BundleDto BuildBundle(Dataset d, Lookups lk)
    {
        var s = d.Settings;
        int days = TimetableBuilder.Days(d);
        int week = SemesterCalendar.WeekNumber(s, DateTime.Today);
        var info = new InfoDto(
            s.Institution, s.Faculty, s.Department, s.AcademicYear, s.Semester,
            s.SemesterStart.ToString("yyyy-MM-dd"), SemesterCalendar.SemesterEnd(s).ToString("yyyy-MM-dd"), s.Weeks, days,
            Enumerable.Range(0, days).Select(Ro.Day).ToArray(),
            s.TimeSlots.OrderBy(x => x.Number).Select(x => new SlotDto(x.Number, Ro.Roman(x.Number), x.Start, x.End)).ToArray(),
            week, WeekCode(SemesterCalendar.Parity(s, week)), SemesterCalendar.InSemester(s, week),
            DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), s.BoardMessage);

        var groups = TimetableBuilder.OrderedGroups(d)
            .Select(g => new GroupDto(g.Code, g.Specialty, g.Year, g.Cycle, Ro.LangShort(g.Language), g.Students,
                g.Subgroups.Select(x => x.Code).ToArray())).ToArray();
        var teachers = d.Teachers.OrderBy(t => t.Name, StringComparer.Create(Ro.Culture, true))
            .Select(t => new TeacherDto(t.Id.ToString(), t.Name, t.Title, t.Department)).ToArray();
        var rooms = d.Rooms.OrderBy(r => r.Name, StringComparer.Create(Ro.Culture, true))
            .Select(r => new RoomDto(r.Id.ToString(), r.Name, r.Building, r.Floor, r.Capacity, r.Kind.ToString(), r.KindLabel,
                new[] { r.AllowsCurs ? "curs" : null, r.AllowsSeminar ? "seminar" : null, r.AllowsLaborator ? "laborator" : null }
                    .Where(x => x != null).Select(x => x!).ToArray(),
                r.HasProjector, r.Computers, r.Notes)).ToArray();
        var subjects = d.Subjects.OrderBy(x => x.Name, StringComparer.Create(Ro.Culture, true))
            .Select(x => new SubjectDto(x.Id.ToString(), x.Name, x.Short, x.Color, x.Evaluation)).ToArray();
        var lessons = d.Lessons.OrderBy(l => l.Day).ThenBy(l => l.Slot).Select(l => ToDto(l, d, lk)).ToArray();
        return new BundleDto(info, groups, teachers, rooms, subjects, lessons);
    }

    public static LessonDto ToDto(Lesson l, Dataset d, Lookups lk)
    {
        var slot = d.Settings.Slot(l.Slot);
        var t = lk.Teacher(l.TeacherId);
        return new LessonDto(
            l.Id.ToString(), l.Day, Ro.Day(l.Day), l.Slot, Ro.Roman(l.Slot), slot?.Start ?? "", slot?.End ?? "",
            WeekCode(l.Week), Ro.WeekLabel(l.Week), TypeCode(l.Type), Ro.TypeLabel(l.Type),
            l.SubjectId.ToString(), lk.SubjectName(l.SubjectId), lk.SubjectShort(l.SubjectId), lk.SubjectColor(l.SubjectId),
            l.TeacherId?.ToString(), t?.Name ?? "—", t?.Title ?? "", l.RoomId?.ToString(), lk.RoomName(l.RoomId),
            l.Groups.ToArray(), l.Note);
    }
}

/// <summary>Resursele web încorporate (site public, panou web, documentația API).</summary>
public static class WebAssets
{
    public static string? Text(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Web/" + name);
        if (s == null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        _ => "text/plain; charset=utf-8",
    };

    public static readonly string[] SiteFiles = { "index.html", "board.html", "docs.html", "site.css", "site.js", "board.js" };
}
