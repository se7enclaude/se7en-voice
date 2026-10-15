using System.Windows;
using System.Windows.Media;

namespace Se7enVoice.Services;

/// <summary>
/// Thème clair (par défaut) ou sombre : remplace la palette (premier dictionnaire fusionné de l'app).
/// Les styles utilisent des DynamicResource, et les contrôles dessinés relisent leurs couleurs au rendu.
/// </summary>
public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var merged = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative) };
        if (merged.Count > 0) merged[0] = palette; else merged.Insert(0, palette);
        foreach (Window w in Application.Current.Windows) Redraw(w);
    }

    private static void Redraw(DependencyObject d)
    {
        if (d is UIElement u) u.InvalidateVisual();
        int n = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < n; i++) Redraw(VisualTreeHelper.GetChild(d, i));
    }
}
