using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Se7enVoice.Controls;

/// <summary>Couleurs et pinceaux partagés par les contrôles dessinés (design Centrale).</summary>
internal static class Ink
{
    public static readonly Color Mint = Color.FromRgb(0x4F, 0xD1, 0xB0), Volt = Color.FromRgb(0xDD, 0xF2, 0x47),
        Amber = Color.FromRgb(0xED, 0xB8, 0x42), Coral = Color.FromRgb(0xF2, 0x60, 0x5A);

    public static readonly Brush Track = Freeze(new SolidColorBrush(Color.FromArgb(0x17, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Unlit = Freeze(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Knob = Freeze(new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x20)));
    public static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)));
    public static readonly Pen Hairline = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), 1));
    public static readonly Pen ZeroMark = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), 1) { DashStyle = new DashStyle([3, 3], 0) });
    public static readonly Brush Volt400 = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xF2, 0x47)));

    /// <summary>Rampe « chaleur » des vu-mètres : menthe → volt → ambre → corail.</summary>
    public static Brush Heat(double f) => f < 0.65 ? MintB : f < 0.85 ? VoltB : f < 0.94 ? AmberB : CoralB;
    private static readonly Brush MintB = Freeze(new SolidColorBrush(Mint)), VoltB = Freeze(new SolidColorBrush(Volt)),
        AmberB = Freeze(new SolidColorBrush(Amber)), CoralB = Freeze(new SolidColorBrush(Coral));

    public static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    public static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public static DependencyProperty Dp<TOwner, T>(string name, T def, bool render = true) =>
        DependencyProperty.Register(name, typeof(T), typeof(TOwner),
            new FrameworkPropertyMetadata(def, render ? FrameworkPropertyMetadataOptions.AffectsRender : FrameworkPropertyMetadataOptions.None));
}

