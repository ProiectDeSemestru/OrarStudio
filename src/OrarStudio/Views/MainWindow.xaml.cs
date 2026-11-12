using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using OrarStudio.UI;
using OrarStudio.ViewModels;

namespace OrarStudio.Views;

public partial class MainWindow : Window
{
    private int _dimCount;

    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizedState();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged oldVm) oldVm.PropertyChanged -= OnVmPropertyChanged;
            if (e.NewValue is INotifyPropertyChanged newVm) newVm.PropertyChanged += OnVmPropertyChanged;
        };
        Closing += OnClosing;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Estompează fereastra cât timp este deschis un dialog modal (contor pentru dialoguri imbricate).</summary>
    public void SetDimmed(bool dimmed)
    {
        _dimCount = Math.Max(0, _dimCount + (dimmed ? 1 : -1));
        Dim.Visibility = _dimCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.CurrentPage)) return;
        // tranziție discretă între pagini
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        PageHost.BeginAnimation(OpacityProperty, fade);
        PageShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
    }

    private void UpdateMaximizedState()
    {
        // la maximizare, fereastra cu WindowChrome depășește ecranul cu grosimea marginii
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurează" : "Maximizează";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu == null) return;
        MoreButton.ContextMenu.DataContext = DataContext;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.Placement = PlacementMode.Bottom;
        MoreButton.ContextMenu.IsOpen = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // datele sunt salvate automat; închidem și ferestrele secundare (panoul informativ)
        AppServices.Store.SaveNow();
        foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w != this).ToList())
        {
            try { w.Close(); } catch { /* ignorat */ }
        }
    }
}
