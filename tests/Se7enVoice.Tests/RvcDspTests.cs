using System.Text.Json;
using Se7enVoice.Core.Ai;

namespace Se7enVoice.Tests;

/// <summary>Portage C# de la chaîne RVC comparé à la référence Python (librosa / RVC) : Fixtures/rvc_reference.json.</summary>
public class RvcDspTests
{
    private static readonly JsonElement Ref = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rvc_reference.json"))).RootElement;

    private static float[] Flat(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e[0].ValueKind == JsonValueKind.Array
            ? e.EnumerateArray().SelectMany(r => r.EnumerateArray().Select(v => v.GetSingle())).ToArray()
            : e.EnumerateArray().Select(v => v.GetSingle()).ToArray();

    [Fact]
    public void Mel_spectrogram_matches_librosa()
    {
        var x = Flat(Ref.GetProperty("x"));
        var expected = Flat(Ref.GetProperty("mel")); // [128][100]
        var mel = new float[128 * 100];
        new MelSpectrogram().Compute(x, 100, mel);
        double maxErr = expected.Zip(mel, (a, b) => Math.Abs(a - b)).Max();
        Assert.True(maxErr < 2e-3, $"écart max {maxErr}");
    }

    [Fact]
    public void Rmvpe_decoding_and_coarse_pitch_match_rvc()
    {
        var sal = Flat(Ref.GetProperty("sal"));
        var expected = Flat(Ref.GetProperty("f0"));
        var f0 = new float[100];
        Pitch.DecodeRmvpe(sal, 100, f0);
        for (int i = 0; i < 100; i++) Assert.True(Math.Abs(expected[i] - f0[i]) < 0.05, $"trame {i} : {f0[i]} au lieu de {expected[i]}");
        var coarse = Ref.GetProperty("coarse").EnumerateArray().Select(v => v.GetInt64()).ToArray();
        for (int i = 0; i < 100; i++) Assert.Equal(coarse[i], Pitch.Coarse(expected[i]));
    }

    [Fact]
    public void Resampler_keeps_a_tone_and_removes_what_cannot_fit()
    {
        static (double rms, double freq) Measure(List<float> y, int rate, double f)
        {
            // corrélation avec un sinus à f (amplitude) sur la seconde moitié
            int n = y.Count / 2;
            double re = 0, im = 0, e = 0;
            for (int i = n; i < y.Count; i++)
            {
                double t = (double)i / rate;
                re += y[i] * Math.Cos(2 * Math.PI * f * t);
                im += y[i] * Math.Sin(2 * Math.PI * f * t);
                e += y[i] * (double)y[i];
            }
            return (Math.Sqrt(e / (y.Count - n)), 2 * Math.Sqrt(re * re + im * im) / (y.Count - n));
        }
        var tone = Enumerable.Range(0, 48000).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / 48000.0))).ToArray();
        var outTone = new List<float>();
        var rs = new StreamResampler(48000, 16000);
        for (int i = 0; i < tone.Length; i += 480) rs.Process(tone.AsSpan(i, 480), outTone);
        Assert.InRange(outTone.Count, 15900, 16000);
        Assert.InRange(Measure(outTone, 16000, 440).freq, 0.47, 0.53);     // 440 Hz conservé

        var high = Enumerable.Range(0, 48000).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 12000 * i / 48000.0))).ToArray();
        var outHigh = new List<float>();
        var rs2 = new StreamResampler(48000, 16000);
        for (int i = 0; i < high.Length; i += 480) rs2.Process(high.AsSpan(i, 480), outHigh);
        Assert.True(Measure(outHigh, 16000, 4000).rms < 0.02);            // 12 kHz ne revient pas en 4 kHz
    }

    [Fact]
    public void Sola_joins_blocks_without_jumps()
    {
        // un sinus continu découpé en blocs qui se chevauchent : la sortie doit rester un sinus continu
        const int block = 480 * 12, fade = 480 * 3, search = 480;
        var sola = new SolaStitcher(block, fade, search);
        var all = new List<float>();
        double phase = 0;
        var src = Enumerable.Range(0, 48000 * 3).Select(i => (float)Math.Sin(2 * Math.PI * 220 * i / 48000.0)).ToArray();
        var outBlock = new float[block];
        for (int start = 0; start + sola.Needed <= src.Length; start += block)
        {
            // le morceau proposé est décalé de 0..search (comme les sorties successives du modèle)
            int jitter = (start / block * 137) % search;
            int s = Math.Min(src.Length - sola.Needed, start + jitter);
            sola.Process(src.AsSpan(s, sola.Needed), outBlock);
            all.AddRange(outBlock);
        }
        double maxStep = 0;
        for (int i = block + 1; i < all.Count; i++) maxStep = Math.Max(maxStep, Math.Abs(all[i] - all[i - 1]));
        Assert.True(maxStep < 0.06, $"saut max {maxStep}");   // un sinus à 220 Hz avance d'au plus 0,029 par échantillon
        _ = phase;
    }

    [Fact]
    public void Voice_range_gives_the_shift_to_a_target_voice()
    {
        var r = new VoiceRange();
        Assert.Equal(0, r.SemitonesTo(210));
        r.Add(Enumerable.Repeat(105f, 300).Concat(Enumerable.Repeat(0f, 50)).ToArray()); // voix d'homme à 105 Hz
        Assert.InRange(r.SemitonesTo(210), 11.9, 12.1);
    }
}
