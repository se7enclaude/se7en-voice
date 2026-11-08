using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Se7enVoice.Core.Updates;

/// <summary>État d'un composant téléchargé : absent, à jour, mise à jour disponible.</summary>
public enum ComponentState { Missing, UpToDate, UpdateAvailable, Unknown }

/// <summary>Un fichier de modèle vérifié : empreinte, taille et date de la vérification.</summary>
public sealed record VerifiedFile(string Sha256, long Size, DateTime VerifiedAt);

/// <summary>
/// Ce que se7en Voice sait de ses composants, gardé dans %LocalAppData%\Se7enVoice\composants.json :
/// les modèles de la voix IA vérifiés (empreinte SHA-256 + date) et la dernière version de VB-CABLE connue.
/// </summary>
public sealed class ComponentsFile
{
    public Dictionary<string, VerifiedFile> AiFiles { get; set; } = new();
    public int? CableLatestPack { get; set; }
    public string? CableLatestDriver { get; set; }
    public DateTime? CableCheckedAt { get; set; }
    public int? CableNotifiedPack { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static ComponentsFile Load(string path)
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<ComponentsFile>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception) { /* fichier abîmé : on repart de zéro, rien n'est perdu (tout se revérifie) */ }
        return new();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, true);
    }
}

public static class ComponentStatus
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(12);
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>« 9 nov. 2026 ».</summary>
    public static string Day(DateTime when) => when.ToLocalTime().ToString("d MMM yyyy", Fr);

    /// <summary>Compare deux versions « 3.3.1.7 » / « 3.5.2.0 » chiffre par chiffre.</summary>
    public static int CompareVersions(string? a, string? b)
    {
        var x = Numbers(a);
        var y = Numbers(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int c = (i < x.Length ? x[i] : 0).CompareTo(i < y.Length ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }

    private static int[] Numbers(string? v) =>
        Regex.Matches(v ?? "", @"\d+").Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToArray();

    public static bool Due(DateTime? checkedAt, DateTime now) => checkedAt is null || now - checkedAt.Value >= CheckEvery;

    // ------------------------------------------------------------------ VB-CABLE (Seven Microphone)

    /// <summary>Le numéro du pack le plus récent listé sur vb-audio.com/Cable (« VBCABLE_Driver_Pack46.zip » → 46).</summary>
    public static int? LatestPack(string html)
    {
        var packs = Regex.Matches(html ?? "", @"VBCABLE_Driver_Pack(\d+)\.zip").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        return packs.Count > 0 ? packs.Max() : null;
    }

    /// <summary>La version du pilote dans un fichier .inf (« DriverVer = 10/31/2026,3.5.2.0 » → « 3.5.2.0 »).</summary>
    public static string? InfDriverVersion(string inf)
    {
        var m = Regex.Match(inf ?? "", @"^\s*DriverVer\s*=\s*[^,\r\n]*,\s*([\d.]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static ComponentState CableState(bool installed, string? installedDriver, string? latestDriver)
    {
        if (!installed) return ComponentState.Missing;
        if (string.IsNullOrEmpty(installedDriver) || string.IsNullOrEmpty(latestDriver)) return ComponentState.Unknown;
        return CompareVersions(installedDriver, latestDriver) >= 0 ? ComponentState.UpToDate : ComponentState.UpdateAvailable;
    }

    /// <summary>La ligne affichée sous Seven Microphone.</summary>
    public static string CableText(bool installed, string? installedDriver, ComponentsFile f) =>
        CableState(installed, installedDriver, f.CableLatestDriver) switch
        {
            ComponentState.Missing => "Pas installé",
            ComponentState.UpToDate => $"À jour (pilote {installedDriver}, Pack {f.CableLatestPack}, vérifié le {Day(f.CableCheckedAt ?? DateTime.Now)})",
            ComponentState.UpdateAvailable => $"Mise à jour disponible : pilote {installedDriver} → {f.CableLatestDriver} (Pack {f.CableLatestPack})",
            _ when f.CableCheckedAt is null => $"Installé (pilote {installedDriver ?? "?"}), dernière version pas encore vérifiée",
            _ => $"Installé (pilote {installedDriver ?? "?"})",
        };

    // ------------------------------------------------------------------ modèles de la voix IA

    /// <summary>
    /// Les modèles attendus par cette version de se7en Voice (nom, taille, empreinte) comparés à ceux vérifiés sur le disque.
    /// <paramref name="onDisk"/> donne la taille du fichier présent (null s'il manque).
    /// </summary>
    public static ComponentState AiState(IEnumerable<(string Name, long Size, string Sha256)> expected,
                                         IReadOnlyDictionary<string, VerifiedFile> verified, Func<string, long?> onDisk)
    {
        bool anyPresent = false, allGood = true, unverified = false;
        foreach (var f in expected)
        {
            long? size = onDisk(f.Name);
            if (size is null) { allGood = false; continue; }
            anyPresent = true;
            if (verified.TryGetValue(f.Name, out var v))
            {
                if (!string.Equals(v.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase) || v.Size != size) allGood = false;
            }
            else if (size == f.Size) unverified = true;   // installé avant le suivi : vérifié en arrière-plan
            else allGood = false;
        }
        if (!anyPresent) return ComponentState.Missing;
        if (!allGood) return ComponentState.UpdateAvailable;
        return unverified ? ComponentState.Unknown : ComponentState.UpToDate;
    }

    public static string AiText(ComponentState state, string release, IReadOnlyDictionary<string, VerifiedFile> verified) => state switch
    {
        ComponentState.UpToDate => $"Modèles à jour ({release}, vérifiés le {Day(verified.Values.Max(v => v.VerifiedAt))})",
        ComponentState.UpdateAvailable => "Mise à jour disponible : nouveaux modèles de voix IA à télécharger",
        ComponentState.Unknown => "Modèles installés · vérification de leur empreinte…",
        _ => "",
    };
}
