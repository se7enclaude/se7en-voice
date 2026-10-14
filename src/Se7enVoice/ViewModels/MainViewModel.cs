using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Se7enVoice.Audio;
using Se7enVoice.Core;
using Se7enVoice.Core.Dsp;
using Se7enVoice.Core.Presets;
using Se7enVoice.Services;

namespace Se7enVoice.ViewModels;

public sealed class MainViewModel : Observable, IDisposable
{
    private readonly AppSettings _settings;
    private readonly PresetStore _store = new(Path.Combine(AppSettings.Folder, "presets"));
    private readonly DispatcherTimer _meterTimer, _saveTimer, _retryTimer, _deviceDebounce;
    private bool _dirty;

    private List<AudioDevice> _microphones = [], _speakers = [];
    private AudioDevice? _selectedMicrophone, _selectedMonitor;
    private bool _muted, _monitorOn, _cableFound, _live, _modified;
    private string _status = "Démarrage…", _cableName = "";
    private double _inputLevel, _outputLevel;
    private PresetItem? _current;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        Engine.Settings = _settings.Voice;
        Engine.Soundboard.Volume = (float)Math.Pow(10, _settings.SoundboardVolumeDb / 20);
        Engine.SoundsToMonitor = _settings.SoundsToMonitor;
        Engine.Faulted += msg => Dispatcher.BeginInvoke(() => OnFaulted(msg));
        _monitorOn = _settings.MonitorOn;

        foreach (var p in BuiltInPresets.All) Presets.Add(new PresetItem(p));
        foreach (var p in _store.LoadAll()) UserPresets.Add(new PresetItem(p));
        _current = AllPresets.FirstOrDefault(p => p.Name == _settings.CurrentPreset);
        if (_current != null)
        {
            _current.IsSelected = true;
            _modified = DiffersFromPreset(_settings.Voice);
        }

        for (int i = 0; i < _settings.Sounds.Count; i++)
        {
            var s = _settings.Sounds[i];
            if (File.Exists(s.Path)) Sounds.Add(new SoundItem(s.Name, s.Path, Sounds.Count));
        }

