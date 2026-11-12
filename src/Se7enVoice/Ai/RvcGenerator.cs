using System.IO;
using Microsoft.ML.OnnxRuntime;
using Se7enVoice.Core.Ai;

namespace Se7enVoice.Ai;

/// <summary>
/// Le générateur d'une voix : les voix VCTK fournies (squelette 48 kHz et ses 109 voix), ou une voix importée — une
/// copie du squelette de sa fréquence (32, 40 ou 48 kHz) dont les poids ont été remplacés à l'import par ceux du fichier
/// .pth (<see cref="AiVoiceImporter"/>) : pas de conversion ni de Python chez l'utilisateur.
/// </summary>
public sealed class RvcGenerator : IDisposable
{
    private RvcGenerator(InferenceSession session, int rate, string? file)
    {
        Session = session;
        Rate = rate;
        File = file;
    }

    public InferenceSession Session { get; }
    public int Rate { get; }
    public string? File { get; }

    public static RvcGenerator Open(AiVoiceInfo voice, bool gpu)
    {
        var options = RvcEngine.Options(gpu);
        if (!voice.IsCustom)
            return new RvcGenerator(new InferenceSession(Path.Combine(AiModels.Folder, AiModels.Generator), options), 48000, null);
        if (!System.IO.File.Exists(voice.File))
            throw new FileNotFoundException("Le fichier de cette voix a disparu : importez-la de nouveau.");
        return new RvcGenerator(new InferenceSession(voice.File, options), voice.Rate, voice.File);
    }

    public void Dispose() => Session.Dispose();
}

/// <summary>Import d'une voix RVC v2 (.pth) : vérification, puis copie du squelette avec les poids de la voix.</summary>
public static class AiVoiceImporter
{
    public static string Folder => Path.Combine(AiModels.Folder, "perso");

    /// <summary>Lit et vérifie le fichier (lecture sûre : aucun code du fichier n'est exécuté). Lève une exception au message clair.</summary>
    public static RvcVoiceModel Inspect(string pth)
    {
        RvcVoiceModel m;
        try { m = RvcVoiceImport.Load(pth); }
        catch (InvalidDataException) { throw; }
        catch (Exception)
        {
            throw new InvalidDataException("Ce fichier n'est pas une voix RVC (.pth) lisible.");
        }
        if (RvcVoiceImport.Problem(m) is { } problem) throw new InvalidDataException(problem);
        return m;
    }

    /// <summary>Écrit le générateur de la voix (≈ 110 Mo) dans le dossier des voix perso ; le squelette doit être là.</summary>
    public static string Build(RvcVoiceModel m, string id)
    {
        // une seule copie du squelette en mémoire : les poids sont remplacés directement dedans
        var model = System.IO.File.ReadAllBytes(Path.Combine(AiModels.Folder, AiModels.SkeletonFor(m.SampleRate)));
        OnnxPatch.ApplyInPlace(model, m.Weights);
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, id + ".onnx"), tmp = path + ".part";
        System.IO.File.WriteAllBytes(tmp, model);
        System.IO.File.Move(tmp, path, true);
        return path;
    }
}
