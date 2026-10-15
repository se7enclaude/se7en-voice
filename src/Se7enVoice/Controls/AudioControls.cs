using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Se7enVoice.Controls;

/// <summary>Pinceaux des contrôles dessinés : lus dans le thème actif (clair/sombre) à chaque rendu.</summary>
internal static class Ink
{
    public static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)));
    public static readonly Brush Amber = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x20)));
    public static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)));
    public static readonly Brush Violet = Freeze(new SolidColorBrush(Color.FromRgb(0x7A, 0x64, 0xF0)));
    public static readonly Brush White = Freeze(new SolidColorBrush(Colors.White));
    public static readonly Brush InkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16)));
    public static readonly Brush Stone = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0x9A, 0x93, 0x8D)));
    public static readonly Brush CapShadow = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)));
    public static readonly Pen CapLine = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x14, 0x14, 0x16)), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    /// <summary>Vu-mètres : vert, puis ambre, puis rouge près du 0 dB.</summary>
    public static Brush Heat(double f) => f < 0.7 ? Green : f < 0.88 ? Amber : Red;

    public static Brush Theme(FrameworkElement e, string key, Brush fallback) => e.TryFindResource(key) as Brush ?? fallback;

    public static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    public static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public static DependencyProperty Dp<TOwner, T>(string name, T def, bool render = true) =>
        DependencyProperty.Register(name, typeof(T), typeof(TOwner),
            new FrameworkPropertyMetadata(def, render ? FrameworkPropertyMetadataOptions.AffectsRender : FrameworkPropertyMetadataOptions.None));
}

/// <summary>Piste de slider : rail 6 px en creux, remplissage couleur depuis l'origine.</summary>
public sealed class PillTrack : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Ink.Dp<PillTrack, double>(nameof(Value), 0);
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<PillTrack, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<PillTrack, double>(nameof(Maximum), 1);
    public static readonly DependencyProperty OriginProperty = Ink.Dp<PillTrack, double>(nameof(Origin), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<PillTrack, Brush>(nameof(Tone), Ink.Violet);

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
        const double t = 6;
        double y = (h - t) / 2;
        dc.DrawRoundedRectangle(Ink.Theme(this, "WellStrongBrush", Ink.Stone), null, new Rect(0, y, w, t), t / 2, t / 2);
        double range = Maximum - Minimum;
        if (range <= 0) return;
        double f = Math.Clamp((Value - Minimum) / range, 0, 1);
        double o = double.IsNaN(Origin) ? 0 : Math.Clamp((Origin - Minimum) / range, 0, 1);
        double x0 = Math.Min(o, f) * w, x1 = Math.Max(o, f) * w;
        if (x1 - x0 >= 0.5) dc.DrawRoundedRectangle(Tone, null, new Rect(x0, y, x1 - x0, t), t / 2, t / 2);
    }
}

