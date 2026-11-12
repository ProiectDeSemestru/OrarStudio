using System.Windows.Controls;
using System.Windows.Input;
using OrarStudio.ViewModels;

namespace OrarStudio.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
}

public partial class ActivitiesView : UserControl
{
    public ActivitiesView() => InitializeComponent();
}

public partial class GroupsView : UserControl
{
    public GroupsView() => InitializeComponent();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GroupsViewModel vm && vm.Selected != null) vm.OpenScheduleCommand.Execute(null);
    }
}

public partial class TeachersView : UserControl
{
    public TeachersView() => InitializeComponent();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TeachersViewModel vm && vm.Selected != null) vm.OpenScheduleCommand.Execute(null);
    }
}

public partial class SubjectsView : UserControl
{
    public SubjectsView() => InitializeComponent();
}

public partial class RoomsView : UserControl
{
    public RoomsView() => InitializeComponent();

    private void Rooms_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RoomsViewModel vm && vm.Selected != null) vm.OpenScheduleCommand.Execute(null);
    }
}

public partial class ExportView : UserControl
{
    public ExportView() => InitializeComponent();
}

public partial class WebApiView : UserControl
{
    public WebApiView() => InitializeComponent();
}

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
}
