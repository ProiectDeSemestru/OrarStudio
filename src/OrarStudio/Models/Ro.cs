using System.Globalization;
using System.Text;

namespace OrarStudio.Models;

/// <summary>Etichete în limba română și utilitare de text.</summary>
public static class Ro
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("ro-RO");

    public static readonly string[] Days = { "Luni", "Marți", "Miercuri", "Joi", "Vineri", "Sâmbătă", "Duminică" };
    public static readonly string[] DaysShort = { "Lun", "Mar", "Mie", "Joi", "Vin", "Sâm", "Dum" };

    public static string Day(int index) => index >= 0 && index < Days.Length ? Days[index] : "?";
    public static string DayShort(int index) => index >= 0 && index < DaysShort.Length ? DaysShort[index] : "?";

    /// <summary>Indexul zilei (0 = luni … 6 = duminică).</summary>
    public static int DayIndex(DateTime date) => ((int)date.DayOfWeek + 6) % 7;

    public static string Roman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII",
        8 => "VIII", 9 => "IX", 10 => "X", 11 => "XI", 12 => "XII", _ => n.ToString(),
    };

    public static int FromRoman(string s)
    {
        var map = new Dictionary<char, int> { ['I'] = 1, ['V'] = 5, ['X'] = 10 };
        int total = 0, prev = 0;
        foreach (var ch in Enumerable.Reverse(s.ToUpperInvariant().ToCharArray()))
        {
            if (!map.TryGetValue(ch, out var v)) return 0;
            total += v < prev ? -v : v;
            prev = Math.Max(prev, v);
        }
        return total;
    }

    public static string TypeLabel(LessonType t) => t switch
    {
        LessonType.Curs => "Curs",
        LessonType.Seminar => "Seminar",
        _ => "Laborator",
    };

    public static string TypeShort(LessonType t) => t switch
    {
        LessonType.Curs => "C",
        LessonType.Seminar => "S",
        _ => "L",
    };

    public static string WeekLabel(WeekParity w) => w switch
    {
        WeekParity.Saptamanal => "Săptămânal",
        WeekParity.Impara => "Săptămâna impară",
        _ => "Săptămâna pară",
    };

    public static string WeekShort(WeekParity w) => w switch
    {
        WeekParity.Saptamanal => "",
        WeekParity.Impara => "impar",
        _ => "par",
    };

    public static string KindLabel(RoomKind k) => k switch
    {
        RoomKind.Amfiteatru => "Amfiteatru",
        RoomKind.SalaSeminar => "Sală de seminar",
        RoomKind.LaboratorInformatica => "Laborator informatică",
        RoomKind.LaboratorSpecializat => "Laborator specializat",
        _ => "Sală universală",
    };

    public static string LangLabel(TeachingLanguage l) => l switch
    {
        TeachingLanguage.RO => "Română",
        TeachingLanguage.RU => "Rusă",
        _ => "Engleză",
    };

    public static string LangShort(TeachingLanguage l) => l switch
    {
        TeachingLanguage.RO => "ro",
        TeachingLanguage.RU => "ru",
        _ => "en",
    };

    /// <summary>Acord numeral românesc: „1 lecție", „5 lecții", „20 de lecții".</summary>
    public static string Count(int n, string one, string many)
    {
        if (n == 1) return $"1 {one}";
        int r = Math.Abs(n) % 100;
        bool de = n != 0 && (r == 0 && Math.Abs(n) >= 100 || r >= 20);
        return de ? $"{n} de {many}" : $"{n} {many}";
    }

    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Cheie de comparare: fără diacritice, litere mici, doar litere/cifre și un singur spațiu.</summary>
    public static string Key(string text)
    {
        // „â" și „î" notează același sunet (ortografii diferite: Hâncu / Hîncu)
        var s = RemoveDiacritics((text ?? "").Replace('â', 'î').Replace('Â', 'Î')).ToLowerInvariant();
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == '+') { sb.Append(ch); space = false; }
            else if (!space && sb.Length > 0) { sb.Append(' '); space = true; }
        }
        return sb.ToString().Trim();
    }

    /// <summary>Căutare tolerantă (fără diacritice, fără majuscule).</summary>
    public static bool Matches(string? haystack, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle)) return true;
        if (string.IsNullOrEmpty(haystack)) return false;
        return RemoveDiacritics(haystack).Contains(RemoveDiacritics(needle.Trim()), StringComparison.OrdinalIgnoreCase);
    }

    public static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) { if (!space) sb.Append(' '); space = true; }
            else { sb.Append(ch); space = false; }
        }
        return sb.ToString().Trim();
    }
}

/// <summary>Generare abrevieri pentru discipline (ex. „Algoritmi și structuri de date" → „ASD").</summary>
public static class Abbrev
{
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "și", "si", "şi", "de", "în", "in", "pe", "a", "al", "ale", "la", "cu", "pentru", "din", "lui",
        "limbajul", "the", "of", "and", "or", "for", "to",
    };

    public static string Make(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var tokens = name.Split(new[] { ' ', ',', '(', ')', '/', ':' }, StringSplitOptions.RemoveEmptyEntries)
                         .Where(t => !Stop.Contains(t)).ToList();
        if (tokens.Count == 0) return name.Length <= 4 ? name : name[..4];
        if (tokens.Count == 1)
        {
            var t = tokens[0];
            return IsAcronym(t) ? t : (t.Length <= 6 ? t : t[..1].ToUpperInvariant() + t[1..Math.Min(4, t.Length)].ToLowerInvariant() + ".");
        }

        // bucăți: (text, este acronim original?)
        var chunks = new List<(string Text, bool Acronym)>();
        var initials = new StringBuilder();
        foreach (var t in tokens)
        {
            if (IsAcronym(t))
            {
                if (initials.Length > 0) { chunks.Add((initials.ToString(), false)); initials.Clear(); }
                chunks.Add((t, true));
            }
            else if (char.IsLetter(t[0]))
            {
                initials.Append(char.ToUpperInvariant(t[0]));
            }
        }
        if (initials.Length > 0) chunks.Add((initials.ToString(), false));

        var result = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (i > 0) result.Append(chunks[i - 1].Acronym && chunks[i].Acronym ? "/" : " ");
            result.Append(chunks[i].Text);
        }
        var r = result.ToString();
        return r.Length > 12 ? r.Replace(" ", "")[..Math.Min(12, r.Replace(" ", "").Length)] : r;
    }

    private static bool IsAcronym(string t) =>
        t.Length >= 2 && (t.Count(char.IsUpper) >= 2 || t.Any(char.IsDigit) || t.Contains('+') || t.Contains('#'));
}

/// <summary>Paletă de culori pentru discipline.</summary>
public static class Palette
{
    public static readonly string[] Subjects =
    {
        "#6366F1", "#0EA5E9", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#14B8A6", "#F97316",
        "#EC4899", "#84CC16", "#06B6D4", "#A855F7", "#22C55E", "#EAB308", "#3B82F6", "#F43F5E",
    };

    public static string SubjectColor(int index) => Subjects[Math.Abs(index) % Subjects.Length];
}
