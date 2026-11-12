using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OrarStudio.Services;

/// <summary>
/// Sursa unică de adevăr a aplicației: setul de date curent, istoricul Undo/Redo (instantanee JSON),
/// analiza conflictelor și salvarea automată.
/// </summary>
public sealed partial class DataStore : ObservableObject
{
    private const int MaxHistory = 80;
    private readonly List<(string Label, string Json)> _undo = new();
    private readonly List<(string Label, string Json)> _redo = new();
    private readonly DispatcherTimer _saveTimer;

    public Dataset Data { get; } = new();
    public Lookups Lk { get; private set; }
    public ConflictReport Report { get; private set; } = ConflictReport.Empty;
    public int Version { get; private set; }

    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private DateTime? _lastSaved;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;
    [ObservableProperty] private string _undoLabel = "";
    [ObservableProperty] private string _redoLabel = "";

    /// <summary>Se declanșează după orice modificare a datelor (editare, undo/redo, încărcare).</summary>
    public event EventHandler? Changed;

    public DataStore()
    {
        Lk = new Lookups(Data);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public string EffectivePath => FilePath ?? AppPaths.WorkspaceFile;

    /// <summary>Aplică o modificare ca pas anulabil (Ctrl+Z).</summary>
    public void Mutate(string label, Action<Dataset> change)
    {
        var before = Json.Serialize(Data, false);
        try
        {
            change(Data);
        }
        catch
        {
            Restore(before);
            AfterChange(save: false);
            throw;
        }
        _undo.Add((label, before));
        if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
        _redo.Clear();
        AfterChange(save: true);
    }

    public string? Undo()
    {
        if (_undo.Count == 0) return null;
        var (label, json) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add((label, Json.Serialize(Data, false)));
        Restore(json);
        AfterChange(save: true);
        return label;
    }

    public string? Redo()
    {
        if (_redo.Count == 0) return null;
        var (label, json) = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add((label, Json.Serialize(Data, false)));
        Restore(json);
        AfterChange(save: true);
        return label;
    }

    /// <summary>Încarcă un set de date nou (golește istoricul).</summary>
    public void Load(Dataset dataset, string? path, bool saveNow = false)
    {
        Normalize(dataset);
        Data.ReplaceContents(dataset);
        FilePath = path;
        _undo.Clear();
        _redo.Clear();
        AfterChange(save: false);
        if (saveNow) SaveNow();
    }

    public void Open(string path) => Load(ReadFile(path), path);

    public static Dataset ReadFile(string path)
    {
        var json = File.ReadAllText(path);
        var d = Json.Deserialize<Dataset>(json) ?? throw new InvalidDataException("Fișierul nu conține un orar valid.");
        Normalize(d);
        return d;
    }

    public void SaveAs(string path)
    {
        FilePath = path;
        SaveNow();
    }

    public void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            Data.Modified = DateTime.Now;
            AppPaths.WriteAllTextAtomic(EffectivePath, Json.Serialize(Data));
            LastSaved = DateTime.Now;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Log.Error(ex, "Salvare automată");
        }
    }

    /// <summary>Forțează reîmprospătarea (ex. după schimbarea setărilor de afișare).</summary>
    public void Touch() => AfterChange(save: false);

    private void Restore(string json)
    {
        var d = Json.Deserialize<Dataset>(json)!;
        Normalize(d);
        Data.ReplaceContents(d);
    }

    private void AfterChange(bool save)
    {
        Version++;
        Lk = new Lookups(Data);
        Report = ConflictService.Analyze(Data, Lk);
        CanUndo = _undo.Count > 0;
        CanRedo = _redo.Count > 0;
        UndoLabel = _undo.Count > 0 ? "Anulează: " + _undo[^1].Label : "Nimic de anulat";
        RedoLabel = _redo.Count > 0 ? "Refă: " + _redo[^1].Label : "Nimic de refăcut";
        if (save)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Repară datele incomplete (fișiere vechi sau editate manual).</summary>
    public static void Normalize(Dataset d)
    {
        d.Settings ??= new ScheduleSettings();
        if (d.Settings.TimeSlots == null || d.Settings.TimeSlots.Count == 0)
            d.Settings.TimeSlots = ScheduleSettings.DefaultSlots();
        if (d.Settings.DaysPerWeek is < 1 or > 7) d.Settings.DaysPerWeek = 5;
        if (d.Settings.Weeks <= 0) d.Settings.Weeks = 15;
        d.Rooms ??= new();
        d.Subjects ??= new();
        d.Teachers ??= new();
        d.Groups ??= new();
        d.Activities ??= new();
        d.Lessons ??= new();
        foreach (var t in d.Teachers) t.Unavailable ??= new();
        foreach (var g in d.Groups) g.Subgroups ??= new();
        foreach (var a in d.Activities) a.Groups ??= new();
        foreach (var l in d.Lessons) l.Groups ??= new();
    }
}
