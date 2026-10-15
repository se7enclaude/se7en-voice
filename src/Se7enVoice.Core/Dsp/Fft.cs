namespace Se7enVoice.Core.Dsp;

/// <summary>FFT complexe radix-2 en place (taille puissance de 2), tables précalculées.</summary>
public sealed class Fft
{
    private readonly int[] _rev;
    private readonly double[] _cos, _sin;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("La taille doit être une puissance de 2.", nameof(size));
        Size = size;
        int bits = (int)Math.Log2(size);
        _rev = new int[size];
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _rev[i] = r;
        }
        _cos = new double[size / 2];
        _sin = new double[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            _cos[i] = Math.Cos(2 * Math.PI * i / size);
            _sin[i] = Math.Sin(2 * Math.PI * i / size);
        }
    }

    public int Size { get; }

    /// <summary>Transformée directe (<paramref name="inverse"/> = false) ou inverse, normalisée par 1/N.</summary>
    public void Transform(double[] re, double[] im, bool inverse = false)
    {
        int n = Size;
        for (int i = 0; i < n; i++)
        {
            int j = _rev[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        double sign = inverse ? 1 : -1;
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1, step = n / len;
            for (int start = 0; start < n; start += len)
            {
                for (int k = 0; k < half; k++)
                {
                    double wr = _cos[k * step], wi = sign * _sin[k * step];
                    int a = start + k, b = a + half;
                    double tr = re[b] * wr - im[b] * wi;
                    double ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                }
            }
        }
        if (inverse)
        {
            double s = 1.0 / n;
            for (int i = 0; i < n; i++) { re[i] *= s; im[i] *= s; }
        }
    }
}
