namespace Se7enVoice.Core.Dsp;

/// <summary>
/// Effet dans le domaine fréquentiel : la voix est découpée en trames qui se chevauchent à 75 %
/// (fenêtre de Hann), passée en FFT, modifiée par <see cref="ProcessSpectrum"/>, puis recomposée.
/// Latence : <see cref="LatencySamples"/> (21 ms à 48 kHz), seulement quand l'effet est allumé.
/// </summary>
public abstract class StftEffect : Effect
{
    protected readonly int N, Hop;
    protected readonly Fft Fft;
    /// <summary>Spectre de la trame en cours (N cases, seules 0..N/2 sont à modifier).</summary>
    protected readonly double[] Re, Im;
    private readonly double[] _window, _outAccum;
    private readonly float[] _inFifo, _outFifo;
    private readonly double _norm;
    private int _rover;

    protected StftEffect(int sampleRate) : base(sampleRate)
    {
        N = sampleRate > 48000 ? 2048 : 1024;
        Hop = N / 4;
        Fft = new Fft(N);
        Re = new double[N];
        Im = new double[N];
        _window = new double[N];
        for (int i = 0; i < N; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        _norm = 1 / 1.5; // somme des fenêtres de Hann² à 75 % de recouvrement
        _inFifo = new float[N];
        _outFifo = new float[Hop];
        _outAccum = new double[N];
        _rover = Fill;
    }

    /// <summary>Retard entre l'entrée et la sortie, en échantillons.</summary>
    public int LatencySamples => N;

    private int Fill => N - Hop;

    /// <summary>Nombre de trames analysées par seconde.</summary>
    protected double FrameRate => (double)SampleRate / Hop;

    public override void Process(Span<float> buffer)
    {
        int latency = Fill;
        for (int i = 0; i < buffer.Length; i++)
        {
            _inFifo[_rover] = buffer[i];
            buffer[i] = _outFifo[_rover - latency];
            if (++_rover < N) continue;
            _rover = latency;
            Frame();
        }
    }

    private void Frame()
    {
        for (int k = 0; k < N; k++) { Re[k] = _inFifo[k] * _window[k]; Im[k] = 0; }
        Fft.Transform(Re, Im);
        ProcessSpectrum();
        int half = N / 2;
        Im[0] = 0; Im[half] = 0;
        for (int k = 1; k < half; k++) { Re[N - k] = Re[k]; Im[N - k] = -Im[k]; }
        Fft.Transform(Re, Im, inverse: true);

        for (int k = 0; k < N; k++) _outAccum[k] += Re[k] * _window[k] * _norm;
        for (int k = 0; k < Hop; k++) _outFifo[k] = (float)_outAccum[k];
        Array.Copy(_outAccum, Hop, _outAccum, 0, N - Hop);
        Array.Clear(_outAccum, N - Hop, Hop);
        Array.Copy(_inFifo, Hop, _inFifo, 0, N - Hop);
    }

    /// <summary>Modifie <see cref="Re"/>/<see cref="Im"/> pour les cases 0..N/2 (le reste est recopié par symétrie).</summary>
    protected abstract void ProcessSpectrum();

    public override void Reset()
    {
        Array.Clear(_inFifo); Array.Clear(_outFifo); Array.Clear(_outAccum);
        _rover = Fill;
    }
}

/// <summary>
/// Suppression de bruit spectrale (type « Krisp » simplifié) : le bruit de fond de chaque bande
/// est estimé en continu (minimum suivi) puis soustrait. Enlève ventilo, souffle, clim, PC
/// bruyant… même pendant qu'on parle, contrairement à l'anti-bruit (gate).
/// </summary>
public sealed class SpectralDenoiser : StftEffect
{
    private readonly double[] _noise, _power, _slow, _gain, _raw;
    private readonly double _rise;
    private double _over = 3, _floor = 0.1;
    private int _frames;

    public SpectralDenoiser(int sampleRate) : base(sampleRate)
    {
        int bins = N / 2 + 1;
        _noise = new double[bins]; _power = new double[bins]; _slow = new double[bins]; _gain = new double[bins]; _raw = new double[bins];
        _rise = Math.Pow(10, 4.0 / 10 / FrameRate); // l'estimation du bruit peut remonter de 4 dB/s
        Reset();
    }

    /// <summary>0..1 : force de la suppression.</summary>
    public double Amount
    {
        set
        {
            double a = Math.Clamp(value, 0, 1);
            _over = 1.5 + a * 3.5;
            _floor = Math.Pow(10, -(8 + a * 24) / 20);
        }
    }

