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

public sealed partial class MainViewModel : Observable, IDisposable
{
    private readonly AppSettings _settings;
    private readonly PresetStore _store = new(Path.Combine(AppSettings.Folder, "presets"));
    private readonly DispatcherTimer _meterTimer, _saveTimer, _retryTimer, _deviceDebounce, _keyTimer;
    private bool _dirty;
    private string _presetFilter = AllFilter, _presetSearch = "";
    private bool _bypass, _capturingKey, _mixerVisible, _windowShown = true;
    private int _keyCaptureTicks;
    private double _sinceVoice = 99;
    private readonly System.Diagnostics.Stopwatch _meterClock = System.Diagnostics.Stopwatch.StartNew();
    private TimeSpan _lastMeterTick;

    // Push-to-talk : lu par un minuteur hors du fil de l'interface (une interface occupée ne retarde pas la parole).
    private readonly object _pttLock = new();
    private System.Threading.Timer? _pttTimer;
    private bool _pttActive;
    private int _pttKey;

    // Redémarrage du micro : sur un autre fil, un seul à la fois (le dernier demandé gagne).
    private readonly SemaphoreSlim _restartGate = new(1, 1);
    private int _restartRequest;
    private bool _audioRestarting, _disposed;

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
        foreach (var p in AllPresets) p.IsFavorite = _settings.Favorites.Contains(p.Name);
        Mixer = new MixerViewModel(_settings, Engine, MarkDirty, msg => Notify?.Invoke(msg));
        foreach (var c in _settings.AiCustomVoices) AiVoices.Add(new AiVoiceItem(c.ToInfo()));
        UpdateAiParams(); // voix IA choisie (mise en évidence avant même le démarrage)
        RefreshPresetView();
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

