using System.Windows;
using System.Windows.Media;

namespace Se7enVoice.Controls;

/// <summary>
/// Jauge en parallélogramme (« BoostMeter » du design Slipstream). Sert de vu-mètre et de
/// piste pour les sliders. Passe en rayures quand <see cref="Charged"/> est vrai.
/// </summary>
public sealed class SlantMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0.0);
    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 1.0);
    public static readonly DependencyProperty OriginProperty = Register(nameof(Origin), double.NaN);
    public static readonly DependencyProperty ToneProperty = Register(nameof(Tone), (Brush)new SolidColorBrush(Color.FromRgb(0x19, 0xD9, 0xA3)));
    public static readonly DependencyProperty ChargedToneProperty = Register(nameof(ChargedTone), (Brush)new SolidColorBrush(Color.FromRgb(0xFF, 0x33, 0x44)));
    public static readonly DependencyProperty ChargedAboveProperty = Register(nameof(ChargedAbove), 2.0);

    /// <summary>À poser sur un Slider : valeur d'où part le remplissage de sa jauge.</summary>
    public static readonly DependencyProperty SliderOriginProperty =
        DependencyProperty.RegisterAttached("SliderOrigin", typeof(double), typeof(SlantMeter), new PropertyMetadata(double.NaN));

    public static double GetSliderOrigin(DependencyObject o) => (double)o.GetValue(SliderOriginProperty);
    public static void SetSliderOrigin(DependencyObject o, double v) => o.SetValue(SliderOriginProperty, v);

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x4A)));
    private static readonly Pen TrackPen = FreezePen(new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x28, 0x84)), 2));
    private static readonly Brush Highlight = Freeze(new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen OriginPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0xDC, 0xD8, 0xFB)), 2));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>Valeur « zéro » d'où part le remplissage (ex. 0 pour un slider -12..+12). NaN = depuis la gauche.</summary>
    public double Origin { get => (double)GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    public Brush Tone { get => (Brush)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public Brush ChargedTone { get => (Brush)GetValue(ChargedToneProperty); set => SetValue(ChargedToneProperty, value); }
    /// <summary>Au-delà de cette fraction (0..1), la jauge passe en couleur « chargée » rayée.</summary>
    public double ChargedAbove { get => (double)GetValue(ChargedAboveProperty); set => SetValue(ChargedAboveProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double cut = Math.Min(h * 0.8, w / 4);

        dc.DrawGeometry(TrackBrush, TrackPen, Para(0, w, h, cut));

        double range = Maximum - Minimum;
        if (range <= 0) return;
        double f = Math.Clamp((Value - Minimum) / range, 0, 1);
        double o = double.IsNaN(Origin) ? 0 : Math.Clamp((Origin - Minimum) / range, 0, 1);
        double x0 = Math.Min(o, f) * w, x1 = Math.Max(o, f) * w;
        if (!double.IsNaN(Origin))
        {
            double ox = o * w;
            dc.DrawLine(OriginPen, new Point(ox + cut / 2, 2), new Point(ox - cut / 2 + 1, h - 2));
        }
        if (x1 - x0 < 1) return;

        bool charged = f >= ChargedAbove;
        Brush fill = charged ? ChargedTone : Tone;
        var geo = Para(x0, x1, h, cut);
        dc.PushClip(Para(0, w, h, cut));
        dc.DrawGeometry(fill, null, geo);
        if (charged)
        {
            dc.PushClip(geo);
            for (double x = x0 - h; x < x1 + h; x += 14)
                dc.DrawGeometry(Highlight, null, Para(x, x + 5, h, h * 0.9));
            dc.Pop();
        }
        else if (x1 - x0 > cut * 2)
        {
            dc.DrawRectangle(Highlight, null, new Rect(x0 + cut, 3, Math.Max(0, x1 - x0 - cut * 2), Math.Max(2, h * 0.12)));
        }
        dc.Pop();
    }

    private static Geometry Para(double x0, double x1, double h, double cut)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x0 + cut, 0), true, true);
            c.LineTo(new Point(x1, 0), false, false);
            c.LineTo(new Point(x1 - cut, h), false, false);
            c.LineTo(new Point(x0, h), false, false);
        }
        g.Freeze();
        return g;
    }

    private static DependencyProperty Register<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(SlantMeter),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
}
