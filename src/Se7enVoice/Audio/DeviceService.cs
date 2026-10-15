using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Se7enVoice.Audio;

public sealed record AudioDevice(string Id, string Name, bool IsCable)
{
    public override string ToString() => Name;
}

/// <summary>Liste les micros et sorties, repère VB-CABLE et prévient quand on (dé)branche quelque chose.</summary>
public sealed class DeviceService : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public DeviceService() => _enumerator.RegisterEndpointNotificationCallback(this);

    /// <summary>Déclenché (sur un thread COM) quand la liste des périphériques change.</summary>
    public event Action? Changed;

    /// <summary>
    /// VB-CABLE se reconnaît à son adaptateur « VB-Audio Virtual Cable ». On ne se fie pas au nom
    /// de l'entrée, car on propose justement de la renommer.
    /// </summary>
    public static bool IsCable(MMDevice d)
    {
        string adapter = SafeGet(() => d.DeviceFriendlyName);
        string name = SafeGet(() => d.FriendlyName);
        return adapter.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
            || name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("CABLE ", StringComparison.OrdinalIgnoreCase)
            // Une fois renommé par se7en Voice : « Seven Microphone (se7en Voice) »
            || adapter.Equals(CableRenamer.AdapterName, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(CableRenamer.MicName, StringComparison.OrdinalIgnoreCase);
    }

    public List<AudioDevice> Microphones() => List(DataFlow.Capture).Where(d => !d.IsCable).ToList();

    public List<AudioDevice> Speakers() => List(DataFlow.Render).Where(d => !d.IsCable).ToList();

    public AudioDevice? CableInput() => List(DataFlow.Render).FirstOrDefault(d => d.IsCable);

    public AudioDevice? CableOutput() => List(DataFlow.Capture).FirstOrDefault(d => d.IsCable);

    public string? DefaultMicrophoneId() => DefaultId(DataFlow.Capture);

    public string? DefaultSpeakerId() => DefaultId(DataFlow.Render);

    /// <summary>La sortie par défaut de Windows (multimédia) est le micro virtuel : le son du PC part vers Discord.</summary>
    public bool DefaultOutputIsCable()
    {
        try { return IsCable(_enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)); }
        catch (Exception) { return false; }
    }

    public MMDevice? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try
        {
            var d = _enumerator.GetDevice(id);
            return d.State == DeviceState.Active ? d : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<AudioDevice> List(DataFlow flow)
    {
        var result = new List<AudioDevice>();
        try
        {
            foreach (var d in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                result.Add(new AudioDevice(d.ID, SafeGet(() => d.FriendlyName), IsCable(d)));
        }
        catch (Exception)
        {
            // Service audio indisponible : liste vide.
        }
        return result;
    }

    private string? DefaultId(DataFlow flow)
    {
        try
        {
            // Le périphérique « par défaut » de Windows, puis celui « de communication ».
            var d = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            if (IsCable(d)) d = _enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
            return IsCable(d) ? null : d.ID;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SafeGet(Func<string> f)
    {
        try { return f() ?? ""; } catch (Exception) { return ""; }
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => Changed?.Invoke();
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => Changed?.Invoke();
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => Changed?.Invoke();
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch (Exception) { }
        _enumerator.Dispose();
    }
}
