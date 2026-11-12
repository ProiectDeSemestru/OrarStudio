using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.ComponentModel;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace OrarStudio.Import;

/// <summary>Un rând din previzualizarea importului (editabil înainte de import).</summary>
public sealed partial class ImportRow : ObservableObject
{
    [ObservableProperty] private bool _include = true;
    [ObservableProperty] private string _subject = "";
    [ObservableProperty] private LessonType _type;
    [ObservableProperty] private int _hours = 2;
    [ObservableProperty] private string _groups = "";
    [ObservableProperty] private int _students;
    [ObservableProperty] private string _teacher = "";
    [ObservableProperty] private TeachingLanguage _language;
    [ObservableProperty] private string _evaluation = "";
    [ObservableProperty] private string _warning = "";

    public string Source { get; set; } = "";
    public string File { get; set; } = "";
    public string Department { get; set; } = "";
    public int Year { get; set; }
    public string Section { get; set; } = "";
}

public sealed class ImportPreview
{
    public List<ImportRow> Rows { get; } = new();
    public List<string> Messages { get; } = new();
    public List<string> Wishes { get; } = new();
    public List<SlotKey> SuggestedUnavailable { get; } = new();
    public string? WishesFile { get; set; }
    public string? AcademicYear { get; set; }
    public string? Semester { get; set; }
    public DateTime? SemesterStart { get; set; }
    public int? Weeks { get; set; }
    public List<string> Files { get; } = new();
}

public sealed class ImportResult
{
    public int Activities { get; set; }
    public int Subjects { get; set; }
    public int Teachers { get; set; }
    public int Groups { get; set; }
    public int Subgroups { get; set; }
    public int Duplicates { get; set; }
    public int WishSlots { get; set; }