        // Capture de la touche du push-to-talk : on lit l'état du clavier/souris ~60 fois par seconde.
        _keyTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Normal, (_, _) => KeyTick(), Dispatcher);
        _keyTimer.Stop();

        RefreshDevices(restart: true);
        _meterTimer.Start();
        _saveTimer.Start();
        UpdateKeyTimer();
        StartComponentChecks();
    }

    public MixerViewModel Mixer { get; }

    /// <summary>Voix de la barre du bas : les favoris, sinon une sélection de départ.</summary>
    public ObservableCollection<PresetItem> QuickVoices { get; } = [];

    private void RefreshQuickVoices()
    {
        var favs = AllPresets.Where(p => p.IsFavorite).Take(6).ToList();
        if (favs.Count == 0)
            favs = new[] { "Normal", "Streamer", "Voix grave", "Robot", "Démon" }
                .Select(n => AllPresets.FirstOrDefault(p => p.Name == n)).OfType<PresetItem>().ToList();
        QuickVoices.Clear();
        foreach (var p in favs) QuickVoices.Add(p);
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
    public bool CableRenamed => CableRenamer.IsRenamed(CableName);

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
        AutoRenameCable();

        OutputOnCable = Devices.DefaultOutputIsCable();
        Raise(nameof(MicFound));
        Raise(nameof(ReadyForDiscord));

        if (restart) RestartAudio();
    }

    private void OnDevicesChanged()
    {
        bool hadCable = CableFound;
        string? mic = _selectedMicrophone?.Id;
        RefreshDevices(restart: false);
        if (hadCable != CableFound || mic != _selectedMicrophone?.Id || !Engine.IsRunning) RestartAudio();
        if (hadCable != CableFound) RefreshComponentTexts();
        if (!hadCable && CableFound) Notify?.Invoke("Micro virtuel détecté · choisissez « " + CableName + " » comme micro dans Discord");
    }

    /// <summary>
    /// (Re)démarre le micro. Le travail lent (ouverture des périphériques, WASAPI) se fait sur un autre fil pour ne pas
    /// figer la fenêtre ; les redémarrages sont faits un par un, et seul le dernier demandé compte. L'état affiché est
    /// mis à jour sur le fil de l'interface.
    /// </summary>
    public void RestartAudio() => _ = RestartAudioAsync();

    private async Task RestartAudioAsync()
    {
        int request = ++_restartRequest;
        await _restartGate.WaitAsync();
        try
        {
            if (request != _restartRequest || _disposed) return; // un redémarrage plus récent attend : il fera le travail
            _audioRestarting = true;
            string? micId = _selectedMicrophone?.Id, monitorId = _selectedMonitor?.Id;
            bool monitorOn = MonitorOn;

            // 1) arrêt du moteur d'abord : plus aucun bloc audio ne peut toucher la voix IA qu'on va libérer
            var devices = await Task.Run<(NAudio.CoreAudioApi.MMDevice? mic, NAudio.CoreAudioApi.MMDevice? cable, NAudio.CoreAudioApi.MMDevice? monitor)>(() =>
            {
                var mic = Devices.Get(micId);
                Engine.Stop();
                if (mic == null) return default;
                var cable = Devices.Get(Devices.CableInput()?.Id);
                var monitor = monitorOn ? Devices.Get(monitorId) ?? Devices.Get(Devices.DefaultSpeakerId()) : null;
                return (mic, cable, monitor);
            });
            StopAi(); // la voix IA est liée à la fréquence du micro : on la recrée après
            if (devices.mic == null)
            {
                Live = false;
                Status = "Aucun micro branché";
                return;
            }

            // 2) démarrage
            await Task.Run(() =>
            {
                if (!_disposed) Engine.Start(devices.mic, devices.cable, devices.monitor);
            });
            if (_disposed) return;
            _audioRestarting = false;
            _ = ApplyAiAsync();
            Mixer.RestartMicApps();
            Raise(nameof(LatencyText)); Raise(nameof(RateText));
            Live = true;
            Status = Engine.CableConnected ? "En direct" : "Test · micro virtuel absent";
        }
        catch (Exception ex)
        {
            Log.Error("Ouverture du micro impossible", ex);
            if (!_disposed)
            {
                Live = false;
                Status = "Micro indisponible";
                Notify?.Invoke("Impossible d'ouvrir le micro : " + ex.Message);
                _retryTimer.Start();
            }
        }
        finally
        {
            _audioRestarting = false;
            _restartGate.Release();
        }
    }

    private NAudio.CoreAudioApi.MMDevice? MonitorDevice() =>
        Devices.Get(_selectedMonitor?.Id) ?? Devices.Get(Devices.DefaultSpeakerId());

    private void OnFaulted(string message)
    {
        Log.Write("Moteur audio : " + message);
        Live = false;
        Status = "Reconnexion…";
        _retryTimer.Stop();
        _retryTimer.Start();
    }

    private void UpdateMeters()
    {
        // Fenêtre cachée (zone de notification) : 4 tours par seconde suffisent, sauf si le ducking doit suivre la voix.
        var interval = TimeSpan.FromMilliseconds(!_windowShown && !Mixer.DuckingOn ? 250 : 33);
        if (_meterTimer.Interval != interval) _meterTimer.Interval = interval;
        var now = _meterClock.Elapsed;
        double dt = Math.Clamp((now - _lastMeterTick).TotalSeconds, 0, 1);
        _lastMeterTick = now;

        TickAi();
        var (i, o, a) = Engine.TakeLevels();
        InputLevel = Smooth(InputLevel, ToMeter(i));
        OutputLevel = Smooth(OutputLevel, Muted ? 0 : ToMeter(o));

        // « Je parle » : le micro dépasse le seuil de l'anti-bruit (ou -45 dB) depuis moins de 300 ms.
        double thresholdDb = GateOn ? GateThresholdDb + 6 : -45;
        _sinceVoice = i > 0 && 20 * Math.Log10(i) > thresholdDb && !Muted ? 0 : _sinceVoice + dt;
        Mixer.Tick(_sinceVoice < 0.3, _mixerVisible, a, _windowShown);
        Talking = _sinceVoice < 0.3 && Engine.TalkOpen && !Muted;

        if (Engine.IsRecording) Raise(nameof(RecordingTime));
        if (AutotuneOn) Raise(nameof(DetectedNote));

        static double ToMeter(float peak) => peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
        static double Smooth(double old, double now) => now > old ? now : Math.Max(now, old - 0.035);
    }

    // ---------- Presets ----------

    public PresetItem? CurrentPreset => _current;
    public string CurrentPresetName => _current?.Name ?? "Perso";
    public string CurrentPresetTagline => _current?.Tagline ?? "Réglages perso";
    public string CurrentPresetIcon => _current?.Icon ?? "\uE713";
    public System.Windows.Media.Brush CurrentPresetTone => _current?.ToneBrush ?? Tones.Brush("volt");

    public const string AllFilter = "Tout", FavoritesFilter = "Favoris";

    public IReadOnlyList<string> PresetFilters { get; } =
        [AllFilter, FavoritesFilter, .. PresetCategories.All, PresetCategories.Mine];

    /// <summary>Presets affichés (filtre de rayon + recherche).</summary>
    public ObservableCollection<PresetItem> VisiblePresets { get; } = [];

    public string PresetFilter
    {
        get => _presetFilter;
        set { if (Set(ref _presetFilter, value ?? AllFilter)) RefreshPresetView(); }
    }

    public string PresetSearch
    {
        get => _presetSearch;
        set { if (Set(ref _presetSearch, value ?? "")) RefreshPresetView(); }
    }

    public int PresetCount => AllPresets.Count();
    public string LibraryTitle => $"{VisiblePresets.Count} / {PresetCount}";
    public bool NoPresetMatch => VisiblePresets.Count == 0;

    private void RefreshPresetView()
    {
        var q = _presetSearch.Trim();
        var items = AllPresets.Where(p =>
            (_presetFilter == AllFilter
             || (_presetFilter == FavoritesFilter && p.IsFavorite)
             || p.Category == _presetFilter)
            && (q.Length == 0
                || p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || p.Tagline.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || p.Category.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
        // Favoris d'abord quand on affiche tout.
        if (_presetFilter == AllFilter && q.Length == 0) items = items.OrderByDescending(p => p.IsFavorite);
        VisiblePresets.Clear();
        foreach (var p in items) VisiblePresets.Add(p);
        Raise(nameof(NoPresetMatch));
        Raise(nameof(PresetCount));
        RefreshQuickVoices();
        Raise(nameof(LibraryTitle));
    }

    public void ToggleFavorite(PresetItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        _settings.Favorites.RemoveAll(n => n == item.Name);
        if (item.IsFavorite) _settings.Favorites.Add(item.Name);
        MarkDirty();
        RefreshPresetView();
    }

    /// <summary>Favori n°<paramref name="index"/> (raccourcis Ctrl+Alt+F1…F9).</summary>
    public void ApplyFavorite(int index)
    {
        var favs = AllPresets.Where(p => p.IsFavorite).ToList();
        if (index >= 0 && index < favs.Count) ApplyPreset(favs[index]);
        else Notify?.Invoke("Pas de favori n°" + (index + 1) + " · cliquez sur l'étoile d'une voix pour l'ajouter");
    }
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
        preset with
        {
            VolumeDb = Voice.VolumeDb, GateOn = Voice.GateOn, GateThresholdDb = Voice.GateThresholdDb,
            // Le débruitage IA suit le micro, sauf si la voix l'allume elle-même.
            DenoiseOn = preset.DenoiseOn || Voice.DenoiseOn, DenoiseAmount = preset.DenoiseOn ? preset.DenoiseAmount : Voice.DenoiseAmount,
            // Le coupe-bas de l'assistant micro aussi (la voix peut couper plus haut).
            LowCutOn = preset.LowCutOn || _settings.MicLowCutOn,
            LowCutHz = preset.LowCutOn && _settings.MicLowCutOn ? Math.Max(preset.LowCutHz, _settings.MicLowCutHz)
                     : preset.LowCutOn ? preset.LowCutHz : _settings.MicLowCutOn ? _settings.MicLowCutHz : preset.LowCutHz,
        };

    /// <summary>Réglages proposés par l'assistant micro (ou remis par « Annuler »).</summary>
    public void ApplyMicSettings(VoiceSettings value, bool lowCutFromAssistant)
    {
        _settings.MicLowCutOn = lowCutFromAssistant;
        if (lowCutFromAssistant) _settings.MicLowCutHz = value.LowCutHz;
        SetVoice(value, fromPreset: true);
        Modified = DiffersFromPreset(value);
    }

    public NAudio.CoreAudioApi.MMDevice? MicrophoneDevice() => Devices.Get(_selectedMicrophone?.Id);

    public void Toast(string message) => Notify?.Invoke(message);

    public MicAssistant MicAssistant => _micAssistant ??= new MicAssistant(this, Engine);
    private MicAssistant? _micAssistant;

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
        item.IsFavorite = _settings.Favorites.Contains(name);
        RefreshPresetView();
        ApplyPreset(item);
        Notify?.Invoke($"Voix enregistrée · « {name} » ajoutée à vos presets");
    }

    public void DeletePreset(PresetItem item)
    {
        if (!item.IsUser) return;
        _store.Delete(item.Name);
        UserPresets.Remove(item);
        _settings.Favorites.Remove(item.Name);
        RefreshPresetView();
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
        RefreshPresetView();
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
        Raise(nameof(ActiveChain));
        Raise(nameof(LatencyText)); Raise(nameof(RateText));
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

    public double FormantSemitones { get => Voice.FormantSemitones; set => SetVoice(Voice with { FormantSemitones = Math.Round(value, 1) }); }
    public bool KeepFormants { get => Voice.KeepFormants; set => SetVoice(Voice with { KeepFormants = value }); }
    public bool DenoiseOn { get => Voice.DenoiseOn; set { SetVoice(Voice with { DenoiseOn = value }); RaiseDenoise(); } }
    public double DenoiseAmount { get => Voice.DenoiseAmount; set { SetVoice(Voice with { DenoiseAmount = Math.Round(value, 2) }); RaiseDenoise(); } }

    // Réduction de bruit en trois crans (carte du studio) : Doux 35 %, Équilibré 60 %, Maximum 90 %.
    public bool DenoiseSoft { get => DenoiseAmount < 0.45; set { if (value) DenoiseAmount = 0.35; } }
    public bool DenoiseBalanced { get => DenoiseAmount is >= 0.45 and < 0.75; set { if (value) DenoiseAmount = 0.6; } }
    public bool DenoiseMax { get => DenoiseAmount >= 0.75; set { if (value) DenoiseAmount = 0.9; } }

    /// <summary>Atténuation maximale du bruit de fond, comme le débruiteur la calcule (8 à 32 dB).</summary>
    public string DenoiseDbText => DenoiseOn ? "-" + Math.Round(8 + DenoiseAmount * 24) : "0";
    public string DenoiseSummary => DenoiseOn ? "Clavier, ventilo et souffle filtrés" : "Désactivée";

    private void RaiseDenoise()
    {
        Raise(nameof(DenoiseSoft)); Raise(nameof(DenoiseBalanced)); Raise(nameof(DenoiseMax));
        Raise(nameof(DenoiseDbText)); Raise(nameof(DenoiseSummary));
    }
    public bool LowCutOn { get => Voice.LowCutOn; set => SetVoice(Voice with { LowCutOn = value }); }
    public double LowCutHz { get => Voice.LowCutHz; set => SetVoice(Voice with { LowCutHz = Math.Round(value) }); }
    public double EqLowMidDb { get => Voice.EqLowMidDb; set => SetVoice(Voice with { EqLowMidDb = Math.Round(value, 1) }); }
    public double EqPresenceDb { get => Voice.EqPresenceDb; set => SetVoice(Voice with { EqPresenceDb = Math.Round(value, 1) }); }
    public bool DeEsserOn { get => Voice.DeEsserOn; set => SetVoice(Voice with { DeEsserOn = value }); }
    public double DeEsserAmount { get => Voice.DeEsserAmount; set => SetVoice(Voice with { DeEsserAmount = Math.Round(value, 2) }); }
    public bool AutotuneOn { get => Voice.AutotuneOn; set => SetVoice(Voice with { AutotuneOn = value }); }
    public int AutotuneKey { get => Voice.AutotuneKey; set => SetVoice(Voice with { AutotuneKey = value }); }
    public int AutotuneScale { get => Voice.AutotuneScale; set => SetVoice(Voice with { AutotuneScale = value }); }
    public double AutotuneSpeed { get => Voice.AutotuneSpeed; set => SetVoice(Voice with { AutotuneSpeed = Math.Round(value, 2) }); }
    public bool HarmonyOn { get => Voice.HarmonyOn; set => SetVoice(Voice with { HarmonyOn = value }); }
    public double HarmonySemitones { get => Voice.HarmonySemitones; set => SetVoice(Voice with { HarmonySemitones = Math.Round(value) }); }
    public double HarmonyMix { get => Voice.HarmonyMix; set => SetVoice(Voice with { HarmonyMix = Math.Round(value, 2) }); }
    public bool WhisperOn { get => Voice.WhisperOn; set => SetVoice(Voice with { WhisperOn = value }); }
    public double WhisperAmount { get => Voice.WhisperAmount; set => SetVoice(Voice with { WhisperAmount = Math.Round(value, 2) }); }
    public bool VibratoOn { get => Voice.VibratoOn; set => SetVoice(Voice with { VibratoOn = value }); }
    public double VibratoRate { get => Voice.VibratoRate; set => SetVoice(Voice with { VibratoRate = Math.Round(value, 1) }); }
    public double VibratoDepth { get => Voice.VibratoDepth; set => SetVoice(Voice with { VibratoDepth = Math.Round(value, 2) }); }
    public bool BitcrushOn { get => Voice.BitcrushOn; set => SetVoice(Voice with { BitcrushOn = value }); }
    public double BitcrushBits { get => Voice.BitcrushBits; set => SetVoice(Voice with { BitcrushBits = Math.Round(value) }); }
    public double BitcrushRateHz { get => Voice.BitcrushRateHz; set => SetVoice(Voice with { BitcrushRateHz = Math.Round(value / 100) * 100 }); }
    public bool FilterOn { get => Voice.FilterOn; set => SetVoice(Voice with { FilterOn = value }); }
    public double FilterLowHz { get => Voice.FilterLowHz; set => SetVoice(Voice with { FilterLowHz = Math.Round(value) }); }
    public double FilterHighHz { get => Voice.FilterHighHz; set => SetVoice(Voice with { FilterHighHz = Math.Round(value / 10) * 10 }); }
    public bool ChorusOn { get => Voice.ChorusOn; set => SetVoice(Voice with { ChorusOn = value }); }
    public double ChorusRate { get => Voice.ChorusRate; set => SetVoice(Voice with { ChorusRate = Math.Round(value, 2) }); }
    public double ChorusDepth { get => Voice.ChorusDepth; set => SetVoice(Voice with { ChorusDepth = Math.Round(value, 2) }); }
    public double ChorusMix { get => Voice.ChorusMix; set => SetVoice(Voice with { ChorusMix = Math.Round(value, 2) }); }

    public IReadOnlyList<string> KeyNames => Autotune.KeyNames;
    public IReadOnlyList<string> ScaleNames => Autotune.ScaleNames;

    /// <summary>Note chantée en ce moment (autotune).</summary>
    public string DetectedNote
    {
        get
        {
            double f = Engine.DetectedFrequency;
            if (f <= 0) return "—";
            int midi = (int)Math.Round(69 + 12 * Math.Log2(f / 440));
            return Autotune.KeyNames[((midi % 12) + 12) % 12] + (midi / 12 - 1);
        }
    }

    /// <summary>Effets allumés, dans l'ordre de la chaîne (pour la barre « chaîne du signal »).</summary>
    public IReadOnlyList<string> ActiveChain
    {
        get
        {
            var v = Voice;
            var list = new List<string>();
            void Add(bool on, string name) { if (on) list.Add(name); }
            Add(v.GateOn, "Anti-bruit");
            Add(v.LowCutOn, "Coupe-bas");
            Add(v.DenoiseOn, "Réduction de bruit");
            Add(v.AutotuneOn, "Autotune");
            Add(Math.Abs(v.PitchSemitones) > 0.01 || v.Depth > 0.001, "Pitch");
            Add(Math.Abs(v.FormantSemitones) > 0.01 || (v.KeepFormants && Math.Abs(v.PitchSemitones) > 0.01), "Timbre");
            Add(v.WhisperOn, "Chuchotement");
            Add(v.HarmonyOn, "Harmoniseur");
            Add(v.RobotOn, "Robot");
            Add(v.VibratoOn, "Vibrato");
            Add(v.DistortionOn, "Drive");
            Add(v.BitcrushOn, "8-bit");
            Add(v.RadioOn, "Radio");
            Add(v.FilterOn, "Filtre");
            Add(v.EqOn, "Égaliseur");
            Add(v.DeEsserOn, "De-esser");
            Add(v.CompressorOn, "Compresseur");
            Add(v.ChorusOn, "Chorus");
            Add(v.EchoOn, "Écho");
            Add(v.ReverbOn, "Reverb");
            list.Add("Limiteur");
            return list;
        }
    }

    public string VersionText { get; } = "v" + (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "");
    public string LatencyText => Live ? FormattableString.Invariant($"{Engine.LatencyMs:0.0} ms") : "—";
    public string RateText => Live && Engine.SampleRate > 0 ? FormattableString.Invariant($"{Engine.SampleRate / 1000.0:0.#} kHz") : "—";

    /// <summary>Remet tous les effets à zéro (garde volume, anti-bruit et débruitage).</summary>
    public void ResetEffects()
    {
        var normal = Presets.First();
        ApplyPreset(normal);
    }
    // ---------- Studio : A/B, enregistrement, push-to-talk ----------

    private bool _talking;
    public bool Talking { get => _talking; private set => Set(ref _talking, value); }

    /// <summary>Inverse de <see cref="Bypass"/> (interrupteur de la carte « voix active »).</summary>
    public bool EffectsOn { get => !_bypass; set => Bypass = !value; }

    /// <summary>Votre voix sans effets, pour comparer (A/B).</summary>
    public bool Bypass
    {
        get => _bypass;
        set
        {
            if (!Set(ref _bypass, value)) return;
            Raise(nameof(EffectsOn));
            Engine.Bypass = value;
            Notify?.Invoke(value ? "Effets coupés · vous entendez votre vraie voix (A/B)" : "Effets rallumés");
        }
    }

    public bool IsRecording => Engine.IsRecording;
    public string RecordingTime => Engine.CurrentRecording is { } r ? r.Duration.ToString(@"mm\:ss") : "00:00";

    public string RecordingsFolder
    {
        get => string.IsNullOrWhiteSpace(_settings.RecordingsFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "se7en Voice")
            : _settings.RecordingsFolder!;
        set { _settings.RecordingsFolder = value; MarkDirty(); Raise(); }
    }

    public void ToggleRecording()
    {
        if (Engine.IsRecording)
        {
            var path = Engine.StopRecording();
            Raise(nameof(IsRecording));
            if (path != null) Notify?.Invoke("Enregistrement sauvegardé · " + Path.GetFileName(path));
            return;
        }
        if (!Live)
        {
            Notify?.Invoke("Le micro n'est pas actif : impossible d'enregistrer.");
            return;
        }
        try
        {
            Engine.StartRecording(RecordingsFolder);
            Notify?.Invoke("Enregistrement lancé · CTRL+ALT+R pour arrêter");
        }
        catch (Exception ex) { Notify?.Invoke("Impossible d'enregistrer : " + ex.Message); }
        Raise(nameof(IsRecording));
    }

    public bool PushToTalkOn
    {
        get => _settings.PushToTalkOn;
        set
        {
            _settings.PushToTalkOn = value;
            MarkDirty();
            Raise();
            UpdateKeyTimer();
        }
    }

    public string PushToTalkKeyName => CapturingKey ? "Appuie sur une touche…" : KeyWatcher.NameOf(_settings.PushToTalkKey);

    public bool CapturingKey { get => _capturingKey; private set { Set(ref _capturingKey, value); Raise(nameof(PushToTalkKeyName)); } }

    /// <summary>La prochaine touche ou bouton de souris enfoncé devient la touche de push-to-talk.</summary>
    public void StartKeyCapture()
    {
        CapturingKey = true;
        _keyCaptureTicks = 0;
        UpdateKeyTimer();
    }

    private void UpdateKeyTimer()
    {
        if (_capturingKey && !_keyTimer.IsEnabled) _keyTimer.Start();
        if (!_capturingKey) _keyTimer.Stop();

        // Push-to-talk (en pause pendant la capture d'une nouvelle touche, comme avant)
        bool ptt = _settings.PushToTalkOn && !_capturingKey && !_disposed;
        lock (_pttLock)
        {
            _pttActive = ptt;
            _pttKey = _settings.PushToTalkKey;
            Engine.TalkOpen = !_settings.PushToTalkOn;
        }
        if (ptt) _pttTimer ??= new System.Threading.Timer(_ => PushToTalkTick(), null, 0, 16);
        else
        {
            _pttTimer?.Dispose();
            _pttTimer = null;
        }
    }

    /// <summary>Minuteur du push-to-talk (fil du pool) : la touche tenue ouvre le micro, directement dans le moteur.</summary>
    private void PushToTalkTick()
    {
        lock (_pttLock)
        {
            // sous verrou : un tour en retard ne peut pas refermer le micro après l'arrêt du push-to-talk
            if (_pttActive) Engine.TalkOpen = KeyWatcher.IsHeld(_pttKey);
        }
    }

    private void KeyTick()
    {
        if (_capturingKey)
        {
            // On ignore les 150 premières ms : le clic qui a lancé la capture.
            if (++_keyCaptureTicks < 10) return;
            if (KeyWatcher.FirstPressed() is int vk)
            {
                _settings.PushToTalkKey = vk;
                MarkDirty();
                CapturingKey = false;
                UpdateKeyTimer();
                Notify?.Invoke("Push-to-talk activé · maintenez « " + KeyWatcher.NameOf(vk) + " » pour parler.");
            }
            else if (_keyCaptureTicks > 600) { CapturingKey = false; UpdateKeyTimer(); }
        }
    }

    /// <summary>La fenêtre affiche la table de mixage (vu-mètres à rafraîchir).</summary>
    public bool MixerVisible { get => _mixerVisible; set => _mixerVisible = value; }

    /// <summary>La fenêtre est à l'écran (ni cachée dans la zone de notification, ni réduite) : sinon, minuteurs ralentis.</summary>
    public bool WindowShown { get => _windowShown; set => _windowShown = value; }

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

    // ---------- Prêt pour Discord : les trois choses à vérifier ----------

    private bool _outputOnCable;

    /// <summary>Un micro physique est branché.</summary>
    public bool MicFound => _selectedMicrophone != null;

    /// <summary>La sortie par défaut de Windows est le micro virtuel : plus de son dans le casque.</summary>
    public bool OutputOnCable { get => _outputOnCable; private set { if (Set(ref _outputOnCable, value)) Raise(nameof(ReadyForDiscord)); } }

    public bool ReadyForDiscord => MicFound && CableFound && !OutputOnCable;

    /// <summary>Remet le casque (ou les haut-parleurs) comme sortie par défaut de Windows.</summary>
    public void FixWindowsOutput()
    {
        var target = _selectedMonitor ?? Speakers.FirstOrDefault();
        if (target != null && DefaultDevice.SetDefaultOutput(target.Id))
        {
            RefreshDevices(restart: false);
            Mixer.Scan();
            Notify?.Invoke("Son rétabli · Windows joue de nouveau dans « " + target.Name + " »");
        }
        else
        {
            Notify?.Invoke("Changement impossible · choisissez votre casque dans Paramètres Windows → Son → Sortie");
        }
    }

    public bool ExpertMode
    {
        get => _settings.ExpertMode;
        set { _settings.ExpertMode = value; MarkDirty(); Raise(); }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { _settings.MinimizeToTray = value; MarkDirty(); Raise(); }
    }

    public bool CableSetupOffered
    {
        get => _settings.CableSetupOffered;
        set { _settings.CableSetupOffered = value; MarkDirty(); }
    }

    public bool CableRenameOffered
    {
        get => _settings.CableRenameOffered;
        set { _settings.CableRenameOffered = value; MarkDirty(); }
    }

    // ---------- Installation de « Seven Microphone » ----------

    private bool _installingCable;
    private string _cableInstallStatus = "";

    public bool InstallingCable { get => _installingCable; private set => Set(ref _installingCable, value); }
    public string CableInstallStatus { get => _cableInstallStatus; private set => Set(ref _cableInstallStatus, value); }

    /// <summary>Le micro virtuel attend un redémarrage du PC (installé mais pas encore visible).</summary>
    public bool CableRebootPending
    {
        get => _settings.CableRebootPending;
        private set { _settings.CableRebootPending = value; MarkDirty(); Raise(); }
    }

    /// <summary>
    /// Télécharge VB-CABLE sur le site officiel, lance son installateur (une demande Windows, puis
    /// « Install Driver ») et renomme le résultat en « Seven Microphone ».
    /// </summary>
    public async Task InstallCableAsync()
    {
        if (InstallingCable) return;
        InstallingCable = true;
        try
        {
            CableInstallStatus = "Téléchargement…";
            var progress = new Progress<double>(f => CableInstallStatus = $"Téléchargement… {f:P0}");
            string setup;
            try { setup = await CableInstaller.DownloadAsync(progress); }
            catch (Exception ex)
            {
                CableInstallStatus = "";
                Notify?.Invoke("Téléchargement impossible · " + ex.Message);
                return;
            }

            CableInstallStatus = "Cliquez « Oui », puis « Install Driver » dans la fenêtre VB-Audio";
            var code = await CableInstaller.RequestElevatedInstallAsync(setup);
            RefreshDevices(restart: true);
            switch (code)
            {
                case null:
                    CableInstallStatus = "";
                    Notify?.Invoke("Installation annulée · Seven Microphone n'est pas installé");
                    break;
                case CableInstaller.Done:
                    CableInstallStatus = "";
                    CableRebootPending = false;
                    Notify?.Invoke("Seven Microphone installé · choisissez-le comme micro dans Discord et vos jeux");
                    break;
                case CableInstaller.RebootNeeded when CableFound:
                    // Pilote visible mais renommage refusé/échoué : on le proposera à nouveau.
                    CableInstallStatus = "";
                    Notify?.Invoke("Micro virtuel installé · renommez-le dans Paramètres (bouton « Renommer »)");
                    break;
                case CableInstaller.RebootNeeded:
                    CableRebootPending = true;
                    CableInstallStatus = "Redémarrez le PC pour terminer l'installation";
                    Notify?.Invoke("Presque fini · redémarrez le PC, Seven Microphone sera prêt au prochain lancement");
                    break;
                default:
                    CableInstallStatus = "";
                    Notify?.Invoke("L'installation n'a pas abouti · réessayez, ou installez VB-CABLE depuis vb-audio.com");
                    break;
            }
        }
        finally
        {
            InstallingCable = false;
            RefreshComponentTexts();
        }
    }

    /// <summary>Au démarrage : le pilote est apparu après le redémarrage, on finit en le renommant.</summary>
    public void FinishPendingCableSetup()
    {
        if (!CableRebootPending || !CableFound) return;
        CableRebootPending = false;
        CableInstallStatus = "";
        if (!CableRenamed) RenameCable();
    }

    private bool _autoRenameDone;

    /// <summary>Dès que le micro virtuel est là sans le bon nom : renommage en silence (pas besoin de droits admin), une fois par lancement.</summary>
    private void AutoRenameCable()
    {
        if (_autoRenameDone || !CableFound || CableRenamed) return;
        _autoRenameDone = true;
        if (CableRenamer.Rename() > 0)
        {
            RefreshDevices(restart: false);
            if (CableRenamed) Notify?.Invoke("Seven Microphone prêt · dans Discord, entrée : « " + CableRenamer.MicName + " », sortie : votre casque");
        }
    }

    public void RenameCable()
    {
        if (CableRenamer.RenameAll())
        {
            RefreshDevices(restart: false);
            Notify?.Invoke("Micro renommé · dans Discord, entrée : « " + CableRenamer.MicName + " », sortie : votre casque");
        }
        else
        {
            Notify?.Invoke("Renommage impossible · " + (CableRenamer.LastError.Length > 0 ? CableRenamer.LastError : "Windows a refusé"));
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
        _disposed = true;
        _meterTimer.Stop();
        _saveTimer.Stop();
        _keyTimer.Stop();
        lock (_pttLock) _pttActive = false;
        _pttTimer?.Dispose();
        _pttTimer = null;
        Engine.StopRecording();
        Mixer.Dispose();
        _settings.Save();
        DisposeAi();
        Engine.Dispose();
        Devices.Dispose();
    }
}
