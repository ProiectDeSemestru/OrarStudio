using System.Windows;
using System.Windows.Input;
using OrarStudio.UI;
using OrarStudio.ViewModels;

namespace OrarStudio.Views;

internal static class WindowExtensions
{
    /// <summary>Închide fereastra cu rezultat (funcționează și dacă nu a fost deschisă modal).</summary>
    public static void CloseWith(this Window window, bool ok)
    {
        try { window.DialogResult = ok; }
        catch (InvalidOperationException) { window.Close(); }
    }
}

/// <summary>Dialogul de editare a unei lecții (deschis prin dublu-clic în grilă).</summary>
public partial class LessonEditorWindow : Window
{
    public LessonEditorWindow()
    {
        InitializeComponent();
        Dialogs.EnableDrag(this, Header);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LessonEditorViewModel old) old.CloseRequested -= Finish;
            if (e.NewValue is LessonEditorViewModel vm) vm.CloseRequested += Finish;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Finish(false);
        };
    }

    private void Finish(bool ok) => this.CloseWith(ok);
}

/// <summary>Previzualizarea importului sarcinii didactice.</summary>
public partial class ImportWindow : Window
{
    public ImportWindow()
    {
        InitializeComponent();
        Dialogs.EnableDrag(this, Header);
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ImportViewModel vm) vm.CloseRequested += ok => this.CloseWith(ok);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && Keyboard.FocusedElement is not System.Windows.Controls.TextBox) this.CloseWith(false);
        };
    }
}

/// <summary>Opțiunile generării automate.</summary>
public partial class AutoScheduleWindow : Window
{
    public AutoScheduleWindow()
    {
        InitializeComponent();
        Dialogs.EnableDrag(this, Header);
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is AutoScheduleViewModel vm) vm.CloseRequested += ok => this.CloseWith(ok);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) this.CloseWith(false);
        };
    }
}
