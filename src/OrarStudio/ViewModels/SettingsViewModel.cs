using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

/// <summary>Setările orarului (instituție, semestru, perechi) și ale aplicației (temă, culori, date).</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    public override string Title => "Setări";
    public override string Icon => "";
    public override string Subtitle => "Instituția, semestrul, perechile, aspectul aplicației și fișierul de date";

    [ObservableProperty] private ScheduleSettings? _draft;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isDark;
    [ObservableProperty] private bool _colorBySubject;
    [ObservableProperty] private string _dataPath = "";
    [ObservableProperty] private string _dataInfo = "";

    public IReadOnlyList<int> DayOptions { get; } = new[] { 5, 6 };
    public IReadOnlyList<int> WeekOptions { get; } = Enumerable.Range(8, 13).ToArray();

    public override void Refresh()
    {
        Draft = Json.Clone(Store.Data.Settings);
        Error = null;
        IsDark = ThemeService.Current == "Dark";
        ColorBySubject = AppServices.Settings.ColorBySubject;
        DataPath = Store.EffectivePath;
        DataInfo = Store.LastSaved is DateTime t
            ? $"Salvare automată activă · ultima salvare la {t:HH:mm:ss}"
            : "Salvare automată activă";
    }

    partial void OnIsDarkChanged(bool value)
    {
        var theme = value ? "Dark" : "Light";
        if (ThemeService.Current == theme) return;
        ThemeService.Apply(theme);
        AppServices.Settings.Theme = theme;
        AppServices.Settings.Save();
        if (AppServices.Main != null) AppServices.Main.IsDark = value;
    }

    partial void OnColorBySubjectChanged(bool value)
    {
        if (AppServices.Settings.ColorBySubject == value) return;
        AppServices.Settings.ColorBySubject = value;
        AppServices.Settings.Save();
        Store.Touch();
    }

    [RelayCommand]
    private void Save()
    {
        if (Draft == null) return;
        var slots = Draft.TimeSlots.ToList();
        if (slots.Count == 0) { Error = "Definiți cel puțin o pereche."; return; }
        TimeSpan previousEnd = TimeSpan.Zero;
        foreach (var s in slots)
        {
            if (!TimeSpan.TryParse(s.Start.Replace('.', ':'), out var st) || !TimeSpan.TryParse(s.End.Replace('.', ':'), out var en))
            {
                Error = $"Ora perechii {Ro.Roman(s.Number)} nu este validă (format HH:mm).";
                return;
            }
            if (en <= st) { Error = $"Perechea {Ro.Roman(s.Number)}: ora de sfârșit trebuie să fie după cea de început."; return; }
            if (st < previousEnd) { Error = $"Perechea {Ro.Roman(s.Number)} se suprapune cu perechea anterioară."; return; }
            previousEnd = en;
            s.Start = st.ToString(@"hh\:mm");
            s.End = en.ToString(@"hh\:mm");
        }
        if (string.IsNullOrWhiteSpace(Draft.Institution)) { Error = "Introduceți denumirea instituției."; return; }
        int maxUsedSlot = Store.Data.Lessons.Select(l => l.Slot).DefaultIfEmpty(0).Max();
        if (maxUsedSlot > slots.Count &&
            !Dialogs.Confirm("Perechi folosite", $"Există lecții în perechea {Ro.Roman(maxUsedSlot)}, care nu va mai exista. Lecțiile vor rămâne, dar nu vor fi afișate. Continuați?", "Continuă"))
            return;
        int maxUsedDay = Store.Data.Lessons.Select(l => l.Day).DefaultIfEmpty(0).Max();
        if (maxUsedDay >= Draft.DaysPerWeek &&
            !Dialogs.Confirm("Zile folosite", $"Există lecții {Ro.Day(maxUsedDay).ToLowerInvariant()}, zi care nu va mai fi afișată. Continuați?", "Continuă"))
            return;

        for (int i = 0; i < slots.Count; i++) slots[i].Number = i + 1;
        var copy = Json.Clone(Draft);
        Store.Mutate("Modificare setări", d => d.Settings = copy);
        Toasts.Show("Setările au fost salvate.", ToastKind.Success, "Anulează", () => Store.Undo(), 4);
    }

    [RelayCommand]
    private void Reset() => Refresh();

    [RelayCommand]
    private void AddSlot()
    {
        if (Draft == null) return;
        var last = Draft.TimeSlots.LastOrDefault();
        var start = last != null ? last.EndTime + TimeSpan.FromMinutes(15) : new TimeSpan(8, 0, 0);
        Draft.TimeSlots.Add(new TimeSlot { Number = Draft.TimeSlots.Count + 1, Start = start.ToString(@"hh\:mm"), End = (start + TimeSpan.FromMinutes(90)).ToString(@"hh\:mm") });
    }

    [RelayCommand]
    private void RemoveSlot(TimeSlot? slot)
    {
        if (Draft == null || slot == null) return;
        Draft.TimeSlots.Remove(slot);
        for (int i = 0; i < Draft.TimeSlots.Count; i++) Draft.TimeSlots[i].Number = i + 1;
    }

    [RelayCommand]
    private void DefaultSlots()
    {
        if (Draft != null) Draft.TimeSlots = ScheduleSettings.DefaultSlots();
        OnPropertyChanged(nameof(Draft));
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        var path = Store.EffectivePath;
        if (File.Exists(path)) Dialogs.ShowInFolder(path);
        else Dialogs.OpenPath(Path.GetDirectoryName(path) ?? AppPaths.DataDir);
    }

    [RelayCommand]
    private void OpenSamples()
    {
        var dir = AppPaths.SamplesDir;
        Dialogs.OpenPath(Directory.Exists(dir) ? dir : AppContext.BaseDirectory);
    }
}