    public override string ToString()
    {
        var parts = new List<string>
        {
            Ro.Count(Activities, "activitate", "activități"),
            Ro.Count(Subjects, "disciplină nouă", "discipline noi"),
            Ro.Count(Teachers, "profesor nou", "profesori noi"),
            Ro.Count(Groups, "grupă nouă", "grupe noi"),
        };
        if (Subgroups > 0) parts.Add(Ro.Count(Subgroups, "subgrupă", "subgrupe"));
        if (Duplicates > 0) parts.Add($"{Duplicates} duplicate ignorate");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// Importul „sarcinii didactice" din fișierele departamentelor (.xlsx, .docx, .csv), ca în fișierele-exemplu:
/// Disciplina (limba, evaluare) | Nr. ore/săptămână + tip (prel./lab./sem.) | Seria/grupa/subgrupa + nr. studenți | Profesorul.
/// </summary>
public static partial class LoadImporter
{
    private sealed record RawRow(string Subject, List<string> Hours, List<string> Groups, List<string> Teachers,
        string Source, string File, int Year, string Section, string Department);

    [GeneratedRegex(@"^\s*(\d+)\s*(ore\s*)?\.?\s*(prel|curs|lab|sem|c\b|s\b|l\b)", RegexOptions.IgnoreCase)]
    private static partial Regex HoursRx();

    [GeneratedRegex(@"(\d+)\s*st\b", RegexOptions.IgnoreCase)]
    private static partial Regex StudentsRx();

    [GeneratedRegex(@"(?<![A-Za-z])(rom|rus|ro|ru|en)(?![A-Za-z])")]
    private static partial Regex LangRx();

    [GeneratedRegex(@"\(\s*(rom\s*\+\s*rus|rom|rus|ro|ru|en|eng)\s*\.?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SubjectLangRx();

    [GeneratedRegex(@"\b(\d{2})\.(\d{2})\.(\d{4})\b")]
    private static partial Regex DateRx();

    public static ImportPreview Parse(IEnumerable<string> paths, ScheduleSettings? settings = null)
    {
        var p = new ImportPreview();
        foreach (var path in paths)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                ms.Position = 0;
                ParseStream(ms, Path.GetFileName(path), p, settings);
            }
            catch (Exception ex)
            {
                p.Messages.Add($"Eroare la citirea fișierului „{Path.GetFileName(path)}”: {ex.Message}");
                Log.Error(ex, "Import " + path);
            }
        }
        foreach (var row in p.Rows) Validate(row);
        return p;
    }

    public static void ParseStream(Stream stream, string fileName, ImportPreview p, ScheduleSettings? settings)
    {
        p.Files.Add(fileName);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        switch (ext)
        {
            case ".xlsx":
            case ".xlsm":
                ParseXlsx(stream, fileName, p);
                break;
            case ".docx":
                ParseDocx(stream, fileName, p, settings);
                break;
            case ".csv":
            case ".txt":
                ParseCsv(stream, fileName, p);
                break;
            default:
                p.Messages.Add($"Format neacceptat: {fileName}. Sunt acceptate fișierele .xlsx, .docx și .csv.");
                break;
        }
    }

    // ───────────────────────────── Excel ─────────────────────────────

    private static void ParseXlsx(Stream stream, string file, ImportPreview p)
    {
        OrarStudio.Export.ExcelSupport.EnsureInit();
        using var wb = new XLWorkbook(stream);
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used is null) continue;
            int lastRow = used.LastRow().RowNumber();
            int lastCol = Math.Min(used.LastColumn().ColumnNumber(), 20);
            string Cell(int r, int c) => c <= 0 ? "" : ws.Cell(r, c).GetFormattedString().Trim();

            int header = 0;
            for (int r = 1; r <= Math.Min(lastRow, 20) && header == 0; r++)
                for (int c = 1; c <= lastCol; c++)
                    if (Cell(r, c).Contains("Disciplina", StringComparison.OrdinalIgnoreCase)) { header = r; break; }

            int cSubj = 2, cHours = 3, cGroups = 4, cTeacher = 5;
            if (header > 0)
            {
                for (int c = 1; c <= lastCol; c++)
                {
                    var h = Ro.Key(Cell(header, c));
                    if (h.Contains("disciplina")) cSubj = c;
                    else if (h.Contains("profesor")) cTeacher = c;
                    else if (h.Contains("grupa") || h.Contains("seria")) cGroups = c;
                    else if (h.Contains("ore") || h.Contains("tipul")) cHours = c;
                }
            }

            int year = 0;
            for (int r = 1; r < Math.Max(header, 2); r++)
            {
                for (int c = 1; c <= lastCol; c++)
                {
                    var text = Cell(r, c);
                    if (text.Length == 0) continue;
                    ParseTitleInfo(text, p);
                    if (year == 0) year = TryYear(text);
                }
            }

            List<ImportRow> last = new();
            for (int r = header + 1; r <= lastRow; r++)
            {
                var subj = Cell(r, cSubj);
                var hours = Cell(r, cHours);
                var groups = Cell(r, cGroups);
                var teacher = Cell(r, cTeacher);
                if (hours.Length == 0 && groups.Length == 0 && teacher.Length == 0)
                {
                    if (IsEvaluation(subj))
                    {
                        foreach (var lr in last) lr.Evaluation = NormalizeEvaluation(subj);
                        continue;
                    }
                    var heading = Cell(r, 1).Length > 0 ? Cell(r, 1) : subj;
                    var y = TryYear(heading);
                    if (y > 0) year = y;
                    continue;
                }
                var raw = new RawRow(subj, Lines(hours), Lines(groups), Lines(teacher),
                    $"{file} · {ws.Name} · rândul {r}", file, year, "", "");
                last = AddRows(raw, p);
            }
        }
    }

    // ───────────────────────────── Word ─────────────────────────────

    private static void ParseDocx(Stream stream, string file, ImportPreview p, ScheduleSettings? settings)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return;

        int year = 0;
        string section = "Licență";
        string department = "";
        bool reduced = false, inWishes = false;
        int tableNo = 0;

