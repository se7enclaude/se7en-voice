namespace Se7enVoice.Core.Dsp;

/// <summary>
/// Chaîne d'effets dans un ordre fixe. <see cref="Settings"/> peut être changé depuis n'importe
/// quel thread : le thread audio applique la nouvelle valeur au début du bloc suivant.
/// </summary>
public sealed class EffectChain
{
    private readonly NoiseGate _gate;
    private readonly Equalizer _eq;
    private readonly PitchShifter _pitch;
    private readonly Deepener _deepener;
    private readonly RingModulator _robot;
    private readonly Distortion _distortion;
    private readonly RadioFilter _radio;
    private readonly Compressor _compressor;
    private readonly Echo _echo;
    private readonly Reverb _reverb;
    private readonly Gain _volume;
    private readonly Effect[] _order;

    private volatile VoiceSettings _settings = new();
    private VoiceSettings? _applied;

    public EffectChain(int sampleRate)
    {
        SampleRate = sampleRate;
        _gate = new(sampleRate);
        _eq = new(sampleRate);
        _pitch = new(sampleRate);
        _deepener = new(sampleRate);
        _robot = new(sampleRate);
        _distortion = new(sampleRate);
        _radio = new(sampleRate);
        _compressor = new(sampleRate);
        _echo = new(sampleRate);
        _reverb = new(sampleRate);
        _volume = new(sampleRate);
        _order = [_gate, _eq, _pitch, _deepener, _robot, _distortion, _radio, _compressor, _echo, _reverb, _volume];
    }

    public int SampleRate { get; }

    public VoiceSettings Settings
    {
        get => _settings;
        set => _settings = value ?? new();
    }

    public void Process(Span<float> buffer)
    {
        var s = _settings;
        if (!ReferenceEquals(s, _applied)) Apply(s);
        foreach (var effect in _order)
            if (effect.Enabled) effect.Process(buffer);
    }

    public void Reset()
    {
        foreach (var effect in _order) effect.Reset();
    }

    private void Apply(VoiceSettings s)
    {
        var previous = _applied;
        _applied = s;

        _gate.Enabled = s.GateOn;
        _gate.ThresholdDb = s.GateThresholdDb;

        _eq.Enabled = s.EqOn;
        _eq.Set(s.EqLowDb, s.EqMidDb, s.EqHighDb);

        _pitch.Semitones = s.PitchSemitones + Deepener.PitchFor(s.Depth);
        _deepener.Amount = s.Depth;

        _robot.Enabled = s.RobotOn;
        _robot.Set(s.RobotFrequency, s.RobotMix);

        _distortion.Enabled = s.DistortionOn;
        _distortion.Drive = s.DistortionDrive;

        _radio.Enabled = s.RadioOn;

        _compressor.Enabled = s.CompressorOn;
        _compressor.Amount = s.CompressorAmount;

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
