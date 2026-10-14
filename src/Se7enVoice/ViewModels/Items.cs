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
    private bool _isSelected;

    public Preset Preset { get; } = preset;
    public string Name => Preset.Name;
    public string Tagline => Preset.BuiltIn ? Preset.Tagline : "Mon preset";
    public string Icon => Preset.Icon;
    public bool IsUser => !Preset.BuiltIn;
    public Brush ToneBrush => Tones.Brush(Preset.Tone);

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
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
    private static readonly string[] Order = ["blaze", "surge", "neon", "volt", "boost"];

    public static Brush Brush(string tone) => tone switch
    {
        "volt" => Res("VoltBrush"),
        "blaze" => Res("BlazeBrush"),
        "surge" => Res("SurgeBrush"),
        "boost" => Res("BoostBrush"),
        "neon" => Res("NeonBrush"),
        "hazard" => Res("HazardBrush"),
        "violet" => Res("Violet200Brush"),
        _ => Res("PaperBrush"),
    };

    public static Brush Cycle(int i) => Brush(Order[((i % Order.Length) + Order.Length) % Order.Length]);

    private static Brush Res(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.White;
}
