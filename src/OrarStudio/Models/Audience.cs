using System.Text.RegularExpressions;

namespace OrarStudio.Models;

/// <summary>
/// Lucrul cu codurile de grupe și subgrupe.
/// „I2301" = grupă întreagă; „I2301-1", „IA2101-SC" = subgrupe (submulțimi ale grupei).
/// Două subgrupe diferite ale aceleiași grupe NU se suprapun; grupa întreagă se suprapune cu oricare subgrupă.
/// </summary>
public static partial class Audience
{
    public static string BaseCode(string code)
    {
        var i = code.IndexOf('-');
        return i < 0 ? code : code[..i];
    }

    public static bool IsSubgroup(string code) => code.Contains('-');

    public static string SubgroupSuffix(string code)
    {
        var i = code.IndexOf('-');
        return i < 0 ? "" : code[(i + 1)..];
    }

    public static bool Overlaps(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        var ba = BaseCode(a);
        var bb = BaseCode(b);
        if (!string.Equals(ba, bb, StringComparison.OrdinalIgnoreCase)) return false;
        // aceeași grupă: conflict dacă una dintre ele este grupa întreagă
        return !IsSubgroup(a) || !IsSubgroup(b);
    }

    public static string? FirstOverlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        foreach (var x in a)
            foreach (var y in b)
                if (Overlaps(x, y)) return IsSubgroup(x) ? y : x;
        return null;
    }

    /// <summary>Lecția privește grupa (direct, printr-o subgrupă sau ca parte dintr-o serie)?</summary>
    public static bool Concerns(IEnumerable<string> codes, string groupCode) =>
        codes.Any(c => string.Equals(BaseCode(c), groupCode, StringComparison.OrdinalIgnoreCase));

    public static string Format(IEnumerable<string> codes) => string.Join(", ", codes);

    /// <summary>Parsare din text introdus de utilizator: „I2301, IA2301-1; M2301".</summary>
    public static List<string> ParseList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Split(new[] { ',', ';', ' ', '+', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim().ToUpperInvariant())
                   .Where(s => s.Length > 0)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    [GeneratedRegex(@"\b([A-Z]{1,8})\s?(\d{4,5})(?:-([A-Z0-9]{1,4}(?:-[A-Z0-9]{1,4})*))?\b")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"^\W*(?:subgr\.?\s*(\d)|(\d)\s*subgr)", RegexOptions.IgnoreCase)]
    private static partial Regex SubgroupRegex();

    /// <summary>
    /// Extrage codurile de grupe dintr-un text liber din fișierele de sarcină didactică, ex.:
    /// „I2301, IA2301, IA2302 ro - 94st.", „I2202 subgr. 1", „IA2102 1 subgr.", „SMMSPA 2301".
    /// </summary>
    public static List<string> Extract(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var matches = CodeRegex().Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var code = m.Groups[1].Value + m.Groups[2].Value;
            if (m.Groups[3].Success && m.Groups[3].Value.Length > 0)
            {
                code += "-" + m.Groups[3].Value;
            }
            else
            {
                int tailStart = m.Index + m.Length;
                int tailEnd = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                var tail = text[tailStart..tailEnd];
                var sm = SubgroupRegex().Match(tail);
                if (sm.Success)
                    code += "-" + (sm.Groups[1].Success && sm.Groups[1].Value != "" ? sm.Groups[1].Value : sm.Groups[2].Value);
            }
            if (!result.Contains(code, StringComparer.OrdinalIgnoreCase)) result.Add(code);
        }
        return result;
    }
}

/// <summary>Calendarul semestrului: numărul săptămânii și paritatea (impară/pară).</summary>
public static class SemesterCalendar
{
    public static DateTime MondayOf(DateTime date) => date.Date.AddDays(-Ro.DayIndex(date));

    public static int WeekNumber(ScheduleSettings s, DateTime date) =>
        (int)Math.Floor((MondayOf(date) - MondayOf(s.SemesterStart)).TotalDays / 7.0) + 1;

    public static WeekParity Parity(ScheduleSettings s, int week)
    {
        bool odd = Math.Abs(week) % 2 == 1;
        if (!s.FirstWeekOdd) odd = !odd;
        return odd ? WeekParity.Impara : WeekParity.Para;
    }

    public static bool InSemester(ScheduleSettings s, int week) => week >= 1 && week <= s.Weeks;

    public static DateTime SemesterEnd(ScheduleSettings s) => MondayOf(s.SemesterStart).AddDays(7 * s.Weeks - 3);

    /// <summary>Lecția are loc în săptămâna cu paritatea dată?</summary>
    public static bool OccursIn(WeekParity lessonWeek, WeekParity parity) =>
        lessonWeek == WeekParity.Saptamanal || lessonWeek == parity;
}
