namespace Se7enVoice.Core.Dsp;

/// <summary>Filtre biquad (formules du « Audio EQ Cookbook » de R. Bristow-Johnson).</summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    public float Process(float x)
    {
        double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        return (float)y;
    }

    public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;

    public void SetLowPass(double sr, double freq, double q = 0.707)
    {
        Prepare(sr, freq, q, out double cos, out double alpha);
        Set((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public void SetHighPass(double sr, double freq, double q = 0.707)
    {
        Prepare(sr, freq, q, out double cos, out double alpha);
        Set((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public void SetPeak(double sr, double freq, double q, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        Prepare(sr, freq, q, out double cos, out double alpha);
        Set(1 + alpha * a, -2 * cos, 1 - alpha * a, 1 + alpha / a, -2 * cos, 1 - alpha / a);
    }

    public void SetLowShelf(double sr, double freq, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        Prepare(sr, freq, 0.707, out double cos, out double alpha);
        double s = 2 * Math.Sqrt(a) * alpha;
        Set(a * ((a + 1) - (a - 1) * cos + s), 2 * a * ((a - 1) - (a + 1) * cos), a * ((a + 1) - (a - 1) * cos - s),
            (a + 1) + (a - 1) * cos + s, -2 * ((a - 1) + (a + 1) * cos), (a + 1) + (a - 1) * cos - s);
    }

    public void SetHighShelf(double sr, double freq, double gainDb)
    {
        double a = Math.Pow(10, gainDb / 40);
        Prepare(sr, freq, 0.707, out double cos, out double alpha);
        double s = 2 * Math.Sqrt(a) * alpha;
        Set(a * ((a + 1) + (a - 1) * cos + s), -2 * a * ((a - 1) + (a + 1) * cos), a * ((a + 1) + (a - 1) * cos - s),
            (a + 1) - (a - 1) * cos + s, 2 * ((a - 1) - (a + 1) * cos), (a + 1) - (a - 1) * cos - s);
    }

    private static void Prepare(double sr, double freq, double q, out double cos, out double alpha)
    {
        freq = Math.Clamp(freq, 10, sr * 0.45);
        double w = 2 * Math.PI * freq / sr;
        cos = Math.Cos(w);
        alpha = Math.Sin(w) / (2 * q);
    }

    private void Set(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0; _a1 = a1 / a0; _a2 = a2 / a0;
    }
}