    protected override void ProcessSpectrum()
    {
        int half = N / 2;
        bool priming = _frames < 12;
        _frames++;
        for (int k = 0; k <= half; k++)
        {
            double p = Re[k] * Re[k] + Im[k] * Im[k] + 1e-14;
            _power[k] = _frames == 1 ? p : 0.6 * _power[k] + 0.4 * p;
            if (priming)
            {
                // Premières trames : moyenne simple, point de départ de l'estimation du bruit.
                _slow[k] += (p - _slow[k]) / _frames;
                _noise[k] = _slow[k];
                continue;
            }
            // Le bruit est suivi sur une puissance très lissée (moins de creux trompeurs) : minimum qui remonte doucement.
            _slow[k] = 0.9 * _slow[k] + 0.1 * p;
            if (_slow[k] < _noise[k]) _noise[k] = _slow[k];
            else _noise[k] *= _rise;

        }
        for (int k = 0; k <= half; k++)
        {
            if (priming) { _raw[k] = 1; continue; }
            double g = 1 - _over * 1.6 * _noise[k] / _power[k];
            _raw[k] = g <= _floor * _floor ? _floor : Math.Max(_floor, Math.Sqrt(g));
        }
        for (int k = 0; k <= half; k++)
        {
            // Lissage en fréquence (3 cases) puis dans le temps (attaque immédiate, relâche douce) : pas de « bruit musical ».
            double g = (_raw[Math.Max(0, k - 1)] + 2 * _raw[k] + _raw[Math.Min(half, k + 1)]) / 4;
            _gain[k] = g > _gain[k] ? g : 0.55 * _gain[k] + 0.45 * g;
            Re[k] *= _gain[k];
            Im[k] *= _gain[k];
        }
    }

    public override void Reset()
    {
        base.Reset();
        Array.Fill(_gain, 1.0);
        Array.Clear(_noise); Array.Clear(_power); Array.Clear(_slow);
        _frames = 0;
    }
}

/// <summary>
/// Timbre (formants) et chuchotement. Les formants sont les résonances de la bouche et de la gorge :
/// les déplacer change le « gabarit » de la voix (homme ↔ femme, enfant, géant) sans toucher à la
/// hauteur. L'enveloppe spectrale est estimée par cepstre, puis étirée par <see cref="Ratio"/>.
/// Le chuchotement remplace les phases par du hasard : la voix perd sa note et devient un souffle.
/// </summary>
public sealed class TimbreShifter : StftEffect
{
    private readonly double[] _cRe, _cIm, _env;
    private readonly int _lifter;
    private double _ratio = 1, _whisper;
    private uint _seed = 0x5E7E7u;

    public TimbreShifter(int sampleRate) : base(sampleRate)
    {
        _cRe = new double[N]; _cIm = new double[N]; _env = new double[N / 2 + 1];
        _lifter = Math.Max(8, (int)Math.Round(sampleRate * 0.0011)); // quéfrence 1,1 ms : sous la période des voix (≤ 800 Hz)
        Enabled = false;
    }

    /// <summary>Facteur d'étirement des formants (&gt; 1 = voix plus « petite », &lt; 1 = plus « grande »).</summary>
    public double Ratio { get => _ratio; set { _ratio = Math.Clamp(value, 0.4, 2.5); UpdateEnabled(); } }

    /// <summary>0..1 : part de souffle (chuchotement).</summary>
    public double Whisper { get => _whisper; set { _whisper = Math.Clamp(value, 0, 1); UpdateEnabled(); } }

    private void UpdateEnabled() => Enabled = Math.Abs(_ratio - 1) > 0.005 || _whisper > 0.005;

    protected override void ProcessSpectrum()
    {
        int half = N / 2;
        if (Math.Abs(_ratio - 1) > 0.005) ShiftFormants(half);
        if (_whisper > 0.005) Whisperize(half);
    }

    private void ShiftFormants(int half)
    {
        // Enveloppe = log|X| lissé par cepstre (on ne garde que les premiers coefficients).
        for (int k = 0; k <= half; k++)
        {
            double l = 0.5 * Math.Log(Re[k] * Re[k] + Im[k] * Im[k] + 1e-12);
            _cRe[k] = l; _cIm[k] = 0;
            if (k > 0 && k < half) { _cRe[N - k] = l; _cIm[N - k] = 0; }
        }
        Fft.Transform(_cRe, _cIm, inverse: true);
        for (int k = _lifter; k <= N - _lifter; k++) { _cRe[k] = 0; _cIm[k] = 0; }
        Fft.Transform(_cRe, _cIm);
        for (int k = 0; k <= half; k++) _env[k] = _cRe[k];

        for (int k = 1; k <= half; k++)
        {
            double src = k / _ratio;
            double target;
            if (src >= half) target = _env[half] - 3;
            else
            {
                int i0 = (int)src;
                double f = src - i0;
                target = _env[i0] + (_env[i0 + 1] - _env[i0]) * f;
            }
            double g = Math.Exp(Math.Clamp(target - _env[k], -3.5, 2.5));
            Re[k] *= g;
            Im[k] *= g;
        }
    }

    private void Whisperize(int half)
    {
        double w = _whisper;
        for (int k = 1; k < half; k++)
        {
            double mag = Math.Sqrt(Re[k] * Re[k] + Im[k] * Im[k]);
            // xorshift : rapide et sans allocation sur le thread audio
            _seed ^= _seed << 13; _seed ^= _seed >> 17; _seed ^= _seed << 5;
            double ph = _seed * (2 * Math.PI / uint.MaxValue);
            Re[k] = (1 - w) * Re[k] + w * mag * Math.Cos(ph);
            Im[k] = (1 - w) * Im[k] + w * mag * Math.Sin(ph);
        }
    }
}
