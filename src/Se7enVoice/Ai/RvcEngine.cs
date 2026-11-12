using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Se7enVoice.Core;
using Se7enVoice.Core.Ai;

namespace Se7enVoice.Ai;

/// <summary>
/// Les trois réseaux de la voix IA (RVC), en ONNX sur la carte graphique (DirectML, toute marque) ou à défaut le
/// processeur : ContentVec (ce qui est dit), RMVPE (la hauteur) et le générateur (la nouvelle voix : les voix VCTK à
/// 48 kHz, ou une voix importée à 32, 40 ou 48 kHz, voir <see cref="RvcGenerator"/>).
/// Une fenêtre fixe de 1 s à 16 kHz donne 1 s de voix convertie (le générateur attend exactement 100 trames).
/// </summary>
public sealed class RvcEngine : IDisposable
{
    public const int Window = 16000, Frames = 100;
    private readonly InferenceSession _vec, _rmvpe;
    private readonly object _genLock = new();
    private RvcGenerator _gen;
    private bool _disposed; // lu et écrit sous _genLock
    private readonly MelSpectrogram _mel = new();
    private readonly float[] _melBuf = new float[MelSpectrogram.Mels * 128];
    private readonly float[] _mel100 = new float[MelSpectrogram.Mels * Frames];
    private readonly float[] _f0 = new float[Frames];
    private readonly float[] _pitchf = new float[Frames];
    private readonly long[] _coarse = new long[Frames];
    private readonly float[] _phone = new float[Frames * 768];
    private readonly float[] _rnd = new float[192 * Frames];
    private readonly Random _rng = new(7);

    private RvcEngine(InferenceSession vec, InferenceSession rmvpe, RvcGenerator gen, bool gpu)
    {
        _vec = vec;
        _rmvpe = rmvpe;
        _gen = gen;
        OnGpu = gpu;
    }

    /// <summary>« Carte graphique » ou « Processeur ».</summary>
    public string Device => OnGpu ? "Carte graphique" : "Processeur";
    public bool OnGpu { get; }
    /// <summary>Pourquoi le moteur tourne sur le processeur alors que la carte graphique a été essayée (null sinon).</summary>
    public string? GpuFallbackReason { get; private set; }
    /// <summary>Fréquence de la voix produite (32, 40 ou 48 kHz selon le générateur).</summary>
    public int OutRate => _gen.Rate;
    /// <summary>Voix chargée : null = voix VCTK fournies, sinon le fichier importé.</summary>
    public string? GeneratorFile => _gen.File;

    /// <summary>
    /// Charge le générateur d'une voix (lent : 1 à 3 s). Tout le travail sur la carte graphique passe par le même verrou
    /// que la conversion : DirectML ne supporte pas deux calculs en même temps sur les mêmes réseaux (la carte peut
    /// se bloquer, erreur 887A0006).
    /// </summary>
    public RvcGenerator OpenGenerator(AiVoiceInfo voice)
    {
        lock (_genLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var g = RvcGenerator.Open(voice, OnGpu);
            try { Run(g, new float[Window], 0, 0, Span<float>.Empty, out _); } // chauffe
            catch { g.Dispose(); throw; }
            return g;
        }
    }

    /// <summary>La carte graphique a été réinitialisée (pilote planté, jeu, mise en veille) : tout est à recharger.</summary>
    public static bool IsDeviceLost(Exception ex) =>
        IsDeviceReset(ex) || ex.Message.Contains("dml_provider_factory", StringComparison.OrdinalIgnoreCase);

