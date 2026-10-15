using System.Windows;
using System.Windows.Media;
using Se7enVoice.Core.Dsp;

namespace Se7enVoice.Controls;

/// <summary>
/// Analyseur de spectre temps réel de la voix envoyée au micro virtuel : barres arrondies
/// (rampe de chaleur des vu-mètres) sur une échelle de fréquences logarithmique 60 Hz – 14 kHz.
/// </summary>
public sealed class SpectrumView : FrameworkElement
{
    private const int Size = 2048, Bars = 36;
    private const double MinHz = 60, MaxHz = 14000, FloorDb = -78, TopDb = -12;

    private static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush CapBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8)));
    private static readonly Brush BarBrush = Freeze(new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0x4F, 0xD1, 0xB0), 0), new GradientStop(Color.FromRgb(0xDD, 0xF2, 0x47), 0.62),
         new GradientStop(Color.FromRgb(0xED, 0xB8, 0x42), 0.82), new GradientStop(Color.FromRgb(0xF2, 0x60, 0x5A), 1)],
        new Point(0, 1), new Point(0, 0)) { MappingMode = BrushMappingMode.RelativeToBoundingBox });

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
        double slot = w / Bars, bar = slot * 0.58, r = bar / 2;
        // Le dégradé couvre toute la hauteur : on découpe chaque barre dedans.
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
        for (int b = 0; b < Bars; b++)
        {
            double x = b * slot + (slot - bar) / 2;
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(x, 0, bar, h), r, r);
            double lh = _levels[b] * h;
            if (lh > 1)
            {
                dc.PushClip(new RectangleGeometry(new Rect(x, h - lh, bar, lh), r, r));
                dc.DrawRectangle(BarBrush, null, new Rect(x, 0, bar, h));
                dc.Pop();
            }
            double py = h - _peaks[b] * h;
            if (_peaks[b] > 0.02) dc.DrawRoundedRectangle(CapBrush, null, new Rect(x, Math.Max(0, py - 3), bar, 2.5), 1, 1);
        }
        dc.Pop();
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
