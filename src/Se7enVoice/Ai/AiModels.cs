using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Se7enVoice.Core.Updates;

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
    public const string Release = "voix-ia-1";
    private const string BaseUrl = "https://github.com/se7enclaude/se7en-voice/releases/download/" + Release + "/";

    public static readonly (string Name, long Size, string Sha256)[] Files =
    [
        (ContentVec, 189009250, "e00bb55c21de38561d4f015bc475ec0b924e50b66b627031cfdba65c6a241efc"),
        (Rmvpe, 180919414, "a6fd69aca6cd3b6d7002bc77785cace48730898945b45d7f5c771c3a056ca89a"),
        (Generator, 115303880, "a0f3f3948fb77313989a9ea3ad4e47211b7656f0ee7b3b143ed971b4cd3e891f"),
    ];

    /// <summary>Squelettes des voix importées à 40 et 32 kHz (le 48 kHz est le générateur des voix VCTK) : téléchargés
    /// seulement à l'import d'une voix de cette fréquence.</summary>
    public static readonly (string Name, long Size, string Sha256)[] Skeletons =
    [
        ("rvc_vctk40k.onnx", 110584968, "33acc0f5b327f7e63d3d97d0d29c578e74e67dd88c6e01df10f9ca9d52260e72"),
        ("rvc_vctk32k.onnx", 112665416, "1da4e7cfff2fc07512719828fdf07c97ed13481fc029f37caba11f80135d0052"),
    ];

    public static string SkeletonFor(int rate) => rate switch
    {
        48000 => Generator,
        40000 => Skeletons[0].Name,
        32000 => Skeletons[1].Name,
        _ => throw new ArgumentOutOfRangeException(nameof(rate)),
    };

    public static bool SkeletonReady(int rate) => rate == 48000 ? Ready : IsComplete(Skeletons[rate == 40000 ? 0 : 1]);

    /// <summary>Télécharge le squelette d'une fréquence (≈ 110 Mo) s'il manque.</summary>
    public static Task DownloadSkeletonAsync(int rate, IProgress<double> progress, CancellationToken cancel) =>
        rate == 48000 ? DownloadAsync(progress, cancel) : DownloadFilesAsync([Skeletons[rate == 40000 ? 0 : 1]], progress, cancel);

    public static long TotalSize => Files.Sum(f => f.Size);

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Se7enVoice", "voix-ia");

    /// <summary>Les trois fichiers sont là avec la bonne taille et, quand elle est connue, la bonne empreinte.</summary>
    public static bool Ready => Files.All(f => new FileInfo(Path.Combine(Folder, f.Name)) is { Exists: true } fi && fi.Length == f.Size);

    /// <summary>Télécharge ce qui manque. <paramref name="progress"/> reçoit 0..1. Lève une exception en cas d'échec.</summary>
    public static Task DownloadAsync(IProgress<double> progress, CancellationToken cancel) => DownloadFilesAsync(Files, progress, cancel);

    private static async Task DownloadFilesAsync((string Name, long Size, string Sha256)[] files, IProgress<double> progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(Folder);
        long total = files.Sum(f => f.Size);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("se7en-Voice");
        long done = files.Where(f => IsComplete(f)).Sum(f => f.Size);
        foreach (var f in files)
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
                    progress.Report((double)done / total);
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
            ComponentsStore.Update(c => c.AiFiles[f.Name] = new VerifiedFile(f.Sha256, f.Size, DateTime.UtcNow));
        }
        progress.Report(1);
    }

    /// <summary>
    /// Présent avec la bonne taille, et pas remplacé depuis par d'autres modèles : si l'empreinte vérifiée ne correspond
    /// plus à celle attendue par cette version de se7en Voice, le fichier est à mettre à jour (et seulement lui).
    /// </summary>
    private static bool IsComplete((string Name, long Size, string Sha256) f)
    {
        if (new FileInfo(Path.Combine(Folder, f.Name)) is not { Exists: true } fi || fi.Length != f.Size) return false;
        var known = ComponentsStore.Current.AiFiles.GetValueOrDefault(f.Name);
        return known is null || string.Equals(known.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>État des modèles : à jour, mise à jour disponible, absents ou pas encore vérifiés.</summary>
    public static ComponentState State =>
        ComponentStatus.AiState(Files, ComponentsStore.Current.AiFiles,
            name => new FileInfo(Path.Combine(Folder, name)) is { Exists: true } fi ? fi.Length : null);

    /// <summary>« Modèles à jour (voix-ia-1, vérifiés le 9 nov. 2026) ».</summary>
    public static string StatusText => ComponentStatus.AiText(State, Release, ComponentsStore.Current.AiFiles);

    /// <summary>
    /// Modèles installés avant le suivi des empreintes : calcule leur SHA-256 une seule fois (en arrière-plan) et la
    /// retient. Un fichier abîmé est supprimé pour être retéléchargé proprement.
    /// </summary>
    public static async Task VerifyInstalledAsync(CancellationToken cancel = default)
    {
        foreach (var f in Files.Concat(Skeletons))
        {
            string path = Path.Combine(Folder, f.Name);
            if (ComponentsStore.Current.AiFiles.ContainsKey(f.Name)) continue;
            if (new FileInfo(path) is not { Exists: true } fi || fi.Length != f.Size) continue;
            string hash;
            await using (var s = File.OpenRead(path))
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(s, cancel));
            if (hash == f.Sha256)
                ComponentsStore.Update(c => c.AiFiles[f.Name] = new VerifiedFile(f.Sha256, f.Size, DateTime.UtcNow));
            else
                File.Delete(path);
        }
    }
}
