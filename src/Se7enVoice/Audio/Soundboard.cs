using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Se7enVoice.Audio;

/// <summary>Sons lus dans le micro (mixés après les effets de voix).</summary>
public sealed class Soundboard
{
    private sealed class Voice(AudioFileReader reader, ISampleProvider source)
    {
        public AudioFileReader Reader { get; } = reader;
        public ISampleProvider Source { get; } = source;
    }

    private readonly object _lock = new();
    private readonly List<Voice> _voices = [];
    private float[] _tmp = new float[4096];

    public int SampleRate { get; set; } = 48000;
    public float Volume { get; set; } = 0.5f;
    public bool IsPlaying { get { lock (_lock) return _voices.Count > 0; } }

    /// <summary>Lance un son. Lève une exception si le fichier est illisible.</summary>
    public void Play(string path)
    {
        var reader = new AudioFileReader(path);
        ISampleProvider p = reader;
        if (p.WaveFormat.Channels == 2) p = new StereoToMonoSampleProvider(p);
        else if (p.WaveFormat.Channels > 2) p = new DownmixProvider(p);
        if (p.WaveFormat.SampleRate != SampleRate) p = new WdlResamplingSampleProvider(p, SampleRate);
        lock (_lock) _voices.Add(new Voice(reader, p));
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var v in _voices) v.Reader.Dispose();
            _voices.Clear();
        }
    }

    /// <summary>Ajoute les sons en cours dans <paramref name="mix"/>. Retourne vrai si quelque chose a été ajouté.</summary>
    public bool MixInto(Span<float> mix)
    {
        lock (_lock)
        {
            if (_voices.Count == 0) return false;
            if (_tmp.Length < mix.Length) _tmp = new float[mix.Length];
            float vol = Volume;
            for (int v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                int read;
                try { read = voice.Source.Read(_tmp, 0, mix.Length); }
                catch (Exception) { read = 0; }
                for (int i = 0; i < read; i++) mix[i] += _tmp[i] * vol;
                if (read < mix.Length)
                {
                    voice.Reader.Dispose();
                    _voices.RemoveAt(v);
                }
            }
            return true;
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
