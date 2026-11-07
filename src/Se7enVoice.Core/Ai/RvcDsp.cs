using Se7enVoice.Core.Dsp;

namespace Se7enVoice.Core.Ai;

/// <summary>
/// Spectre mel de RMVPE, comme RVC (torch) / librosa : 16 kHz, FFT 1024, pas de 160 (100 trames/s), fenêtre de Hann
/// périodique, signal centré (bords en miroir), 128 bandes mel HTK de 30 à 8000 Hz normalisées « slaney », log(max(1e-5)).
/// </summary>
public sealed class MelSpectrogram
{
    public const int SampleRate = 16000, NFft = 1024, Hop = 160, Mels = 128;
    private const double FMin = 30, FMax = 8000;
    private readonly Fft _fft = new(NFft);
    private readonly double[] _window = new double[NFft];
    private readonly float[,] _filters = new float[Mels, NFft / 2 + 1];
    private readonly double[] _re = new double[NFft], _im = new double[NFft], _mag = new double[NFft / 2 + 1];

    public MelSpectrogram()
    {
        for (int i = 0; i < NFft; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / NFft); // périodique
        static double HzToMel(double f) => 2595 * Math.Log10(1 + f / 700);
        static double MelToHz(double m) => 700 * (Math.Pow(10, m / 2595) - 1);
        int bins = NFft / 2 + 1;
        var melF = new double[Mels + 2];
        double lo = HzToMel(FMin), hi = HzToMel(FMax);
        for (int i = 0; i < Mels + 2; i++) melF[i] = MelToHz(lo + (hi - lo) * i / (Mels + 1));
        for (int m = 0; m < Mels; m++)
        {
            double enorm = 2.0 / (melF[m + 2] - melF[m]);
            for (int k = 0; k < bins; k++)
            {
                double f = (double)k * SampleRate / NFft;
                double lower = (f - melF[m]) / (melF[m + 1] - melF[m]);
                double upper = (melF[m + 2] - f) / (melF[m + 2] - melF[m + 1]);
                _filters[m, k] = (float)(Math.Max(0, Math.Min(lower, upper)) * enorm);
            }
        }
    }

    /// <summary>Remplit <paramref name="dest"/> (taille Mels × <paramref name="frames"/>, bande par bande) pour le signal <paramref name="x"/>.</summary>
    public void Compute(ReadOnlySpan<float> x, int frames, Span<float> dest)
    {
        int n = x.Length, pad = NFft / 2, bins = NFft / 2 + 1;
        for (int t = 0; t < frames; t++)
        {
            int start = t * Hop - pad;
            for (int i = 0; i < NFft; i++)
            {
                int j = start + i;
                // miroir sans répéter le bord (numpy « reflect »)
                if (j < 0) j = -j;
                else if (j >= n) j = 2 * (n - 1) - j;
                j = Math.Clamp(j, 0, n - 1);
                _re[i] = x[j] * _window[i];
                _im[i] = 0;
            }
            _fft.Transform(_re, _im);
            for (int k = 0; k < bins; k++) _mag[k] = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            for (int m = 0; m < Mels; m++)
            {
                double s = 0;
                for (int k = 0; k < bins; k++)
                {
                    float w = _filters[m, k];
                    if (w != 0) s += w * _mag[k];
                }
                dest[m * frames + t] = (float)Math.Log(Math.Max(s, 1e-5));
            }
        }
    }
}

/// <summary>Hauteur (f0) : décodage de la sortie de RMVPE et forme « grossière » attendue par le générateur RVC.</summary>
public static class Pitch
{
    public const int Bins = 360;

    /// <summary>Saillance [trames × 360] → f0 en Hz (0 = pas de voix), moyenne pondérée autour du maximum (±4 cases).</summary>
    public static void DecodeRmvpe(ReadOnlySpan<float> salience, int frames, Span<float> f0, float threshold = 0.03f)
    {
        for (int t = 0; t < frames; t++)
        {
            var row = salience.Slice(t * Bins, Bins);
            int c = 0;
            float max = row[0];
            for (int k = 1; k < Bins; k++) if (row[k] > max) { max = row[k]; c = k; }
            int lo = Math.Max(0, c - 4), hi = Math.Min(Bins, c + 5);
            double sw = 0, swc = 0;
            for (int k = lo; k < hi; k++)
            {
                sw += row[k];
                swc += row[k] * (20.0 * k + 1997.3794084376191);
            }
            f0[t] = max > threshold && sw > 0 ? (float)(10 * Math.Pow(2, swc / sw / 1200)) : 0;
        }
    }

    /// <summary>f0 (Hz) → classe 1..255 sur l'échelle mel entre 50 et 1100 Hz (1 = pas de voix), comme RVC.</summary>
    public static long Coarse(float f0)
    {
        if (f0 <= 0) return 1;
        double lo = 1127 * Math.Log(1 + 50 / 700.0), hi = 1127 * Math.Log(1 + 1100 / 700.0);
        double m = 1127 * Math.Log(1 + f0 / 700.0);
        m = (m - lo) * 254 / (hi - lo) + 1;
        return (long)Math.Clamp(Math.Round(m, MidpointRounding.ToEven), 1, 255);
    }
}

/// <summary>
/// Raccord des blocs (SOLA) : chaque nouveau bloc est décalé (0..recherche) pour coller au mieux à la fin du précédent,
/// puis fondu enchaîné : pas de clic ni de « vibration » entre deux blocs.
/// </summary>
public sealed class SolaStitcher
{
    private readonly int _block, _fade, _search;
    private readonly float[] _tail, _fadeIn;

