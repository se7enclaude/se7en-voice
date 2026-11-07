using System.Diagnostics;
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
    private const int Block16 = 16 * BlockMs, Block48 = 48 * BlockMs, Margin48 = 48 * MarginMs;

    private readonly RvcEngine _engine;
    private readonly StreamResampler _toModel;            // micro → 16 kHz
    private readonly List<float> _resampled = [];
    private readonly List<float> _pending = [];           // 16 kHz en attente d'un bloc complet
    private readonly object _inLock = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly float[] _window = new float[RvcEngine.Window];
    private readonly SolaStitcher _sola = new(Block48, 48 * FadeMs, 48 * SearchMs);
    private readonly float[] _blockOut = new float[Block48];
    private readonly ResamplingRing _out;                  // 48 kHz → fréquence du micro, avec tampon et dérive
    private readonly VoiceRange _range = new();
    private readonly Thread _thread;
    private volatile bool _stop;
    private double _semitones;

    public AiVoice(RvcEngine engine, int engineRate)
    {
        _engine = engine;
        _toModel = new StreamResampler(engineRate, 16000);
        // tampon de sortie : un bloc arrive toutes les 100 ms, il faut de quoi tenir jusqu'au suivant (+ marge)
        _out = new ResamplingRing(RvcEngine.OutRate, engineRate, targetLatencyMs: BlockMs + 25);
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
    public bool TooSlow => ComputeMs > BlockMs * 0.9;

    /// <summary>Thread audio : voix du micro (mono, fréquence du micro).</summary>
    public void Push(ReadOnlySpan<float> mic)
    {
        lock (_inLock)
        {
            _resampled.Clear();
            _toModel.Process(mic, _resampled);
            _pending.AddRange(_resampled);
            if (_pending.Count > 16000) _pending.RemoveRange(0, _pending.Count - 16000); // on a pris du retard : on rattrape
        }
        if (_pending.Count >= Block16) _wake.Set();
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
                float[] audio;
                try { audio = _engine.Convert(_window, Sid, _semitones); }
                catch (Exception) { continue; }
                _range.Add(_engine.LastF0[^(Block16 / 160)..]); // trames du dernier bloc (voix de l'utilisateur, avant décalage)
                ComputeMs = watch.Elapsed.TotalMilliseconds;

                int end = audio.Length - Margin48;
                _sola.Process(audio.AsSpan(end - _sola.Needed, _sola.Needed), _blockOut);
                _out.Write(_blockOut);
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread.Join(1000);
        _wake.Dispose(); // le moteur (les réseaux chargés) est gardé : il resservira
    }
}
