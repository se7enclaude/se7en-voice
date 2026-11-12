using System.Diagnostics;
using Se7enVoice.Core;
using Se7enVoice.Core.Ai;
using Se7enVoice.Core.Audio;

namespace Se7enVoice.Ai;

/// <summary>
/// Voix IA en direct : le thread audio dépose la voix du micro (<see cref="Push"/>) et reprend la voix convertie
/// (<see cref="Pull"/>) ; un thread à part fait tourner les réseaux toutes les 100 ms sur la dernière seconde de voix.
/// Retard total ≈ bloc (100 ms) + marge de fin de fenêtre (40 ms) + calcul (~35 ms sur une carte récente) + tampon.
/// </summary>
public sealed class AiVoice : IDisposable
{
    public const int BlockMs = 100, MarginMs = 40, FadeMs = 30, SearchMs = 10;
    private const int Block16 = 16 * BlockMs;
    private readonly int _blockOutLen, _marginOut; // en échantillons de la voix produite (32, 40 ou 48 kHz)

    private readonly RvcEngine _engine;
    private readonly StreamResampler _toModel;            // micro → 16 kHz
    private readonly List<float> _resampled = [];
    private readonly List<float> _pending = [];           // 16 kHz en attente d'un bloc complet
    private readonly object _inLock = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly float[] _window = new float[RvcEngine.Window];
    private readonly SolaStitcher _sola;
    private readonly float[] _blockOut;
    private readonly float[] _audio;                       // voix convertie (1 s), réutilisée à chaque bloc
    private readonly ResamplingRing _out;                  // 48 kHz → fréquence du micro, avec tampon et dérive
    private readonly VoiceRange _range = new();
    private readonly Thread _thread;
    private volatile bool _stop;
    private double _semitones;
    private readonly object _endLock = new();
    private bool _ended;                                   // le fil est terminé (sous _endLock)
    private RvcEngine? _engineToDispose;                   // à libérer par le fil en se terminant (sous _endLock)

