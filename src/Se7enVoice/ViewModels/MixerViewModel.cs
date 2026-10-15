using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using Se7enVoice.Audio;
using Se7enVoice.Core;
using Se7enVoice.Core.Mixer;

namespace Se7enVoice.ViewModels;

/// <summary>
/// Table de mixage façon Voicemeeter, sans pilote : chaque appli qui fait du son (sessions audio
/// de Windows) est rangée dans une tranche (jeux, musique, discussion…) avec fader, muet et
/// vu-mètre. Les volumes sont ceux du mélangeur de Windows : ils restent même app fermée.
/// Une appli peut aussi être envoyée dans le micro virtuel (capture « process loopback »).
/// </summary>
public sealed class MixerViewModel : Observable
{
    private readonly AppSettings _settings;
    private readonly AudioEngine _engine;
    private readonly Action _markDirty;
    private readonly Action<string> _notify;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, MixerApp> _apps = new(StringComparer.OrdinalIgnoreCase);
    private MMDevice? _master;
    private double _masterLevel, _duck = 1, _appsToMicLevel;
    private DateTime _lastScan;

    public MixerViewModel(AppSettings settings, AudioEngine engine, Action markDirty, Action<string> notify)
    {
        _settings = settings;
        _engine = engine;
        _markDirty = markDirty;
        _notify = notify;
        foreach (var cat in AppCategories.All) Channels.Add(new MixerChannel(this, cat));
        _engine.AppsGain = (float)Math.Pow(10, settings.AppsToMicDb / 20);
    }

    public ObservableCollection<MixerChannel> Channels { get; } = [];
    /// <summary>Jeux, musique et discussion sont toujours là ; les autres tranches n'apparaissent que si une appli y joue.</summary>
    public IEnumerable<MixerChannel> VisibleChannels => Channels.Where(c =>
        c.Apps.Count > 0 || c.Category is AppCategories.Games or AppCategories.Music or AppCategories.Chat).ToList();

    public bool AppCaptureSupported => ProcessLoopbackCapture.IsSupported;

    internal AudioEngine Engine => _engine;

    // ---------- Master (sortie Windows par défaut) ----------

    public string MasterName { get; private set; } = "";

