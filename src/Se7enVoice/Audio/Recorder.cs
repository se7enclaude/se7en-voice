using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;

namespace Se7enVoice.Audio;

/// <summary>
/// Enregistre ce qu'entendent tes amis (voix modifiée + sons + applis) dans un .wav.
/// Le thread audio ne fait que déposer des copies (tableaux empruntés à un pool, pas d'allocation) dans une file ;
/// l'écriture disque se fait à côté.
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly BlockingCollection<(float[] Buffer, int Length)> _queue = new(boundedCapacity: 2000);
    private readonly WaveFileWriter _writer;
    private readonly Thread _thread;
    private long _samples;

    public Recorder(string path, int sampleRate)
    {
        Path = path;
        SampleRate = sampleRate;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        _thread = new Thread(Run) { IsBackground = true, Name = "recorder" };
        _thread.Start();
    }

    public string Path { get; }
    public int SampleRate { get; }
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Interlocked.Read(ref _samples) / SampleRate);

    /// <summary>
    /// Appelé depuis le thread audio. Peut arriver juste après l'arrêt (le thread audio avait lu l'enregistrement avant
    /// qu'il soit retiré) : le bloc est alors simplement ignoré.
    /// </summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        var buffer = ArrayPool<float>.Shared.Rent(samples.Length);
        samples.CopyTo(buffer);
        bool added;
        try { added = _queue.TryAdd((buffer, samples.Length)); }
        catch (InvalidOperationException) { added = false; } // file fermée ou libérée (arrêt en cours)
        if (!added)
        {
            ArrayPool<float>.Shared.Return(buffer);
            return;
        }
        Interlocked.Add(ref _samples, samples.Length);
    }

    private void Run()
    {
        foreach (var (buffer, length) in _queue.GetConsumingEnumerable())
        {
            try { _writer.WriteSamples(buffer, 0, length); }
            catch (Exception) { /* disque plein, fichier fermé… on perd la fin plutôt que de planter */ }
            finally { ArrayPool<float>.Shared.Return(buffer); }
        }
        // le fichier est fermé par ce fil, après la dernière écriture (même si l'arrêt n'a pas attendu jusque-là)
        try { _writer.Dispose(); } catch (Exception) { }
    }

    /// <summary>Termine le fichier (attend au plus 3 s la fin des écritures). La file n'est pas libérée : le thread audio peut encore y toquer.</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(3000);
    }
}