/// <summary>Piste de slider en pilule : rail 4 px, remplissage couleur depuis l'origine.</summary>
public sealed class PillTrack : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Ink.Dp<PillTrack, double>(nameof(Value), 0);
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<PillTrack, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<PillTrack, double>(nameof(Maximum), 1);
    public static readonly DependencyProperty OriginProperty = Ink.Dp<PillTrack, double>(nameof(Origin), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<PillTrack, Brush>(nameof(Tone), Ink.Volt400);

    /// <summary>À poser sur un Slider : valeur d'où part le remplissage (NaN = depuis la gauche).</summary>
    public static readonly DependencyProperty SliderOriginProperty =
        DependencyProperty.RegisterAttached("SliderOrigin", typeof(double), typeof(PillTrack), new PropertyMetadata(double.NaN));
    public static double GetSliderOrigin(DependencyObject o) => (double)o.GetValue(SliderOriginProperty);
    public static void SetSliderOrigin(DependencyObject o, double v) => o.SetValue(SliderOriginProperty, v);

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Origin { get => (double)GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    public Brush Tone { get => (Brush)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        const double t = 4;
        double y = (h - t) / 2;
        dc.DrawRoundedRectangle(Ink.Track, null, new Rect(0, y, w, t), t / 2, t / 2);
        double range = Maximum - Minimum;
        if (range <= 0) return;
        double f = Math.Clamp((Value - Minimum) / range, 0, 1);
        double o = double.IsNaN(Origin) ? 0 : Math.Clamp((Origin - Minimum) / range, 0, 1);
        if (!double.IsNaN(Origin)) dc.DrawRectangle(Ink.Muted, null, new Rect(o * w - 0.5, y - 4, 1, t + 8));
        double x0 = Math.Min(o, f) * w, x1 = Math.Max(o, f) * w;
        if (x1 - x0 >= 0.5) dc.DrawRoundedRectangle(Tone, null, new Rect(x0, y, x1 - x0, t), t / 2, t / 2);
    }
}

/// <summary>
/// Potard rotatif (Knob du design system) : arc de 270°, glisser vers le haut/bas ou molette,
/// double-clic = valeur neutre. Valeur, bornes, libellé et format comme <see cref="ParamSlider"/>.
/// </summary>
public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<Knob, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<Knob, double>(nameof(Maximum), 1);
    /// <summary>Valeur neutre (départ de l'arc pour un potard bipolaire, et valeur du double-clic). NaN = minimum.</summary>
    public static readonly DependencyProperty OriginProperty = Ink.Dp<Knob, double>(nameof(Origin), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<Knob, Brush>(nameof(Tone), Ink.Volt400);
    public static readonly DependencyProperty LabelProperty = Ink.Dp<Knob, string>(nameof(Label), "");
    public static readonly DependencyProperty FormatProperty = Ink.Dp<Knob, string>(nameof(Format), "{0:P0}");
    /// <summary>Pas d'aimantation (0 = libre).</summary>
    public static readonly DependencyProperty TickProperty = Ink.Dp<Knob, double>(nameof(Tick), 0, render: false);

    private static readonly Typeface ReadoutFace = new(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#JetBrains Mono Medium"),
        FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface LabelFace = new(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Manrope SemiBold"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush Secondary = Ink.Freeze(new SolidColorBrush(Color.FromRgb(0xA4, 0xA9, 0xB6)));

    private Point _start;
    private double _startValue;

    public Knob()
    {
        Width = 64;
        Height = 96;
        Cursor = Cursors.SizeNS;
        Focusable = false;
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Origin { get => (double)GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    public Brush Tone { get => (Brush)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public double Tick { get => (double)GetValue(TickProperty); set => SetValue(TickProperty, value); }

    private double Neutral => double.IsNaN(Origin) ? Minimum : Origin;

    private void SetFromFraction(double f)
    {
        double v = Minimum + Math.Clamp(f, 0, 1) * (Maximum - Minimum);
        if (Tick > 0) v = Math.Round(v / Tick) * Tick;
        Value = Math.Clamp(v, Minimum, Maximum);
    }

    private double Fraction => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { Value = Neutral; e.Handled = true; return; }
        _start = e.GetPosition(this);
        _startValue = Fraction;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!IsMouseCaptured) return;
        double dy = _start.Y - e.GetPosition(this).Y;
        double speed = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 600 : 160;
        SetFromFraction(_startValue + dy / speed);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double step = Tick > 0 ? Tick / (Maximum - Minimum) : 0.02;
        SetFromFraction(Fraction + Math.Sign(e.Delta) * step);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, 64);
        double c = ActualWidth / 2, cy = size / 2, r = size / 2 - 4;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // zone cliquable pleine

        var track = new Pen(Ink.Track, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, track, Arc(c, cy, r, -135, 135));
        double range = Maximum - Minimum;
        double from = range > 0 && !double.IsNaN(Origin) ? -135 + 270 * Math.Clamp((Origin - Minimum) / range, 0, 1) : -135;
        double ang = -135 + 270 * Fraction;
        if (Math.Abs(ang - from) > 0.5)
        {
            var pen = new Pen(Tone, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(c, cy, r, Math.Min(from, ang), Math.Max(from, ang)));
        }
        dc.DrawEllipse(Ink.Knob, Ink.Hairline, new Point(c, cy), r - 9, r - 9);
        var p1 = Polar(c, cy, r - 13, ang);
        var p0 = Polar(c, cy, Math.Max(2, r - 22), ang);
        dc.DrawLine(new Pen(Ink.White, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, p0, p1);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string text;
        try { text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Format, Value); }
        catch (FormatException) { text = Value.ToString("0.##"); }
        var readout = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ReadoutFace, 11.5, Ink.White, dpi);
        dc.DrawText(readout, new Point(c - readout.Width / 2, size + 2));
        if (!string.IsNullOrEmpty(Label))
        {
            var label = new FormattedText(Label, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 11.5, Secondary, dpi)
            { MaxTextWidth = Math.Max(10, ActualWidth + 16), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxLineCount = 1 };
            dc.DrawText(label, new Point(c - label.MaxTextWidth / 2, size + 18));
        }
    }

    private static Point Polar(double cx, double cy, double r, double deg)
    {
        double a = (deg - 90) * Math.PI / 180;
        return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }

    private static Geometry Arc(double cx, double cy, double r, double a0, double a1)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Polar(cx, cy, r, a0), false, false);
            ctx.ArcTo(Polar(cx, cy, r, a1), new Size(r, r), 0, a1 - a0 > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}

/// <summary>Fader vertical en pilule (tranche de mixage) : remplissage couleur, pastille blanche, repère 0 dB pointillé.</summary>
public sealed class Fader : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Fader),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<Fader, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<Fader, double>(nameof(Maximum), 1);
    /// <summary>Repère pointillé (ex. 0 dB) et valeur du double-clic. NaN = aucun.</summary>
    public static readonly DependencyProperty ZeroProperty = Ink.Dp<Fader, double>(nameof(Zero), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<Fader, Brush>(nameof(Tone), Ink.Volt400);
    public static readonly DependencyProperty IsMutedProperty = Ink.Dp<Fader, bool>(nameof(IsMuted), false);

    public Fader()
    {
        Width = 40;
        Height = 200;
        Cursor = Cursors.SizeNS;
    }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Zero { get => (double)GetValue(ZeroProperty); set => SetValue(ZeroProperty, value); }
    public Brush Tone { get => (Brush)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }

    private double Frac(double v) => Maximum > Minimum ? Math.Clamp((v - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    private void SetFromY(double y)
    {
        double f = 1 - Math.Clamp(y / Math.Max(1, ActualHeight), 0, 1);
        Value = Math.Round((Minimum + f * (Maximum - Minimum)) * 1000) / 1000;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && !double.IsNaN(Zero)) { Value = Zero; e.Handled = true; return; }
        CaptureMouse();
        SetFromY(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) SetFromY(e.GetPosition(this).Y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Value = Math.Clamp(Value + Math.Sign(e.Delta) * (Maximum - Minimum) / 50, Minimum, Maximum);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRoundedRectangle(Ink.Unlit, Ink.Hairline, new Rect(0, 0, w, h), w / 2, w / 2);
        if (!double.IsNaN(Zero))
        {
            double zy = h - Frac(Zero) * h;
            dc.DrawLine(Ink.ZeroMark, new Point(-6, zy), new Point(w + 6, zy));
        }
        double thumb = w - 16;
        double f = Frac(Value);
        double fill = Math.Max(f > 0 ? thumb + 4 : 0, f * (h - 6));
        if (fill > 0)
        {
            var r = new Rect(3, h - 3 - fill, w - 6, fill);
            dc.DrawRoundedRectangle(IsMuted ? Ink.Muted : Tone, null, r, (w - 6) / 2, (w - 6) / 2);
            dc.DrawEllipse(null, new Pen(Ink.White, 2.5), new Point(w / 2, r.Top + 2 + thumb / 2), thumb / 2 - 1.25, thumb / 2 - 1.25);
        }
    }
}

/// <summary>Vu-mètre à segments (rampe menthe → volt → ambre → corail) avec maintien de crête.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Ink.Dp<LevelMeter, double>(nameof(Value), 0);
    public static readonly DependencyProperty VerticalProperty = Ink.Dp<LevelMeter, bool>(nameof(Vertical), false);
    public static readonly DependencyProperty SegmentsProperty = Ink.Dp<LevelMeter, int>(nameof(Segments), 32);
    /// <summary>Couleur unique (sinon rampe de chaleur).</summary>
    public static readonly DependencyProperty ToneProperty = Ink.Dp<LevelMeter, Brush?>(nameof(Tone), null);

    private double _peak;
    private DateTime _peakAt;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool Vertical { get => (bool)GetValue(VerticalProperty); set => SetValue(VerticalProperty, value); }
    public int Segments { get => (int)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public Brush? Tone { get => (Brush?)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public LevelMeter() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        int n = Math.Max(4, Segments);
        if (w <= 0 || h <= 0) return;
        double v = Math.Clamp(Value, 0, 1);
        if (v >= _peak || DateTime.UtcNow - _peakAt > TimeSpan.FromMilliseconds(900)) { _peak = Math.Max(v, _peak - 0.02); if (v >= _peak) _peakAt = DateTime.UtcNow; }
        int lit = (int)Math.Round(v * n);
        int pk = _peak > 0.02 ? Math.Min(n - 1, (int)Math.Round(_peak * n) - 1) : -1;
        const double gap = 2;
        double len = Vertical ? h : w, thick = Vertical ? w : h;
        double seg = (len - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            bool on = i < lit || i == pk;
            var brush = on ? (Tone ?? Ink.Heat((double)i / n)) : Ink.Unlit;
            double p = i * (seg + gap);
            var r = Vertical ? new Rect(0, h - p - seg, thick, seg) : new Rect(p, 0, seg, thick);
            dc.DrawRoundedRectangle(brush, null, r, 2, 2);
        }
    }
}