    /// <summary>Codes DXGI d'une carte bien présente mais retirée, bloquée ou réinitialisée : elle reviendra.</summary>
    private static bool IsDeviceReset(Exception ex) =>
        ex.Message.Contains("887A0005", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("887A0006", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("887A0007", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("887A0020", StringComparison.OrdinalIgnoreCase);

    /// <summary>Met en service <paramref name="g"/> (l'ancien est libéré).</summary>
    public void UseGenerator(RvcGenerator g)
    {
        RvcGenerator? unused;
        lock (_genLock)
        {
            if (_disposed) unused = g; // moteur déjà libéré : le nouveau générateur ne servira jamais
            else
            {
                unused = ReferenceEquals(_gen, g) ? null : _gen;
                _gen = g;
            }
        }
        unused?.Dispose(); // plus aucun calcul ne peut l'utiliser : ils passent tous par le verrou
    }

    /// <summary>Hauteur de la dernière fenêtre (Hz, 0 = pas de voix), pour la hauteur automatique.</summary>
    public ReadOnlySpan<float> LastF0 => _f0;

    /// <summary>
    /// Charge les réseaux sur la carte graphique, ou à défaut sur le processeur (<see cref="GpuFallbackReason"/>).
    /// Une carte présente mais réinitialisée (pilote planté, jeu, mise en veille : codes 887A…) n'entraîne pas de
    /// repli : l'erreur remonte et l'appelant réessaie sur la carte, au lieu de rester sur le processeur (lent) jusqu'au
    /// redémarrage de l'appli.
    /// </summary>
    public static RvcEngine Load(string folder)
    {
        try
        {
            return Create(folder, gpu: true);
        }
        catch (Exception ex) when (!IsDeviceReset(ex))
        {
            // pas de DirectML utilisable (vieille carte, pilote, pas de carte compatible) : le processeur, plus lent
            Log.Error("Voix IA : carte graphique inutilisable, repli sur le processeur", ex);
            var engine = Create(folder, gpu: false);
            engine.GpuFallbackReason = ex.Message;
            return engine;
        }
    }

    private static RvcEngine Create(string folder, bool gpu)
    {
        InferenceSession? vec = null, rmvpe = null;
        RvcGenerator? gen = null;
        try
        {
            vec = new InferenceSession(Path.Combine(folder, AiModels.ContentVec), Options(gpu));
            rmvpe = new InferenceSession(Path.Combine(folder, AiModels.Rmvpe), Options(gpu));
            gen = RvcGenerator.Open(AiVoices.All[0], gpu);
        }
        catch
        {
            // les sessions déjà ouvertes (mémoire de la carte graphique) ne doivent pas fuir
            gen?.Dispose();
            rmvpe?.Dispose();
            vec?.Dispose();
            throw;
        }
        var engine = new RvcEngine(vec, rmvpe, gen, gpu);
        try { engine.Convert(new float[Window], 0, 0); } // chauffe : la première passe compile les noyaux
        catch { engine.Dispose(); throw; }
        return engine;
    }

    internal static SessionOptions Options(bool gpu)
    {
        var o = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
        };
        if (gpu)
        {
            o.EnableMemoryPattern = false; // exigé par DirectML
            o.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            o.AppendExecutionProvider_DML(0);
        }
        else
        {
            o.IntraOpNumThreads = Math.Max(2, Environment.ProcessorCount / 2);
        }
        return o;
    }

    /// <summary>Convertit la fenêtre de 1 s (16 kHz) avec la voix <paramref name="sid"/> décalée de <paramref name="semitones"/>. Renvoie 1 s à <see cref="OutRate"/>.</summary>
    public float[] Convert(float[] window16k, int sid, double semitones)
    {
        lock (_genLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Run(_gen, window16k, sid, semitones, Span<float>.Empty, out var result);
            return result!;
        }
    }

    /// <summary>
    /// Comme <see cref="Convert(float[], int, double)"/>, mais écrit la voix dans <paramref name="dest"/> sans rien allouer
    /// (le fil de la voix IA convertit 10 fois par seconde). Renvoie le nombre d'échantillons écrits.
    /// </summary>
    public int Convert(float[] window16k, int sid, double semitones, Span<float> dest)
    {
        if (dest.IsEmpty) throw new ArgumentException("Tampon de sortie vide.", nameof(dest));
        lock (_genLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return Run(_gen, window16k, sid, semitones, dest, out _);
        }
    }

    /// <summary>Données d'un tenseur de sortie, sans copie quand ONNX Runtime renvoie un tenseur dense (le cas normal).</summary>
    private static ReadOnlySpan<float> Data(Tensor<float> t) => t is DenseTensor<float> d ? d.Buffer.Span : t.ToArray();

    /// <summary>
    /// Calcule une fenêtre et copie la voix dans <paramref name="dest"/> ; <paramref name="dest"/> vide : la voix est
    /// renvoyée dans un nouveau tableau (<paramref name="array"/>).
    /// </summary>
    private int Run(RvcGenerator gen, float[] window16k, int sid, double semitones, Span<float> dest, out float[]? array)
    {
        array = null;
        // 1) ce qui est dit : 50 trames/s de 768, doublées à 100 trames/s
        using (var vecOut = _vec.Run([NamedOnnxValue.CreateFromTensor("source", new DenseTensor<float>(window16k, [1, Window]))]))
        {
            var embed = vecOut.First().AsTensor<float>();
            int n = embed.Dimensions[1];
            var data = Data(embed); // [1, n, 768], trame après trame
            for (int t = 0; t < Frames; t++)
            {
                int src = Math.Min(n - 1, t / 2);
                data.Slice(src * 768, 768).CopyTo(_phone.AsSpan(t * 768, 768));
            }
        }

        // 2) la hauteur : spectre mel → RMVPE → f0 (complété à un multiple de 32 trames par des zéros, comme RVC)
        Array.Clear(_melBuf);
        _mel.Compute(window16k, Frames, _mel100);
        for (int m = 0; m < MelSpectrogram.Mels; m++) Array.Copy(_mel100, m * Frames, _melBuf, m * 128, Frames);
        using (var rOut = _rmvpe.Run([NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(_melBuf, [1, MelSpectrogram.Mels, 128]))]))
        {
            var sal = Data(rOut.First().AsTensor<float>());
            Pitch.DecodeRmvpe(sal[..(Frames * Pitch.Bins)], Frames, _f0);
        }
        double ratio = Math.Pow(2, semitones / 12);
        for (int t = 0; t < Frames; t++)
        {
            _pitchf[t] = (float)(_f0[t] * ratio);
            _coarse[t] = Pitch.Coarse(_pitchf[t]);
        }

        // 3) la nouvelle voix
        for (int i = 0; i < _rnd.Length; i++)
        {
            double u1 = 1 - _rng.NextDouble(), u2 = _rng.NextDouble();
            _rnd[i] = (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("phone", new DenseTensor<float>(_phone, [1, Frames, 768])),
            NamedOnnxValue.CreateFromTensor("phone_lengths", new DenseTensor<long>(new long[] { Frames }, [1])),
            NamedOnnxValue.CreateFromTensor("pitch", new DenseTensor<long>(_coarse, [1, Frames])),
            NamedOnnxValue.CreateFromTensor("pitchf", new DenseTensor<float>(_pitchf, [1, Frames])),
            NamedOnnxValue.CreateFromTensor("sid", new DenseTensor<long>(new long[] { sid }, [1])),
            NamedOnnxValue.CreateFromTensor("rnd", new DenseTensor<float>(_rnd, [1, 192, Frames])),
        };
        using var gOut = gen.Session.Run(inputs);
        var audio = Data(gOut.First().AsTensor<float>());
        if (dest.IsEmpty)
        {
            array = audio.ToArray(); // copie seulement hors du fil temps réel (chauffe, outils)
            return array.Length;
        }
        int count = Math.Min(audio.Length, dest.Length);
        audio[..count].CopyTo(dest);
        return count;
    }

    /// <summary>
    /// Libère les trois réseaux sous le même verrou que les calculs : jamais pendant un <c>Run</c> (une session ONNX
    /// détruite en plein calcul fait tomber l'appli). Attend donc la fin du calcul en cours.
    /// </summary>
    public void Dispose()
    {
        lock (_genLock)
        {
            if (_disposed) return;
            _disposed = true;
            _vec.Dispose();
            _rmvpe.Dispose();
            _gen.Dispose();
        }
    }
}
