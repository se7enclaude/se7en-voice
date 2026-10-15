using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Se7enVoice.Controls;

/// <summary>Propriétés attachées utilisées par les gabarits du thème (icône d'un bouton/onglet, sous-titre d'une carte).</summary>
public static class Ui
{
    /// <summary>Glyphe Segoe Fluent Icons affiché devant le texte.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetIcon(DependencyObject o) => (string)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string v) => o.SetValue(IconProperty, v);

    /// <summary>Sous-titre d'une carte (sous le titre, en gris).</summary>
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.RegisterAttached("Subtitle", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetSubtitle(DependencyObject o) => (string)o.GetValue(SubtitleProperty);
    public static void SetSubtitle(DependencyObject o, string v) => o.SetValue(SubtitleProperty, v);

    /// <summary>Halo coloré sous un bouton « gel ».</summary>
    public static readonly DependencyProperty GlowProperty =
        DependencyProperty.RegisterAttached("Glow", typeof(System.Windows.Media.Effects.Effect), typeof(Ui), new PropertyMetadata(null));
    public static System.Windows.Media.Effects.Effect? GetGlow(DependencyObject o) => (System.Windows.Media.Effects.Effect?)o.GetValue(GlowProperty);
    public static void SetGlow(DependencyObject o, System.Windows.Media.Effects.Effect? v) => o.SetValue(GlowProperty, v);

    /// <summary>Reflet clair en haut du bouton (aspect « gel »).</summary>
    public static readonly DependencyProperty SheenProperty =
        DependencyProperty.RegisterAttached("Sheen", typeof(bool), typeof(Ui), new PropertyMetadata(true));
    public static bool GetSheen(DependencyObject o) => (bool)o.GetValue(SheenProperty);
    public static void SetSheen(DependencyObject o, bool v) => o.SetValue(SheenProperty, v);
}

/// <summary>
/// Rayon « pilule » = moitié de la hauteur. WPF ne borne pas les rayons trop grands (999 donne un ovale),
/// on le calcule donc à partir de la hauteur réelle.
/// </summary>
public sealed class PillRadius : IValueConverter
{
    public static PillRadius Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new CornerRadius(value is double h && h > 0 ? h / 2 : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
