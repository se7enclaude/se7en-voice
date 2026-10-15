using System.Windows;
using System.Windows.Media;
using Se7enVoice.Core.Dsp;

namespace Se7enVoice.Controls;

/// <summary>
/// Analyseur temps réel de la voix envoyée au micro virtuel, façon écran HUD : grille de 1 px,
/// courbe blanche du spectre et trace plus pâle des crêtes, sur une échelle logarithmique 60 Hz – 14 kHz.
/// </summary>
public sealed class SpectrumView : FrameworkElement
{
    private const int Size = 2048, Bars = 48;
    private const double MinHz = 60, MaxHz = 14000, FloorDb = -78, TopDb = -12;

    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen AxisPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)), 1));
    private static readonly Pen LinePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)), 1.5) { LineJoin = PenLineJoin.Round });
    private static readonly Pen PeakPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)), 1.2) { LineJoin = PenLineJoin.Round });
    private static readonly Brush AreaBrush = Freeze(new LinearGradientBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90));

    private readonly Fft _fft = new(Size);
    private readonly double[] _re = new double[Size], _im = new double[Size], _window = new double[Size];
    private readonly float[] _samples = new float[Size];
    private readonly double[] _levels = new double[Bars], _peaks = new double[Bars];
    private readonly int[] _edges = new int[Bars + 1];
    private int _rate;

    public SpectrumView()
    {
        for (int i = 0; i < Size; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Size);
        IsHitTestVisible = false;
    }

    /// <summary>Calcule une nouvelle image à partir des derniers échantillons (appelé ~30 fois par seconde).</summary>
    public void Update(Action<float[]> fill, int sampleRate)
    {
        if (sampleRate <= 0) sampleRate = 48000;
        if (sampleRate != _rate)
        {
            _rate = sampleRate;
            for (int b = 0; b <= Bars; b++)
            {
                double hz = MinHz * Math.Pow(MaxHz / MinHz, (double)b / Bars);
                _edges[b] = Math.Clamp((int)Math.Round(hz * Size / sampleRate), 1, Size / 2 - 1);
            }
        }
        fill(_samples);
        for (int i = 0; i < Size; i++) { _re[i] = _samples[i] * _window[i]; _im[i] = 0; }
        _fft.Transform(_re, _im);

        for (int b = 0; b < Bars; b++)
        {
            int k0 = _edges[b], k1 = Math.Max(k0 + 1, _edges[b + 1]);
            double max = 0;
            for (int k = k0; k < k1; k++) max = Math.Max(max, _re[k] * _re[k] + _im[k] * _im[k]);
            // Amplitude crête d'une sinusoïde après fenêtre de Hann : |X| ≈ A·N/4. Pente +3 dB/oct pour une voix « plate ».
            double db = 10 * Math.Log10(max + 1e-20) - 20 * Math.Log10(Size / 4.0) + 3 * Math.Log2((double)k0 / _edges[0] + 1);
            double v = Math.Clamp((db - FloorDb) / (TopDb - FloorDb), 0, 1);
            _levels[b] = v > _levels[b] ? v : Math.Max(v, _levels[b] - 0.045);
            _peaks[b] = _levels[b] > _peaks[b] ? _levels[b] : Math.Max(0, _peaks[b] - 0.012);
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        for (double y = 0.5; y < h; y += 15) dc.DrawLine(GridPen, new Point(0, y), new Point(w, y));
        for (double x = 0.5; x < w; x += 24) dc.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
        dc.DrawLine(AxisPen, new Point(0, h - 0.5), new Point(w, h - 0.5));

        double top = 30, span = h - top - 16;
        var line = Curve(_levels, w, h, top, span, closed: false);
        dc.DrawGeometry(AreaBrush, null, Curve(_levels, w, h, top, span, closed: true));
        dc.DrawGeometry(null, PeakPen, Curve(_peaks, w, h, top, span, closed: false));
        dc.DrawGeometry(null, LinePen, line);
        dc.Pop();
    }

    /// <summary>Courbe lissée (Catmull-Rom) passant par le niveau de chaque bande.</summary>
    private static Geometry Curve(double[] v, double w, double h, double top, double span, bool closed)
    {
        var pts = new Point[v.Length];
        for (int i = 0; i < v.Length; i++) pts[i] = new Point(i * w / (v.Length - 1), top + span - Math.Clamp(v[i], 0, 1) * span);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(closed ? new Point(0, top + span) : pts[0], closed, closed);
            if (closed) ctx.LineTo(pts[0], false, false);
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var p0 = pts[Math.Max(0, i - 1)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Math.Min(pts.Length - 1, i + 2)];
                var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
                var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
                ctx.BezierTo(c1, c2, p2, true, true);
            }
            if (closed) ctx.LineTo(new Point(w, top + span), false, false);
        }
        g.Freeze();
        return g;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