        foreach (var el in body.Elements())
        {
            if (el is W.Paragraph para)
            {
                var text = Ro.CollapseSpaces(para.InnerText);
                if (text.Length == 0) continue;
                var compact = Ro.Key(text).Replace(" ", "");

                if (compact.StartsWith("anul"))
                {
                    var y = TryYear(text);
                    if (y > 0) year = y;
                    section = "Licență";
                    reduced = false;
                }
                else if (compact.StartsWith("masterat"))
                {
                    section = "Master";
                    reduced = false;
                }
                else if (compact.StartsWith("frecventaredusa"))
                {
                    reduced = true;
                }
                else if (compact.StartsWith("dolean"))
                {
                    inWishes = true;
                }
                else if (inWishes)
                {
                    p.Wishes.Add(text);
                    p.WishesFile = file;
                    DetectUnavailable(text, p, settings);
                }
                else if (compact.Contains("departamentului"))
                {
                    var m = Regex.Match(text, @"DEPARTAMENTULUI\s*[”""“„'«]?\s*([^”""“»]+)", RegexOptions.IgnoreCase);
                    if (m.Success) department = m.Groups[1].Value.Trim().Trim('"', '”', '“', '\'');
                }
                else
                {
                    if (compact.Contains("semestrul") || compact.Contains("anuldestudii")) ParseTitleInfo(text, p);
                    var ym = Regex.Match(Ro.RemoveDiacritics(text), @"\ban\.?\s*([IVX]+)\s*\)", RegexOptions.IgnoreCase);
                    if (ym.Success) year = Ro.FromRoman(ym.Groups[1].Value);
                }
                continue;
            }

            if (el is not W.Table table) continue;
            tableNo++;
            int rowNo = 0;
            int cSubj = 1, cHours = 2, cGroups = 3, cTeacher = 4;
            foreach (var tr in table.Elements<W.TableRow>())
            {
                rowNo++;
                var cells = tr.Elements<W.TableCell>()
                              .Select(tc => tc.Elements<W.Paragraph>()
                                              .Select(pp => Ro.CollapseSpaces(pp.InnerText))
                                              .Where(x => x.Length > 0).ToList())
                              .ToList();
                var joined = string.Join(" ", cells.SelectMany(c => c));
                if (joined.Length == 0) continue;
                if (joined.Contains("Disciplina", StringComparison.OrdinalIgnoreCase))
                {
                    for (int c = 0; c < cells.Count; c++)
                    {
                        var h = Ro.Key(string.Join(" ", cells[c]));
                        if (h.Contains("disciplina")) cSubj = c;
                        else if (h.Contains("profesor")) cTeacher = c;
                        else if (h.Contains("grupa") || h.Contains("seria")) cGroups = c;
                        else if (h.Contains("ore") || h.Contains("tipul")) cHours = c;
                    }
                    continue;
                }

                int shift = cells.Count == 4 ? -1 : 0;
                int iS = cSubj + shift, iH = cHours + shift, iG = cGroups + shift, iT = cTeacher + shift;
                if (iS < 0 || iT >= cells.Count || iH < 0 || iG < 0) continue;
                if (cells[iH].Count == 0) continue;

                var subject = string.Join(" ", cells[iS]);
                if (reduced || cells[iH].Any(h => Regex.IsMatch(h, @"\bore\b", RegexOptions.IgnoreCase)))
                {
                    p.Messages.Add($"Ignorat (frecvență redusă – ore totale, nu săptămânale): „{CleanSubject(subject).Name}” — {file}, tabelul {tableNo}, rândul {rowNo}.");
                    continue;
                }
                var raw = new RawRow(subject, cells[iH], cells[iG], cells[iT],
                    $"{file} · tabelul {tableNo} · rândul {rowNo}", file, year, section, department);
                AddRows(raw, p);
            }
        }
    }

    // ───────────────────────────── CSV ─────────────────────────────

    /// <summary>CSV simplu: Disciplina; Ore și tip (ex. „2 lab."); Grupe (+ nr. studenți); Profesor.</summary>
    private static void ParseCsv(Stream stream, string file, ImportPreview p)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        int lineNo = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var sep = line.Count(c => c == ';') >= line.Count(c => c == ',') ? ';' : ',';
            var cols = SplitCsv(line, sep);
            if (cols.Count < 4) continue;
            if (cols[0].Contains("Disciplina", StringComparison.OrdinalIgnoreCase)) continue;
            var raw = new RawRow(cols[0], new List<string> { cols[1] }, new List<string> { cols[2] }, new List<string> { cols[3] },
                $"{file} · linia {lineNo}", file, 0, "", "");
            AddRows(raw, p);
        }
    }

    private static List<string> SplitCsv(string line, char sep)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == sep && !quoted) { result.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    // ───────────────────────────── Parsare rânduri ─────────────────────────────

    private static List<string> Lines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private static List<ImportRow> AddRows(RawRow raw, ImportPreview p)
    {
        var created = new List<ImportRow>();
        var (subject, subjLang, eval) = CleanSubject(raw.Subject);
        if (subject.Length == 0) return created;

        var hours = raw.Hours.Select(ParseHours).Where(h => h != null).Select(h => h!.Value).ToList();
        if (hours.Count == 0) return created;
        int n = hours.Count;

        var groupsText = string.Join(" ", raw.Groups);
        var allCodes = Audience.Extract(groupsText);
        List<List<string>> groupsPer;
        string warning = "";
        if (n == 1) groupsPer = new() { allCodes };
        else if (allCodes.Count == n) groupsPer = allCodes.Select(c => new List<string> { c }).ToList();
        else if (raw.Groups.Count == n) groupsPer = raw.Groups.Select(Audience.Extract).ToList();
        else
        {
            groupsPer = Enumerable.Range(0, n).Select(_ => allCodes.ToList()).ToList();
            warning = "Repartizarea grupelor pe rânduri este ambiguă — verificați.";
        }

        List<string> teachers = raw.Teachers.Count == n
            ? raw.Teachers
            : Enumerable.Range(0, n).Select(_ => string.Join(" ", raw.Teachers)).ToList();

        List<int> students;
        if (n == 1) students = new() { ParseStudents(groupsText) };
        else if (raw.Groups.Count == n) students = raw.Groups.Select(ParseStudents).ToList();
        else students = Enumerable.Repeat(0, n).ToList();

        var lang = ParseLang(groupsText) ?? subjLang ?? TeachingLanguage.RO;

        for (int i = 0; i < n; i++)
        {
            var row = new ImportRow
            {
                Subject = subject,
                Type = hours[i].Type,
                Hours = hours[i].Hours,
                Groups = string.Join(", ", groupsPer[i]),
                Students = students[i],
                Teacher = teachers[i].Trim().TrimEnd(',', ';').Trim(),
                Language = lang,
                Evaluation = eval,
                Warning = warning,
                Source = raw.Source,
                File = raw.File,
                Year = raw.Year,
                Section = raw.Section,
                Department = raw.Department,
            };
            p.Rows.Add(row);
            created.Add(row);
        }
        return created;
    }

    private static void Validate(ImportRow row)
    {
        var warnings = new List<string>();
        if (!string.IsNullOrEmpty(row.Warning)) warnings.Add(row.Warning);
        if (Audience.ParseList(row.Groups).Count == 0) warnings.Add("Nu s-au recunoscut grupele.");
        if (string.IsNullOrWhiteSpace(row.Teacher)) warnings.Add("Fără profesor.");
        if (row.Hours is <= 0 or > 12) warnings.Add("Număr de ore neobișnuit.");
        row.Warning = string.Join(" ", warnings.Distinct());
    }

    private static (int Hours, LessonType Type)? ParseHours(string text)
    {
        var m = HoursRx().Match(text);
        if (!m.Success) return null;
        int hours = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var t = m.Groups[3].Value.ToLowerInvariant();
        var type = t.StartsWith("prel") || t.StartsWith("curs") || t == "c" ? LessonType.Curs
                 : t.StartsWith("lab") || t == "l" ? LessonType.Laborator
                 : LessonType.Seminar;
        return (hours, type);
    }

    private static int ParseStudents(string text)
    {
        var m = StudentsRx().Match(text ?? "");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    private static TeachingLanguage? ParseLang(string text)
    {
        var m = LangRx().Match(text ?? "");
        if (!m.Success) return null;
        return m.Groups[1].Value switch
        {
            "ru" or "rus" => TeachingLanguage.RU,
            "en" => TeachingLanguage.EN,
            _ => TeachingLanguage.RO,
        };
    }

    private static bool IsEvaluation(string s) =>
        Regex.IsMatch(s ?? "", @"^\(?\s*(examen|colocviu|evaluare|test)\b", RegexOptions.IgnoreCase);

    private static string NormalizeEvaluation(string s) =>
        Regex.IsMatch(s, "colocviu", RegexOptions.IgnoreCase) ? "Colocviu" : "Examen";

    /// <summary>Curăță denumirea disciplinei: scoate limba „(ro)", „(Examen)", codurile de pachete opționale etc.</summary>
    public static (string Name, TeachingLanguage? Lang, string Evaluation) CleanSubject(string text)
    {
        var s = text ?? "";
        string eval = "";
        if (Regex.IsMatch(s, "examen", RegexOptions.IgnoreCase)) eval = "Examen";
        else if (Regex.IsMatch(s, "colocviu", RegexOptions.IgnoreCase)) eval = "Colocviu";
        s = Regex.Replace(s, @"\(?\s*\b(examen|colocviu)\b\s*\)?", " ", RegexOptions.IgnoreCase);

        TeachingLanguage? lang = null;
        var lm = SubjectLangRx().Match(s);
        if (lm.Success)
        {
            var v = lm.Groups[1].Value.ToLowerInvariant();
            lang = v.StartsWith("ru") ? TeachingLanguage.RU : v.StartsWith("en") ? TeachingLanguage.EN : TeachingLanguage.RO;
        }
        s = SubjectLangRx().Replace(s, " ");
        s = Regex.Replace(s, @"^\s*P/O\s*\d+[\s(]*(S\.\d{2}\.[A-Z]\.\d{3})?[\s)]*:?\s*([a-z]\)\s*)?", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"^\s*\(?\s*S\.\d{2}\.[A-Z]\.\d{3}\s*\)?\s*:?\s*", "");
        s = Regex.Replace(s, @"\(\s*\)", " ");
        s = s.Replace("/", " ");
        s = Ro.CollapseSpaces(s).Trim(' ', ',', ';', '-', '.', ':');
        if (s.EndsWith(" (rom", StringComparison.OrdinalIgnoreCase)) s = s[..^5];
        return (s, lang, eval);
    }

    public static (string Name, string Title, string Department)? ParseTeacher(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = Ro.CollapseSpaces(text).Trim(',', ';', ' ');
        string dept = "";
        var dash = s.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            dept = s[(dash + 3)..].Trim();
            s = s[..dash].Trim();
        }
        var comma = s.IndexOf(',');
        var name = comma < 0 ? s : s[..comma];
        var title = comma < 0 ? "" : s[(comma + 1)..].Trim().Trim(',', ' ');
        name = name.Trim().Trim(',');
        if (name.Length == 0) return null;
        return (name, Ro.CollapseSpaces(title), dept);
    }

    private static int TryYear(string text)
    {
        var t = Ro.RemoveDiacritics(text ?? "");
        var m = Regex.Match(t, @"\banul\s+(\d)\b", RegexOptions.IgnoreCase);
        if (m.Success) return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var compact = Regex.Replace(t, @"\s+", "").ToUpperInvariant();
        var m2 = Regex.Match(compact, @"^ANUL([IVX]+)");
        return m2.Success ? Ro.FromRoman(m2.Groups[1].Value) : 0;
    }

    private static void ParseTitleInfo(string text, ImportPreview p)
    {
        var t = Ro.RemoveDiacritics(text);
        var ay = Regex.Match(t, @"(20\d{2})\s*-\s*(20\d{2})");
        if (ay.Success) p.AcademicYear ??= $"{ay.Groups[1].Value}-{ay.Groups[2].Value}";
        var dates = DateRx().Matches(t);
        if (dates.Count > 0 && p.SemesterStart == null &&
            DateTime.TryParseExact(dates[0].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            p.SemesterStart = start;
            p.AcademicYear ??= start.Month >= 8 ? $"{start.Year}-{start.Year + 1}" : $"{start.Year - 1}-{start.Year}";
        }
        var wm = Regex.Match(t, @"(\d{1,2})\s*saptamani", RegexOptions.IgnoreCase);
        if (wm.Success) p.Weeks ??= int.Parse(wm.Groups[1].Value, CultureInfo.InvariantCulture);
        var sm = Regex.Match(t, @"semestrul\s+([IVX]+)\b", RegexOptions.IgnoreCase);
        if (sm.Success) p.Semester ??= sm.Groups[1].Value.ToUpperInvariant();
    }

    /// <summary>Detectează doleanțe de tipul „Miercuri, de la 13:00 până la 15:00 profesorii să fie liberi".</summary>
    private static void DetectUnavailable(string text, ImportPreview p, ScheduleSettings? settings)
    {
        var t = Ro.RemoveDiacritics(text).ToLowerInvariant();
        string[] dayKeys = { "luni", "marti", "miercuri", "joi", "vineri", "sambata" };
        var days = dayKeys.Select((k, i) => (k, i)).Where(x => Regex.IsMatch(t, $@"\b{x.k}\b")).Select(x => x.i).ToList();
        var times = Regex.Matches(t, @"\b(\d{1,2})[:.](\d{2})\b")
                         .Select(m => new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 0)).ToList();
        if (days.Count == 0 || times.Count < 2) return;
        var from = times.Min();
        var to = times.Max();
        var slots = settings?.TimeSlots ?? ScheduleSettings.DefaultSlots();
        foreach (var day in days)
            foreach (var slot in slots)
                if (slot.StartTime < to && slot.EndTime > from)
                {
                    var key = new SlotKey(day, slot.Number);
                    if (!p.SuggestedUnavailable.Contains(key)) p.SuggestedUnavailable.Add(key);
                }
    }

    // ───────────────────────────── Aplicare ─────────────────────────────

    public static ImportResult Apply(ImportPreview p, Dataset d, bool replaceLoad, bool applyWishes, bool updateSettings)
    {
        var r = new ImportResult();
        if (replaceLoad)
        {
            d.Lessons.Clear();
            d.Activities.Clear();
        }
        if (updateSettings)
        {
            if (p.AcademicYear != null) d.Settings.AcademicYear = p.AcademicYear;
            if (p.SemesterStart != null) d.Settings.SemesterStart = p.SemesterStart.Value;
            if (p.Weeks != null) d.Settings.Weeks = p.Weeks.Value;
            if (p.Semester != null) d.Settings.Semester = p.Semester;
        }

        var subjects = new Dictionary<string, Subject>();
        foreach (var s in d.Subjects) subjects.TryAdd(Ro.Key(s.Name), s);
        var teachers = new Dictionary<string, Teacher>();
        foreach (var t in d.Teachers) teachers.TryAdd(Ro.Key(t.Name), t);
        var groups = new Dictionary<string, StudentGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in d.Groups) groups.TryAdd(g.Code, g);
        var wishTeachers = new HashSet<Guid>();
        int ayStart = ParseAyStart(p.AcademicYear ?? d.Settings.AcademicYear);

        foreach (var row in p.Rows.Where(x => x.Include))
        {
            var (name, _, _) = CleanSubject(row.Subject);
            if (name.Length == 0) name = row.Subject.Trim();
            if (name.Length == 0) continue;

            var sk = Ro.Key(name);
            if (!subjects.TryGetValue(sk, out var subj))
            {
                subj = new Subject
                {
                    Name = name,
                    ShortName = Abbrev.Make(name),
                    Color = Palette.SubjectColor(d.Subjects.Count),
                    Evaluation = row.Evaluation,
                    Department = row.Department,
                };
                d.Subjects.Add(subj);
                subjects[sk] = subj;
                r.Subjects++;
            }
            else if (string.IsNullOrEmpty(subj.Evaluation) && row.Evaluation.Length > 0)
            {
                subj.Evaluation = row.Evaluation;
            }

            Guid? teacherId = null;
            if (ParseTeacher(row.Teacher) is { } tp)
            {
                var tk = Ro.Key(tp.Name);
                if (!teachers.TryGetValue(tk, out var t))
                {
                    t = new Teacher
                    {
                        Name = tp.Name,
                        Title = tp.Title,
                        Department = tp.Department.Length > 0 ? tp.Department : row.Department,
                    };
                    d.Teachers.Add(t);
                    teachers[tk] = t;
                    r.Teachers++;
                }
                else if (string.IsNullOrEmpty(t.Title) && tp.Title.Length > 0)
                {
                    t.Title = tp.Title;
                }
                teacherId = t.Id;
                if (p.WishesFile != null && row.File == p.WishesFile) wishTeachers.Add(t.Id);
            }

            var codes = Audience.ParseList(row.Groups);
            foreach (var code in codes)
            {
                var baseCode = Audience.BaseCode(code);
                if (!groups.TryGetValue(baseCode, out var g))
                {
                    var prefix = new string(baseCode.TakeWhile(char.IsLetter).ToArray());
                    g = new StudentGroup
                    {
                        Code = baseCode,
                        Specialty = SpecialtyOf(prefix),
                        Cycle = row.Section == "Master" || prefix.StartsWith("SM", StringComparison.OrdinalIgnoreCase) ? "Master" : "Licență",
                        Year = row.Year > 0 ? row.Year : YearFromCode(baseCode, ayStart),
                        Language = row.Language,
                    };
                    d.Groups.Add(g);
                    groups[baseCode] = g;
                    r.Groups++;
                }
                if (Audience.IsSubgroup(code) && !g.Subgroups.Any(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)))
                {
                    g.Subgroups.Add(new Subgroup { Code = code, Students = codes.Count == 1 ? row.Students : 0 });
                    r.Subgroups++;
                }
            }

            if (codes.Count == 1 && row.Students > 0)
            {
                var code = codes[0];
                var g = groups[Audience.BaseCode(code)];
                if (Audience.IsSubgroup(code))
                {
                    var sg = g.Subgroups.First(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
                    if (sg.Students == 0) sg.Students = row.Students;
                }
                else if (g.Students == 0 || row.Type != LessonType.Curs)
                {
                    g.Students = Math.Max(g.Students, row.Students);
                }
            }

            bool duplicate = d.Activities.Any(a =>
                a.SubjectId == subj.Id && a.Type == row.Type && a.TeacherId == teacherId && a.HoursPerWeek == row.Hours &&
                a.Groups.Count == codes.Count && !a.Groups.Except(codes, StringComparer.OrdinalIgnoreCase).Any());
            if (duplicate)
            {
                r.Duplicates++;
                continue;
            }

            d.Activities.Add(new TeachingActivity
            {
                SubjectId = subj.Id,
                Type = row.Type,
                HoursPerWeek = Math.Clamp(row.Hours, 1, 12),
                TeacherId = teacherId,
                Groups = codes,
                Language = row.Language,
                Students = row.Students,
                Source = row.Source,
            });
            r.Activities++;
        }

        foreach (var g in d.Groups)
        {
            if (g.Students != 0 || g.Subgroups.Count == 0) continue;
            // subgrupele numerice (-1, -2) împart grupa; traseele (-SC, -WD) se pot suprapune cu ele
            var numeric = g.Subgroups.Where(s => Audience.SubgroupSuffix(s.Code).All(char.IsDigit)).ToList();
            g.Students = (numeric.Count > 0 ? numeric : g.Subgroups.ToList()).Sum(s => s.Students);
        }

        if (applyWishes && p.SuggestedUnavailable.Count > 0)
        {
            foreach (var t in d.Teachers.Where(t => wishTeachers.Contains(t.Id)))
                foreach (var key in p.SuggestedUnavailable)
                    if (!t.Unavailable.Contains(key))
                    {
                        t.Unavailable.Add(key);
                        r.WishSlots++;
                    }
        }
        return r;
    }

    private static int ParseAyStart(string? ay)
    {
        var m = Regex.Match(ay ?? "", @"(20\d{2})");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : DateTime.Today.Year;
    }

    private static int YearFromCode(string code, int ayStart)
    {
        var m = Regex.Match(code, @"(\d{2})\d{2}");
        if (!m.Success) return 1;
        int admission = 2000 + int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return Math.Clamp(ayStart - admission + 1, 1, 6);
    }

    private static string SpecialtyOf(string prefix) => prefix.ToUpperInvariant() switch
    {
        "I" => "Informatica",
        "IA" => "Informatica Aplicată",
        "M" => "Matematica",
        "DJ" => "Dezvoltarea jocurilor video",
        "IAFR" => "Informatica Aplicată (frecvență redusă)",
        "SMMSPA" => "Structuri matematice și metode de soluționare a problemelor aplicative",
        _ => "",
    };
}
