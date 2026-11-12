using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace OrarStudio.UI;

/// <summary>Proprietăți atașate folosite de stilurile din Themes/Controls.xaml.</summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static string? GetPlaceholder(DependencyObject o) => (string?)o.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject o, string? v) => o.SetValue(PlaceholderProperty, v);

    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached("HoverBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverBackground(DependencyObject o) => (Brush?)o.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject o, Brush? v) => o.SetValue(HoverBackgroundProperty, v);

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.RegisterAttached("PressedBackground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetPressedBackground(DependencyObject o) => (Brush?)o.GetValue(PressedBackgroundProperty);
    public static void SetPressedBackground(DependencyObject o, Brush? v) => o.SetValue(PressedBackgroundProperty, v);

    public static readonly DependencyProperty HoverForegroundProperty =
        DependencyProperty.RegisterAttached("HoverForeground", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetHoverForeground(DependencyObject o) => (Brush?)o.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(DependencyObject o, Brush? v) => o.SetValue(HoverForegroundProperty, v);

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(8)));
    public static CornerRadius GetCornerRadius(DependencyObject o) => (CornerRadius)o.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject o, CornerRadius v) => o.SetValue(CornerRadiusProperty, v);
}

/// <summary>
/// Definiții de rânduri/coloane pentru un Grid folosit ca ItemsPanel: „auto 120 *150 *150 …"
/// (auto = Auto, număr = pixeli, *N = stea cu minim N).
/// </summary>
public static class GridDefs
{
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.RegisterAttached("Columns", typeof(string), typeof(GridDefs), new PropertyMetadata(null, OnColumns));
    public static string? GetColumns(DependencyObject o) => (string?)o.GetValue(ColumnsProperty);
    public static void SetColumns(DependencyObject o, string? v) => o.SetValue(ColumnsProperty, v);

    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.RegisterAttached("Rows", typeof(string), typeof(GridDefs), new PropertyMetadata(null, OnRows));
    public static string? GetRows(DependencyObject o) => (string?)o.GetValue(RowsProperty);
    public static void SetRows(DependencyObject o, string? v) => o.SetValue(RowsProperty, v);

    private static void OnColumns(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid g) return;
        g.ColumnDefinitions.Clear();
        foreach (var (len, min) in Parse(e.NewValue as string))
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = len, MinWidth = min });
    }

    private static void OnRows(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid g) return;
        g.RowDefinitions.Clear();
        foreach (var (len, min) in Parse(e.NewValue as string))
            g.RowDefinitions.Add(new RowDefinition { Height = len, MinHeight = min });
    }

    private static IEnumerable<(GridLength, double)> Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) yield break;
        foreach (var token in spec.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("auto", StringComparison.OrdinalIgnoreCase)) { yield return (GridLength.Auto, 0); continue; }
            if (token.StartsWith('*'))
            {
                double min = token.Length > 1 && double.TryParse(token[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 0;
                yield return (new GridLength(1, GridUnitType.Star), min);
                continue;
            }
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                yield return (new GridLength(px), 0);
        }
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool b = value is bool v && v;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => value is Visibility v && (v == Visibility.Visible) != Invert;
}

/// <summary>Maximul dintre mai multe valori numerice (ex. lățimea zonei vizibile și lățimea minimă a grilei).</summary>
public sealed class MaxConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        double max = 0;
        foreach (var v in values)
            if (v is double d && !double.IsNaN(d) && !double.IsInfinity(d)) max = Math.Max(max, d);
        return max;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => !(value is bool b && b);
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type t, object p, CultureInfo c)
    {
        bool visible = value != null && !(value is string s && s.Length == 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool visible = value is int n && n > 0;
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Etichete românești pentru enumerări.</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value switch
    {
        LessonType lt => Ro.TypeLabel(lt),
        WeekParity w => w == WeekParity.Saptamanal ? "Săptămânal" : w == WeekParity.Impara ? "Impară" : "Pară",
        RoomKind k => Ro.KindLabel(k),
        TeachingLanguage l => Ro.LangLabel(l),
        ScheduleViewMode m => m switch { ScheduleViewMode.Group => "Grupă", ScheduleViewMode.Teacher => "Profesor", _ => "Auditoriu" },
        null => "",
        _ => value.ToString() ?? "",
    };
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static SolidColorBrush Get(string? hex)
    {
        hex ??= "#6366F1";
        if (Cache.TryGetValue(hex, out var b)) return b;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            b = new SolidColorBrush(color);
        }
        catch
        {
            b = new SolidColorBrush(Color.FromRgb(0x63, 0x66, 0xF1));
        }
        b.Freeze();
        Cache[hex] = b;
        return b;
    }

    public object Convert(object value, Type t, object p, CultureInfo c) => Get(value as string);
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>true dacă valoarea este egală cu parametrul (pentru butoane radio legate la enumerări).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value != null && p != null && string.Equals(value.ToString(), p.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type t, object p, CultureInfo c)
    {
        if (value is true && p != null)
        {
            var target = Nullable.GetUnderlyingType(t) ?? t;
            return target.IsEnum ? Enum.Parse(target, p.ToString()!, true) : p;
        }
        return Binding.DoNothing;
    }
}

/// <summary>Raport 0..1 → lungime stea (pentru bare de progres / grafice); parametrul „rest" dă 1 − raport.</summary>
public sealed class StarLengthConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        double r = value switch { double d => d, float f => f, int i => i, _ => 0 };
        r = Math.Clamp(r, 0, 1);
        if (p is string s && s == "rest") r = 1 - r;
        return new GridLength(Math.Max(r, 0.0001), GridUnitType.Star);
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var s = (value as string ?? "").Trim();
        if (s.Length == 0) return "?";
        var parts = s.Split(new[] { ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant();
        return (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Comută tema (luminoasă/întunecată) înlocuind dicționarul de culori.</summary>
public static class ThemeService
{
    public static string Current { get; private set; } = "Light";
    public static event EventHandler? Changed;

    public static void Apply(string theme)
    {
        theme = theme == "Dark" ? "Dark" : "Light";
        var dicts = Application.Current.Resources.MergedDictionaries;
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{theme}.xaml", UriKind.Absolute) };
        if (dicts.Count > 0) dicts[0] = dict;
        else dicts.Add(dict);
        Current = theme;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Brush Brush(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
}
