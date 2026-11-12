using System.Collections.Concurrent;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Se7enVoice.Audio;

/// <summary>
/// Sons lus dans le micro (mixés après les effets de voix). Le fil audio ne touche jamais aux fichiers : un fil à part
/// décode chaque son dans un tampon circulaire que le fil audio ne fait que lire. Le début du son est décodé dès le
/// déclenchement (<see cref="Play"/>), il part donc au bloc audio suivant, comme avant.
/// </summary>
public sealed class Soundboard
{
    private const int RingSize = 1 << 16, Mask = RingSize - 1; // ≈ 1,4 s à 48 kHz
    private const int Chunk = 4096;

    /// <summary>Un son en cours : un producteur (le décodage) et un consommateur (le fil audio), sans verrou.</summary>
    private sealed class Voice(AudioFileReader reader, ISampleProvider source) : IDisposable
    {
        private readonly float[] _ring = new float[RingSize];
        private long _write, _read;
        /// <summary>Le décodage a atteint la fin du fichier (écrit après le dernier échantillon).</summary>
        private volatile bool _endOfFile;
        /// <summary>Joué jusqu'au bout ou arrêté : le fil de décodage peut le libérer.</summary>
        public volatile bool Done;

        /// <summary>Fil de décodage (ou <see cref="Play"/> avant la publication) : remplit le tampon, au plus <paramref name="max"/> échantillons.</summary>
        public void Fill(float[] tmp, int max)
        {
            while (!_endOfFile && !Done && max > 0)
            {
                int free = RingSize - (int)(_write - Volatile.Read(ref _read));
                int want = Math.Min(Math.Min(free, tmp.Length), max);
                if (want <= 0) return;
                int got;
                try { got = source.Read(tmp, 0, want); }
                catch (Exception) { got = 0; } // fichier abîmé : le son s'arrête là
                for (int i = 0; i < got; i++) _ring[(_write + i) & Mask] = tmp[i];
                Volatile.Write(ref _write, _write + got);
                max -= got;
                if (got == 0) _endOfFile = true;
            }
        }

        /// <summary>Fil audio : ajoute la suite du son dans <paramref name="mix"/>.</summary>
        public void MixInto(Span<float> mix, float vol)
        {
            long r = _read;
            int n = (int)Math.Min(Volatile.Read(ref _write) - r, mix.Length);
            for (int i = 0; i < n; i++) mix[i] += _ring[(r + i) & Mask] * vol;
            Volatile.Write(ref _read, r + n);
            // fin du fichier lue avant la position d'écriture : celle-ci est alors définitive
            if (_endOfFile && r + n == Volatile.Read(ref _write)) Done = true;
        }

        public void Dispose()
        {
            Done = true;
            reader.Dispose();
        }
    }

    private readonly object _lock = new();
    private volatile Voice[] _voices = [];
    private readonly ConcurrentQueue<Voice> _retired = new();
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _decoder;

    public int SampleRate { get; set; } = 48000;
    public float Volume { get; set; } = 0.5f;
    public bool IsPlaying => _voices.Any(v => !v.Done);

    /// <summary>Lance un son. Lève une exception si le fichier est illisible.</summary>
    public void Play(string path)
    {
        var reader = new AudioFileReader(path);
        ISampleProvider p = reader;
        if (p.WaveFormat.Channels == 2) p = new StereoToMonoSampleProvider(p);
        else if (p.WaveFormat.Channels > 2) p = new DownmixProvider(p);
        if (p.WaveFormat.SampleRate != SampleRate) p = new WdlResamplingSampleProvider(p, SampleRate);
        var voice = new Voice(reader, p);
        voice.Fill(new float[Chunk], Math.Max(Chunk, SampleRate / 2)); // ½ s prête avant même le prochain bloc audio
        lock (_lock)
        {
            _voices = [.. _voices, voice];
            if (_decoder == null)
            {
                _decoder = new Thread(DecodeLoop) { IsBackground = true, Name = "soundboard", Priority = ThreadPriority.AboveNormal };
                _decoder.Start();
            }
        }
        _wake.Set();
    }

    public void StopAll()
    {
        Voice[] old;
        lock (_lock)
        {
            old = _voices;
            _voices = [];
        }
        foreach (var v in old)
        {
            v.Done = true;
            _retired.Enqueue(v); // libéré par le fil de décodage : il est peut-être en train de le lire
        }
        if (old.Length > 0) _wake.Set();
    }

    /// <summary>Fil audio : ajoute les sons en cours dans <paramref name="mix"/>. Retourne vrai si quelque chose a été ajouté.</summary>
    public bool MixInto(Span<float> mix)
    {
        var voices = _voices;
        if (voices.Length == 0) return false;
        float vol = Volume;
        bool any = false;
        foreach (var v in voices)
        {
            if (v.Done) continue;
            any = true;
            v.MixInto(mix, vol);
        }
        return any;
    }

    private void DecodeLoop()
    {
        var tmp = new float[Chunk];
        while (true)
        {
            _wake.WaitOne(_voices.Length == 0 && _retired.IsEmpty ? Timeout.Infinite : 20);
            var voices = _voices;
            foreach (var v in voices) v.Fill(tmp, int.MaxValue);

            if (voices.Any(v => v.Done))
            {
                List<Voice> finished;
                lock (_lock)
                {
                    finished = _voices.Where(v => v.Done).ToList();
                    _voices = _voices.Where(v => !v.Done).ToArray();
                }
                foreach (var v in finished) v.Dispose();
            }
            while (_retired.TryDequeue(out var r)) r.Dispose();
        }
    }

    /// <summary>Mélange un fichier multicanal (5.1…) en mono.</summary>
    private sealed class DownmixProvider(ISampleProvider source) : ISampleProvider
    {
        private float[] _buf = [];
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int ch = source.WaveFormat.Channels;
            if (_buf.Length < count * ch) _buf = new float[count * ch];
            int frames = source.Read(_buf, 0, count * ch) / ch;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++) sum += _buf[f * ch + c];
                buffer[offset + f] = sum / ch;
            }
            return frames;
        }
    }
}
