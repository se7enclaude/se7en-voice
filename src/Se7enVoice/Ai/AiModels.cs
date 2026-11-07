using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Se7enVoice.Ai;

/// <summary>
/// Les modèles de la voix IA (≈ 485 Mo), téléchargés une fois au premier usage depuis la release « voix-ia-1 » du
/// dépôt de se7en Voice, vérifiés par empreinte SHA-256, rangés dans %LocalAppData%\Se7enVoice\voix-ia.
/// <list type="bullet">
/// <item>ContentVec (licence MIT, poids d'Applio) converti en ONNX demi-précision ;</item>
/// <item>RMVPE (projet RVC, licence MIT) en demi-précision ;</item>
/// <item>générateur RVC v2 48 kHz de base (licence MIT), entraîné sur le corpus VCTK (CSTR, Université d'Édimbourg,
/// licence CC BY 4.0) : ses 109 voix.</item>
/// </list>
/// </summary>
public static class AiModels
{
    public const string ContentVec = "contentvec768_fp16.onnx", Rmvpe = "rmvpe_fp16.onnx", Generator = "rvc_vctk48k.onnx";
    private const string BaseUrl = "https://github.com/se7enclaude/se7en-voice/releases/download/voix-ia-1/";

    public static readonly (string Name, long Size, string Sha256)[] Files =
    [
        (ContentVec, 189009250, "e00bb55c21de38561d4f015bc475ec0b924e50b66b627031cfdba65c6a241efc"),
        (Rmvpe, 180919414, "a6fd69aca6cd3b6d7002bc77785cace48730898945b45d7f5c771c3a056ca89a"),
        (Generator, 115303880, "a0f3f3948fb77313989a9ea3ad4e47211b7656f0ee7b3b143ed971b4cd3e891f"),
    ];

    public static long TotalSize => Files.Sum(f => f.Size);

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Se7enVoice", "voix-ia");

    /// <summary>Les trois fichiers sont là avec la bonne taille (l'empreinte est vérifiée au téléchargement).</summary>
    public static bool Ready => Files.All(f => new FileInfo(Path.Combine(Folder, f.Name)) is { Exists: true } fi && fi.Length == f.Size);

    /// <summary>Télécharge ce qui manque. <paramref name="progress"/> reçoit 0..1. Lève une exception en cas d'échec.</summary>
    public static async Task DownloadAsync(IProgress<double> progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(Folder);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("se7en-Voice");
        long done = Files.Where(f => IsComplete(f)).Sum(f => f.Size);
        foreach (var f in Files)
        {
            if (IsComplete(f)) continue;
            string path = Path.Combine(Folder, f.Name), tmp = path + ".part";
            using (var resp = await http.GetAsync(BaseUrl + f.Name, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(cancel);
                await using var dst = File.Create(tmp);
                var buf = new byte[1 << 20];
                int n;
                while ((n = await src.ReadAsync(buf, cancel)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), cancel);
                    done += n;
                    progress.Report((double)done / TotalSize);
                }
            }
            string hash;
            await using (var check = File.OpenRead(tmp))
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check, cancel));
            if (hash != f.Sha256)
            {
                File.Delete(tmp);
                throw new IOException($"Fichier abîmé pendant le téléchargement ({f.Name}). Réessayez.");
            }
            File.Move(tmp, path, true);
        }
        progress.Report(1);
    }

    private static bool IsComplete((string Name, long Size, string Sha256) f) =>
        new FileInfo(Path.Combine(Folder, f.Name)) is { Exists: true } fi && fi.Length == f.Size;
}
