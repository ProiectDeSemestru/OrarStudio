using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.Export;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

/// <summary>Orarul general: matricea (zile × perechi) × grupe, ca pe panoul de la decanat.</summary>
public sealed partial class OverviewViewModel : PageViewModel
{
    private bool _loading;

    public override string Title => "Orarul general";
    public override string Icon => "";
    public override string Subtitle => $"{Ro.Count(VisibleGroups.Count, "grupă", "grupe")} · cursurile comune (serii) sunt unite pe mai multe coloane";

    public ObservableCollection<Option<string>> YearFilters { get; } = new();
    [ObservableProperty] private Option<string>? _yearFilter;
    [ObservableProperty] private bool _compact;
    [ObservableProperty] private bool _hideEmptySlots = true;

    public List<StudentGroup> VisibleGroups { get; private set; } = new();

    /// <summary>Vizualizarea reconstruiește grila la acest eveniment.</summary>
    public event EventHandler? Rebuilt;

    partial void OnYearFilterChanged(Option<string>? value)
    {
        if (!_loading) Rebuild();
    }

    partial void OnCompactChanged(bool value) => Rebuild();
    partial void OnHideEmptySlotsChanged(bool value) => Rebuild();

    public override void Refresh()
    {
        _loading = true;
        var keep = YearFilter?.Value ?? "all";
        YearFilters.Clear();
        YearFilters.Add(new Option<string>("all", "Toate"));
        foreach (var g in TimetableBuilder.OrderedGroups(Store.Data).Select(g => (g.Cycle, g.Year)).Distinct())
            YearFilters.Add(new Option<string>($"{g.Cycle}|{g.Year}", g.Cycle == "Licență" ? $"Anul {g.Year}" : $"{g.Cycle} {g.Year}"));
        YearFilter = YearFilters.FirstOrDefault(f => f.Value == keep) ?? YearFilters[0];
        _loading = false;
        Rebuild();
    }

    private void Rebuild()
    {
        var all = TimetableBuilder.OrderedGroups(Store.Data);
        VisibleGroups = YearFilter == null || YearFilter.Value == "all"
            ? all.ToList()
            : all.Where(g => $"{g.Cycle}|{g.Year}" == YearFilter.Value).ToList();
        OnPropertyChanged(nameof(Subtitle));
        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    public void OpenGroup(string code, Guid? lessonId)
    {
        var main = AppServices.Main;
        main?.Navigate(main.Timetable, new TimetableTarget(ScheduleViewMode.Group, code, lessonId));
    }

    public void EditLesson(Guid lessonId)
    {
        var lesson = Store.Data.Lessons.FirstOrDefault(l => l.Id == lessonId);
        if (lesson == null) return;
        Dialogs.ShowModal(new Views.LessonEditorWindow { DataContext = LessonEditorViewModel.ForExisting(lesson) });
    }

    [RelayCommand]
    private async Task ExportPdf()
    {
        var path = Dialogs.SaveFile("Document PDF (*.pdf)|*.pdf", "Orar general.pdf", "Exportă orarul general");
        if (path == null || AppServices.Main == null) return;
        var copy = Json.Clone(Store.Data);
        var codes = VisibleGroups.Select(g => g.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await AppServices.Main.RunBusy("Se generează PDF…", () => Task.Run(() =>
        {
            var lk = new Lookups(copy);
            var groups = TimetableBuilder.OrderedGroups(copy).Where(g => codes.Contains(g.Code)).ToList();
            File.WriteAllBytes(path, PdfExporter.Overview(copy, lk, groups));
        }));
        Toasts.Show($"PDF salvat: {Path.GetFileName(path)}", ToastKind.Success, "Deschide", () => Dialogs.OpenPath(path));
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        var path = Dialogs.SaveFile("Registru Excel (*.xlsx)|*.xlsx", "Orar general.xlsx", "Exportă în Excel");
        if (path == null || AppServices.Main == null) return;
        var copy = Json.Clone(Store.Data);
        await AppServices.Main.RunBusy("Se generează registrul Excel…", () => Task.Run(() => ExcelExporter.ExportWorkbook(copy, new Lookups(copy), path)));
        Toasts.Show($"Excel salvat: {Path.GetFileName(path)}", ToastKind.Success, "Deschide", () => Dialogs.OpenPath(path));
    }
}