        _meterTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => UpdateMeters(), Dispatcher);
        _saveTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => SaveIfDirty(), Dispatcher);
        _retryTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, (_, _) => { _retryTimer!.Stop(); RefreshDevices(restart: true); }, Dispatcher);
        _retryTimer.Stop();
        _deviceDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Normal, (_, _) => { _deviceDebounce!.Stop(); OnDevicesChanged(); }, Dispatcher);
        _deviceDebounce.Stop();
        Devices.Changed += () => Dispatcher.BeginInvoke(() => { _deviceDebounce.Stop(); _deviceDebounce.Start(); });

        RefreshDevices(restart: true);
        _meterTimer.Start();
        _saveTimer.Start();
    }

    private static Dispatcher Dispatcher => System.Windows.Application.Current.Dispatcher;

    public AudioEngine Engine { get; } = new();
    public DeviceService Devices { get; } = new();

    public ObservableCollection<PresetItem> Presets { get; } = [];
    public ObservableCollection<PresetItem> UserPresets { get; } = [];
    public ObservableCollection<SoundItem> Sounds { get; } = [];
    public IEnumerable<PresetItem> AllPresets => Presets.Concat(UserPresets);

    /// <summary>Pour l'onomatopée et les notifications.</summary>
    public event Action<PresetItem>? PresetApplied;
    public event Action<string>? Notify;

    // ---------- Périphériques ----------

    public List<AudioDevice> Microphones { get => _microphones; private set => Set(ref _microphones, value); }
    public List<AudioDevice> Speakers { get => _speakers; private set => Set(ref _speakers, value); }

    public AudioDevice? SelectedMicrophone
    {
        get => _selectedMicrophone;
        set
        {
            if (value == null || value.Id == _selectedMicrophone?.Id) return;
            _selectedMicrophone = value;
            Raise();
            _settings.InputDeviceId = value.Id;
            MarkDirty();
            RestartAudio();
        }
    }

    public AudioDevice? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (value == null || value.Id == _selectedMonitor?.Id) return;
            _selectedMonitor = value;
            Raise();
            _settings.MonitorDeviceId = value.Id;
            MarkDirty();
            if (MonitorOn) Engine.SetMonitor(Devices.Get(value.Id));
        }
    }

    public bool CableFound { get => _cableFound; private set => Set(ref _cableFound, value); }
    public string CableName { get => _cableName; private set => Set(ref _cableName, value); }
    public bool CableRenamed => CableName.StartsWith("se7en", StringComparison.OrdinalIgnoreCase);

    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Live { get => _live; private set => Set(ref _live, value); }

    public bool Muted
    {
        get => _muted;
        set { if (Set(ref _muted, value)) Engine.Muted = value; }
    }

    public bool MonitorOn
    {
        get => _monitorOn;
        set
        {
            if (!Set(ref _monitorOn, value)) return;
            _settings.MonitorOn = value;
            MarkDirty();
            Engine.SetMonitor(value ? MonitorDevice() : null);
        }
    }

    public double InputLevel { get => _inputLevel; private set => Set(ref _inputLevel, value); }
    public double OutputLevel { get => _outputLevel; private set => Set(ref _outputLevel, value); }

    public void RefreshDevices(bool restart)
    {
        Microphones = Devices.Microphones();
        Speakers = Devices.Speakers();

        _selectedMicrophone = Microphones.FirstOrDefault(d => d.Id == _settings.InputDeviceId)
            ?? Microphones.FirstOrDefault(d => d.Id == Devices.DefaultMicrophoneId())
            ?? Microphones.FirstOrDefault();
        Raise(nameof(SelectedMicrophone));
        _selectedMonitor = Speakers.FirstOrDefault(d => d.Id == _settings.MonitorDeviceId)
            ?? Speakers.FirstOrDefault(d => d.Id == Devices.DefaultSpeakerId())
            ?? Speakers.FirstOrDefault();
        Raise(nameof(SelectedMonitor));

        var cableOut = Devices.CableOutput();
        CableFound = Devices.CableInput() != null;
        CableName = cableOut?.Name ?? "";
        Raise(nameof(CableRenamed));

        if (restart) RestartAudio();
    }

    private void OnDevicesChanged()
    {
        bool hadCable = CableFound;
        string? mic = _selectedMicrophone?.Id;
        RefreshDevices(restart: false);
        if (hadCable != CableFound || mic != _selectedMicrophone?.Id || !Engine.IsRunning) RestartAudio();
        if (!hadCable && CableFound) Notify?.Invoke("Micro virtuel détecté ! Choisis « " + CableName + " » comme micro dans Discord.");
    }

    public void RestartAudio()
    {
        var mic = Devices.Get(_selectedMicrophone?.Id);
        if (mic == null)
        {
            Engine.Stop();
            Live = false;
            Status = "Aucun micro branché";
            return;
        }
        try
        {
            var cable = Devices.Get(Devices.CableInput()?.Id);
            Engine.Start(mic, cable, MonitorOn ? MonitorDevice() : null);
            Live = true;
            Status = Engine.CableConnected ? "En direct" : "Test · micro virtuel absent";
        }
        catch (Exception ex)
        {
            Live = false;
            Status = "Micro indisponible";
            Notify?.Invoke("Impossible d'ouvrir le micro : " + ex.Message);
            _retryTimer.Start();
        }
    }

    private NAudio.CoreAudioApi.MMDevice? MonitorDevice() =>
        Devices.Get(_selectedMonitor?.Id) ?? Devices.Get(Devices.DefaultSpeakerId());

    private void OnFaulted(string message)
    {
        Live = false;
        Status = "Reconnexion…";
        _retryTimer.Stop();
        _retryTimer.Start();
    }

    private void UpdateMeters()
    {
        var (i, o) = Engine.TakeLevels();
        InputLevel = Smooth(InputLevel, ToMeter(i));
        OutputLevel = Smooth(OutputLevel, Muted ? 0 : ToMeter(o));

        static double ToMeter(float peak) => peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
        static double Smooth(double old, double now) => now > old ? now : Math.Max(now, old - 0.035);
    }

    // ---------- Presets ----------

    public PresetItem? CurrentPreset => _current;
    public string CurrentPresetName => _current?.Name ?? "Perso";
    public bool Modified { get => _modified; private set => Set(ref _modified, value); }

    public void ApplyPreset(PresetItem item)
    {
        if (_current != null) _current.IsSelected = false;
        _current = item;
        item.IsSelected = true;
        _settings.CurrentPreset = item.Name;
        Modified = false;
        SetVoice(KeepMicSettings(item.Preset.Settings), fromPreset: true);
        Raise(nameof(CurrentPresetName));
        PresetApplied?.Invoke(item);
    }

    /// <summary>
    /// Le volume et l'anti-bruit dépendent du micro de chacun, pas de la voix choisie :
    /// changer de preset les conserve.
    /// </summary>
    private VoiceSettings KeepMicSettings(VoiceSettings preset) =>
        preset with { VolumeDb = Voice.VolumeDb, GateOn = Voice.GateOn, GateThresholdDb = Voice.GateThresholdDb };

    private bool DiffersFromPreset(VoiceSettings v) =>
        _current != null && KeepMicSettings(_current.Preset.Settings) != v;

    public void StepPreset(int direction)
    {
        var all = AllPresets.ToList();
        int i = _current == null ? 0 : all.IndexOf(_current);
        ApplyPreset(all[((i + direction) % all.Count + all.Count) % all.Count]);
    }

    public void SaveCurrentAs(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return;
        var existing = UserPresets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        var tones = new[] { "blaze", "surge", "neon", "boost", "volt" };
        var preset = new Preset
        {
            Name = name,
            Icon = "",
            Tone = existing?.Preset.Tone ?? tones[UserPresets.Count % tones.Length],
            Settings = Voice,
        };
        _store.Save(preset);
        var item = new PresetItem(preset);
        if (existing != null) UserPresets[UserPresets.IndexOf(existing)] = item;
        else UserPresets.Add(item);
        ApplyPreset(item);
        Notify?.Invoke($"Preset « {name} » enregistré.");
    }

    public void DeletePreset(PresetItem item)
    {
        if (!item.IsUser) return;
        _store.Delete(item.Name);
        UserPresets.Remove(item);
        if (_current == item)
        {
            _current = null;
            Raise(nameof(CurrentPresetName));
        }
    }

    public bool ImportPreset(string file)
    {
        var p = PresetStore.TryRead(file);
        if (p == null) return false;
        _store.Save(p);
        var old = UserPresets.FirstOrDefault(x => x.Name == p.Name);
        if (old != null) UserPresets.Remove(old);
        UserPresets.Add(new PresetItem(p));
        return true;
    }

    // ---------- Réglages de voix (liés aux sliders) ----------

    public VoiceSettings Voice => Engine.Settings;

    private void SetVoice(VoiceSettings value, bool fromPreset = false, [CallerMemberName] string? name = null)
    {
        Engine.Settings = value;
        _settings.Voice = value;
        MarkDirty();
        if (fromPreset) Raise(string.Empty);
        else
        {
            Raise(name);
            Modified = DiffersFromPreset(value);
        }
    }

    public double VolumeDb { get => Voice.VolumeDb; set => SetVoice(Voice with { VolumeDb = Math.Round(value, 1) }); }
    public double PitchSemitones { get => Voice.PitchSemitones; set => SetVoice(Voice with { PitchSemitones = Math.Round(value) }); }
    public double Depth { get => Voice.Depth; set => SetVoice(Voice with { Depth = Math.Round(value, 2) }); }
    public bool GateOn { get => Voice.GateOn; set => SetVoice(Voice with { GateOn = value }); }
    public double GateThresholdDb { get => Voice.GateThresholdDb; set => SetVoice(Voice with { GateThresholdDb = Math.Round(value) }); }
    public bool EqOn { get => Voice.EqOn; set => SetVoice(Voice with { EqOn = value }); }
    public double EqLowDb { get => Voice.EqLowDb; set => SetVoice(Voice with { EqLowDb = Math.Round(value, 1) }); }
    public double EqMidDb { get => Voice.EqMidDb; set => SetVoice(Voice with { EqMidDb = Math.Round(value, 1) }); }
    public double EqHighDb { get => Voice.EqHighDb; set => SetVoice(Voice with { EqHighDb = Math.Round(value, 1) }); }
    public bool CompressorOn { get => Voice.CompressorOn; set => SetVoice(Voice with { CompressorOn = value }); }
    public double CompressorAmount { get => Voice.CompressorAmount; set => SetVoice(Voice with { CompressorAmount = value }); }
    public bool RobotOn { get => Voice.RobotOn; set => SetVoice(Voice with { RobotOn = value }); }
    public double RobotFrequency { get => Voice.RobotFrequency; set => SetVoice(Voice with { RobotFrequency = Math.Round(value) }); }
    public double RobotMix { get => Voice.RobotMix; set => SetVoice(Voice with { RobotMix = value }); }
    public bool DistortionOn { get => Voice.DistortionOn; set => SetVoice(Voice with { DistortionOn = value }); }
    public double DistortionDrive { get => Voice.DistortionDrive; set => SetVoice(Voice with { DistortionDrive = value }); }
    public bool RadioOn { get => Voice.RadioOn; set => SetVoice(Voice with { RadioOn = value }); }
    public bool EchoOn { get => Voice.EchoOn; set => SetVoice(Voice with { EchoOn = value }); }
    public double EchoTimeMs { get => Voice.EchoTimeMs; set => SetVoice(Voice with { EchoTimeMs = Math.Round(value) }); }
    public double EchoFeedback { get => Voice.EchoFeedback; set => SetVoice(Voice with { EchoFeedback = value }); }
    public double EchoMix { get => Voice.EchoMix; set => SetVoice(Voice with { EchoMix = value }); }
    public bool ReverbOn { get => Voice.ReverbOn; set => SetVoice(Voice with { ReverbOn = value }); }
    public double ReverbSize { get => Voice.ReverbSize; set => SetVoice(Voice with { ReverbSize = value }); }
    public double ReverbMix { get => Voice.ReverbMix; set => SetVoice(Voice with { ReverbMix = value }); }

    // ---------- Soundboard ----------

    public double SoundVolumeDb
    {
        get => _settings.SoundboardVolumeDb;
        set
        {
            _settings.SoundboardVolumeDb = Math.Round(value);
            Engine.Soundboard.Volume = (float)Math.Pow(10, value / 20);
            MarkDirty();
            Raise();
        }
    }

    public bool SoundsToMonitor
    {
        get => _settings.SoundsToMonitor;
        set
        {
            _settings.SoundsToMonitor = value;
            Engine.SoundsToMonitor = value;
            MarkDirty();
            Raise();
        }
    }

    public void AddSounds(IEnumerable<string> paths)
    {
        string[] ok = [".mp3", ".wav", ".ogg", ".m4a", ".aac", ".wma", ".flac"];
        foreach (var p in paths)
        {
            if (!ok.Contains(Path.GetExtension(p).ToLowerInvariant())) continue;
            if (Sounds.Any(s => string.Equals(s.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            Sounds.Add(new SoundItem(Path.GetFileNameWithoutExtension(p), p, Sounds.Count));
        }
        SaveSounds();
    }

    public void RemoveSound(SoundItem item)
    {
        Sounds.Remove(item);
        for (int i = 0; i < Sounds.Count; i++) Sounds[i].Index = i;
        SaveSounds();
    }

    public void PlaySound(SoundItem item)
    {
        if (!Live)
        {
            Notify?.Invoke("Le micro n'est pas actif : impossible de jouer le son.");
            return;
        }
        try { Engine.Soundboard.Play(item.Path); }
        catch (Exception ex) { Notify?.Invoke($"Impossible de lire « {item.Name} » : {ex.Message}"); }
    }

    public void PlaySoundAt(int index)
    {
        if (index >= 0 && index < Sounds.Count) PlaySound(Sounds[index]);
    }

    public void StopSounds() => Engine.Soundboard.StopAll();

    private void SaveSounds()
    {
        _settings.Sounds = Sounds.Select(s => new SoundEntry { Name = s.Name, Path = s.Path }).ToList();
        MarkDirty();
    }

    // ---------- Application ----------

    public bool StartWithWindows
    {
        get => Autostart.IsEnabled;
        set
        {
            try { Autostart.Set(value); }
            catch (Exception ex) { Notify?.Invoke("Impossible de changer le démarrage auto : " + ex.Message); }
            Raise();
        }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { _settings.MinimizeToTray = value; MarkDirty(); Raise(); }
    }

    public bool CableRenameOffered
    {
        get => _settings.CableRenameOffered;
        set { _settings.CableRenameOffered = value; MarkDirty(); }
    }

    public void RenameCable()
    {
        if (CableRenamer.RequestElevatedRename())
        {
            RefreshDevices(restart: false);
            Notify?.Invoke("C'est fait ! Dans Discord, choisis « " + CableRenamer.MicName + " » comme micro.");
        }
        else
        {
            Notify?.Invoke("Renommage annulé. Tu peux quand même choisir « CABLE Output » dans Discord.");
        }
    }

    private void MarkDirty() => _dirty = true;

    private void SaveIfDirty()
    {
        if (!_dirty) return;
        _dirty = false;
        _settings.Save();
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _saveTimer.Stop();
        _settings.Save();
        Engine.Dispose();
        Devices.Dispose();
    }
}
