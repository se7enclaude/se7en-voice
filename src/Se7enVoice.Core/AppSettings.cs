using System.Text.Json;
using Se7enVoice.Core.Dsp;
using Se7enVoice.Core.Presets;

namespace Se7enVoice.Core;

public sealed class SoundEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>Réglages mémorisés entre deux lancements.</summary>
public sealed class AppSettings
{
    public string? InputDeviceId { get; set; }
    public string? CableDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public bool MonitorOn { get; set; }
    public VoiceSettings Voice { get; set; } = new();
    public string CurrentPreset { get; set; } = "Normal";
    public bool MinimizeToTray { get; set; } = true;
    public double SoundboardVolumeDb { get; set; } = -6;
    public bool SoundsToMonitor { get; set; } = true;
    public List<SoundEntry> Sounds { get; set; } = [];
    public bool CableRenameOffered { get; set; }

    public static string Folder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Se7enVoice");

    private static string FilePath => System.IO.Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), PresetStore.Json) ?? new();
        }
        catch (Exception)
        {
            // Fichier abîmé : on repart des réglages par défaut.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, PresetStore.Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException)
        {
            // Pas grave : on réessaiera au prochain changement.
        }
    }
}