    public AiVoice(RvcEngine engine, int engineRate)
    {
        _engine = engine;
        _toModel = new StreamResampler(engineRate, 16000);
        // tampon de sortie : un bloc arrive toutes les 100 ms, il faut de quoi tenir jusqu'au suivant (+ marge)
        int k = engine.OutRate / 1000;
        _blockOutLen = k * BlockMs;
        _marginOut = k * MarginMs;
        _sola = new SolaStitcher(_blockOutLen, k * FadeMs, k * SearchMs);
        _blockOut = new float[_blockOutLen];
        _audio = new float[engine.OutRate * 2]; // le générateur rend 1 s : de la marge au cas où
        OutRate = engine.OutRate;
        EngineRate = engineRate;
        _out = new ResamplingRing(engine.OutRate, engineRate, targetLatencyMs: BlockMs + 25);
        _thread = new Thread(Loop) { IsBackground = true, Name = "voix-ia", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Numéro de la voix dans le modèle (0..108 pour les voix VCTK).</summary>
    public int Sid { get; set; }
    /// <summary>Hauteur habituelle de la voix choisie (Hz) : la hauteur automatique y amène la voix de l'utilisateur.</summary>
    public double TargetHz { get; set; } = 150;
    /// <summary>null = automatique, sinon décalage fixe en demi-tons.</summary>
    public double? ManualSemitones { get; set; }

    /// <summary>Décalage appliqué en ce moment (demi-tons).</summary>
    public double Semitones => _semitones;
    /// <summary>Temps de calcul du dernier bloc (ms) ; au-delà de <see cref="BlockMs"/>, la carte ne suit pas.</summary>
    public double ComputeMs { get; private set; }
    public double LatencyMs => BlockMs + MarginMs + ComputeMs + (BlockMs + 25) / 2.0;
    public string Device => _engine.Device;
    /// <summary>Le moteur utilisé (libéré avec la voix par <see cref="DisposeWithEngine"/>).</summary>
    public RvcEngine Engine => _engine;
    /// <summary>Fréquence de la voix produite au moment de la création (si le générateur change de fréquence, il faut recréer).</summary>
    public int OutRate { get; }
    /// <summary>Fréquence du micro pour laquelle cette conversion a été créée.</summary>
    public int EngineRate { get; }
    public bool TooSlow => ComputeMs > BlockMs * 0.9;
    private int _errors;
    /// <summary>Non null quand la conversion échoue en boucle (carte graphique réinitialisée…) : il faut recharger le moteur.</summary>
    public volatile Exception? Failure;

    /// <summary>Thread audio : voix du micro (mono, fréquence du micro).</summary>
    public void Push(ReadOnlySpan<float> mic)
    {
        lock (_inLock)
        {
            _resampled.Clear();
            _toModel.Process(mic, _resampled);
            _pending.AddRange(_resampled);
            if (_pending.Count > 16000) _pending.RemoveRange(0, _pending.Count - 16000); // on a pris du retard : on rattrape
            if (_pending.Count < Block16) return;
        }
        Wake();
    }

    /// <summary>
    /// Réveille le fil de calcul. Le fil audio peut encore appeler <see cref="Push"/> juste après l'arrêt (il avait lu la
    /// voix IA avant qu'elle soit retirée du moteur) : le signal peut alors être déjà libéré, ce n'est pas une erreur.
    /// </summary>
    private void Wake()
    {
        if (_stop && _ended) return;
        try { _wake.Set(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Thread audio : remplace <paramref name="dest"/> par la voix convertie (silence au tout début).</summary>
    public void Pull(Span<float> dest) => _out.Read(dest);

    private void Loop()
    {
        var block = new float[Block16];
        var watch = new Stopwatch();
        while (!_stop)
        {
            _wake.WaitOne(200);
            while (!_stop)
            {
                lock (_inLock)
                {
                    if (_pending.Count < Block16) break;
                    _pending.CopyTo(0, block, 0, Block16);
                    _pending.RemoveRange(0, Block16);
                }
                // fenêtre glissante d'une seconde
                Array.Copy(_window, Block16, _window, 0, _window.Length - Block16);
                Array.Copy(block, 0, _window, _window.Length - Block16, Block16);

                watch.Restart();
                double semis = ManualSemitones ?? _range.SemitonesTo(TargetHz);
                // la hauteur automatique bouge doucement (pas de saut au milieu d'une phrase)
                _semitones = ManualSemitones ?? _semitones + (semis - _semitones) * 0.1;
                int length;
                try { length = _engine.Convert(_window, Sid, _semitones, _audio); _errors = 0; }
                catch (Exception ex)
                {
                    if (_stop) break; // arrêt en cours (moteur libéré) : ce n'est pas une panne
                    // 3 échecs de suite (≈ 0,3 s) : la carte graphique est perdue, l'interface recharge tout
                    if (++_errors >= 3 && Failure == null)
                    {
                        Failure = ex;
                        Log.Error("Voix IA : la conversion échoue en boucle", ex);
                    }
                    if (Failure != null) Thread.Sleep(50);
                    continue;
                }
                _range.Add(_engine.LastF0[^(Block16 / 160)..]); // trames du dernier bloc (voix de l'utilisateur, avant décalage)
                ComputeMs = watch.Elapsed.TotalMilliseconds;

                int end = length - _marginOut;
                _sola.Process(_audio.AsSpan(end - _sola.Needed, _sola.Needed), _blockOut);
                _out.Write(_blockOut);
            }
        }

        // fin du fil : plus personne n'attend le signal ; le moteur est libéré ici si on nous l'a confié
        RvcEngine? engine;
        lock (_endLock)
        {
            _ended = true;
            engine = _engineToDispose;
        }
        _wake.Dispose();
        try { engine?.Dispose(); }
        catch (Exception ex) { Log.Error("Voix IA : libération du moteur", ex); }
    }

    /// <summary>Arrête la conversion (attend au plus 1 s la fin du calcul en cours). Le moteur est gardé : il resservira.</summary>
    public void Dispose()
    {
        _stop = true;
        Wake();
        _thread.Join(1000);
    }

    /// <summary>
    /// Arrête la conversion et libère aussi le moteur : tout de suite si le fil s'est arrêté, sinon par le fil lui-même
    /// quand son calcul en cours se termine (jamais de réseau détruit pendant un calcul, et l'interface n'attend pas).
    /// </summary>
    public void DisposeWithEngine()
    {
        Dispose();
        bool now;
        lock (_endLock)
        {
            now = _ended;
            if (!now) _engineToDispose = _engine;
        }
        if (now) _engine.Dispose();
    }
}
