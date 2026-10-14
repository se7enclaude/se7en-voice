namespace Se7enVoice.Core.Dsp;

/// <summary>Un effet mono traité bloc par bloc sur le thread audio.</summary>
public abstract class Effect
{
    protected Effect(int sampleRate) => SampleRate = sampleRate;

    public int SampleRate { get; }
    public bool Enabled { get; set; } = true;

    public abstract void Process(Span<float> buffer);
    public virtual void Reset() { }

    protected float Coef(double ms) => (float)Math.Exp(-1.0 / (SampleRate * ms / 1000.0));
}

/// <summary>Volume en dB, lissé pour éviter les clics.</summary>
public sealed class Gain(int sampleRate) : Effect(sampleRate)
{
    private float _current = 1, _target = 1;

    public double Db { set => _target = (float)Math.Pow(10, value / 20); }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            _current += (_target - _current) * 0.002f;
            buffer[i] *= _current;
        }
    }
}

/// <summary>Coupe le bruit de fond quand on ne parle pas.</summary>
public sealed class NoiseGate : Effect
{
    private readonly float _envAttack, _envRelease, _openCoef, _closeCoef;
    private readonly int _holdSamples;
    private float _env, _gain, _threshold = 0.003f;
    private int _hold;

    public NoiseGate(int sampleRate) : base(sampleRate)
    {
        _envAttack = Coef(1); _envRelease = Coef(60);
        _openCoef = Coef(2); _closeCoef = Coef(80);
        _holdSamples = sampleRate / 8;
    }

    public double ThresholdDb { set => _threshold = (float)Math.Pow(10, value / 20); }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float a = Math.Abs(buffer[i]);
            _env = a > _env ? _envAttack * _env + (1 - _envAttack) * a : _envRelease * _env + (1 - _envRelease) * a;
            if (_env > _threshold) _hold = _holdSamples;
            else if (_hold > 0) _hold--;
            float target = _hold > 0 ? 1 : 0;
            float c = target > _gain ? _openCoef : _closeCoef;
            _gain = c * _gain + (1 - c) * target;
            buffer[i] *= _gain;
        }
    }

    public override void Reset() { _env = 0; _gain = 0; _hold = 0; }
}

/// <summary>Égaliseur 3 bandes : graves 200 Hz, médiums 1 kHz, aigus 4 kHz.</summary>
public sealed class Equalizer(int sampleRate) : Effect(sampleRate)
{
    private readonly Biquad _low = new(), _mid = new(), _high = new();

    public void Set(double lowDb, double midDb, double highDb)
    {
        _low.SetLowShelf(SampleRate, 200, lowDb);
        _mid.SetPeak(SampleRate, 1000, 0.8, midDb);
        _high.SetHighShelf(SampleRate, 4000, highDb);
    }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = _high.Process(_mid.Process(_low.Process(buffer[i])));
    }

    public override void Reset() { _low.Reset(); _mid.Reset(); _high.Reset(); }
}

/// <summary>
/// Rend la voix plus grave et plus « masculine » sans effet robotique : plus de poitrine
/// (graves vers 150 Hz), moins de nasal, un peu de compression et une saturation très douce
/// qui ajoute des harmoniques graves. La baisse de hauteur est faite par <see cref="PitchShifter"/>.
/// </summary>
public sealed class Deepener(int sampleRate) : Effect(sampleRate)
{
    private readonly Biquad _chest = new(), _body = new(), _nasal = new(), _air = new();
    private readonly float _attack = (float)Math.Exp(-1.0 / (sampleRate * 0.005)), _release = (float)Math.Exp(-1.0 / (sampleRate * 0.15));
    private float _env, _drive = 1, _norm = 1, _makeup = 1, _amount;

    /// <summary>Baisse de hauteur associée, en demi-tons.</summary>
    public static double PitchFor(double depth) => -3.5 * Math.Clamp(depth, 0, 1);

    public double Amount
    {
        set
        {
            double d = Math.Clamp(value, 0, 1);
            _amount = (float)d;
            Enabled = d > 0.001;
            _chest.SetLowShelf(SampleRate, 170, d * 7);
            _body.SetPeak(SampleRate, 120, 1.0, d * 3);
            _nasal.SetPeak(SampleRate, 1100, 1.2, -d * 3);
            _air.SetHighShelf(SampleRate, 6500, -d * 2);
            _drive = (float)(1 + d * 1.5);
            _norm = 1 / MathF.Tanh(_drive);
            _makeup = (float)Math.Pow(10, d * 3 / 20);
        }
    }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = _air.Process(_nasal.Process(_body.Process(_chest.Process(buffer[i]))));

            // Compression douce (seuil -24 dB, ratio jusqu'à 3:1)
            float a = Math.Abs(x);
            float c = a > _env ? _attack : _release;
            _env = c * _env + (1 - c) * a;
            float over = _env / 0.063f;
            float gain = over > 1 ? MathF.Pow(over, -(_amount * 0.66f)) : 1;

