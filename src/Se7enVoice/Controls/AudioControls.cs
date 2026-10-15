using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Se7enVoice.Controls;

/// <summary>Pinceaux et polices des contrôles dessinés (design « Centrale » : noir, métal, lignes blanches, LEDs rares).</summary>
internal static class Ink
{
    public static readonly Brush Bone = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE7, 0xEB)));
    public static readonly Brush Bone50 = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6)));
    public static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x3D)));
    public static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x3D, 0x33)));
    public static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x4B, 0x53)));
    public static readonly Brush Off = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Faint = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    public static readonly Brush Secondary = Freeze(new SolidColorBrush(Color.FromRgb(0xA9, 0xAB, 0xB4)));
    public static readonly Brush Tertiary = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x8D, 0x98)));
    public static readonly Brush Slot = Freeze(new SolidColorBrush(Colors.Black));
    public static readonly Pen SlotEdge = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)), 1));
    public static readonly Pen Black1 = FreezePen(new Pen(Brushes.Black, 1));

    public static readonly Typeface Mono = Face("IBM Plex Mono Medium", FontWeights.Medium);
    public static readonly Typeface Label = Face("Outfit Medium", FontWeights.Medium);

    private static Typeface Face(string family, FontWeight weight) =>
        new(new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#" + family), FontStyles.Normal, weight, FontStretches.Normal);

    /// <summary>Vu-mètres : blanc, orange au-delà de 80 %, rouge au-delà de 94 %.</summary>
    public static Brush Heat(double f) => f >= 0.94 ? Red : f >= 0.8 ? Orange : Bone;

    public static Brush Theme(FrameworkElement e, string key, Brush fallback) => e.TryFindResource(key) as Brush ?? fallback;

    public static Brush Alpha(Brush b, double a)
    {
        if (b is not SolidColorBrush s) return b;
        var c = s.Color;
        return Freeze(new SolidColorBrush(Color.FromArgb((byte)(c.A * a), c.R, c.G, c.B)));
    }

    public static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    public static Pen FreezePen(Pen p) { p.Freeze(); return p; }

    public static DependencyProperty Dp<TOwner, T>(string name, T def, bool render = true) =>
        DependencyProperty.Register(name, typeof(T), typeof(TOwner),
            new FrameworkPropertyMetadata(def, render ? FrameworkPropertyMetadataOptions.AffectsRender : FrameworkPropertyMetadataOptions.None));
}

/// <summary>Piste de slider horizontal : fente noire en creux, filet de couleur depuis l'origine.</summary>
public sealed class PillTrack : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Ink.Dp<PillTrack, double>(nameof(Value), 0);
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<PillTrack, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<PillTrack, double>(nameof(Maximum), 1);
    public static readonly DependencyProperty OriginProperty = Ink.Dp<PillTrack, double>(nameof(Origin), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<PillTrack, Brush>(nameof(Tone), Ink.Bone);

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
        double y = h / 2;
        // Graduations fines sous la fente
        for (int i = 0; i <= 10; i++)
        {
            double x = Math.Round(i * (w - 1) / 10) + 0.5;
            dc.DrawRectangle(Ink.Faint, null, new Rect(x, y - (i % 5 == 0 ? 7 : 5), 1, i % 5 == 0 ? 14 : 10));
        }
        dc.DrawRoundedRectangle(Ink.Slot, Ink.SlotEdge, new Rect(0, y - 2, w, 4), 2, 2);
        double range = Maximum - Minimum;
        if (range <= 0) return;
        double f = Math.Clamp((Value - Minimum) / range, 0, 1);
        double o = double.IsNaN(Origin) ? 0 : Math.Clamp((Origin - Minimum) / range, 0, 1);
        double x0 = Math.Min(o, f) * w, x1 = Math.Max(o, f) * w;
        if (x1 - x0 >= 0.5) dc.DrawRoundedRectangle(Ink.Alpha(Tone, 0.75), null, new Rect(x0, y - 1, x1 - x0, 2), 1, 1);
    }
}

