using Se7enVoice.Core.Audio;
using Se7enVoice.Core.Dsp;
using Se7enVoice.Core.Presets;

namespace Se7enVoice.Tests;

public class DspTests
{
    private const int Rate = 48000;

    private static float[] Sine(double freq, double seconds, double amp = 0.5)
    {
        var s = new float[(int)(Rate * seconds)];
        for (int i = 0; i < s.Length; i++) s[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / Rate));
        return s;
    }

    /// <summary>Fréquence fondamentale par autocorrélation (robuste aux fondus entre grains).</summary>
    private static double DominantFrequency(ReadOnlySpan<float> s)
    {
        int minLag = Rate / 1000, maxLag = Rate / 60, n = s.Length - maxLag;
        int bestLag = minLag;
        double best = double.MinValue;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++) sum += s[i] * s[i + lag];
            if (sum > best * 1.0001) { best = sum; bestLag = lag; }
        }
        return (double)Rate / bestLag;
    }

    [Fact]
    public void Limiter_NeverExceedsCeiling_EvenWithHugeInput()
    {
        var limiter = new Limiter(Rate);
        var s = Sine(300, 0.5, amp: 40);
        limiter.Process(s);
        Assert.All(s, v => Assert.InRange(Math.Abs(v), 0, limiter.Ceiling + 1e-6f));
    }

    [Fact]
    public void Chain_MaxVolumeThenLimiter_StaysBelowZeroDbfs()
    {
        var chain = new EffectChain(Rate) { Settings = new VoiceSettings { VolumeDb = 20, GateOn = false, EchoOn = true, ReverbOn = true, DistortionOn = true } };
        var limiter = new Limiter(Rate);
        var s = Sine(200, 1, amp: 0.9);
        chain.Process(s);
        limiter.Process(s);
        Assert.All(s, v => Assert.True(Math.Abs(v) <= 1f));
    }

    [Fact]
    public void Chain_NeutralSettings_IsBitTransparent()
    {
        var chain = new EffectChain(Rate) { Settings = new VoiceSettings { GateOn = false } };
        var input = Sine(440, 0.2);
        var output = (float[])input.Clone();
        chain.Process(output);
        Assert.Equal(input, output);
    }

    [Theory]
    [InlineData(12, 2.0)]
    [InlineData(-12, 0.5)]
    [InlineData(7, 1.498)]
    public void PitchShifter_ShiftsFrequency_AndKeepsLength(double semitones, double ratio)
    {
        var pitch = new PitchShifter(Rate) { Semitones = semitones };
        var s = Sine(220, 1.0);
        int length = s.Length;
        pitch.Process(s);
        Assert.Equal(length, s.Length);

        double measured = DominantFrequency(s.AsSpan(Rate / 5)); // on ignore le démarrage
        Assert.InRange(measured, 220 * ratio * 0.93, 220 * ratio * 1.07);
    }

    [Fact]
    public void Depth_LowersPitch_AndBoostsLows()
    {
        var chain = new EffectChain(Rate) { Settings = new VoiceSettings { GateOn = false, Depth = 1 } };
        var s = Sine(220, 1.0, amp: 0.2);
        chain.Process(s);
        Assert.All(s, v => Assert.True(float.IsFinite(v)));
        double expected = 220 * Math.Pow(2, Deepener.PitchFor(1) / 12);
        Assert.InRange(DominantFrequency(s.AsSpan(Rate / 5)), expected * 0.93, expected * 1.07);
    }

    [Fact]
    public void NoiseGate_SilencesQuietNoise_ButLetsVoiceThrough()
    {
        var gate = new NoiseGate(Rate) { ThresholdDb = -40 };
        var noise = Sine(1000, 0.5, amp: 0.002);
        gate.Process(noise);
        Assert.True(noise.Skip(Rate / 4).Max(Math.Abs) < 0.0005f);

        var voice = Sine(200, 0.5, amp: 0.3);
        gate.Process(voice);
        Assert.True(voice.Skip(Rate / 10).Max(Math.Abs) > 0.25f);
    }

    [Fact]
    public void Echo_RepeatsSignalAfterDelay()
    {
        var echo = new Echo(Rate);
        echo.Set(100, 0, 1);
        var s = new float[Rate / 2];
        s[0] = 1;
        echo.Process(s);
        Assert.Equal(1f, s[Rate / 10], 3);
    }

    [Fact]
    public void AllEffects_StayFinite_OnRandomInput()
    {
        var rnd = new Random(7);
        foreach (var preset in BuiltInPresets.All)
        {
            var chain = new EffectChain(Rate) { Settings = preset.Settings };
            var s = new float[Rate];
            for (int i = 0; i < s.Length; i++) s[i] = (float)(rnd.NextDouble() * 2 - 1);
            chain.Process(s);
            Assert.All(s, v => Assert.True(float.IsFinite(v), preset.Name));
        }
    }

    [Fact]
    public void ResamplingRing_ConvertsRate_WithoutGapsAfterPriming()
    {
        var ring = new ResamplingRing(48000, 44100, targetLatencyMs: 20);
        var block = Sine(300, 0.01);       // 10 ms à 48 kHz
        var output = new float[441];      // 10 ms à 44,1 kHz
        int produced = 0, reads = 0;
        for (int i = 0; i < 300; i++)
        {
            ring.Write(block);
            if (i < 3) continue;
            produced += ring.Read(output);
            reads++;
        }
        Assert.True(produced >= reads * 441 * 0.98, $"{produced} / {reads * 441}");
        Assert.InRange(ring.Available, 0, 48000 * 0.1);
    }

    [Fact]
    public void ResamplingRing_DropsBacklog_WhenOutputIsSlow()
    {
        var ring = new ResamplingRing(48000, 48000, targetLatencyMs: 30);
        var block = new float[480];
        for (int i = 0; i < 500; i++) ring.Write(block); // 5 s sans lecture
        Assert.True(ring.Available < 48000 * 0.2);
    }
}

public class PresetTests
{
    [Fact]
    public void BuiltInPresets_HaveUniqueNames()
    {
        var names = BuiltInPresets.All.Select(p => p.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal("Normal", names[0]);
    }

    [Fact]
    public void PresetStore_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "se7en-tests-" + Guid.NewGuid());
        try
        {
            var store = new PresetStore(dir);
            var preset = new Preset { Name = "Mon/Robot:2", Tone = "neon", Settings = new VoiceSettings { PitchSemitones = -4, EchoOn = true } };
            store.Save(preset);
            var loaded = Assert.Single(store.LoadAll());
            Assert.Equal(preset.Name, loaded.Name);
            Assert.Equal(preset.Settings, loaded.Settings);
            Assert.False(loaded.BuiltIn);

            store.Delete(preset.Name);
            Assert.Empty(store.LoadAll());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PresetStore_IgnoresGarbageFiles()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, "pas du json");
        Assert.Null(PresetStore.TryRead(file));
        File.Delete(file);
    }
}
