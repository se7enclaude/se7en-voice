using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;

namespace Se7enVoice.Audio;

/// <summary>
/// Enregistre ce qu'entendent tes amis (voix modifiée + sons + applis) dans un .wav.
/// Le thread audio ne fait que déposer des copies dans une file ; l'écriture disque se fait à côté.
/// </summary>
public sealed class Recorder : IDisposable
{
    private readonly BlockingCollection<float[]> _queue = new(boundedCapacity: 2000);
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

    /// <summary>Appelé depuis le thread audio.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        _queue.TryAdd(samples.ToArray());
        Interlocked.Add(ref _samples, samples.Length);
    }

    private void Run()
    {
        foreach (var block in _queue.GetConsumingEnumerable())
        {
            try { _writer.WriteSamples(block, 0, block.Length); }
            catch (IOException) { /* disque plein… on perd la fin plutôt que de planter */ }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(3000);
        _writer.Dispose();
        _queue.Dispose();
    }
}
