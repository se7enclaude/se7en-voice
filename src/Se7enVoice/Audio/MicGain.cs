using NAudio.CoreAudioApi;

namespace Se7enVoice.Audio;

/// <summary>Volume du micro dans Windows (Paramètres › Son › Entrée), en dB. Certains micros n'en ont pas (plage vide).</summary>
public static class MicGain
{
    /// <summary>(niveau, minimum, maximum) en dB ; min = max quand le micro n'a pas de volume réglable.</summary>
    public static (double Level, double Min, double Max) Read(MMDevice? mic)
    {
        try
        {
            if (mic == null) return (0, 0, 0);
            var v = mic.AudioEndpointVolume;
            var range = v.VolumeRange;
            return (v.MasterVolumeLevel, range.MinDecibels, range.MaxDecibels);
        }
        catch (Exception)
        {
            return (0, 0, 0);
        }
    }

    /// <summary>Règle le volume (dB, borné à la plage du micro). Faux si Windows refuse.</summary>
    public static bool Set(MMDevice? mic, double db)
    {
        try
        {
            if (mic == null) return false;
            var v = mic.AudioEndpointVolume;
            var range = v.VolumeRange;
            v.MasterVolumeLevel = (float)Math.Clamp(db, range.MinDecibels, range.MaxDecibels);
            if (v.Mute) v.Mute = false;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