            float y = x * gain * _makeup;
            // Saturation très légère, dosée par l'intensité
            buffer[i] = y * (1 - _amount * 0.5f) + MathF.Tanh(y * _drive) * _norm * 0.6f * (_amount * 0.5f);
        }
    }

    public override void Reset()
    {
        _chest.Reset(); _body.Reset(); _nasal.Reset(); _air.Reset(); _env = 0;
    }
}

/// <summary>
/// Change la hauteur de la voix sans changer sa vitesse : deux têtes de lecture glissent
/// dans une ligne à retard et se relaient en fondu (faible latence, ~40 ms de fenêtre).
/// </summary>
public sealed class PitchShifter : Effect
{
    private readonly float[] _buf;
    private readonly int _mask;
    private readonly double _window;
    private int _write;
    private double _phase, _step;

    public PitchShifter(int sampleRate) : base(sampleRate)
    {
        _window = sampleRate * 0.04;
        int size = 1;
        while (size < _window * 2 + 4) size <<= 1;
        _buf = new float[size];
        _mask = size - 1;
    }

    public double Semitones
    {
        set
        {
            double ratio = Math.Pow(2, value / 12);
            _step = (1 - ratio) / _window;
            Enabled = Math.Abs(value) > 0.01;
        }
    }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            _buf[_write] = buffer[i];
            double p2 = _phase + 0.5;
            if (p2 >= 1) p2 -= 1;
            double g1 = Math.Sin(Math.PI * _phase);
            double g2 = Math.Sin(Math.PI * p2);
            buffer[i] = (float)(Read(_phase * _window) * g1 * g1 + Read(p2 * _window) * g2 * g2);

            _phase += _step;
            if (_phase >= 1) _phase -= 1;
            else if (_phase < 0) _phase += 1;
            _write = (_write + 1) & _mask;
        }
    }

    private float Read(double delay)
    {
        double pos = _write - delay;
        int i0 = (int)Math.Floor(pos);
        float frac = (float)(pos - i0);
        float a = _buf[i0 & _mask], b = _buf[(i0 + 1) & _mask];
        return a + (b - a) * frac;
    }

    public override void Reset() { Array.Clear(_buf); _phase = 0; }
}

/// <summary>Modulation en anneau : voix de robot (basse fréquence) ou d'alien (haute fréquence).</summary>
public sealed class RingModulator(int sampleRate) : Effect(sampleRate)
{
    private double _phase, _inc, _mix = 1;

    public void Set(double frequency, double mix)
    {
        _inc = 2 * Math.PI * frequency / SampleRate;
        _mix = Math.Clamp(mix, 0, 1);
    }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = buffer[i];
            buffer[i] = (float)(x * (1 - _mix) + x * Math.Sin(_phase) * _mix * 1.4);
            _phase += _inc;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }
    }
}

/// <summary>Saturation douce (voix de démon, mégaphone…).</summary>
public sealed class Distortion(int sampleRate) : Effect(sampleRate)
{
    private float _k = 5, _norm = 1;

    public double Drive
    {
        set
        {
            _k = (float)(1 + Math.Clamp(value, 0, 1) * 30);
            _norm = 1 / MathF.Tanh(_k);
        }
    }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = MathF.Tanh(buffer[i] * _k) * _norm * 0.7f;
    }
}

/// <summary>Effet radio / talkie-walkie : bande étroite + légère saturation.</summary>
public sealed class RadioFilter : Effect
{
    private readonly Biquad _hp1 = new(), _hp2 = new(), _lp1 = new(), _lp2 = new();

    public RadioFilter(int sampleRate) : base(sampleRate)
    {
        _hp1.SetHighPass(sampleRate, 450, 0.9); _hp2.SetHighPass(sampleRate, 450, 0.9);
        _lp1.SetLowPass(sampleRate, 2800, 0.9); _lp2.SetLowPass(sampleRate, 2800, 0.9);
    }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float x = _lp2.Process(_lp1.Process(_hp2.Process(_hp1.Process(buffer[i]))));
            buffer[i] = MathF.Tanh(x * 4) * 0.5f;
        }
    }

    public override void Reset() { _hp1.Reset(); _hp2.Reset(); _lp1.Reset(); _lp2.Reset(); }
}

/// <summary>Compresseur : rend la voix plus régulière et plus présente.</summary>
public sealed class Compressor : Effect
{
    private readonly float _attack, _release;
    private float _env;
    private double _thresholdDb = -20, _ratio = 4, _makeup = 1;

    public Compressor(int sampleRate) : base(sampleRate)
    {
        _attack = Coef(5); _release = Coef(120);
    }

    public double Amount
    {
        set
        {
            double a = Math.Clamp(value, 0, 1);
            _thresholdDb = -10 - a * 25;
            _ratio = 2 + a * 6;
            _makeup = Math.Pow(10, -_thresholdDb * (1 - 1 / _ratio) * 0.5 / 20);
        }
    }

