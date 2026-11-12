using OrarStudio.Api;
using OrarStudio.Export;
using OrarStudio.Import;

namespace OrarStudio.Tests;

/// <summary>Teste pentru grupe/subgrupe și parsarea codurilor din fișierele de sarcină didactică.</summary>
public class AudienceTests
{
    [Theory]
    [InlineData("I2301", "I2301", true)]
    [InlineData("I2301", "I2301-1", true)]       // grupa întreagă ↔ subgrupa ei
    [InlineData("I2301-1", "I2301-2", false)]    // subgrupe diferite pot avea lecții în paralel
    [InlineData("IA2101-SC", "IA2101-WD", false)] // trasee diferite
    [InlineData("I2301", "IA2301", false)]
    public void Overlaps_RespectsSubgroups(string a, string b, bool expected) =>
        Assert.Equal(expected, Audience.Overlaps(a, b));

    [Fact]
    public void Extract_ReadsSeriesAndStudentCounts() =>
        Assert.Equal(new[] { "I2301", "IA2301", "IA2302" }, Audience.Extract("I2301, IA2301, IA2302 ro - 94st."));

    [Theory]
    [InlineData("I2202 subgr. 1", "I2202-1")]
    [InlineData("IA2102 1 subgr.", "IA2102-1")]
    [InlineData("SMMSPA 2301", "SMMSPA2301")]
    [InlineData("DJ2303-1, ru - 15st.", "DJ2303-1")]
    [InlineData("M2201,ro - 6st.", "M2201")]
    public void Extract_HandlesDocxAndXlsxNotations(string text, string expected) =>
        Assert.Equal(expected, Assert.Single(Audience.Extract(text)));

    [Fact]
    public void Extract_TwoSubgroupsOnOneLine() =>
        Assert.Equal(new[] { "IA2102-1", "IA2102-2" }, Audience.Extract("IA2102 1 subgr. IA2102 2 subgr"));
}

public class ImporterTests
{
    [Fact]
    public void CleanSubject_RemovesLanguageAndEvaluation()
    {
        var (name, lang, eval) = LoadImporter.CleanSubject("Logica şi teor. mulţ-lor (rom.) / (Examen)");
        Assert.Equal("Logica şi teor. mulţ-lor", name);
        Assert.Equal(TeachingLanguage.RO, lang);
        Assert.Equal("Examen", eval);
    }

    [Fact]
    public void CleanSubject_RemovesOptionalPackagePrefix()
    {
        var (name, _, _) = LoadImporter.CleanSubject("P/O 3 (S.05.A.148): a) Capit. suplim-re ecuații diferențiale (rom.) /  Examen");
        Assert.Equal("Capit. suplim-re ecuații diferențiale", name);
    }

    [Fact]
    public void ParseTeacher_SplitsNameTitleAndDepartment()
    {
        var t = LoadImporter.ParseTeacher("O. Darii, asist. univ. - Fizica");
        Assert.NotNull(t);
        Assert.Equal("O. Darii", t!.Value.Name);
        Assert.Equal("asist. univ.", t.Value.Title);
        Assert.Equal("Fizica", t.Value.Department);
    }

    [Fact]
    public void Key_TreatsCedillaCommaAndCircumflexVariantsAsEqual()
    {
        Assert.Equal(Ro.Key("Probab. şi statistică"), Ro.Key("Probab. și statistică"));
        Assert.Equal(Ro.Key("B. Hâncu"), Ro.Key("B. Hîncu"));
    }

    [Fact]
    public void SampleFiles_AreImportedCompletely()
    {
        var d = SampleData.Create(schedule: false);
        Assert.Equal(229, d.Activities.Count);
        Assert.Equal(24, d.Groups.Count);
        Assert.Contains(d.Groups, g => g.Code == "I2301" && g.Students == 32 && g.Subgroups.Count == 2);
        Assert.Equal("2023-2024", d.Settings.AcademicYear);
    }
}

public class ConflictTests
{
    private static (Dataset D, Guid Teacher, Guid Subject, Guid Room) Setup()
    {
        var d = new Dataset();
        var t = new Teacher { Name = "V. Arnaut" };
        var s = new Subject { Name = "Fundamentele Programării" };
        var r = new Room { Name = "101", Capacity = 120, Kind = RoomKind.Amfiteatru, AllowsCurs = true };
        d.Teachers.Add(t);
        d.Subjects.Add(s);
        d.Rooms.Add(r);
        d.Groups.Add(new StudentGroup { Code = "I2301", Students = 32 });
        return (d, t.Id, s.Id, r.Id);
    }

    [Fact]
    public void SameTeacherSameSlot_IsConflict()
    {
        var (d, t, s, _) = Setup();
        d.Lessons.Add(new Lesson { SubjectId = s, TeacherId = t, Groups = new() { "I2301" }, Day = 0, Slot = 1 });
        d.Lessons.Add(new Lesson { SubjectId = s, TeacherId = t, Groups = new() { "IA2301" }, Day = 0, Slot = 1 });
        var report = ConflictService.Analyze(d, new Lookups(d));
        Assert.Contains(report.Issues, i => i.Kind == IssueKind.TeacherConflict);
    }

    [Fact]
    public void OddAndEvenWeeks_DoNotConflict()
    {
        var (d, t, s, r) = Setup();
        d.Lessons.Add(new Lesson { SubjectId = s, TeacherId = t, RoomId = r, Groups = new() { "I2301" }, Day = 1, Slot = 2, Week = WeekParity.Impara });
        d.Lessons.Add(new Lesson { SubjectId = s, TeacherId = t, RoomId = r, Groups = new() { "I2301" }, Day = 1, Slot = 2, Week = WeekParity.Para });
        Assert.Equal(0, ConflictService.Analyze(d, new Lookups(d)).Errors);
    }