/// <summary>
/// Bouton rotatif noir brillant (Knob du design) : arc fin de 270°, repère blanc lumineux, butées aux extrémités.
/// Glisser vers le haut/bas (Maj = fin) ou molette ; double-clic = valeur neutre. Libellé en capitales, valeur en mono.
/// </summary>
public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<Knob, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<Knob, double>(nameof(Maximum), 1);
    /// <summary>Valeur neutre (départ de l'arc d'un bouton bipolaire, et valeur du double-clic). NaN = minimum.</summary>
    public static readonly DependencyProperty OriginProperty = Ink.Dp<Knob, double>(nameof(Origin), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<Knob, Brush>(nameof(Tone), Ink.Bone);
    public static readonly DependencyProperty LabelProperty = Ink.Dp<Knob, string>(nameof(Label), "");
    public static readonly DependencyProperty FormatProperty = Ink.Dp<Knob, string>(nameof(Format), "{0:P0}");
    /// <summary>Pas d'aimantation (0 = libre).</summary>
    public static readonly DependencyProperty TickProperty = Ink.Dp<Knob, double>(nameof(Tick), 0, render: false);
    /// <summary>Diamètre du bouton (le reste de la largeur sert à l'arc et au libellé).</summary>
    public static readonly DependencyProperty DiameterProperty = Ink.Dp<Knob, double>(nameof(Diameter), 48);

    private static readonly Brush BodyBrush = Ink.Freeze(new RadialGradientBrush(
        [new GradientStop(Color.FromRgb(0x34, 0x34, 0x3B), 0), new GradientStop(Color.FromRgb(0x14, 0x14, 0x17), 0.45), new GradientStop(Color.FromRgb(0x05, 0x05, 0x06), 1)])
    { Center = new Point(0.5, 0.3), GradientOrigin = new Point(0.5, 0.3), RadiusX = 0.75, RadiusY = 0.75 });
    private static readonly Brush ShadowBrush = Ink.Freeze(new RadialGradientBrush(
        [new GradientStop(Color.FromArgb(0xB3, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1)]));
    private static readonly Pen RimLight = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen BezelPen = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x41)), 2.5));
    private static readonly Pen TrackPen = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Pen TickPen = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen StopPen = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), 1.5));
    private static readonly Pen IndicatorGlow = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    private static readonly Pen Indicator = Ink.FreezePen(new Pen(Ink.Bone50, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

    private Point _start;
    private double _startValue;

    public Knob()
    {
        Width = 76;
        Height = 104;
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
    public double Diameter { get => (double)GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }

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
        double speed = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 600 : 180;
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
        double d = Math.Min(Diameter, ActualWidth - 20);
        double c = ActualWidth / 2, cy = d / 2 + 10, r = d / 2 + 5;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        // Graduations (11 crans) et butées aux extrémités
        for (int i = 0; i <= 10; i++)
        {
            double a = -135 + 27 * i;
            bool end = i == 0 || i == 10;
            dc.DrawLine(end ? StopPen : TickPen, Polar(c, cy, r + 3, a), Polar(c, cy, r + (end ? 8 : 6), a));
        }

        // Piste de course, puis arc de valeur lumineux (depuis le neutre pour un réglage bipolaire)
        dc.DrawGeometry(null, TrackPen, Arc(c, cy, r, -135, 135));
        double range = Maximum - Minimum;
        double from = range > 0 && !double.IsNaN(Origin) ? -135 + 270 * Math.Clamp((Origin - Minimum) / range, 0, 1) : -135;
        double ang = -135 + 270 * Fraction;
        if (Math.Abs(ang - from) > 0.5)
        {
            var arc = Arc(c, cy, r, Math.Min(from, ang), Math.Max(from, ang));
            dc.DrawGeometry(null, new Pen(Ink.Alpha(Tone, 0.22), 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
            dc.DrawGeometry(null, new Pen(Tone, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        }

        // Corps : ombre, bague métal, dégradé noir brillant, reflet en haut
        double k = d / 2 - 2;
        dc.DrawEllipse(ShadowBrush, null, new Point(c, cy + 4), k + 6, k + 6);
        dc.DrawEllipse(Brushes.Black, BezelPen, new Point(c, cy), k + 1.5, k + 1.5);
        dc.DrawEllipse(BodyBrush, Ink.Black1, new Point(c, cy), k - 1, k - 1);
        dc.DrawGeometry(null, RimLight, Arc(c, cy, k - 2.5, -60, 60));

        // Repère : trait blanc du bord vers le centre
        var p0 = Polar(c, cy, k - 3, ang);
        var p1 = Polar(c, cy, k * 0.35, ang);
        dc.DrawLine(IndicatorGlow, p0, p1);
        dc.DrawLine(Indicator, p0, p1);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        string text;
        try { text = string.Format(culture, Format, Value); }
        catch (FormatException) { text = Value.ToString("0.##"); }
        double y = cy + r + 7;
        if (!string.IsNullOrEmpty(Label))
        {
            var label = new FormattedText(Caps.Space(Label), culture, FlowDirection.LeftToRight, Ink.Label, 10,
                Ink.Theme(this, "TextSecondaryBrush", Ink.Secondary), dpi)
            { MaxTextWidth = Math.Max(10, ActualWidth + 12), Trimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxLineCount = 1 };
            dc.DrawText(label, new Point(c - label.MaxTextWidth / 2, y));
            y += 15;
        }
        var readout = new FormattedText(text, culture, FlowDirection.LeftToRight, Ink.Mono, 10.5, Ink.Theme(this, "TextPrimaryBrush", Ink.Bone), dpi);
        dc.DrawText(readout, new Point(c - readout.Width / 2, y));
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

/// <summary>
/// Fader de console : graduations, fente noire, filet de couleur, capuchon métal à ligne lumineuse.
/// Repère « unité » (0 dB) plus marqué ; double-clic = unité.
/// </summary>
public sealed class Fader : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Fader),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MinimumProperty = Ink.Dp<Fader, double>(nameof(Minimum), 0);
    public static readonly DependencyProperty MaximumProperty = Ink.Dp<Fader, double>(nameof(Maximum), 1);
    /// <summary>Repère « unité » (ex. 0 dB) et valeur du double-clic. NaN = aucun.</summary>
    public static readonly DependencyProperty ZeroProperty = Ink.Dp<Fader, double>(nameof(Zero), double.NaN);
    public static readonly DependencyProperty ToneProperty = Ink.Dp<Fader, Brush>(nameof(Tone), Ink.Bone);
    public static readonly DependencyProperty IsMutedProperty = Ink.Dp<Fader, bool>(nameof(IsMuted), false);

    private const double CapH = 30;

    private static readonly Brush CapBrush = Ink.Freeze(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0x4A, 0x4B, 0x53), 0), new GradientStop(Color.FromRgb(0x26, 0x26, 0x2C), 0.48),
         new GradientStop(Color.FromRgb(0x1C, 0x1C, 0x21), 0.52), new GradientStop(Color.FromRgb(0x2C, 0x2C, 0x33), 1)], 90));
    private static readonly Brush CapShadow = Ink.Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));
    private static readonly Pen CapLight = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen UnityPen = Ink.FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)), 1));

    public Fader()
    {
        Width = 36;
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
        double cx = Math.Round(w / 2), top = CapH / 2, bottom = h - CapH / 2;

        // Graduations
        for (int i = 0; i <= 10; i++)
        {
            double y = Math.Round(top + i * Travel / 10) + 0.5;
            double inset = i % 5 == 0 ? 2 : 6;
            dc.DrawRectangle(Ink.Faint, null, new Rect(inset, y, w - inset * 2, 1));
        }
        if (!double.IsNaN(Zero))
        {
            double zy = Math.Round(bottom - Frac(Zero) * Travel) + 0.5;
            dc.DrawLine(UnityPen, new Point(0, zy), new Point(w, zy));
        }

        // Fente et filet de niveau
        dc.DrawRoundedRectangle(Ink.Slot, Ink.SlotEdge, new Rect(cx - 2, top, 4, bottom - top), 2, 2);
        double vy = bottom - Frac(Value) * Travel;
        var tone = IsMuted ? Ink.Muted : Tone;
        if (bottom - vy > 0.5) dc.DrawRoundedRectangle(Ink.Alpha(tone, 0.7), null, new Rect(cx - 1, vy, 2, bottom - vy), 1, 1);

        // Capuchon métal
        var cap = new Rect(1, vy - CapH / 2, w - 2, CapH);
        dc.DrawRoundedRectangle(CapShadow, null, new Rect(cap.X, cap.Y + 4, cap.Width, cap.Height), 4, 4);
        dc.DrawRoundedRectangle(CapBrush, Ink.Black1, cap, 4, 4);
        dc.DrawLine(CapLight, new Point(cap.X + 3, cap.Y + 1.5), new Point(cap.Right - 3, cap.Y + 1.5));
        dc.DrawRoundedRectangle(Ink.Alpha(tone, 0.45), null, new Rect(cap.X + 3, vy - 2.5, cap.Width - 6, 5), 2, 2);
        dc.DrawRectangle(tone, null, new Rect(cap.X + 4, vy - 1, cap.Width - 8, 2));
    }
}