    public double MasterVolume
    {
        get { try { return _master?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0; } catch (Exception) { return 0; } }
        set { try { if (_master != null) _master.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0, 1); } catch (Exception) { } Raise(); }
    }

    public bool MasterMuted
    {
        get { try { return _master?.AudioEndpointVolume.Mute ?? false; } catch (Exception) { return false; } }
        set { try { if (_master != null) _master.AudioEndpointVolume.Mute = value; } catch (Exception) { } Raise(); }
    }

    public double MasterLevel { get => _masterLevel; private set => Set(ref _masterLevel, value); }

    // ---------- Applis dans le micro ----------

    public double AppsToMicDb
    {
        get => _settings.AppsToMicDb;
        set
        {
            _settings.AppsToMicDb = Math.Round(value);
            _engine.AppsGain = (float)Math.Pow(10, value / 20);
            _markDirty();
            Raise();
        }
    }

    public double AppsToMicLevel { get => _appsToMicLevel; private set => Set(ref _appsToMicLevel, value); }

    // ---------- Ducking ----------

    public bool DuckOnMyVoice { get => _settings.DuckOnMyVoice; set { _settings.DuckOnMyVoice = value; _markDirty(); Raise(); if (!value) ResetDuck(); } }
    public bool DuckOnFriends { get => _settings.DuckOnFriends; set { _settings.DuckOnFriends = value; _markDirty(); Raise(); if (!value) ResetDuck(); } }
    public double DuckAmount { get => _settings.DuckAmount; set { _settings.DuckAmount = Math.Round(value, 2); _markDirty(); Raise(); } }

    private void ResetDuck()
    {
        if (_duck >= 0.999) return;
        _duck = 1;
        foreach (var c in Channels) c.ApplyAll();
    }

    /// <summary>Atténuation appliquée à une tranche par le ducking (1 = rien).</summary>
    internal double DuckFor(string category) => category is AppCategories.Music || (category is AppCategories.Games && DuckOnFriends) ? _duck : 1;

    // ---------- Boucle ----------

    /// <summary>Appelé ~30 fois par seconde par la fenêtre. <paramref name="voiceActive"/> : je parle en ce moment.</summary>
    public void Tick(bool voiceActive, bool visible, float appsPeak)
    {
        if (DateTime.UtcNow - _lastScan > TimeSpan.FromSeconds(visible ? 1.5 : 4))
        {
            _lastScan = DateTime.UtcNow;
            Scan();
        }

        // Ducking : on baisse vite (80 ms) et on remonte doucement (600 ms).
        bool friends = DuckOnFriends && Channels.First(c => c.Category == AppCategories.Chat).Level > 0.45;
        bool duck = (DuckOnMyVoice && voiceActive) || friends;
        double target = duck ? 1 - 0.9 * DuckAmount : 1;
        double prev = _duck;
        _duck += (target - _duck) * (target < _duck ? 0.35 : 0.06);
        if (Math.Abs(_duck - target) < 0.005) _duck = target;
        if (Math.Abs(_duck - prev) > 0.002)
            foreach (var c in Channels)
                if (DuckFor(c.Category) < 1 || prev < 1) c.ApplyAll();

        foreach (var app in _apps.Values) app.UpdateLevel();
        foreach (var c in Channels) c.UpdateLevel();
        if (!visible) return;
        try { MasterLevel = Meter(_master?.AudioMeterInformation.MasterPeakValue ?? 0); } catch (Exception) { MasterLevel = 0; }
        AppsToMicLevel = Math.Max(Meter(appsPeak), AppsToMicLevel - 0.035);
    }

    internal static double Meter(float peak) => peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);

    /// <summary>Relit la liste des applis qui ont une session audio.</summary>
    public void Scan()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var def = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (_master?.ID != def.ID)
            {
                _master = def;
                MasterName = def.FriendlyName;
                Raise(nameof(MasterName)); Raise(nameof(MasterVolume)); Raise(nameof(MasterMuted));
            }

            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (DeviceService.IsCable(device)) continue;
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateExpired) continue;
                    var (key, name, path, pid) = Identify(s);
                    if (key == null || pid == Environment.ProcessId) continue;
                    seen.Add(key);
                    if (!_apps.TryGetValue(key, out var app))
                    {
                        var cat = _settings.AppCategoryOverrides.TryGetValue(key, out var o) && AppCategories.All.Contains(o)
                            ? o : AppCategories.Classify(key, path, s.IsSystemSoundsSession);
                        app = new MixerApp(this, key, name, cat, path);
                        _apps[key] = app;
                        ChannelOf(cat).Add(app);
                    }
                    app.Attach(s, pid);
                }
            }
        }
        catch (Exception)
        {
            // Service audio occupé : on réessaiera au prochain tour.
        }

        foreach (var gone in _apps.Values.Where(a => !seen.Contains(a.Key)).ToList())
        {
            _apps.Remove(gone.Key);
            ChannelOf(gone.Category).Remove(gone);
            gone.StopMic();
        }
        foreach (var app in _apps.Values) app.EndScan();
        RaiseVisibleIfChanged();
    }

    private static (string? key, string name, string? path, int pid) Identify(AudioSessionControl s)
    {
        if (s.IsSystemSoundsSession) return ("Sons système", "Sons système", null, 0);
        int pid = (int)s.GetProcessID;
        if (pid == 0) return (null, "", null, 0);
        try
        {
            using var p = Process.GetProcessById(pid);
            string key = p.ProcessName;
            string? path = null;
            string name = key;
            try
            {
                path = p.MainModule?.FileName;
                var desc = p.MainModule?.FileVersionInfo.FileDescription;
                if (!string.IsNullOrWhiteSpace(desc) && desc.Length < 40) name = desc.Trim();
            }
            catch (Exception) { /* jeu lancé en admin, anti-triche… on garde le nom du programme */ }
            if (name == key && name.Length > 0) name = char.ToUpper(name[0]) + name[1..];
            return (key, name, path, pid);
        }
        catch (Exception)
        {
            return (null, "", null, 0);
        }
    }

    private string _visibleKey = "";

    /// <summary>On ne reconstruit les tranches que si leur liste change (sinon un fader en cours de glissement serait lâché).</summary>
    private void RaiseVisibleIfChanged()
    {
        var key = string.Join("|", VisibleChannels.Select(c => c.Category));
        if (key == _visibleKey) return;
        _visibleKey = key;
        Raise(nameof(VisibleChannels));
    }

    internal MixerChannel ChannelOf(string category) =>
        Channels.FirstOrDefault(c => c.Category == category) ?? Channels.First(c => c.Category == AppCategories.Other);

    internal void Move(MixerApp app, string category)
    {
        if (app.Category == category) return;
        ChannelOf(app.Category).Remove(app);
        app.Category = category;
        app.RaiseCategory();
        ChannelOf(category).Add(app);
        _settings.AppCategoryOverrides[app.Key] = category;
        _markDirty();
        app.Apply();
        RaiseVisibleIfChanged();
    }

    // ---------- Persistance (appelée par les tranches et les applis) ----------

    internal double? CategoryVolume(string cat) => _settings.CategoryVolumes.TryGetValue(cat, out var v) ? v : null;
    internal bool CategoryMuted(string cat) => _settings.CategoryMutes.TryGetValue(cat, out var m) && m;
    internal double? AppVolume(string key) => _settings.AppVolumes.TryGetValue(key, out var v) ? v : null;
    internal bool AppMuted(string key) => _settings.AppMutes.TryGetValue(key, out var m) && m;
    internal bool IsManaged(string cat, string key) =>
        _settings.CategoryVolumes.ContainsKey(cat) || _settings.CategoryMutes.ContainsKey(cat)
        || _settings.AppVolumes.ContainsKey(key) || _settings.AppMutes.ContainsKey(key) || DuckFor(cat) < 1;

    internal void SaveCategory(string cat, double? volume, bool? muted)
    {
        if (volume is double v) _settings.CategoryVolumes[cat] = Math.Round(v, 3);
        if (muted is bool m) _settings.CategoryMutes[cat] = m;
        _markDirty();
    }

    internal void SaveApp(string key, double? volume, bool? muted)
    {
        if (volume is double v) _settings.AppVolumes[key] = Math.Round(v, 3);
        if (muted is bool m) _settings.AppMutes[key] = m;
        _markDirty();
    }

    internal bool WantsMic(string key) => _settings.AppsToMic.Contains(key, StringComparer.OrdinalIgnoreCase);

    internal bool SetMic(MixerApp app, bool on)
    {
        _settings.AppsToMic.RemoveAll(k => string.Equals(k, app.Key, StringComparison.OrdinalIgnoreCase));
        if (on) _settings.AppsToMic.Add(app.Key);
        _markDirty();
        if (!on) { app.StopMic(); return true; }
        if (!AppCaptureSupported)
        {
            _notify("Envoyer une appli dans le micro demande Windows 11 (ou un Windows 10 récent)");
            return false;
        }
        return app.StartMic(_notify);
    }

    /// <summary>Le moteur audio a redémarré (il a coupé les captures) : on relance celles des applis concernées.</summary>
    public void RestartMicApps()
    {
        foreach (var app in _apps.Values) app.RestartMic(_notify);
    }

    public void Dispose()
    {
        foreach (var app in _apps.Values) app.StopMic();
        _enumerator.Dispose();
    }
}

