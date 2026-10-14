using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Se7enVoice.Core.Audio;
using Se7enVoice.Core.Dsp;

namespace Se7enVoice.Audio;

/// <summary>
/// Vrai micro → effets → VB-CABLE (que Discord/les jeux utilisent comme micro),
/// avec un retour optionnel dans le casque pour s'écouter.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private Output? _cable, _monitor;
    private EffectChain? _chain;
    private Limiter? _limiter;
    private float[] _mono = [], _voice = [];
    private VoiceSettings _settings = new();
    private volatile bool _muted, _soundsToMonitor = true;
    private float _inPeak, _outPeak;

    public Soundboard Soundboard { get; } = new();

    public bool IsRunning { get; private set; }
    public string? InputName { get; private set; }
    public bool CableConnected => _cable != null;
    public int SampleRate { get; private set; }

    /// <summary>Le micro ou une sortie a disparu (débranché…). Déclenché hors du thread UI.</summary>
    public event Action<string>? Faulted;

    public VoiceSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            var chain = _chain;
            if (chain != null) chain.Settings = value;
        }
    }

    public bool Muted { get => _muted; set => _muted = value; }
    public bool SoundsToMonitor { get => _soundsToMonitor; set => _soundsToMonitor = value; }

    /// <summary>Niveaux crête (0..1) depuis le dernier appel, pour les vu-mètres.</summary>
    public (float input, float output) TakeLevels()
    {
        float i = Interlocked.Exchange(ref _inPeak, 0);
        float o = Interlocked.Exchange(ref _outPeak, 0);
        return (i, o);
    }

    public void Start(MMDevice microphone, MMDevice? cable, MMDevice? monitor)
    {
        Stop();
        lock (_lock)
        {
            var capture = new WasapiCapture(microphone, true, 10);
            var fmt = capture.WaveFormat;
            SampleRate = fmt.SampleRate;
            _chain = new EffectChain(SampleRate) { Settings = _settings };
            _limiter = new Limiter(SampleRate);
            Soundboard.SampleRate = SampleRate;

            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception != null) Faulted?.Invoke("Le micro ne répond plus : " + e.Exception.Message);
            };

            if (cable != null) _cable = Output.Open(cable, SampleRate, this);
            if (monitor != null) _monitor = Output.Open(monitor, SampleRate, this);

            _capture = capture;
            InputName = microphone.FriendlyName;
            capture.StartRecording();
            IsRunning = true;
        }
    }

    /// <summary>Active/désactive le retour casque sans couper le reste.</summary>
    public void SetMonitor(MMDevice? monitor)
    {
        lock (_lock)
        {
            _monitor?.Dispose();
            _monitor = null;
            if (monitor != null && IsRunning) _monitor = Output.Open(monitor, SampleRate, this);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            IsRunning = false;
            if (_capture != null)
            {
                _capture.DataAvailable -= OnData;
                try { _capture.StopRecording(); } catch (Exception) { }
                _capture.Dispose();
                _capture = null;
            }
            _cable?.Dispose(); _cable = null;
            _monitor?.Dispose(); _monitor = null;
            Soundboard.StopAll();
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var capture = _capture;
        var chain = _chain;
        var limiter = _limiter;
        if (capture == null || chain == null || limiter == null || e.BytesRecorded == 0) return;

        int frames = ToMono(e.Buffer, e.BytesRecorded, capture.WaveFormat, ref _mono);
        var voice = _mono.AsSpan(0, frames);
        StorePeak(ref _inPeak, voice);

        chain.Process(voice);
        if (_muted) voice.Clear();

        // Les sons de la soundboard passent après les effets : ils ne sont pas déformés.
        if (_voice.Length < frames) _voice = new float[frames];
        var monitorMix = _voice.AsSpan(0, frames);
        voice.CopyTo(monitorMix);
        bool sounds = Soundboard.MixInto(voice);
        if (sounds && _soundsToMonitor) voice.CopyTo(monitorMix);

        limiter.Process(voice);
        StorePeak(ref _outPeak, voice);

        _cable?.Ring.Write(voice);
        _monitor?.Ring.Write(monitorMix);
    }

    private static void StorePeak(ref float target, ReadOnlySpan<float> s)
    {
        float peak = 0;
        foreach (var v in s) { float a = Math.Abs(v); if (a > peak) peak = a; }
        float old;
        do { old = Volatile.Read(ref target); if (peak <= old) return; }
        while (Interlocked.CompareExchange(ref target, peak, old) != old);
    }

    /// <summary>Convertit le format du micro (float/PCM, mono/stéréo…) en mono float.</summary>
    private static int ToMono(byte[] data, int bytes, WaveFormat fmt, ref float[] mono)
    {
        int ch = Math.Max(1, fmt.Channels);
        int bps = fmt.BitsPerSample / 8;
        int frames = bytes / (bps * ch);
        if (mono.Length < frames) mono = new float[frames * 2];

        bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat
            || (fmt is WaveFormatExtensible ext && ext.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);

        // Beaucoup de micros USB sont en « stéréo » avec une seule capsule : on prend le canal le plus fort.
        for (int f = 0; f < frames; f++)
        {
            float best = 0;
            for (int c = 0; c < ch; c++)
            {
                int o = (f * ch + c) * bps;
                float v = isFloat && bps == 4 ? BitConverter.ToSingle(data, o)
                    : bps == 2 ? BitConverter.ToInt16(data, o) / 32768f
                    : bps == 3 ? ((data[o] << 8 | data[o + 1] << 16 | data[o + 2] << 24) >> 8) / 8388608f
                    : bps == 4 ? BitConverter.ToInt32(data, o) / 2147483648f
                    : 0;
                if (Math.Abs(v) > Math.Abs(best)) best = v;
            }
            mono[f] = best;
        }
        return frames;
    }

    public void Dispose() => Stop();

    /// <summary>Une sortie WASAPI alimentée par un <see cref="ResamplingRing"/>.</summary>
    private sealed class Output : IWaveProvider, IDisposable
    {
        private readonly WasapiOut _out;
        private readonly int _channels;
        private float[] _tmp = new float[4096];

        private Output(MMDevice device, int inputRate, AudioEngine owner)
        {
            var mix = device.AudioClient.MixFormat;
            _channels = Math.Max(1, mix.Channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, _channels);
            Ring = new ResamplingRing(inputRate, mix.SampleRate);
            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 20);
            _out.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null) owner.Faulted?.Invoke("Une sortie audio ne répond plus : " + e.Exception.Message);
            };
            _out.Init(this);
            _out.Play();
        }

        public ResamplingRing Ring { get; }
        public WaveFormat WaveFormat { get; }

        public static Output? Open(MMDevice device, int inputRate, AudioEngine owner)
        {
            try { return new Output(device, inputRate, owner); }
            catch (Exception ex)
            {
                owner.Faulted?.Invoke($"Impossible d'ouvrir « {device.FriendlyName} » : {ex.Message}");
                return null;
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int frames = count / (4 * _channels);
            if (_tmp.Length < frames) _tmp = new float[frames];
            var mono = _tmp.AsSpan(0, frames);
            Ring.Read(mono);
            var dest = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, frames * 4 * _channels));
            for (int f = 0, i = 0; f < frames; f++)
                for (int c = 0; c < _channels; c++) dest[i++] = mono[f];
            return count;
        }

        public void Dispose()
        {
            try { _out.Stop(); } catch (Exception) { }
            _out.Dispose();
        }
    }
}
