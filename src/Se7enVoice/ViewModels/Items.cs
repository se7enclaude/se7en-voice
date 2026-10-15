using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Se7enVoice.Core.Presets;

namespace Se7enVoice.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}

public sealed class PresetItem(Preset preset) : Observable
{
    private bool _isSelected, _isFavorite;

    public Preset Preset { get; } = preset;
    public string Name => Preset.Name;
    public string Tagline => Preset.BuiltIn ? Preset.Tagline : "Mon preset";
    public string Icon => string.IsNullOrEmpty(Preset.Icon) ? "" : Preset.Icon;
    public string Category => Preset.BuiltIn ? Preset.Category : PresetCategories.Mine;
    public bool IsUser => !Preset.BuiltIn;
    public Brush ToneBrush => Tones.Brush(Preset.Tone);

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsFavorite { get => _isFavorite; set => Set(ref _isFavorite, value); }
}

public sealed class SoundItem(string name, string path, int index) : Observable
{
    private int _index = index;

    public string Name { get; } = name;
    public string Path { get; } = path;

    public int Index
    {
        get => _index;
        set { if (Set(ref _index, value)) Raise(nameof(Hotkey)); }
    }

    public string Hotkey => Index < 9 ? $"CTRL+ALT+{Index + 1}" : "";
    public Brush ToneBrush => Tones.Cycle(Index);
}

public static class Tones
{
    private static readonly string[] Order = ["violet", "mint", "peach", "sky", "rose"];

    /// <summary>
    /// Teintes des presets (noms historiques gardés pour les presets déjà partagés) vers les dégradés
    /// « puits d'icône » du design Centrale Audio : violet, menthe, rose, pêche, ciel, neutre.
    /// </summary>
    public static Brush Brush(string tone) => tone switch
    {
        "volt" or "violet" or "iris" => Res("ToneViolet"),
        "blaze" or "amber" or "peach" => Res("TonePeach"),
        "surge" or "sky" => Res("ToneSky"),
        "boost" or "mint" => Res("ToneMint"),
        "neon" or "rose" or "hazard" or "coral" => Res("ToneRose"),
        _ => Res("ToneNeutral"),
    };

    public static Brush Cycle(int i) => Brush(Order[((i % Order.Length) + Order.Length) % Order.Length]);

    private static Brush Res(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
