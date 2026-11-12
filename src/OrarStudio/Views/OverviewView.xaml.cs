using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OrarStudio.UI;
using OrarStudio.ViewModels;

namespace OrarStudio.Views;

/// <summary>
/// Orarul general (ca pe panoul de la decanat): rânduri = zile × perechi, coloane = grupe.
/// Lecțiile comune mai multor grupe (cursuri pe serii) sunt unite pe mai multe coloane.
/// Grila este construită din cod pentru performanță; antetele rămân fixe la derulare.
/// </summary>
public partial class OverviewView : UserControl
{
    private OverviewViewModel? _vm;

    public OverviewView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm != null) _vm.Rebuilt -= OnRebuilt;
            _vm = e.NewValue as OverviewViewModel;
            if (_vm != null) _vm.Rebuilt += OnRebuilt;
        };
        Loaded += (_, _) => Build();
        Unloaded += (_, _) =>
        {
            if (_vm != null) _vm.Rebuilt -= OnRebuilt;
        };
    }

    private void OnRebuilt(object? sender, EventArgs e) => Build();

    private void Body_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        LeftScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void Build()
    {
        if (_vm == null || !IsLoaded) return;
        var store = AppServices.Store;
        var d = store.Data;
        var lk = store.Lk;
        var groups = _vm.VisibleGroups;
        bool compact = _vm.Compact;
        int days = TimetableBuilder.Days(d);
        var slots = d.Settings.TimeSlots.OrderBy(s => s.Number).ToList();
        if (_vm.HideEmptySlots)
        {
            var codes = groups.Select(g => g.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int maxUsed = d.Lessons.Where(l => l.Groups.Any(c => codes.Contains(Audience.BaseCode(c)))).Select(l => l.Slot).DefaultIfEmpty(0).Max();
            slots = slots.Where(s => s.Number <= Math.Max(maxUsed, 4)).ToList();
        }

        HeaderGrid.Children.Clear();
        HeaderGrid.ColumnDefinitions.Clear();
        LeftGrid.Children.Clear();
        LeftGrid.RowDefinitions.Clear();
        LeftGrid.ColumnDefinitions.Clear();
        BodyGrid.Children.Clear();
        BodyGrid.RowDefinitions.Clear();
        BodyGrid.ColumnDefinitions.Clear();
        Empty.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (groups.Count == 0) return;

        double colW = compact ? 132 : 176;
        const double dayW = 36, slotW = 74, headerH = 48;
        Corner.Width = dayW + slotW;
        Corner.Height = headerH;

        // antet: grupele
        for (int i = 0; i < groups.Count; i++)
        {
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });
            var g = groups[i];
            var head = new Border
            {
                Height = headerH,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(6, 4, 6, 4),
                Cursor = Cursors.Hand,
                ToolTip = $"{g.Code} — {g.Specialty}, anul {g.Year}, {g.Students} studenți. Dublu-clic: orarul grupei",
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        Text(g.Code, 13, FontWeights.Bold, "Brush.Text", HorizontalAlignment.Center),
                        Text($"anul {g.Year} · {Ro.LangShort(g.Language)}" + (g.Students > 0 ? $" · {g.Students} st." : ""), 10.5, FontWeights.Normal, "Brush.TextMuted", HorizontalAlignment.Center),
                    },
                },
            };
            head.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            head.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            var code = g.Code;
            head.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) _vm.OpenGroup(code, null); };
            Grid.SetColumn(head, i);
            HeaderGrid.Children.Add(head);
        }

        // coloanele stânga + corp
        LeftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(dayW) });
        LeftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(slotW) });
        for (int i = 0; i < groups.Count; i++)
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });

        var byCell = d.Lessons.GroupBy(l => (l.Day, l.Slot)).ToDictionary(g => g.Key, g => g.ToList());
        int row = 0;
        for (int day = 0; day < days; day++)
        {
            int dayStart = row;
            for (int si = 0; si < slots.Count; si++)
            {
                var slot = slots[si];
                byCell.TryGetValue((day, slot.Number), out var here);
                here ??= new List<Lesson>();
                var cells = groups.Select(g => here.Where(l => Audience.Concerns(l.Groups, g.Code))
                                                   .OrderBy(l => l.Week).ThenBy(l => Audience.Format(l.Groups)).ToList()).ToList();
                int maxCount = cells.Count == 0 ? 0 : cells.Max(c => c.Count);
                double height = maxCount == 0 ? (compact ? 30 : 38) : maxCount * (compact ? 32 : 50) + 8;
                LeftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
                BodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
                bool lastOfDay = si == slots.Count - 1;

                var slotCell = new Border
                {
                    BorderThickness = new Thickness(0, 0, 1, lastOfDay ? 2 : 1),
                    Child = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            Text(Ro.Roman(slot.Number), 13, FontWeights.Bold, "Brush.Accent", HorizontalAlignment.Center),
                            Text(slot.Label, 10, FontWeights.Normal, "Brush.TextMuted", HorizontalAlignment.Center),
                        },
                    },
                };
                slotCell.SetResourceReference(Border.BorderBrushProperty, lastOfDay ? "Brush.BorderStrong" : "Brush.GridLine");
                Grid.SetRow(slotCell, row);
                Grid.SetColumn(slotCell, 1);
                LeftGrid.Children.Add(slotCell);

                int i = 0;
                while (i < groups.Count)
                {
                    int span = 1;
                    if (cells[i].Count > 0)
                        while (i + span < groups.Count && SameLessons(cells[i], cells[i + span])) span++;
                    var cell = BuildCell(cells[i], span == 1 ? groups[i].Code : null, groups[i].Code, compact, lastOfDay, lk);
                    Grid.SetRow(cell, row);
                    Grid.SetColumn(cell, i);
                    Grid.SetColumnSpan(cell, span);
                    BodyGrid.Children.Add(cell);
                    i += span;
                }
                row++;
            }

            var dayCell = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 2),
                Child = new TextBlock
                {
                    Text = Ro.Day(day).ToUpperInvariant(),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    LayoutTransform = new RotateTransform(-90),
                },
            };
            dayCell.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
            dayCell.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderStrong");
            ((TextBlock)dayCell.Child).SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentSoftText");
            Grid.SetRow(dayCell, dayStart);
            Grid.SetRowSpan(dayCell, slots.Count);
            Grid.SetColumn(dayCell, 0);
            LeftGrid.Children.Add(dayCell);
        }
    }

    private static bool SameLessons(List<Lesson> a, List<Lesson> b) =>
        a.Count == b.Count && a.Select(x => x.Id).OrderBy(x => x).SequenceEqual(b.Select(x => x.Id).OrderBy(x => x));

    private Border BuildCell(List<Lesson> lessons, string? contextGroup, string anyGroup, bool compact, bool lastOfDay, Lookups lk)
    {
        var cell = new Border
        {
            BorderThickness = new Thickness(0, 0, 1, lastOfDay ? 2 : 1),
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
        };
        cell.SetResourceReference(Border.BorderBrushProperty, lastOfDay ? "Brush.BorderStrong" : "Brush.GridLine");
        cell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) _vm?.OpenGroup(anyGroup, null);
        };
        if (lessons.Count == 0) return cell;

        var stack = new StackPanel();
        foreach (var l in lessons)
        {
            var v = TimetableBuilder.View(l, lk, contextGroup);
            var (strong, soft) = l.Type switch
            {
                LessonType.Curs => ("Brush.Curs", "Brush.CursSoft"),
                LessonType.Seminar => ("Brush.Seminar", "Brush.SeminarSoft"),
                _ => ("Brush.Lab", "Brush.LabSoft"),
            };
            var strip = new Border { Width = 3, CornerRadius = new CornerRadius(2, 0, 0, 2) };
            strip.SetResourceReference(Border.BackgroundProperty, strong);

            var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = compact ? 11 : 11.5 };
            title.Inlines.Add(new System.Windows.Documents.Run(v.SubjectShort) { FontWeight = FontWeights.Bold });
            title.Inlines.Add(new System.Windows.Documents.Run($" ({Ro.TypeShort(v.Type)}) · {v.Room}"));
            title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

            var content = new StackPanel { Margin = new Thickness(6, 2, 4, 2) };
            content.Children.Add(title);
            var extra = new List<string>();
            if (!compact) extra.Add(v.Teacher);
            if (v.Week != WeekParity.Saptamanal) extra.Add(Ro.WeekShort(v.Week));
            if (v.Subgroup.Length > 0) extra.Add(v.Subgroup);
            if (extra.Count > 0)
            {
                var sub = new TextBlock { Text = string.Join(" · ", extra), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
                sub.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
                content.Children.Add(sub);
            }

            var panel = new DockPanel();
            DockPanel.SetDock(strip, Dock.Left);
            panel.Children.Add(strip);
            panel.Children.Add(content);

            var issues = AppServices.Store.Report.ByLesson[l.Id].Where(i => i.Severity == IssueSeverity.Error).ToList();
            var tip = new StringBuilder();
            tip.AppendLine($"{v.Subject} — {v.TypeLabel}");
            tip.AppendLine($"Profesor: {v.TeacherFull}");
            tip.AppendLine($"Grupe: {v.Groups}");
            tip.AppendLine($"Auditoriu: {v.Room} · {Ro.WeekLabel(v.Week)}");
            foreach (var i in issues) tip.AppendLine("⛔ " + i.Message);
            tip.Append("Dublu-clic: editare");

            var card = new Border
            {
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 2),
                Child = panel,
                ToolTip = tip.ToString(),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(issues.Count > 0 ? 1.5 : 0),
                MinHeight = compact ? 26 : 42,
            };
            card.SetResourceReference(Border.BackgroundProperty, soft);
            if (issues.Count > 0) card.SetResourceReference(Border.BorderBrushProperty, "Brush.Danger");
            var id = l.Id;
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount != 2) return;
                e.Handled = true;
                _vm?.EditLesson(id);
            };
            stack.Children.Add(card);
        }
        cell.Child = stack;
        return cell;
    }

    private static TextBlock Text(string text, double size, FontWeight weight, string brush, HorizontalAlignment align)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, HorizontalAlignment = align, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }
}
