using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
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
    private readonly MelSpectrogram _mel = new();
    private readonly float[] _melBuf = new float[MelSpectrogram.Mels * 128];
    private readonly float[] _f0 = new float[Frames];
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
            var g = RvcGenerator.Open(voice, OnGpu);
            try { Run(g, new float[Window], 0); } // chauffe
            catch { g.Dispose(); throw; }
            return g;
        }
    }

    /// <summary>La carte graphique a été réinitialisée (pilote planté, jeu, mise en veille) : tout est à recharger.</summary>
    public static bool IsDeviceLost(Exception ex) =>
        ex.Message.Contains("887A0005", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("887A0006", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("887A0007", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("887A0020", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("dml_provider_factory", StringComparison.OrdinalIgnoreCase);

    /// <summary>Met en service <paramref name="g"/> (l'ancien est libéré).</summary>
    public void UseGenerator(RvcGenerator g)
    {
        RvcGenerator old;
        lock (_genLock) { old = _gen; _gen = g; }
        if (!ReferenceEquals(old, g)) old.Dispose();
    }

    /// <summary>Hauteur de la dernière fenêtre (Hz, 0 = pas de voix), pour la hauteur automatique.</summary>
    public ReadOnlySpan<float> LastF0 => _f0;

    public static RvcEngine Load(string folder)
    {
        try
        {
            return Create(folder, gpu: true);
        }
        catch (Exception)
        {
            return Create(folder, gpu: false); // pas de DirectML (vieille carte, pilote) : le processeur, plus lent
        }
    }

    private static RvcEngine Create(string folder, bool gpu)
    {
        InferenceSession Open(string file) => new(Path.Combine(folder, file), Options(gpu));
        var engine = new RvcEngine(Open(AiModels.ContentVec), Open(AiModels.Rmvpe), RvcGenerator.Open(AiVoices.All[0], gpu), gpu);
        engine.Convert(new float[Window], 0, 0); // chauffe : la première passe compile les noyaux
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
        lock (_genLock) return Run(_gen, window16k, sid, semitones);
    }

    private float[] Run(RvcGenerator gen, float[] window16k, int sid, double semitones = 0)
    {
        // 1) ce qui est dit : 50 trames/s de 768, doublées à 100 trames/s
        using (var vecOut = _vec.Run([NamedOnnxValue.CreateFromTensor("source", new DenseTensor<float>(window16k, [1, Window]))]))
        {
            var embed = vecOut.First().AsTensor<float>();
            int n = embed.Dimensions[1];
            for (int t = 0; t < Frames; t++)
            {
                int src = Math.Min(n - 1, t / 2);
                for (int k = 0; k < 768; k++) _phone[t * 768 + k] = embed[0, src, k];
            }
        }

        // 2) la hauteur : spectre mel → RMVPE → f0 (complété à un multiple de 32 trames par des zéros, comme RVC)
        Array.Clear(_melBuf);
        var mel = new float[MelSpectrogram.Mels * Frames];
        _mel.Compute(window16k, Frames, mel);
        for (int m = 0; m < MelSpectrogram.Mels; m++) Array.Copy(mel, m * Frames, _melBuf, m * 128, Frames);
        using (var rOut = _rmvpe.Run([NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(_melBuf, [1, MelSpectrogram.Mels, 128]))]))
        {
            var sal = rOut.First().AsTensor<float>().ToArray();
            Pitch.DecodeRmvpe(sal.AsSpan(0, Frames * Pitch.Bins), Frames, _f0);
        }
        double ratio = Math.Pow(2, semitones / 12);
        var pitchf = new float[Frames];
        for (int t = 0; t < Frames; t++)
        {
            pitchf[t] = (float)(_f0[t] * ratio);
            _coarse[t] = Pitch.Coarse(pitchf[t]);
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
            NamedOnnxValue.CreateFromTensor("pitchf", new DenseTensor<float>(pitchf, [1, Frames])),
            NamedOnnxValue.CreateFromTensor("sid", new DenseTensor<long>(new long[] { sid }, [1])),
            NamedOnnxValue.CreateFromTensor("rnd", new DenseTensor<float>(_rnd, [1, 192, Frames])),
        };
        using var gOut = gen.Session.Run(inputs);
        return gOut.First().AsTensor<float>().ToArray();
    }

    public void Dispose()
    {
        _vec.Dispose();
        _rmvpe.Dispose();
        lock (_genLock) _gen.Dispose();
    }
}