/// <summary>Une tranche de la table de mixage (Jeux, Musique…).</summary>
public sealed class MixerChannel(MixerViewModel owner, string category) : Observable
{
    private double _level;

    public string Category { get; } = category;
    public string Icon => AppCategories.IconOf(Category);
    /// <summary>Couleur fixe de la source (design Centrale Audio : jeu pêche, musique menthe, Discord ciel, navigateur rose).</summary>
    public Brush ToneBrush => Tones.Brush(Category switch
    {
        AppCategories.Games => "peach",
        AppCategories.Music => "mint",
        AppCategories.Chat => "sky",
        AppCategories.Browser => "rose",
        AppCategories.System => "paper",
        _ => "violet",
    });
    public ObservableCollection<MixerApp> Apps { get; } = [];
    public string Summary => Apps.Count == 0 ? "Aucune appli" : string.Join(" · ", Apps.Select(a => a.Name));

    public double Volume
    {
        get => owner.CategoryVolume(Category) ?? 1;
        set
        {
            owner.SaveCategory(Category, Math.Clamp(value, 0, 1), null);
            Raise();
            ApplyAll();
        }
    }

    public bool Muted
    {
        get => owner.CategoryMuted(Category);
        set
        {
            owner.SaveCategory(Category, null, value);
            Raise();
            ApplyAll();
        }
    }

    public double Level { get => _level; private set => Set(ref _level, value); }

    internal MixerViewModel Owner => owner;

    internal void Add(MixerApp app) { Apps.Add(app); Raise(nameof(Summary)); }
    internal void Remove(MixerApp app) { Apps.Remove(app); Raise(nameof(Summary)); }

    internal void ApplyAll() { foreach (var a in Apps) a.Apply(); }

    internal void UpdateLevel()
    {
        double peak = Apps.Count == 0 ? 0 : Apps.Max(a => a.Level);
        Level = peak > _level ? peak : Math.Max(peak, _level - 0.035);
    }
}

