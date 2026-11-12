using System.Windows;
using System.Windows.Input;
using OrarStudio.ViewModels;

namespace OrarStudio.Views;

/// <summary>Panoul informativ pentru ecranele de pe hol: ceas, perechea curentă, ce are loc acum și ce urmează.</summary>
public partial class BoardWindow : Window
{
    private WindowState _previousState;
    private bool _fullscreen;

    public BoardWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateRowsPerPage();
        Loaded += (_, _) => UpdateRowsPerPage();
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
        DataContextChanged += (_, _) => UpdateHeader();
    }

    private BoardViewModel? Vm => DataContext as BoardViewModel;

    private void UpdateRowsPerPage()
    {
        if (Vm == null || TableHost.ActualHeight <= 0) return;
        Vm.RowsPerPage = Math.Max(3, (int)((TableHost.ActualHeight - 40) / 94));
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        if (Vm == null) return;
        Vm.PropertyChanged -= VmChanged;
        Vm.PropertyChanged += VmChanged;
        SetHeader();
    }

    private void VmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BoardViewModel.Mode)) SetHeader();
    }

    private void SetHeader() =>
        FirstHeader.Text = Vm?.Mode?.Value switch { "groups" => "GRUPA", "teachers" => "PROFESORUL", _ => "AUDITORIUL" };

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _previousState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
            Controls.Visibility = Visibility.Collapsed;
            _fullscreen = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _previousState;
            Controls.Visibility = Visibility.Visible;
            _fullscreen = false;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_fullscreen) ToggleFullscreen();
                else Close();
                e.Handled = true;
                break;
            case Key.Right:
            case Key.PageDown:
                Vm?.NextPageCommand.Execute(null);
                break;
            case Key.Left:
            case Key.PageUp:
                Vm?.PrevPageCommand.Execute(null);
                break;
        }
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void Next_Click(object sender, RoutedEventArgs e) => Vm?.NextPageCommand.Execute(null);
    private void Prev_Click(object sender, RoutedEventArgs e) => Vm?.PrevPageCommand.Execute(null);
}