    public SolaStitcher(int block, int fade, int search)
    {
        _block = block;
        _fade = fade;
        _search = search;
        _tail = new float[fade];
        _fadeIn = new float[fade];
        for (int i = 0; i < fade; i++)
        {
            double s = Math.Sin(Math.PI / 2 * (i + 0.5) / fade);
            _fadeIn[i] = (float)(s * s);
        }
    }

    /// <summary>Longueur du morceau à fournir : bloc + fondu + recherche.</summary>
    public int Needed => _block + _fade + _search;

    /// <summary><paramref name="segment"/> (taille <see cref="Needed"/>) → <paramref name="output"/> (taille bloc).</summary>
    public void Process(ReadOnlySpan<float> segment, Span<float> output)
    {
        int best = 0;
        double bestScore = double.NegativeInfinity, tailNorm = 0;
        foreach (var v in _tail) tailNorm += v * (double)v;
        for (int k = 0; k <= _search; k++)
        {
            double dot = 0, norm = 0;
            for (int i = 0; i < _fade; i++)
            {
                double a = segment[k + i];
                dot += a * _tail[i];
                norm += a * a;
            }
            double score = dot / (Math.Sqrt(norm * tailNorm) + 1e-6);
            if (score > bestScore) { bestScore = score; best = k; }
        }
        var seg = segment.Slice(best, _block + _fade);
        for (int i = 0; i < _block; i++) output[i] = seg[i];
        for (int i = 0; i < _fade; i++) output[i] = seg[i] * _fadeIn[i] + _tail[i] * (1 - _fadeIn[i]);
        seg.Slice(_block, _fade).CopyTo(_tail);
    }

    public void Reset() => Array.Clear(_tail);
}

/// <summary>Rééchantillonneur continu (sinc fenêtré, filtre anti-repliement) : micro 48 kHz → 16 kHz, et 48 kHz → sortie.</summary>
public sealed class StreamResampler
{
    private const int Half = 16;         // demi-largeur du noyau (à la fréquence la plus basse)
    private readonly double _step, _cutoff;
    private readonly int _half;
    private readonly List<float> _hist = [];
    private double _pos;                  // position de lecture dans _hist (échantillons d'entrée)

    public StreamResampler(int inputRate, int outputRate)
    {
        InputRate = inputRate;
        OutputRate = outputRate;
        _step = (double)inputRate / outputRate;
        _cutoff = Math.Min(1.0, (double)outputRate / inputRate) * 0.92;
        _half = (int)Math.Ceiling(Half / Math.Min(1.0, _cutoff));
        for (int i = 0; i < _half; i++) _hist.Add(0);
        _pos = _half;
    }

    public int InputRate { get; }
    public int OutputRate { get; }

    /// <summary>Ajoute <paramref name="input"/> et ajoute à <paramref name="output"/> les échantillons produits.</summary>
    public void Process(ReadOnlySpan<float> input, List<float> output)
    {
        foreach (var v in input) _hist.Add(v);
        if (InputRate == OutputRate)
        {
            for (int i = _half; i < _hist.Count; i++) output.Add(_hist[i]);
            _hist.RemoveRange(0, _hist.Count - _half);
            return;
        }
        while (_pos + _half < _hist.Count)
        {
            int center = (int)_pos;
            double frac = _pos - center, sum = 0;
            for (int k = -_half + 1; k <= _half; k++)
            {
                double x = k - frac;
                double arg = Math.PI * x * _cutoff;
                double sinc = Math.Abs(arg) < 1e-9 ? 1 : Math.Sin(arg) / arg;
                double win = 0.5 + 0.5 * Math.Cos(Math.PI * x / (_half + 1)); // Hann
                sum += _hist[center + k] * sinc * win;
            }
            output.Add((float)(sum * _cutoff));
            _pos += _step;
        }
        // on garde juste l'historique utile
        int drop = (int)_pos - _half;
        if (drop > 0)
        {
            _hist.RemoveRange(0, drop);
            _pos -= drop;
        }
    }
}

/// <summary>
/// Hauteur habituelle de la voix de l'utilisateur (médiane des trames voisées récentes) : sert à décaler
/// automatiquement vers la hauteur de la voix IA choisie (une voix d'homme vers une voix de femme : +12 environ).
/// </summary>
public sealed class VoiceRange
{
    private readonly float[] _ring;
    private int _count, _next;

    public VoiceRange(int frames = 1500) => _ring = new float[frames];

    public void Add(ReadOnlySpan<float> f0)
    {
        foreach (var v in f0)
        {
            if (v < 60 || v > 600) continue;
            _ring[_next] = v;
            _next = (_next + 1) % _ring.Length;
            if (_count < _ring.Length) _count++;
        }
    }

    /// <summary>Hauteur médiane (Hz), 0 tant qu'on n'a pas assez entendu (environ 2 s de voix).</summary>
    public float Median
    {
        get
        {
            if (_count < 200) return 0;
            var a = _ring.AsSpan(0, _count).ToArray();
            Array.Sort(a);
            return a[a.Length / 2];
        }
    }

    /// <summary>Décalage en demi-tons pour aller de la voix de l'utilisateur à <paramref name="targetHz"/> (0 si inconnu).</summary>
    public double SemitonesTo(double targetHz) => Median > 0 ? 12 * Math.Log2(targetHz / Median) : 0;
}
