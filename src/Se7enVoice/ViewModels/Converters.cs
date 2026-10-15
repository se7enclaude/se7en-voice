using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Se7enVoice.ViewModels;

public static class Converters
{
    public static IValueConverter BoolToVisibility { get; } = new Lambda(v => v is true);
    public static IValueConverter InverseBoolToVisibility { get; } = new Lambda(v => v is not true);
    public static IValueConverter NotEmptyToVisibility { get; } = new Lambda(v => v is string s && s.Length > 0);
    public static IValueConverter CountToVisibility { get; } = new Lambda(v => v is int n && n > 0);
    public static IValueConverter ZeroToVisibility { get; } = new Lambda(v => v is int n && n == 0);
    public static IValueConverter Not { get; } = new Negate();
    public static IValueConverter IsNotEmpty { get; } = new NonEmpty();
    /// <summary>État du moteur pour le badge de la barre du haut.</summary>
    public static IValueConverter LiveText { get; } = new Text(v => v is true ? "MOTEUR ACTIF" : "MICRO INACTIF");

    private sealed class Text(Func<object?, string> text) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => text(value);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class Negate : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    }

    private sealed class NonEmpty : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string s && s.Length > 0;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class Lambda(Func<object?, bool> visible) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            visible(value) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
