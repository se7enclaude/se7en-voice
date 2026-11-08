using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Microsoft.Win32;
using Se7enVoice.Ai;
using Se7enVoice.Core.Updates;

namespace Se7enVoice.Audio;

/// <summary>
/// Seven Microphone est-il à jour ? Compare la version du pilote VB-CABLE installé (registre de Windows) à celle du
/// dernier pack publié sur vb-audio.com (lue dans son fichier .inf). Le pack (≈ 1,3 Mo) n'est relu que quand son
/// numéro change ; la vérification a lieu au plus toutes les 12 h.
/// </summary>
public static class CableUpdates
{
    private const string Page = "https://vb-audio.com/Cable/";
    private const string PackUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack{0}.zip";
    private const string AudioClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e96c-e325-11ce-bfc1-08002be10318}";

    /// <summary>Version du pilote « VB-Audio Virtual Cable » installé (ex. « 3.3.1.7 »), ou null.</summary>
    public static string? InstalledDriverVersion()
    {
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(AudioClass);
            if (cls == null) return null;
            string? best = null;
            foreach (var name in cls.GetSubKeyNames())
            {
                try
                {
                    using var k = cls.OpenSubKey(name);
                    if (k?.GetValue("DriverDesc") is string desc && desc.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase)
                        && k.GetValue("DriverVersion") is string ver
                        && (best == null || ComponentStatus.CompareVersions(ver, best) > 0))
                        best = ver;
                }
                catch (Exception) { /* sous-clé protégée */ }
            }
            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Met à jour la dernière version connue (réseau). Ne lève jamais : hors-ligne, le dernier résultat reste.</summary>
    public static async Task CheckAsync(bool force = false, CancellationToken ct = default)
    {
        var state = ComponentsStore.Current;
        if (!force && !ComponentStatus.Due(state.CableCheckedAt, DateTime.UtcNow)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("se7en-voice");
            var pack = ComponentStatus.LatestPack(await http.GetStringAsync(Page, ct));
            if (pack == null) return;
            string? driver = state.CableLatestDriver;
            if (pack != state.CableLatestPack || driver == null)
                driver = await DriverVersionOfPackAsync(http, pack.Value, ct);
            ComponentsStore.Update(c =>
            {
                c.CableLatestPack = pack;
                if (driver != null) c.CableLatestDriver = driver;
                c.CableCheckedAt = DateTime.UtcNow;
            });
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* hors-ligne ou site changé : on garde l'état connu */ }
    }

    /// <summary>Lit la version du pilote Windows 10/11 dans le pack (le plus élevé des .inf « win10 »).</summary>
    private static async Task<string?> DriverVersionOfPackAsync(HttpClient http, int pack, CancellationToken ct)
    {
        var bytes = await http.GetByteArrayAsync(string.Format(PackUrl, pack), ct);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        string? best = null;
        foreach (var e in zip.Entries.Where(e => e.Name.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)
                                                 && e.Name.Contains("win10", StringComparison.OrdinalIgnoreCase)))
        {
            using var r = new StreamReader(e.Open());
            var v = ComponentStatus.InfDriverVersion(await r.ReadToEndAsync(ct));
            if (v != null && (best == null || ComponentStatus.CompareVersions(v, best) > 0)) best = v;
        }
        return best;
    }
}