/// <summary>Une appli dans une tranche (peut avoir plusieurs sessions audio : plusieurs sorties, onglets…).</summary>
public sealed class MixerApp : Observable
{
    private readonly MixerViewModel _owner;
    private readonly List<AudioSessionControl> _sessions = [], _scan = [];
    private double _level;
    private int _pid;
    private bool _mic, _micActive;

    internal MixerApp(MixerViewModel owner, string key, string name, string category, string? path)
    {
        _owner = owner;
        Key = key;
        Name = name;
        Category = category;
        Path = path;
        _mic = owner.WantsMic(key);
    }

    public string Key { get; }
    public string Name { get; }
    public string? Path { get; }
    public string Category { get; internal set; }
    public IReadOnlyList<string> Categories => AppCategories.All;
    public Brush ToneBrush => _owner.ChannelOf(Category).ToneBrush;
    public string Icon => AppCategories.IconOf(Category);

    public double Volume
    {
        get => _owner.AppVolume(Key) ?? 1;
        set
        {
            _owner.SaveApp(Key, Math.Clamp(value, 0, 1), null);
            Raise();
            Apply();
        }
    }

    public bool Muted
    {
        get => _owner.AppMuted(Key);
        set
        {
            _owner.SaveApp(Key, null, value);
            Raise();
            Apply();
        }
    }

    /// <summary>Le son de l'appli est envoyé dans le micro virtuel.</summary>
    public bool ToMic
    {
        get => _mic;
        set
        {
            if (_mic == value) return;
            _mic = value;
            if (!_owner.SetMic(this, value)) _mic = false;
            Raise();
        }
    }

    public bool CanSendToMic => Key != "Sons système";

    public double Level { get => _level; private set => Set(ref _level, value); }

    public string MoveTo
    {
        get => Category;
        set { if (value != null && value != Category) _owner.Move(this, value); }
    }

    internal void RaiseCategory()
    {
        Raise(nameof(Category)); Raise(nameof(ToneBrush)); Raise(nameof(Icon)); Raise(nameof(MoveTo));
    }

    internal void Attach(AudioSessionControl s, int pid)
    {
        _scan.Add(s);
        if (pid != 0 && (_pid == 0 || !_micActive)) _pid = pid;
    }

    internal void EndScan()
    {
        bool fresh = _scan.Count != _sessions.Count;
        _sessions.Clear();
        _sessions.AddRange(_scan);
        _scan.Clear();
        if (fresh) Apply();
        if (_mic && !_micActive) RestartMicQuiet();
    }

    /// <summary>Applique volume × tranche × ducking aux sessions Windows (seulement si l'utilisateur a réglé quelque chose).</summary>
    internal void Apply()
    {
        var channel = _owner.ChannelOf(Category);
        if (!_owner.IsManaged(Category, Key)) return;
        float vol = (float)Math.Clamp(Volume * channel.Volume * _owner.DuckFor(Category), 0, 1);
        bool mute = Muted || channel.Muted;
        foreach (var s in _sessions)
        {
            try
            {
                var sv = s.SimpleAudioVolume;
                if (Math.Abs(sv.Volume - vol) > 0.001) sv.Volume = vol;
                if (sv.Mute != mute) sv.Mute = mute;
            }
            catch (Exception) { /* session fermée entre deux scans */ }
        }
    }

    internal void UpdateLevel()
    {
        float peak = 0;
        foreach (var s in _sessions)
        {
            try { peak = Math.Max(peak, s.AudioMeterInformation.MasterPeakValue); } catch (Exception) { }
        }
        double m = MixerViewModel.Meter(peak);
        Level = m > _level ? m : Math.Max(m, _level - 0.035);
    }

    internal bool StartMic(Action<string> notify)
    {
        if (_pid == 0) return false;
        try
        {
            _owner.Engine.AddApp(_pid);
            _micActive = true;
            return true;
        }
        catch (Exception ex)
        {
            notify($"Impossible d'envoyer « {Name} » dans le micro : {ex.Message}");
            return false;
        }
    }

    private void RestartMicQuiet()
    {
        if (_pid == 0 || !_owner.Engine.IsRunning) return;
        try { _owner.Engine.AddApp(_pid); _micActive = true; } catch (Exception) { }
    }

    internal void RestartMic(Action<string> notify)
    {
        _micActive = false;
        if (_mic && _owner.Engine.IsRunning) StartMic(notify);
    }

    internal void StopMic()
    {
        if (_micActive) _owner.Engine.RemoveApp(_pid);
        _micActive = false;
    }
}