    public override void Process(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float a = Math.Abs(buffer[i]);
            float c = a > _env ? _attack : _release;
            _env = c * _env + (1 - c) * a;
            double envDb = 20 * Math.Log10(_env + 1e-9);
            double over = envDb - _thresholdDb;
            double gainDb = over > 0 ? -over * (1 - 1 / _ratio) : 0;
            buffer[i] = (float)(buffer[i] * Math.Pow(10, gainDb / 20) * _makeup);
        }
    }

    public override void Reset() => _env = 0;
}

/// <summary>Écho (délai avec répétitions).</summary>
public sealed class Echo : Effect
{
    private readonly float[] _buf;
    private int _write, _delay;
    private float _feedback = 0.4f, _mix = 0.35f;

    public Echo(int sampleRate) : base(sampleRate)
    {
        _buf = new float[sampleRate * 2];
        _delay = sampleRate / 4;
    }

    public void Set(double timeMs, double feedback, double mix)
    {
        _delay = Math.Clamp((int)(SampleRate * timeMs / 1000), 1, _buf.Length - 1);
        _feedback = (float)Math.Clamp(feedback, 0, 0.9);
        _mix = (float)Math.Clamp(mix, 0, 1);
    }

    public override void Process(Span<float> buffer)
    {
        int len = _buf.Length;
        for (int i = 0; i < buffer.Length; i++)
        {
            int r = _write - _delay;
            if (r < 0) r += len;
            float d = _buf[r];
            float x = buffer[i];
            _buf[_write] = x + d * _feedback;
            buffer[i] = x + d * _mix;
            if (++_write == len) _write = 0;
        }
    }

    public override void Reset() => Array.Clear(_buf);
}

/// <summary>Réverbération (algorithme Freeverb simplifié, mono).</summary>
public sealed class Reverb : Effect
{
    private static readonly int[] CombTunings = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllPassTunings = [556, 441, 341, 225];

    private readonly float[][] _combs;
    private readonly int[] _combIdx;
    private readonly float[] _combStore;
    private readonly float[][] _allPasses;
    private readonly int[] _apIdx;
    private float _feedback = 0.84f, _damp = 0.3f, _mix = 0.3f;

    public Reverb(int sampleRate) : base(sampleRate)
    {
        double scale = sampleRate / 44100.0;
        _combs = CombTunings.Select(t => new float[(int)(t * scale)]).ToArray();
        _allPasses = AllPassTunings.Select(t => new float[(int)(t * scale)]).ToArray();
        _combIdx = new int[_combs.Length];
        _combStore = new float[_combs.Length];
        _apIdx = new int[_allPasses.Length];
    }

    public void Set(double size, double mix)
    {
        _feedback = (float)(0.7 + Math.Clamp(size, 0, 1) * 0.28);
        _mix = (float)Math.Clamp(mix, 0, 1);
    }

    public override void Process(Span<float> buffer)
    {
        for (int n = 0; n < buffer.Length; n++)
        {
            float input = buffer[n] * 0.015f;
            float acc = 0;
            for (int c = 0; c < _combs.Length; c++)
            {
                var b = _combs[c];
                int i = _combIdx[c];
                float y = b[i];
                _combStore[c] = y * (1 - _damp) + _combStore[c] * _damp;
                b[i] = input + _combStore[c] * _feedback;
                _combIdx[c] = i + 1 == b.Length ? 0 : i + 1;
                acc += y;
            }
            for (int a = 0; a < _allPasses.Length; a++)
            {
                var b = _allPasses[a];
                int i = _apIdx[a];
                float bufOut = b[i];
                float o = -acc + bufOut;
                b[i] = acc + bufOut * 0.5f;
                _apIdx[a] = i + 1 == b.Length ? 0 : i + 1;
                acc = o;
            }
            buffer[n] = buffer[n] * (1 - _mix * 0.5f) + acc * _mix * 3f;
        }
    }

    public override void Reset()
    {
        foreach (var b in _combs) Array.Clear(b);
        foreach (var b in _allPasses) Array.Clear(b);
        Array.Clear(_combStore);
    }
}

/// <summary>
/// Limiteur de sécurité en bout de chaîne : le signal ne dépasse jamais <see cref="Ceiling"/>,
/// ce qui évite la saturation dans Discord même avec le volume à fond.
/// </summary>
public sealed class Limiter : Effect
{
    private readonly float _release;
    private float _gain = 1;

    public Limiter(int sampleRate) : base(sampleRate) => _release = Coef(80);

    public float Ceiling { get; set; } = 0.944f; // -0,5 dBFS

    public override void Process(Span<float> buffer)
    {
        float ceiling = Ceiling;
        for (int i = 0; i < buffer.Length; i++)
        {
            float a = Math.Abs(buffer[i]);
            float needed = a > ceiling ? ceiling / a : 1;
            _gain = Math.Min(needed, _release * _gain + (1 - _release));
            float y = buffer[i] * _gain;
            buffer[i] = Math.Clamp(y, -ceiling, ceiling);
        }
    }

    public override void Reset() => _gain = 1;
}
