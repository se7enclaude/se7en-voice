namespace Se7enVoice.Core.Audio;

/// <summary>
/// Tampon circulaire mono entre le micro (producteur) et une sortie (consommateur) qui tourne
/// éventuellement à une autre fréquence. Il rééchantillonne (interpolation linéaire, largement
/// suffisant pour la voix) et corrige la dérive d'horloge entre les deux cartes son en gardant
/// un remplissage proche de <see cref="TargetLatencyMs"/>.
/// </summary>
public sealed class ResamplingRing
{
    private readonly object _lock = new();
    private readonly float[] _buf;
    private readonly int _mask;
    private long _write;     // nombre total d'échantillons écrits
    private double _read;    // position de lecture (fractionnaire)
    private bool _priming = true;

    public ResamplingRing(int inputRate, int outputRate, double targetLatencyMs = 30)
    {
        InputRate = inputRate;
        OutputRate = outputRate;
        TargetLatencyMs = targetLatencyMs;
        int size = 1;
        while (size < inputRate) size <<= 1; // ~1 s de marge
        _buf = new float[size];
        _mask = size - 1;
    }

    public int InputRate { get; }
    public int OutputRate { get; }
    public double TargetLatencyMs { get; }

    private int TargetSamples => (int)(InputRate * TargetLatencyMs / 1000);

    public int Available
    {
        get { lock (_lock) return (int)(_write - (long)_read); }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (var s in samples)
            {
                _buf[_write & _mask] = s;
                _write++;
            }
            // Trop de retard accumulé : on saute vers la cible pour rester réactif.
            if (_write - _read > Math.Max(TargetSamples * 4, InputRate / 10))
                _read = _write - TargetSamples;
            if (_write - (long)_read > _buf.Length - 2)
                _read = _write - TargetSamples;
        }
    }

    /// <summary>Remplit <paramref name="dest"/> (mono, à <see cref="OutputRate"/>). Retourne le nombre d'échantillons réels (le reste est du silence).</summary>
    public int Read(Span<float> dest)
    {
        lock (_lock)
        {
            double step = (double)InputRate / OutputRate;
            long available = _write - (long)_read;

            if (_priming)
            {
                if (available < TargetSamples)
                {
                    dest.Clear();
                    return 0;
                }
                _priming = false;
            }

            // Correction douce de dérive : ±0,5 % de vitesse selon le remplissage.
            double fill = available - TargetSamples;
            step *= 1 + Math.Clamp(fill / (InputRate * 2.0), -0.005, 0.005);

            int produced = 0;
            for (; produced < dest.Length; produced++)
            {
                long i0 = (long)Math.Floor(_read);
                if (i0 + 1 >= _write)
                {
                    _priming = true; // plus de données : on attend de reremplir le tampon
                    break;
                }
                float frac = (float)(_read - i0);
                float a = _buf[i0 & _mask], b = _buf[(i0 + 1) & _mask];
                dest[produced] = a + (b - a) * frac;
                _read += step;
            }
            dest[produced..].Clear();
            return produced;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _read = _write;
            _priming = true;
        }
    }
}