    [Fact]
    public void ParallelSubgroups_DoNotConflict_ButWholeGroupDoes()
    {
        var (d, _, s, _) = Setup();
        d.Lessons.Add(new Lesson { SubjectId = s, Groups = new() { "I2301-1" }, Day = 2, Slot = 3, Type = LessonType.Laborator });
        d.Lessons.Add(new Lesson { SubjectId = s, Groups = new() { "I2301-2" }, Day = 2, Slot = 3, Type = LessonType.Laborator });
        Assert.Equal(0, ConflictService.Analyze(d, new Lookups(d)).Errors);

        d.Lessons.Add(new Lesson { SubjectId = s, Groups = new() { "I2301" }, Day = 2, Slot = 3 });
        Assert.True(ConflictService.Analyze(d, new Lookups(d)).Errors > 0);
    }

    [Fact]
    public void LabInLectureHall_AndOvercrowdedRoom_AreWarnings()
    {
        var (d, t, s, r) = Setup();
        d.Rooms[0].Capacity = 20;
        d.Lessons.Add(new Lesson { SubjectId = s, TeacherId = t, RoomId = r, Groups = new() { "I2301" }, Type = LessonType.Laborator });
        var issues = ConflictService.Analyze(d, new Lookups(d)).Issues;
        Assert.Contains(issues, i => i.Kind == IssueKind.RoomType && i.Severity == IssueSeverity.Warning);
        Assert.Contains(issues, i => i.Kind == IssueKind.RoomCapacity && i.Severity == IssueSeverity.Warning);
    }
}

public class SchedulerTests
{
    [Fact]
    public void AutoScheduler_PlacesSampleLoadWithoutConflicts()
    {
        var d = SampleData.Create(schedule: false);
        var result = AutoScheduler.Run(d, new AutoScheduleOptions());
        foreach (var l in result.Added) d.Lessons.Add(l);
        var lk = new Lookups(d);

        Assert.Empty(result.Unplaced);
        Assert.Equal(0, ConflictService.Analyze(d, lk).Errors);
        Assert.All(d.Activities, a => Assert.Equal(a.HoursPerWeek, lk.PlacedHoursOf(a.Id)));
    }

    [Fact]
    public void AutoScheduler_RespectsTeacherUnavailability()
    {
        var d = SampleData.Create(schedule: false);
        var teacher = d.Teachers.First(t => t.Name == "L. Novac");
        for (int day = 0; day < 5; day++)
            for (int slot = 1; slot <= 3; slot++)
                teacher.Unavailable.Add(new SlotKey(day, slot));
        var result = AutoScheduler.Run(d, new AutoScheduleOptions());
        Assert.DoesNotContain(result.Added, l => l.TeacherId == teacher.Id && l.Slot <= 3);
    }
}

public class CalendarAndTextTests
{
    [Fact]
    public void WeekParity_AlternatesFromSemesterStart()
    {
        var s = new ScheduleSettings { SemesterStart = new DateTime(2023, 9, 1), FirstWeekOdd = true };
        Assert.Equal(1, SemesterCalendar.WeekNumber(s, new DateTime(2023, 9, 1)));
        Assert.Equal(2, SemesterCalendar.WeekNumber(s, new DateTime(2023, 9, 4)));
        Assert.Equal(WeekParity.Impara, SemesterCalendar.Parity(s, 1));
        Assert.Equal(WeekParity.Para, SemesterCalendar.Parity(s, 2));
    }

    [Theory]
    [InlineData(1, "1 lecție")]
    [InlineData(5, "5 lecții")]
    [InlineData(20, "20 de lecții")]
    [InlineData(101, "101 lecții")]
    public void RomanianNumeralAgreement(int n, string expected) =>
        Assert.Equal(expected, Ro.Count(n, "lecție", "lecții"));

    [Theory]
    [InlineData("Algoritmi și structuri de date", "ASD")]
    [InlineData("HTML și CSS", "HTML/CSS")]
    [InlineData("Programare Orientată Obiect", "POO")]
    public void Abbreviations(string name, string expected) => Assert.Equal(expected, Abbrev.Make(name));
}

public class ExportTests
{
    [Fact]
    public void PdfExcelIcsAndBoard_AreGeneratedForSampleData()
    {
        var d = SampleData.Create();
        var lk = new Lookups(d);
        var group = TimetableBuilder.OrderedGroups(d).First();

        var pdf = PdfExporter.Timetables(d, new[] { TimetableBuilder.ForGroup(d, lk, group) }, "test");
        Assert.True(pdf.Length > 10_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        var xlsx = Path.Combine(Path.GetTempPath(), $"orar-test-{Guid.NewGuid():N}.xlsx");
        try
        {
            ExcelExporter.ExportWorkbook(d, lk, xlsx);
            Assert.True(new FileInfo(xlsx).Length > 10_000);
        }
        finally { File.Delete(xlsx); }

        var ics = IcsExporter.Build(d, TimetableBuilder.ForGroup(d, lk, group));
        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains("RRULE:FREQ=WEEKLY", ics);

        var state = BoardLogic.Compute(d, lk, new DateTime(2023, 10, 3, 10, 5, 0), "rooms");
        Assert.Equal(2, state.CurrentSlot);
        Assert.Contains(state.Rows, r => r.Busy);
    }
}
