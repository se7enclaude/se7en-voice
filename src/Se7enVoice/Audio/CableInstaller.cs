using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;

namespace Se7enVoice.Audio;

/// <summary>
/// Installe « Seven Microphone » : le pilote gratuit VB-CABLE, téléchargé sur le site officiel de
/// VB-Audio, puis renommé. La licence de VB-CABLE interdit de l'intégrer à notre propre procédure
/// d'installation : on lance donc son installateur tel quel (l'utilisateur clique « Install Driver »
/// et accepte la licence de VB-Audio), et on ne fait que le télécharger, le vérifier et renommer.
/// Une seule demande Windows : l'app se relance en admin avec <see cref="Argument"/>, ce processus
/// lance l'installateur (droits hérités) puis renomme les deux entrées.
/// </summary>
public static class CableInstaller
{
    public const string Argument = "--install-cable";
    private const string Page = "https://vb-audio.com/Cable/";
    private const string Fallback = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip";

    /// <summary>Codes de sortie du processus admin.</summary>
    public const int Done = 0, Failed = 1, RebootNeeded = 3;

    private static string WorkDir => Path.Combine(Path.GetTempPath(), "se7en-voice-cable");

    /// <summary>Télécharge le dernier pack officiel, le décompresse et vérifie la signature de l'installateur. Retourne son chemin.</summary>
    public static async Task<string> DownloadAsync(IProgress<double> progress, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("se7en-voice");

        string url = Fallback;
        try
        {
            // Le site liste « VBCABLE_Driver_PackNN.zip » : on prend le numéro le plus récent.
            var html = await http.GetStringAsync(Page, ct);
            var best = Regex.Matches(html, @"https://download\.vb-audio\.com/Download_CABLE/VBCABLE_Driver_Pack(\d+)\.zip")
                .OrderByDescending(m => int.Parse(m.Groups[1].Value)).FirstOrDefault();
            if (best != null) url = best.Value;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { /* site indisponible : version connue */ }

        if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true);
        Directory.CreateDirectory(WorkDir);
        var zip = Path.Combine(WorkDir, "VBCABLE_Driver_Pack.zip");

        using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 1_400_000;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                progress.Report(Math.Min(1, (double)done / total));
            }
        }

        var dir = Path.Combine(WorkDir, "pack");
        ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
        var setup = Path.Combine(dir, Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe");
        if (!File.Exists(setup)) throw new InvalidOperationException("le pack téléchargé ne contient pas l'installateur.");
        if (!IsSignedByVbAudio(setup)) throw new InvalidOperationException("l'installateur téléchargé n'est pas signé par VB-Audio, installation annulée.");
        return setup;
    }

    /// <summary>Signature Authenticode valide (chaîne de confiance Windows) au nom de l'auteur de VB-CABLE.</summary>
    private static bool IsSignedByVbAudio(string file)
    {
        try
        {
#pragma warning disable SYSLIB0057 // lecture du certificat Authenticode d'un .exe (pas d'équivalent X509CertificateLoader)
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(file));
#pragma warning restore SYSLIB0057
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid; // certificat expiré mais signature horodatée
            string subject = cert.Subject;
            return chain.Build(cert)
                && (subject.Contains("Burel", StringComparison.OrdinalIgnoreCase) || subject.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Relance l'app en admin pour installer puis renommer. Retourne le code de sortie, ou null si l'utilisateur refuse.</summary>
    public static async Task<int?> RequestElevatedInstallAsync(string setup)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{Argument} \"{setup}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            if (p == null) return Failed;
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
        catch (Win32Exception)
        {
            return null; // « Non » sur la demande Windows.
        }
    }

    /// <summary>Dans le processus admin : lance l'installateur de VB-Audio, attend le pilote, renomme.</summary>
    public static int RunElevated(string setup)
    {
        try
        {
            if (!File.Exists(setup) || !IsSignedByVbAudio(setup)) return Failed;
            using (var p = Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(setup)! }))
                p?.WaitForExit();

            // Le pilote apparaît en général tout de suite ; sinon il faut redémarrer le PC.
            var until = DateTime.UtcNow.AddSeconds(25);
            while (DateTime.UtcNow < until)
            {
                if (CableEndpointsPresent()) return CableRenamer.RunElevated() == 0 ? Done : RebootNeeded;
                Thread.Sleep(1000);
            }
            return RebootNeeded;
        }
        catch (Exception)
        {
            return Failed;
        }
    }

    private static bool CableEndpointsPresent()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Any(DeviceService.IsCable)
            && enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Any(DeviceService.IsCable);
    }
}
