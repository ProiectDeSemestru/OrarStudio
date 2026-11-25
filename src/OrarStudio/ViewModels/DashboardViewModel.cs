using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

public sealed class TypeShare
{
    public LessonType Type { get; init; }
    public string Label { get; init; } = "";
    public int Count { get; init; }
    public double Ratio { get; init; }
    public string Percent { get; init; } = "";
}

/// <summary>Panoul de control: indicatori, probleme detectate, distribuții și acțiuni rapide.</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    public override string Title => "Panou de control";
    public override string Icon => "";
    public override string Subtitle
    {
        get
        {
            var s = Store.Data.Settings;
            return $"{s.Faculty} · anul universitar {s.AcademicYear}, semestrul {s.Semester}";
        }
    }

    [ObservableProperty] private int _lessons;
    [ObservableProperty] private int _groups;
    [ObservableProperty] private int _teachers;
    [ObservableProperty] private int _rooms;
    [ObservableProperty] private int _subjects;
    [ObservableProperty] private int _activities;
    [ObservableProperty] private int _students;
    [ObservableProperty] private int _hoursPlanned;
    [ObservableProperty] private int _hoursPlaced;
    [ObservableProperty] private double _coverage;
    [ObservableProperty] private string _coverageText = "";
    [ObservableProperty] private int _unplacedActivities;
    [ObservableProperty] private int _errors;
    [ObservableProperty] private int _warnings;
    [ObservableProperty] private int _conflictingLessons;
    [ObservableProperty] private string _weekText = "";
    [ObservableProperty] private string _todayText = "";
    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private bool _hasIssues;
    /// <summary>Proiect nou, fără sarcină didactică: se afișează „Primii pași".</summary>
    [ObservableProperty] private bool _isEmpty;

    public ObservableCollection<BarItem> DayBars { get; } = new();
    public ObservableCollection<BarItem> RoomBars { get; } = new();
    public ObservableCollection<BarItem> TeacherBars { get; } = new();
    public ObservableCollection<TypeShare> TypeShares { get; } = new();
    public ObservableCollection<IssueVM> TopIssues { get; } = new();

    public override void Refresh()
    {
        var d = Store.Data;
        var lk = Store.Lk;
        var s = d.Settings;
        Lessons = d.Lessons.Count;
        IsEmpty = d.Activities.Count == 0 && d.Lessons.Count == 0;
        Groups = d.Groups.Count;
        Teachers = d.Teachers.Count;
        Rooms = d.Rooms.Count;
        Subjects = d.Subjects.Count;
        Activities = d.Activities.Count;
        Students = d.Groups.Sum(g => g.Students);
        HoursPlanned = d.Activities.Sum(a => a.HoursPerWeek);
        HoursPlaced = d.Activities.Sum(a => Math.Min(a.HoursPerWeek, lk.PlacedHoursOf(a.Id)));
        Coverage = HoursPlanned == 0 ? 0 : HoursPlaced / (double)HoursPlanned;
        CoverageText = $"{Coverage:P0}";
        UnplacedActivities = d.Activities.Count(a => lk.PlacedHoursOf(a.Id) < a.HoursPerWeek);
        Errors = Store.Report.Errors;
        Warnings = Store.Report.Warnings;
        ConflictingLessons = Store.Report.ConflictingLessons;

        int hour = DateTime.Now.Hour;
        Greeting = hour < 12 ? "Bună dimineața!" : hour < 18 ? "Bună ziua!" : "Bună seara!";
        int week = SemesterCalendar.WeekNumber(s, DateTime.Today);
        var parity = SemesterCalendar.Parity(s, week);
        WeekText = SemesterCalendar.InSemester(s, week)
            ? $"Săptămâna {week} din {s.Weeks} · {(parity == WeekParity.Impara ? "impară" : "pară")}"
            : $"În afara semestrului ({s.SemesterStart:dd.MM.yyyy} – {SemesterCalendar.SemesterEnd(s):dd.MM.yyyy}) · săptămână {(parity == WeekParity.Impara ? "impară" : "pară")}";
        int dayIdx = Ro.DayIndex(DateTime.Today);
        int today = dayIdx < TimetableBuilder.Days(d) ? d.Lessons.Count(l => l.Day == dayIdx && SemesterCalendar.OccursIn(l.Week, parity)) : 0;
        TodayText = dayIdx < TimetableBuilder.Days(d) ? $"Astăzi ({Ro.Day(dayIdx).ToLowerInvariant()}): {Ro.Count(today, "lecție", "lecții")}" : "Astăzi nu sunt lecții";

        // distribuția pe zile
        DayBars.Clear();
        int days = TimetableBuilder.Days(d);
        var perDay = Enumerable.Range(0, days).Select(i => d.Lessons.Where(l => l.Day == i).Sum(l => l.Hours) / 2.0).ToList();
        double maxDay = Math.Max(1, perDay.DefaultIfEmpty(0).Max());
        for (int i = 0; i < days; i++)
            DayBars.Add(new BarItem
            {
                Label = Ro.DayShort(i), Value = perDay[i], Ratio = perDay[i] / maxDay,
                ValueText = perDay[i].ToString("0.#"),
                Tooltip = $"{Ro.Day(i)}: {perDay[i]:0.#} perechi pe săptămână (media săptămânilor pare/impare)",
            });

        // ocuparea auditoriilor
        RoomBars.Clear();
        foreach (var r in d.Rooms.Select(r => (Room: r, Occ: TimetableBuilder.RoomOccupancy(d, r))).OrderByDescending(x => x.Occ).Take(8))
            RoomBars.Add(new BarItem
            {
                Label = r.Room.Name, Detail = r.Room.KindLabel, Value = r.Occ, Ratio = r.Occ, ValueText = r.Occ.ToString("P0"),
                Tooltip = $"Auditoriul {r.Room.Name} ({r.Room.KindLabel.ToLowerInvariant()}, {r.Room.Capacity} locuri): ocupat {r.Occ:P0} din timpul disponibil",
                Tag = r.Room,
            });

        // încărcarea profesorilor
        TeacherBars.Clear();
        var load = d.Teachers.Select(t => (Teacher: t, Hours: d.Lessons.Where(l => l.TeacherId == t.Id).Sum(l => l.Hours)))
                             .Where(x => x.Hours > 0).OrderByDescending(x => x.Hours).Take(8).ToList();
        double maxLoad = Math.Max(1, load.Select(x => x.Hours).DefaultIfEmpty(0).Max());
        foreach (var (t, h) in load)
            TeacherBars.Add(new BarItem
            {
                Label = t.Name, Detail = t.Title, Value = h, Ratio = h / maxLoad, ValueText = $"{h} ore",
                Tooltip = $"{t.FullLabel}: {h} ore/săptămână în orar", Tag = t,
            });

        // tipuri de lecții
        TypeShares.Clear();
        int total = Math.Max(1, d.Lessons.Count);
        foreach (var type in new[] { LessonType.Curs, LessonType.Seminar, LessonType.Laborator })
        {
            int c = d.Lessons.Count(l => l.Type == type);
            TypeShares.Add(new TypeShare { Type = type, Label = Ro.TypeLabel(type), Count = c, Ratio = c / (double)total, Percent = $"{c / (double)total:P0}" });
        }

        // probleme
        TopIssues.Clear();
        foreach (var issue in Store.Report.Issues.Where(i => i.Severity != IssueSeverity.Info)
                     .GroupBy(i => i.Message).Select(g => g.First()).Take(7))
        {
            var l = d.Lessons.FirstOrDefault(x => x.Id == issue.LessonId);
            TopIssues.Add(new IssueVM
            {
                Issue = issue,
                Where = l == null ? "" : $"{Ro.Day(l.Day)}, perechea {Ro.Roman(l.Slot)} · {lk.SubjectShort(l.SubjectId)} · {Audience.Format(l.Groups)}",
            });
        }
        HasIssues = TopIssues.Count > 0;
    }

    [RelayCommand]
    private void OpenIssue(IssueVM? issue)
    {
        var main = AppServices.Main;
        if (issue == null || main == null) return;
        main.Navigate(main.Timetable);
        main.Timetable.FocusLesson(issue.Issue.LessonId);
    }

    [RelayCommand]
    private void OpenRoom(BarItem? bar)
    {
        var main = AppServices.Main;
        if (bar?.Tag is Room r && main != null)
            main.Navigate(main.Timetable, new TimetableTarget(ScheduleViewMode.Room, r.Id.ToString()));
    }

    [RelayCommand]
    private void OpenTeacher(BarItem? bar)
    {
        var main = AppServices.Main;
        if (bar?.Tag is Teacher t && main != null)
            main.Navigate(main.Timetable, new TimetableTarget(ScheduleViewMode.Teacher, t.Id.ToString()));
    }
}
