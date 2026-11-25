using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using OrarStudio.UI;
using OrarStudio.ViewModels;

namespace OrarStudio.Views;

/// <summary>
/// Editorul de orar — evenimentele de interfață: clic (selecție), dublu-clic (editare / lecție nouă),
/// drag &amp; drop (plasare, mutare, copiere cu Ctrl, scoatere din orar), meniuri contextuale, tastatură.
/// </summary>
public partial class TimetableView : UserControl
{
    private const string PayloadFormat = "OrarStudio.DragPayload";
    private Point _dragStart;
    private object? _pendingPayload;
    private FrameworkElement? _pendingSource;
    private DragAdorner? _adorner;
    private AdornerLayer? _layer;
    private bool _dragging;

    public TimetableView()
    {
        InitializeComponent();
        Loaded += (_, _) => Focus();
    }

    private TimetableViewModel? Vm => DataContext as TimetableViewModel;

    // ───────────────────────── Cartonașe de lecții ─────────────────────────

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not LessonCardVM card || Vm == null) return;
        e.Handled = true;
        Focus();
        if (e.ClickCount == 2)
        {
            _pendingPayload = null;
            Vm.EditLesson(card);
            return;
        }
        Vm.Select(card);
        _pendingPayload = card;
        _pendingSource = fe;
        _dragStart = e.GetPosition(this);
    }

    private void Card_MouseMove(object sender, MouseEventArgs e) => TryStartDrag(e);

    private void Card_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not LessonCardVM card || Vm == null) return;
        e.Handled = true;
        Vm.Select(card);
        var menu = BuildLessonMenu(card);
        menu.PlacementTarget = fe;
        menu.IsOpen = true;
    }

    // ───────────────────────── Activități (panoul din stânga) ─────────────────────────

    private void Activity_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ActivityItemVM item || Vm == null) return;
        if (e.ClickCount == 2)
        {
            _pendingPayload = null;
            e.Handled = true;
            Vm.AutoPlace(item);
            return;
        }
        _pendingPayload = item;
        _pendingSource = fe;
        _dragStart = e.GetPosition(this);
    }

    private void Activity_MouseMove(object sender, MouseEventArgs e) => TryStartDrag(e);

    private void Activity_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not ActivityItemVM item || Vm == null) return;
        e.Handled = true;
        var menu = new ContextMenu();
        menu.Items.Add(Item("Plasează automat", "", () => Vm.AutoPlace(item), "dublu-clic"));
        menu.Items.Add(Item("Editează în sarcina didactică", "", () => Vm.EditActivity(item)));
        menu.PlacementTarget = fe;
        menu.IsOpen = true;
    }

    // ───────────────────────── Drag & drop ─────────────────────────

    private void TryStartDrag(MouseEventArgs e)
    {
        if (_dragging || _pendingPayload == null || _pendingSource == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        StartDrag(_pendingSource, _pendingPayload, e);
    }

    private void StartDrag(FrameworkElement source, object payload, MouseEventArgs e)
    {
        if (Vm == null) return;
        _dragging = true;
        try
        {
            Vm.BeginDrag(payload);
            if (payload is LessonCardVM) RemoveZone.Visibility = Visibility.Visible;
            _layer = AdornerLayer.GetAdornerLayer(RootGrid);
            if (_layer != null)
            {
                _adorner = new DragAdorner(RootGrid, source, e.GetPosition(source));
                _adorner.Move(e.GetPosition(RootGrid));
                _layer.Add(_adorner);
            }
            var data = new DataObject(PayloadFormat, payload);
            DragDrop.DoDragDrop(source, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            if (_adorner != null && _layer != null) _layer.Remove(_adorner);
            _adorner = null;
            _layer = null;
            RemoveZone.Visibility = Visibility.Collapsed;
            _pendingPayload = null;
            _pendingSource = null;
            _dragging = false;
            Vm.EndDrag();
        }
    }

    private static object? Payload(DragEventArgs e) =>
        e.Data.GetDataPresent(PayloadFormat) ? e.Data.GetData(PayloadFormat) : null;

    private void Root_PreviewDragOver(object sender, DragEventArgs e) => _adorner?.Move(e.GetPosition(RootGrid));

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        // nicio celulă sub cursor
        e.Effects = DragDropEffects.None;
        Vm?.HoverCell(null);
        e.Handled = true;
    }

    private void Cell_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not CellVM cell || Vm == null) return;
        var payload = Payload(e);
        if (payload == null)
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            bool copy = payload is LessonCardVM && (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            e.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move;
            Vm.HoverCell(cell);
        }
        e.Handled = true;
    }

    private void Cell_Drop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not CellVM cell || Vm == null) return;
        var payload = Payload(e);
        e.Handled = true;
        if (payload == null) return;
        bool copy = payload is LessonCardVM && (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
        Vm.EndDrag();
        Vm.Drop(payload, cell, copy);
    }

    private void Pending_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = Payload(e) is LessonCardVM ? DragDropEffects.Move : DragDropEffects.None;
        Vm?.HoverCell(null);
        e.Handled = true;
    }

    private void Pending_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (Payload(e) is LessonCardVM card) Vm?.DropOnPending(card);
    }

    // ───────────────────────── Celule ─────────────────────────

    private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not CellVM cell || Vm == null) return;
        Focus();
        if (e.ClickCount == 2)
        {
            e.Handled = true;
            Vm.NewLessonAt(cell);
        }
        else
        {
            Vm.Select(null);
        }
    }

    private void Cell_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not CellVM cell || Vm == null) return;
        e.Handled = true;
        var menu = new ContextMenu();
        menu.Items.Add(Item($"Adaugă lecție — {Ro.Day(cell.Day)}, perechea {Ro.Roman(cell.Slot)}…", "", () => Vm.NewLessonAt(cell), "dublu-clic"));
        if (Vm.Mode == ScheduleViewMode.Teacher)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(cell.IsUnavailable
                ? Item("Elimină indisponibilitatea (doleanța)", "", () => Vm.ToggleUnavailable(cell))
                : Item("Marchează intervalul ca indisponibil", "", () => Vm.ToggleUnavailable(cell)));
        }
        menu.PlacementTarget = fe;
        menu.IsOpen = true;
    }

    // ───────────────────────── Vizualizarea „Toate …" ─────────────────────────

    /// <summary>Antetele (coloanele și perechile) rămân fixe la derularea matricei.</summary>
    private void MatrixBody_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        MatrixHeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
        MatrixLeftScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    /// <summary>Dublu-clic pe antetul unei coloane: se deschide doar acea grupă / acel profesor / auditoriu.</summary>
    private void EntityHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { Tag: EntityHeaderVM header } || Vm == null) return;
        e.Handled = true;
        Vm.OpenEntity(header.Key);
    }

    // ───────────────────────── Meniul unei lecții ─────────────────────────

    private ContextMenu BuildLessonMenu(LessonCardVM card)
    {
        var vm = Vm!;
        var store = AppServices.Store;
        var menu = new ContextMenu();
        menu.Items.Add(Item("Editează…", "", () => vm.EditLesson(card), "Enter"));

        var weeks = new MenuItem { Header = "Periodicitate", Icon = Glyph("") };
        foreach (var (week, label) in new[] { (WeekParity.Saptamanal, "În fiecare săptămână"), (WeekParity.Impara, "Doar săptămânile impare"), (WeekParity.Para, "Doar săptămânile pare") })
        {
            var mi = Item(label, null, () => vm.SetWeek(card, week));
            mi.IsChecked = card.Week == week;
            weeks.Items.Add(mi);
        }
        menu.Items.Add(weeks);

        var rooms = new MenuItem { Header = "Schimbă auditoriul", Icon = Glyph("") };
        foreach (var (room, free, suitable) in vm.RoomChoices(card).Take(24))
        {
            var label = $"{room.Name} — {room.KindLabel.ToLowerInvariant()}, {room.Capacity} locuri" +
                        (!free ? " · ocupat" : !suitable ? " · nepotrivit" : "");
            var mi = Item(label, null, () => vm.SetRoom(card, room.Id));
            mi.IsChecked = card.Model.RoomId == room.Id;
            mi.IsEnabled = free || card.Model.RoomId == room.Id;
            rooms.Items.Add(mi);
        }
        if (card.Model.RoomId != null)
        {
            rooms.Items.Add(new Separator());
            rooms.Items.Add(Item("Fără auditoriu", null, () => vm.SetRoom(card, null)));
        }
        menu.Items.Add(rooms);

        menu.Items.Add(card.Locked
            ? Item("Deblochează", "", () => vm.ToggleLock(card), "Ctrl+L")
            : Item("Blochează (generatorul nu o mută)", "", () => vm.ToggleLock(card), "Ctrl+L"));

        menu.Items.Add(new Separator());
        var lesson = card.Model;
        // în „Toate …" se poate deschide și orarul unei singure coloane de același tip
        if (vm.Mode != ScheduleViewMode.Group || vm.IsMatrix)
            foreach (var g in lesson.Groups.Select(Audience.BaseCode).Distinct(StringComparer.OrdinalIgnoreCase).Take(4))
                menu.Items.Add(Item($"Deschide orarul grupei {g}", "", () => vm.OpenRelated(ScheduleViewMode.Group, g)));
        if ((vm.Mode != ScheduleViewMode.Teacher || vm.IsMatrix) && lesson.TeacherId is Guid tid)
            menu.Items.Add(Item($"Deschide orarul profesorului {store.Lk.TeacherName(tid)}", "", () => vm.OpenRelated(ScheduleViewMode.Teacher, tid.ToString())));
        if ((vm.Mode != ScheduleViewMode.Room || vm.IsMatrix) && lesson.RoomId is Guid rid)
            menu.Items.Add(Item($"Deschide orarul auditoriului {store.Lk.RoomName(rid)}", "", () => vm.OpenRelated(ScheduleViewMode.Room, rid.ToString())));
        if (lesson.ActivityId is Guid aid && AppServices.Main is { } main)
            menu.Items.Add(Item("Arată în sarcina didactică", "", () => main.Navigate(main.Activities, aid)));

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Scoate din orar", "", () => vm.DeleteLesson(card), "Del"));
        return menu;
    }

    private static MenuItem Item(string header, string? glyph, Action action, string? gesture = null)
    {
        var mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
        if (glyph != null) mi.Icon = Glyph(glyph);
        mi.Click += (_, _) => action();
        return mi;
    }

    private static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Font.Icons"),
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ───────────────────────── Tastatură ─────────────────────────

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm == null || e.OriginalSource is TextBox || e.OriginalSource is ComboBox || e.OriginalSource is ComboBoxItem) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var card = Vm.SelectedLesson;

        switch (key)
        {
            case Key.Delete when card != null:
                Vm.DeleteLesson(card);
                e.Handled = true;
                break;
            case Key.Enter when card != null:
                Vm.EditLesson(card);
                e.Handled = true;
                break;
            case Key.Escape:
                Vm.Select(null);
                e.Handled = true;
                break;
            case Key.L when ctrl && card != null:
                Vm.ToggleLock(card);
                e.Handled = true;
                break;
            case Key.Left when alt && card != null:
                Vm.MoveSelectedBy(-1, 0);
                e.Handled = true;
                break;
            case Key.Right when alt && card != null:
                Vm.MoveSelectedBy(1, 0);
                e.Handled = true;
                break;
            case Key.Up when alt && card != null:
                Vm.MoveSelectedBy(0, -1);
                e.Handled = true;
                break;
            case Key.Down when alt && card != null:
                Vm.MoveSelectedBy(0, 1);
                e.Handled = true;
                break;
            case Key.PageUp:
                Vm.PrevEntityCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageDown:
                Vm.NextEntityCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
