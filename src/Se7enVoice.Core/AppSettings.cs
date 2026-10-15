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
    /// <summary>Seven Microphone installé, en attente d'un redémarrage du PC pour apparaître.</summary>
    public bool CableRebootPending { get; set; }
    /// <summary>La proposition d'installer Seven Microphone au premier lancement a déjà été montrée.</summary>
    public bool CableSetupOffered { get; set; }

    // Studio
    public List<string> Favorites { get; set; } = [];
    public bool PushToTalkOn { get; set; }
    public int PushToTalkKey { get; set; } = 0x56; // V
    public string? RecordingsFolder { get; set; }

    // Table de mixage
    /// <summary>Volume (0..1) des tranches que l'utilisateur a réglées. Une tranche absente n'est jamais touchée.</summary>
    public Dictionary<string, double> CategoryVolumes { get; set; } = [];
    public Dictionary<string, bool> CategoryMutes { get; set; } = [];
    /// <summary>Volume propre de chaque appli (0..1), multiplié par celui de sa tranche.</summary>
    public Dictionary<string, double> AppVolumes { get; set; } = [];
    public Dictionary<string, bool> AppMutes { get; set; } = [];
    /// <summary>Tranche choisie à la main pour une appli (nom du programme → tranche).</summary>
    public Dictionary<string, string> AppCategoryOverrides { get; set; } = [];
    /// <summary>Applis dont le son est envoyé dans le micro virtuel (noms de programme).</summary>
    public List<string> AppsToMic { get; set; } = [];
    public double AppsToMicDb { get; set; } = -6;
    public bool DuckOnMyVoice { get; set; }
    public bool DuckOnFriends { get; set; }
    public double DuckAmount { get; set; } = 0.6;

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