/// <summary>Vumètre en matrice de points LED : blanc, orange au-delà de 80 %, rouge au-delà de 94 %, avec maintien de crête.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Ink.Dp<LevelMeter, double>(nameof(Value), 0);
    public static readonly DependencyProperty VerticalProperty = Ink.Dp<LevelMeter, bool>(nameof(Vertical), false);
    public static readonly DependencyProperty SegmentsProperty = Ink.Dp<LevelMeter, int>(nameof(Segments), 32);
    /// <summary>Couleur unique (sinon blanc/orange/rouge).</summary>
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
        var off = Ink.Theme(this, "MeterOffBrush", Ink.Off);
        double len = Vertical ? h : w, thick = Vertical ? w : h;
        double dot = Math.Min(thick, 5), r = dot / 2;
        double pitch = n > 1 ? (len - dot) / (n - 1) : 0;
        for (int i = 0; i < n; i++)
        {
            double t = (i + 1.0) / n;
            bool on = i < lit || i == pk;
            double p = r + i * pitch;
            var center = Vertical ? new Point(w / 2, h - p) : new Point(p, h / 2);
            if (!on) { dc.DrawEllipse(off, null, center, r, r); continue; }
            var brush = Tone ?? Ink.Heat(t);
            if (Tone is null && t >= 0.8) dc.DrawEllipse(Ink.Alpha(brush, 0.35), null, center, r + 1.5, r + 1.5);
            dc.DrawEllipse(brush, null, center, r, r);
        }
    }
}

