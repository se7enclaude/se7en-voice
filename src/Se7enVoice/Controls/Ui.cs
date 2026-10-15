using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Se7enVoice.Controls;

/// <summary>Propriétés attachées utilisées par les gabarits du thème (icône d'un bouton, sous-titre d'un module, libellé en capitales).</summary>
public static class Ui
{
    /// <summary>Glyphe Segoe Fluent Icons affiché devant le texte.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetIcon(DependencyObject o) => (string)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string v) => o.SetValue(IconProperty, v);

    /// <summary>Sous-titre d'un module (sous le titre, en gris).</summary>
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.RegisterAttached("Subtitle", typeof(string), typeof(Ui), new PropertyMetadata(""));
    public static string GetSubtitle(DependencyObject o) => (string)o.GetValue(SubtitleProperty);
    public static void SetSubtitle(DependencyObject o, string v) => o.SetValue(SubtitleProperty, v);

    /// <summary>Texte d'un TextBlock en capitales espacées : &lt;TextBlock c:Ui.Caps="Table de mixage" /&gt;.</summary>
    public static readonly DependencyProperty CapsProperty =
        DependencyProperty.RegisterAttached("Caps", typeof(string), typeof(Ui), new PropertyMetadata("", (o, e) =>
        {
            if (o is TextBlock t) t.Text = Caps.Space((string)e.NewValue);
        }));
    public static string GetCaps(DependencyObject o) => (string)o.GetValue(CapsProperty);
    public static void SetCaps(DependencyObject o, string v) => o.SetValue(CapsProperty, v);
}

/// <summary>
/// Capitales espacées (tracking .16em du design). WPF n'a pas d'espacement des lettres :
/// on glisse une espace ultrafine (U+200A) entre chaque caractère.
/// </summary>
public sealed class Caps : IValueConverter
{
    public static Caps Instance { get; } = new();

    public static string Space(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var up = s.ToUpper(CultureInfo.GetCultureInfo("fr-FR"));
        var sb = new StringBuilder(up.Length * 2);
        for (int i = 0; i < up.Length; i++)
        {
            if (i > 0 && up[i] != ' ' && up[i - 1] != ' ') sb.Append(' ');
            sb.Append(up[i] == ' ' ? " " : up[i].ToString());
        }
        return sb.ToString();
    }

    /// <summary>ConverterParameter="tight" : capitales sans espacement, pour les libellés étroits (noms de tranches).</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not string s ? value : parameter as string == "tight" ? s.ToUpper(CultureInfo.GetCultureInfo("fr-FR")) : Space(s);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
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

/// <summary>Cadre HUD : coins coupés à 45° (haut gauche, bas droit) et filet blanc fin. Réservé aux dialogues et diagnostics.</summary>
public sealed class HudFrame : Decorator
{
    public static readonly DependencyProperty ChamferProperty = Ink.Dp<HudFrame, double>(nameof(Chamfer), 18);
    public static readonly DependencyProperty FillProperty = Ink.Dp<HudFrame, Brush?>(nameof(Fill), null);
    public static readonly DependencyProperty StrokeProperty = Ink.Dp<HudFrame, Brush?>(nameof(Stroke), null);
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(HudFrame),
        new FrameworkPropertyMetadata(new Thickness(20), FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Chamfer { get => (double)GetValue(ChamferProperty); set => SetValue(ChamferProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        var p = Padding;
        var inner = new Size(Math.Max(0, constraint.Width - p.Left - p.Right), Math.Max(0, constraint.Height - p.Top - p.Bottom));
        Child?.Measure(inner);
        var d = Child?.DesiredSize ?? default;
        return new Size(d.Width + p.Left + p.Right, d.Height + p.Top + p.Bottom);
    }

    protected override Size ArrangeOverride(Size size)
    {
        var p = Padding;
        Child?.Arrange(new Rect(p.Left, p.Top, Math.Max(0, size.Width - p.Left - p.Right), Math.Max(0, size.Height - p.Top - p.Bottom)));
        return size;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, c = Math.Min(Chamfer, Math.Min(w, h) / 3);
        if (w <= 0 || h <= 0) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c + 0.5, 0.5), true, true);
            ctx.PolyLineTo([new Point(w - 0.5, 0.5), new Point(w - 0.5, h - c - 0.5), new Point(w - c - 0.5, h - 0.5),
                            new Point(0.5, h - 0.5), new Point(0.5, c + 0.5)], true, false);
        }
        g.Freeze();
        dc.DrawGeometry(Fill, Stroke is null ? null : new Pen(Stroke, 1), g);
    }
}