/// <summary>
/// Potard (Knob du design system) : arc de 270° coloré autour d'un capuchon blanc à repère sombre.
/// Glisser vers le haut/bas ou molette ; double-clic = valeur neutre. Libellé en petites capitales, valeur en mono.
/// </summary>
public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<Knob, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<Knob, double>(nameof(Maximum), 1);
    /// <summary>Valeur neutre (départ de l'arc d'un potard bipolaire, et valeur du double-clic). NaN = minimum.</summary>
    public static readonly DependencyProperty OriginProperty = Ink.Dp<Knob, double>(nameof(Origin), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<Knob, Brush>(nameof(Tone), Ink.Violet);
    public static readonly DependencyProperty LabelProperty = Ink.Dp<Knob, string>(nameof(Label), "");
    public static readonly DependencyProperty FormatProperty = Ink.Dp<Knob, string>(nameof(Format), "{0:P0}");
    /// <summary>Pas d'aimantation (0 = libre).</summary>
    public static readonly DependencyProperty TickProperty = Ink.Dp<Knob, double>(nameof(Tick), 0, render: false);

    private static readonly Typeface ReadoutFace = new(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Geist Mono Medium"),
        FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface LabelFace = new(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#Geist Medium"),
        FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private Point _start;
    private double _startValue;

    public Knob()
    {
        Width = 72;
        Height = 100;
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
    private double Fraction => Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;

    private void SetFromFraction(double f)
    {
        double v = Minimum + Math.Clamp(f, 0, 1) * (Maximum - Minimum);
        if (Tick > 0) v = Math.Round(v / Tick) * Tick;
        Value = Math.Clamp(v, Minimum, Maximum);
    }

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

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => ReleaseMouseCapture();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double step = Tick > 0 ? Tick / (Maximum - Minimum) : 0.02;
        SetFromFraction(Fraction + Math.Sign(e.Delta) * step);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, 62);
        double c = ActualWidth / 2, cy = size / 2, r = size / 2 - 3;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var trackPen = new Pen(Ink.Theme(this, "WellStrongBrush", Ink.Stone), 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, trackPen, Arc(c, cy, r, -135, 135));
        double range = Maximum - Minimum;
        double from = range > 0 && !double.IsNaN(Origin) ? -135 + 270 * Math.Clamp((Origin - Minimum) / range, 0, 1) : -135;
        double ang = -135 + 270 * Fraction;
        if (Math.Abs(ang - from) > 0.5)
        {
            var pen = new Pen(Tone, 5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(c, cy, r, Math.Min(from, ang), Math.Max(from, ang)));
        }
        // Capuchon blanc avec ombre douce et repère sombre
        double cap = r - 8;
        dc.DrawEllipse(Ink.CapShadow, null, new Point(c, cy + 2.5), cap, cap);
        dc.DrawEllipse(Ink.White, null, new Point(c, cy), cap, cap);
        var p1 = Polar(c, cy, cap - 3, ang);
        var p0 = Polar(c, cy, cap - 11, ang);
        dc.DrawLine(new Pen(Ink.InkBrush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, p0, p1);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        string text;
        try { text = string.Format(culture, Format, Value); }
        catch (FormatException) { text = Value.ToString("0.##"); }
        if (!string.IsNullOrEmpty(Label))
        {
            var label = new FormattedText(Label.ToUpper(culture), culture, FlowDirection.LeftToRight, LabelFace, 10.5,
                Ink.Theme(this, "TextSecondaryBrush", Ink.Stone), dpi)
            { MaxTextWidth = Math.Max(10, ActualWidth + 14), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxLineCount = 1 };
            dc.DrawText(label, new Point(c - label.MaxTextWidth / 2, size + 4));
        }
        var readout = new FormattedText(text, culture, FlowDirection.LeftToRight, ReadoutFace, 12, Ink.Theme(this, "TextPrimaryBrush", Ink.InkBrush), dpi);
        dc.DrawText(readout, new Point(c - readout.Width / 2, size + 20));
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

/// <summary>Fader vertical : rail fin en creux, remplissage couleur, curseur blanc en pilule, repère « unité » (0 dB).</summary>
public sealed class Fader : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Fader),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<Fader, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<Fader, double>(nameof(Maximum), 1);
    /// <summary>Repère « unité » (ex. 0 dB) et valeur du double-clic. NaN = aucun.</summary>
    public static readonly DependencyProperty ZeroProperty = Ink.Dp<Fader, double>(nameof(Zero), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<Fader, Brush>(nameof(Tone), Ink.Violet);
    public static readonly DependencyProperty IsMutedProperty = Ink.Dp<Fader, bool>(nameof(IsMuted), false);

    private const double CapH = 22, CapW = 40;

    public Fader()
    {
        Width = 44;
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
    private double Travel => Math.Max(1, ActualHeight - CapH);

    private void SetFromY(double y)
    {
        double f = 1 - Math.Clamp((y - CapH / 2) / Travel, 0, 1);
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
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        double cx = w / 2, bottom = h - CapH / 2;
        const double t = 8;
        dc.DrawRoundedRectangle(Ink.Theme(this, "WellStrongBrush", Ink.Stone), null, new Rect(cx - t / 2, 0, t, h), t / 2, t / 2);

        if (!double.IsNaN(Zero))
        {
            double zy = bottom - Frac(Zero) * Travel;
            var pen = new Pen(Ink.Theme(this, "TextTertiaryBrush", Ink.Stone), 1);
            dc.DrawLine(pen, new Point(2, zy), new Point(w - 2, zy));
        }
        double y = bottom - Frac(Value) * Travel;
        if (h - y > 1) dc.DrawRoundedRectangle(IsMuted ? Ink.Stone : Tone, null, new Rect(cx - t / 2, y, t, h - y), t / 2, t / 2);

        var cap = new Rect(cx - CapW / 2, y - CapH / 2, CapW, CapH);
        dc.DrawRoundedRectangle(Ink.CapShadow, null, new Rect(cap.X, cap.Y + 3, cap.Width, cap.Height), CapH / 2, CapH / 2);
        dc.DrawRoundedRectangle(Ink.White, null, cap, CapH / 2, CapH / 2);
        dc.DrawLine(Ink.CapLine, new Point(cx - 8, y), new Point(cx + 8, y));
    }
}

/// <summary>Vu-mètre à segments (vert → ambre → rouge) avec maintien de crête.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Ink.Dp<LevelMeter, double>(nameof(Value), 0);
    public static readonly DependencyProperty VerticalProperty = Ink.Dp<LevelMeter, bool>(nameof(Vertical), false);
    public static readonly DependencyProperty SegmentsProperty = Ink.Dp<LevelMeter, int>(nameof(Segments), 32);
    /// <summary>Couleur unique (sinon vert/ambre/rouge).</summary>
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
        if (v >= _peak) { _peak = v; _peakAt = DateTime.UtcNow; }
        else if (DateTime.UtcNow - _peakAt > TimeSpan.FromMilliseconds(900)) _peak = Math.Max(v, _peak - 0.02);
        int lit = (int)Math.Round(v * n);
        int pk = _peak > 0.02 ? Math.Min(n - 1, (int)Math.Round(_peak * n) - 1) : -1;
        var off = Ink.Theme(this, "MeterOffBrush", Ink.Stone);
        const double gap = 2;
        double len = Vertical ? h : w, thick = Vertical ? w : h;
        double seg = (len - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            bool on = i < lit || i == pk;
            var brush = on ? (Tone ?? Ink.Heat((double)i / n)) : off;
            double p = i * (seg + gap);
            var r = Vertical ? new Rect(0, h - p - seg, thick, seg) : new Rect(p, 0, seg, thick);
            dc.DrawRoundedRectangle(brush, null, r, 2, 2);
        }
    }
}
