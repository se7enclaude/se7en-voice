namespace Se7enVoice.Core.Dsp;

/// <summary>
/// Chaîne d'effets dans un ordre fixe, celui d'un studio : nettoyage → hauteur et timbre →
/// caractère → couleur → dynamique → espace. <see cref="Settings"/> peut être changé depuis
/// n'importe quel thread : le thread audio applique la nouvelle valeur au début du bloc suivant.
/// </summary>
public sealed class EffectChain
{
    private readonly NoiseGate _gate;
    private readonly LowCut _lowCut;
    private readonly SpectralDenoiser _denoise;
    private readonly PitchShifter _pitch;
    private readonly TimbreShifter _timbre;
    private readonly Deepener _deepener;
    private readonly Harmonizer _harmony;
    private readonly RingModulator _robot;
    private readonly Vibrato _vibrato;
    private readonly Distortion _distortion;
    private readonly Bitcrusher _bitcrush;
    private readonly RadioFilter _radio;
    private readonly BandFilter _filter;
    private readonly Equalizer _eq;
    private readonly DeEsser _deEsser;
    private readonly Compressor _compressor;
    private readonly Chorus _chorus;
    private readonly Echo _echo;
    private readonly Reverb _reverb;
    private readonly Gain _volume;
    private readonly Autotune _autotune;
    private readonly Effect[] _order;

    private volatile VoiceSettings _settings = new();
    private VoiceSettings? _applied;
    private double _basePitch;
    private bool _autotuneOn;

    public EffectChain(int sampleRate)
    {
        SampleRate = sampleRate;
        _gate = new(sampleRate);
        _lowCut = new(sampleRate);
        _denoise = new(sampleRate);
        _pitch = new(sampleRate);
        _timbre = new(sampleRate);
        _deepener = new(sampleRate);
        _harmony = new(sampleRate);
        _robot = new(sampleRate);
        _vibrato = new(sampleRate);
        _distortion = new(sampleRate);
        _bitcrush = new(sampleRate);
        _radio = new(sampleRate);
        _filter = new(sampleRate);
        _eq = new(sampleRate);
        _deEsser = new(sampleRate);
        _compressor = new(sampleRate);
        _chorus = new(sampleRate);
        _echo = new(sampleRate);
        _reverb = new(sampleRate);
        _volume = new(sampleRate);
        _autotune = new(sampleRate);
        _order = [_gate, _lowCut, _denoise, _pitch, _timbre, _deepener, _harmony, _robot, _vibrato, _distortion, _bitcrush,
                  _radio, _filter, _eq, _deEsser, _compressor, _chorus, _echo, _reverb, _volume];
    }

    public int SampleRate { get; }

    public VoiceSettings Settings
    {
        get => _settings;
        set => _settings = value ?? new();
    }

    /// <summary>Note détectée par l'autotune (Hz), 0 sans voix ou autotune éteint.</summary>
    public double DetectedFrequency => _autotuneOn ? _autotune.DetectedFrequency : 0;

    /// <summary>Retard ajouté par les effets allumés, en millisecondes (approximatif).</summary>
    public double LatencyMs
    {
        get
        {
            double samples = 0;
            if (_denoise.Enabled) samples += _denoise.LatencySamples;
            if (_timbre.Enabled) samples += _timbre.LatencySamples;
            if (_pitch.Enabled) samples += SampleRate * 0.02;
            return samples * 1000.0 / SampleRate;
        }
    }

    public void Process(Span<float> buffer)
    {
        var s = _settings;
        if (!ReferenceEquals(s, _applied)) Apply(s);
        if (_autotuneOn)
        {
            // L'analyse se fait sur la voix nettoyée mais pas encore décalée.
            _gate.ProcessIf(buffer);
            _lowCut.ProcessIf(buffer);
            _denoise.ProcessIf(buffer);
            _pitch.Semitones = _basePitch + _autotune.Process(buffer, _basePitch);
            _pitch.Enabled = true;
            for (int i = 3; i < _order.Length; i++)
                if (_order[i].Enabled) _order[i].Process(buffer);
            return;
        }
        foreach (var effect in _order)
            if (effect.Enabled) effect.Process(buffer);
    }

    public void Reset()
    {
        foreach (var effect in _order) effect.Reset();
        _autotune.Reset();
    }

    private void Apply(VoiceSettings s)
    {
        var previous = _applied;
        _applied = s;

        _gate.Enabled = s.GateOn;
        _gate.ThresholdDb = s.GateThresholdDb;

        _lowCut.Enabled = s.LowCutOn;
        _lowCut.Frequency = s.LowCutHz;

        if (s.DenoiseOn && previous is { DenoiseOn: false }) _denoise.Reset();
        _denoise.Enabled = s.DenoiseOn;
        _denoise.Amount = s.DenoiseAmount;

        _basePitch = s.PitchSemitones + Deepener.PitchFor(s.Depth);
        _pitch.Semitones = _basePitch;
        if (s.AutotuneOn && previous is { AutotuneOn: false }) _autotune.Reset();
        _autotuneOn = s.AutotuneOn;
        _autotune.Key = s.AutotuneKey;
        _autotune.Scale = s.AutotuneScale;
        _autotune.Speed = s.AutotuneSpeed;

        // « Garder le timbre » : on compense le déplacement des formants causé par le changement de hauteur.
        double formant = s.FormantSemitones - (s.KeepFormants ? s.PitchSemitones : 0);
        _timbre.Ratio = Math.Pow(2, formant / 12);
        _timbre.Whisper = s.WhisperOn ? s.WhisperAmount : 0;

        _deepener.Amount = s.Depth;

        _harmony.Enabled = s.HarmonyOn;
        _harmony.Set(s.HarmonySemitones, s.HarmonyMix);

        _robot.Enabled = s.RobotOn;
        _robot.Set(s.RobotFrequency, s.RobotMix);

        _vibrato.Enabled = s.VibratoOn;
        _vibrato.Set(s.VibratoRate, s.VibratoDepth);

        _distortion.Enabled = s.DistortionOn;
        _distortion.Drive = s.DistortionDrive;

        _bitcrush.Enabled = s.BitcrushOn;
        _bitcrush.Set(s.BitcrushBits, s.BitcrushRateHz);

        _radio.Enabled = s.RadioOn;

        _filter.Enabled = s.FilterOn;
        _filter.Set(s.FilterLowHz, s.FilterHighHz);

        _eq.Enabled = s.EqOn;
        _eq.Set(s.EqLowDb, s.EqLowMidDb, s.EqMidDb, s.EqPresenceDb, s.EqHighDb);

        _deEsser.Enabled = s.DeEsserOn;
        _deEsser.Amount = s.DeEsserAmount;

        _compressor.Enabled = s.CompressorOn;
        _compressor.Amount = s.CompressorAmount;

        _chorus.Enabled = s.ChorusOn;
        _chorus.Set(s.ChorusRate, s.ChorusDepth, s.ChorusMix);

        // On vide les queues d'écho/réverb quand on les rallume, pour ne pas réentendre un vieux son.
        if (s.EchoOn && previous is { EchoOn: false }) _echo.Reset();
        _echo.Enabled = s.EchoOn;
        _echo.Set(s.EchoTimeMs, s.EchoFeedback, s.EchoMix);

        if (s.ReverbOn && previous is { ReverbOn: false }) _reverb.Reset();
        _reverb.Enabled = s.ReverbOn;
        _reverb.Set(s.ReverbSize, s.ReverbMix);

        _volume.Db = s.VolumeDb;
    }
}
