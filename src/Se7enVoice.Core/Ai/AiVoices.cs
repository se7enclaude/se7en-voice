namespace Se7enVoice.Core.Ai;

/// <summary>Une voix IA : numéro dans le modèle, nom, description, hauteur habituelle (pour la hauteur automatique).</summary>
public sealed record AiVoiceInfo(string Id, int Sid, string Name, string Description, double TargetHz, string Tone)
{
    /// <summary>Voix importée : fichier .pth (copié dans le dossier des voix perso) et sa fréquence. null = voix VCTK fournie.</summary>
    public string? File { get; init; }
    public int Rate { get; init; } = 48000;
    public bool IsCustom => File != null;
}

/// <summary>Voix importée par l'utilisateur (gardée dans les réglages).</summary>
public sealed class CustomAiVoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public int Rate { get; set; } = 40000;
    /// <summary>Hauteur habituelle de la voix (grave ≈ 115 Hz, aiguë ≈ 210 Hz), pour la hauteur automatique.</summary>
    public double TargetHz { get; set; } = 115;

    public AiVoiceInfo ToInfo() => new(Id, 0, Name, TargetHz < 160 ? "Perso · grave" : "Perso · aiguë", TargetHz, "neon")
    {
        File = File,
        Rate = Rate,
    };
}

/// <summary>
/// Les voix IA fournies : choisies parmi les 109 voix du corpus VCTK (CSTR, Université d'Édimbourg, CC BY 4.0)
/// apprises par le modèle de base de RVC, pour leurs timbres bien différents. Le nom est le nôtre (les locuteurs
/// de VCTK sont anonymes).
/// </summary>
public static class AiVoices
{
    public const string Credits = "Voix issues du corpus VCTK (CSTR, Université d'Édimbourg, licence CC BY 4.0), moteur RVC (licence MIT).";

    public static readonly AiVoiceInfo[] All =
    [
        new("vctk-47", 47, "Titan", "Très grave, posée", 105, "blaze"),
        new("vctk-106", 106, "Roc", "Grave et ronde", 110, "surge"),
        new("vctk-39", 39, "Baryton", "Grave, chaleureuse", 115, "volt"),
        new("vctk-101", 101, "Narrateur", "Grave, claire", 118, "neon"),
        new("vctk-28", 28, "Brume", "Mi-grave, douce", 120, "boost"),
        new("vctk-8", 8, "Écho", "Mi-grave, nette", 122, "surge"),
        new("vctk-30", 30, "Aube", "Claire, naturelle", 200, "neon"),
        new("vctk-107", 107, "Plume", "Claire, légère", 205, "boost"),
        new("vctk-79", 79, "Soie", "Claire, douce", 210, "volt"),
        new("vctk-86", 86, "Cristal", "Claire, brillante", 215, "blaze"),
        new("vctk-94", 94, "Nova", "Claire, énergique", 220, "surge"),
        new("vctk-75", 75, "Lumière", "Très claire, aiguë", 230, "neon"),
    ];

    public static AiVoiceInfo? Find(string? id) => All.FirstOrDefault(v => v.Id == id);
}
