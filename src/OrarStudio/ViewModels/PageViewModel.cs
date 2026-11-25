using CommunityToolkit.Mvvm.ComponentModel;
using OrarStudio.UI;

namespace OrarStudio.ViewModels;

/// <summary>Bază pentru paginile aplicației (reîmprospătare „leneșă" la modificarea datelor).</summary>
public abstract partial class PageViewModel : ObservableObject
{
    private bool _stale = true;

    protected static DataStore Store => AppServices.Store;
    protected static ToastService Toasts => AppServices.Toasts;

    public abstract string Title { get; }
    public abstract string Icon { get; }
    public virtual string Subtitle => "";

    public bool IsActive { get; private set; }

    public void Activate(object? parameter)
    {
        IsActive = true;
        if (_stale)
        {
            _stale = false;
            SafeRefresh();
        }
        OnNavigatedTo(parameter);
    }

    public void Deactivate()
    {
        IsActive = false;
        OnNavigatedFrom();
    }

    /// <summary>Apelat la orice modificare a datelor.</summary>
    public void OnDataChanged()
    {
        if (IsActive) SafeRefresh();
        else _stale = true;
    }

    private void SafeRefresh()
    {
        try
        {
            Refresh();
            OnPropertyChanged(nameof(Subtitle));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reîmprospătare " + GetType().Name);
        }
    }

    public abstract void Refresh();
    protected virtual void OnNavigatedTo(object? parameter) { }
    protected virtual void OnNavigatedFrom() { }
}

/// <summary>Element al meniului lateral.</summary>
public sealed partial class NavItem : ObservableObject
{
    public string Title { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Key { get; init; } = "";
    public PageViewModel? Page { get; init; }
    public bool IsHeader { get; init; }
    [ObservableProperty] private string? _badge;
    [ObservableProperty] private bool _badgeIsError;
}

/// <summary>Opțiune generică pentru selectoare (valoare + etichetă + pictogramă).</summary>
public sealed record Option<T>(T Value, string Label, string Icon = "");

/// <summary>Opțiune pentru selectorul de entitate din editorul de orar.</summary>
public sealed class EntityOption
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public object? Model { get; init; }
    /// <summary>Opțiunea „Toate grupele / Toți profesorii / Toate auditoriile" (vizualizare de ansamblu).</summary>
    public bool IsAll { get; init; }
    public override string ToString() => Label;
}

/// <summary>Bară orizontală pentru graficele din panoul de control.</summary>
public sealed class BarItem
{
    public string Label { get; init; } = "";
    public string Detail { get; init; } = "";
    public double Value { get; init; }
    public double Ratio { get; init; }
    public string ValueText { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public object? Tag { get; init; }
}
