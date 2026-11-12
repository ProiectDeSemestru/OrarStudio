using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OrarStudio.Views;

namespace OrarStudio.UI;

/// <summary>Dialoguri: confirmări stilizate, selectoare de fișiere, deschidere fișiere/adrese.</summary>
public static class Dialogs
{
    public static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    /// <summary>Afișează o fereastră modală peste fereastra principală (care este estompată).</summary>
    public static bool? ShowModal(Window dialog)
    {
        var owner = Owner;
        if (owner != null && owner != dialog && owner.IsVisible) dialog.Owner = owner;
        dialog.WindowStartupLocation = dialog.Owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        var main = Application.Current?.MainWindow as MainWindow;
        main?.SetDimmed(true);
        try { return dialog.ShowDialog(); }
        finally { main?.SetDimmed(false); }
    }

    public static bool Confirm(string title, string message, string ok = "Da", string cancel = "Anulează", bool danger = false) =>
        ShowModal(new MessageWindow(title, message, ok, cancel, danger ? MessageKind.Danger : MessageKind.Question)) == true;

    public static void Info(string title, string message) =>
        ShowModal(new MessageWindow(title, message, "Am înțeles", null, MessageKind.Info));

    public static void Error(string title, string message) =>
        ShowModal(new MessageWindow(title, message, "Închide", null, MessageKind.Danger));

    private static bool? Show(CommonDialog dlg)
    {
        var owner = Owner;
        return owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
    }

    public static string[]? OpenFiles(string filter, bool multiselect = false, string? title = null)
    {
        var dlg = new OpenFileDialog { Filter = filter, Multiselect = multiselect, Title = title ?? "Deschide" };
        return Show(dlg) == true ? dlg.FileNames : null;
    }

    public static string? SaveFile(string filter, string fileName, string? title = null, string? initialDir = null)
    {
        var dlg = new SaveFileDialog
        {
            Filter = filter,
            FileName = fileName,
            Title = title ?? "Salvează",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = initialDir ?? AppPaths.DocumentsDir,
        };
        return Show(dlg) == true ? dlg.FileName : null;
    }

    public static string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title, InitialDirectory = AppPaths.DocumentsDir };
        return Show(dlg) == true ? dlg.FolderName : null;
    }

    public static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            Log.Error(ex, "Deschidere " + path);
            AppServices.Toasts.Show("Nu s-a putut deschide: " + path, ToastKind.Error);
        }
    }

    public static void ShowInFolder(string path)
    {
        try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
        catch (Exception ex) { Log.Error(ex, "Explorer"); }
    }

    public static void CopyText(string text, string what = "Textul")
    {
        try
        {
            Clipboard.SetText(text);
            AppServices.Toasts.Show($"{what} a fost copiat în clipboard.", ToastKind.Success, seconds: 3);
        }
        catch (Exception ex) { Log.Error(ex, "Clipboard"); }
    }

    /// <summary>Mută o fereastră fără bordură trăgând de antet.</summary>
    public static void EnableDrag(Window window, UIElement handle)
    {
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.ClickCount == 1)
            {
                try { window.DragMove(); } catch { /* ignorat */ }
            }
        };
    }
}

/// <summary>Adorner care afișează o „fantomă" a elementului tras cu mouse-ul (drag &amp; drop).</summary>
public sealed class DragAdorner : Adorner
{
    private readonly VisualBrush _brush;
    private readonly Size _size;
    private readonly Point _grab;
    private Point _position;

    public DragAdorner(UIElement adornedElement, FrameworkElement source, Point grabOffset) : base(adornedElement)
    {
        _brush = new VisualBrush(source) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
        _size = new Size(Math.Max(1, source.ActualWidth), Math.Max(1, source.ActualHeight));
        _grab = grabOffset;
        IsHitTestVisible = false;
    }

    public void Move(Point position)
    {
        _position = position;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(new Point(_position.X - _grab.X, _position.Y - _grab.Y), _size);
        dc.PushOpacity(0.85);
        dc.DrawRoundedRectangle(_brush, new Pen(new SolidColorBrush(Color.FromArgb(120, 79, 70, 229)), 1.5), rect, 8, 8);
        dc.Pop();
    }
}