/// <summary>LED (carrée par défaut, ronde avec <see cref="Round"/>) avec halo de sa couleur ; éteinte = gris enfoncé.</summary>
public sealed class Led : FrameworkElement
{
    public static readonly DependencyProperty ToneProperty = Ink.Dp<Led, Brush>(nameof(Tone), Ink.Orange);
    public static readonly DependencyProperty IsOnProperty = Ink.Dp<Led, bool>(nameof(IsOn), true);
    public static readonly DependencyProperty RoundProperty = Ink.Dp<Led, bool>(nameof(Round), false);

    public Led()
    {
        Width = 8;
        Height = 8;
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public Brush Tone { get => (Brush)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public bool Round { get => (bool)GetValue(RoundProperty); set => SetValue(RoundProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double r = Round ? w / 2 : 2;
        var rect = new Rect(0, 0, w, h);
        if (!IsOn) { dc.DrawRoundedRectangle(Ink.Theme(this, "Ink400Brush", Ink.Muted), null, rect, r, r); return; }
        for (int i = 3; i >= 1; i--)
        {
            var g = rect; g.Inflate(i * 1.5, i * 1.5);
            dc.DrawRoundedRectangle(Ink.Alpha(Tone, 0.12), null, g, r + i * 1.5, r + i * 1.5);
        }
        dc.DrawRoundedRectangle(Tone, null, rect, r, r);
    }
}
