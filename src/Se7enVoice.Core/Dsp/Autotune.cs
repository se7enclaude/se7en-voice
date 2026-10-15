namespace Se7enVoice.Core.Dsp;

/// <summary>
/// Détecteur de hauteur (algorithme YIN) sur une copie sous-échantillonnée (~16 kHz) de la voix.
/// Une mesure toutes les 10 ms ; <see cref="Frequency"/> vaut 0 quand on ne chante/parle pas.
/// </summary>
public sealed class PitchTracker
{
    private const double MinFreq = 70, MaxFreq = 900, Threshold = 0.15;

    private readonly int _decim, _minLag, _maxLag, _hop;
    private readonly Biquad _aa1 = new(), _aa2 = new();
    private readonly float[] _buf; // fenêtre écrite deux fois : les L dernières valeurs sont toujours contiguës
    private readonly double[] _diff;
    private readonly int _len;
    private int _phase, _count, _sinceLast, _pos;

    public PitchTracker(int sampleRate)
    {
        _decim = Math.Max(1, (int)Math.Round(sampleRate / 16000.0));
        Rate = (double)sampleRate / _decim;
        _aa1.SetLowPass(sampleRate, Rate * 0.42, 0.54);
        _aa2.SetLowPass(sampleRate, Rate * 0.42, 1.31);
        _minLag = (int)(Rate / MaxFreq);
        _maxLag = (int)Math.Ceiling(Rate / MinFreq);
        _len = _maxLag * 2;
        _buf = new float[_len * 2];
        _diff = new double[_maxLag + 1];
        _hop = (int)(Rate / 100);
    }

    /// <summary>Fréquence d'analyse après sous-échantillonnage.</summary>
    public double Rate { get; }

    /// <summary>Dernière hauteur détectée en Hz, 0 si pas de voix.</summary>
    public double Frequency { get; private set; }

    public void Feed(ReadOnlySpan<float> block)
    {
        foreach (var x in block)
        {
            float y = _aa2.Process(_aa1.Process(x));
            if (++_phase < _decim) continue;
            _phase = 0;
            _buf[_pos] = _buf[_pos + _len] = y;
            if (++_pos == _len) _pos = 0;
            if (_count < _len) _count++;
            if (++_sinceLast >= _hop && _count == _len)
            {
                _sinceLast = 0;
                Frequency = Detect();
            }
        }
    }

    private double Detect()
    {
        int w = _maxLag;
        var x = _buf.AsSpan(_pos, _len); // de la plus ancienne à la plus récente
        double energy = 0;
        for (int j = 0; j < w; j++) energy += x[j] * x[j];
        if (energy / w < 1e-5) return 0; // trop faible (~ -50 dBFS) : silence

        _diff[0] = 1;
        double running = 0;
        int best = -1;
        for (int tau = 1; tau <= _maxLag; tau++)
        {
            double d = 0;
            for (int j = 0; j < w; j++)
            {
                double t = x[j] - x[j + tau];
                d += t * t;
            }
            running += d;
            _diff[tau] = running > 0 ? d * tau / running : 1;
        }
        for (int tau = _minLag; tau < _maxLag; tau++)
        {
            if (_diff[tau] < Threshold)
            {
                while (tau + 1 < _maxLag && _diff[tau + 1] < _diff[tau]) tau++;
                best = tau;
                break;
            }
        }
        if (best < 0) return 0;

        // Interpolation parabolique autour du minimum
        double a = _diff[best - 1], b = _diff[best], c = _diff[best + 1];
        double den = a - 2 * b + c;
        double t0 = Math.Abs(den) > 1e-12 ? best + 0.5 * (a - c) / den : best;
        return Rate / t0;
    }

    public void Reset()
    {
        Array.Clear(_buf); _count = 0; _pos = 0; _sinceLast = 0; Frequency = 0;
        _aa1.Reset(); _aa2.Reset();
    }
}

/// <summary>
/// Correction de justesse (« autotune ») : on mesure la note chantée et on calcule de combien
/// de demi-tons la décaler pour tomber sur la note juste de la gamme. Vitesse à fond = effet
/// robotique façon T-Pain ; vitesse basse = correction discrète.
/// </summary>
public sealed class Autotune
{
    private static readonly int[][] Scales =
    [
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11], // chromatique
        [0, 2, 4, 5, 7, 9, 11],                 // majeure
        [0, 2, 3, 5, 7, 8, 10],                 // mineure
        [0, 2, 4, 7, 9],                        // pentatonique
    ];

    public static readonly string[] KeyNames = ["Do", "Do#", "Ré", "Ré#", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "La#", "Si"];
    public static readonly string[] ScaleNames = ["Chromatique", "Majeure", "Mineure", "Pentatonique"];

    private readonly PitchTracker _tracker;
    private readonly int _sampleRate;
    private double _correction, _target = double.NaN;

    public Autotune(int sampleRate)
    {
        _sampleRate = sampleRate;
        _tracker = new PitchTracker(sampleRate);
    }

    public int Key { get; set; }
    public int Scale { get; set; }
    /// <summary>0..1 : 1 = la note est corrigée instantanément.</summary>
    public double Speed { get; set; } = 1;

    public double DetectedFrequency => _tracker.Frequency;

    /// <summary>Analyse le bloc et renvoie la correction (en demi-tons) à ajouter au décalage <paramref name="baseSemitones"/>.</summary>
    public double Process(ReadOnlySpan<float> block, double baseSemitones)
    {
        _tracker.Feed(block);
        double f = _tracker.Frequency;
        if (f > 0)
        {
            double note = 69 + 12 * Math.Log2(f / 440) + baseSemitones;
            // Hystérésis : on garde la note visée tant qu'on n'en est pas franchement sorti.
            if (double.IsNaN(_target) || Math.Abs(note - _target) > 0.65) _target = Nearest(note);
            double wanted = _target - note;
            double tau = 0.004 + (1 - Math.Clamp(Speed, 0, 1)) * 0.16;
            double alpha = 1 - Math.Exp(-block.Length / (_sampleRate * tau));
            _correction += (wanted - _correction) * alpha;
        }
        return _correction;
    }

    private double Nearest(double note)
    {
        var scale = Scales[Math.Clamp(Scale, 0, Scales.Length - 1)];
        double best = note, dist = double.MaxValue;
        int octave = (int)Math.Floor(note / 12);
        for (int o = octave - 1; o <= octave + 1; o++)
            foreach (int deg in scale)
            {
                double cand = o * 12 + ((Key % 12 + 12) % 12) + deg;
                double d = Math.Abs(cand - note);
                if (d < dist) { dist = d; best = cand; }
            }
        return best;
    }

    public void Reset()
    {
        _tracker.Reset();
        _correction = 0;
        _target = double.NaN;
    }
}
