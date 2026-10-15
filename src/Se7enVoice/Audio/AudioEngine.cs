using System.IO;
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
    private volatile bool _muted, _soundsToMonitor = true, _bypass, _talkOpen = true;
    private float _inPeak, _outPeak, _appPeak, _talkGain = 1, _appsGain = 0.5f;
    private volatile ProcessLoopbackCapture[] _apps = [];
    private float[] _appTmp = [];
    private volatile Recorder? _recorder;
    private readonly float[] _scope = new float[4096];
    private int _scopePos;

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
    /// <summary>Comparaison A/B : la voix passe sans effets (le limiteur reste).</summary>
    public bool Bypass { get => _bypass; set => _bypass = value; }
    /// <summary>Push-to-talk : faux tant que la touche n'est pas enfoncée.</summary>
    public bool TalkOpen { get => _talkOpen; set => _talkOpen = value; }
    /// <summary>Volume des applis envoyées dans le micro (gain linéaire).</summary>
    public float AppsGain { get => _appsGain; set => _appsGain = value; }

    /// <summary>Retard total estimé micro → micro virtuel, en ms.</summary>
    public double LatencyMs => 10 + 20 + (_chain?.LatencyMs ?? 0);

    /// <summary>Note chantée détectée par l'autotune (Hz), 0 sinon.</summary>
    public double DetectedFrequency => _chain?.DetectedFrequency ?? 0;

    public bool IsRecording => _recorder != null;
    public Recorder? CurrentRecording => _recorder;

    /// <summary>Copie les derniers échantillons envoyés au micro virtuel (pour l'analyseur de spectre).</summary>
    public void CopyScope(float[] dest)
    {
        int n = Math.Min(dest.Length, _scope.Length);
        int pos = Volatile.Read(ref _scopePos);
        for (int i = 0; i < n; i++) dest[i] = _scope[(pos - n + i) & (_scope.Length - 1)];
    }

    // ---------- Applis envoyées dans le micro ----------

    /// <summary>Envoie le son de l'appli <paramref name="processId"/> dans le micro virtuel. Lève une exception si Windows refuse.</summary>
    public void AddApp(int processId)
    {
        lock (_lock)
        {
            if (_apps.Any(a => a.ProcessId == processId) || SampleRate == 0) return;
            var cap = new ProcessLoopbackCapture(processId, SampleRate);
            _apps = [.. _apps, cap];
        }
    }

    public void RemoveApp(int processId)
    {
        lock (_lock)
        {
            var cap = _apps.FirstOrDefault(a => a.ProcessId == processId);
            if (cap == null) return;
            _apps = _apps.Where(a => a != cap).ToArray();
            cap.Dispose();
        }
    }

    public IReadOnlyCollection<int> AppIds => _apps.Select(a => a.ProcessId).ToArray();

    private void ClearApps()
    {
        var apps = _apps;
        _apps = [];
        foreach (var a in apps) a.Dispose();
    }

    // ---------- Enregistrement ----------

    public string StartRecording(string folder)
    {
        StopRecording();
        var path = Path.Combine(folder, $"se7en Voice {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav");
        _recorder = new Recorder(path, SampleRate == 0 ? 48000 : SampleRate);
        return path;
    }

    public string? StopRecording()
    {
        var r = _recorder;
        _recorder = null;
        if (r == null) return null;
        r.Dispose();
        return r.Path;
    }

    /// <summary>Niveaux crête (0..1) depuis le dernier appel, pour les vu-mètres.</summary>
    public (float input, float output, float apps) TakeLevels()
    {
        float i = Interlocked.Exchange(ref _inPeak, 0);
        float o = Interlocked.Exchange(ref _outPeak, 0);
        float a = Interlocked.Exchange(ref _appPeak, 0);
        return (i, o, a);
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

            ClearApps(); // les captures d'applis sont liées à l'ancienne fréquence : le mixeur les relance
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
            ClearApps();
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

        if (!_bypass) chain.Process(voice);

        // Muet / push-to-talk : fondu de 5 ms pour éviter les clics.
        float target = _muted || !_talkOpen ? 0 : 1;
        if (target != _talkGain || target == 0)
        {
            float step = 1f / (SampleRate * 0.005f);
            for (int i = 0; i < voice.Length; i++)
            {
                _talkGain = target > _talkGain ? Math.Min(target, _talkGain + step) : Math.Max(target, _talkGain - step);
                voice[i] *= _talkGain;
            }
        }

        // Les sons de la soundboard passent après les effets : ils ne sont pas déformés.
        if (_voice.Length < frames) _voice = new float[frames];
        var monitorMix = _voice.AsSpan(0, frames);
        voice.CopyTo(monitorMix);
        bool sounds = Soundboard.MixInto(voice);
        if (sounds && _soundsToMonitor) voice.CopyTo(monitorMix);

        // Applis envoyées dans le micro (musique…) : pas dans le retour casque, on les entend déjà.
        var apps = _apps;
        if (apps.Length > 0)
        {
            if (_appTmp.Length < frames) _appTmp = new float[frames * 2];
            var tmp = _appTmp.AsSpan(0, frames);
            float g = _appsGain;
            foreach (var app in apps)
            {
                app.Ring.Read(tmp);
                for (int i = 0; i < frames; i++) voice[i] += tmp[i] * g;
                StorePeak(ref _appPeak, tmp);
            }
        }

        limiter.Process(voice);
        StorePeak(ref _outPeak, voice);

        _cable?.Ring.Write(voice);
        _monitor?.Ring.Write(monitorMix);
        _recorder?.Write(voice);

        int pos = _scopePos;
        foreach (var v in voice) _scope[pos++ & (_scope.Length - 1)] = v;
        Volatile.Write(ref _scopePos, pos & (_scope.Length - 1));
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

    public void Dispose()
    {
        StopRecording();
        Stop();
    }

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
