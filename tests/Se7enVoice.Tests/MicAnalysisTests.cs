using Se7enVoice.Core.Dsp;

namespace Se7enVoice.Tests;

public class MicAnalysisTests
{
    private const int Sr = 48000;

    /// <summary>Bruit blanc à <paramref name="db"/> dBFS (RMS).</summary>
    private static float[] Noise(double seconds, double db, int seed = 1)
    {
        var r = new Random(seed);
        var a = new float[(int)(seconds * Sr)];
        double amp = Math.Pow(10, db / 20) * Math.Sqrt(3); // uniforme [-x, x] : RMS = x/√3
        for (int i = 0; i < a.Length; i++) a[i] = (float)((r.NextDouble() * 2 - 1) * amp);
        return a;
    }

    /// <summary>« Voix » : syllabes de 200 ms (son harmonique à 150 Hz) séparées de 100 ms de silence, sur un bruit.</summary>
    private static float[] Speech(double seconds, double voiceDb, double noiseDb, double rumbleDb = -200)
    {
        var a = Noise(seconds, noiseDb, 7);
        double amp = Math.Pow(10, voiceDb / 20) * Math.Sqrt(2) / Math.Sqrt(1 + 0.25 + 0.11);
        double rumble = Math.Pow(10, rumbleDb / 20) * Math.Sqrt(2);
        for (int i = 0; i < a.Length; i++)
        {
            double t = (double)i / Sr;
            bool on = t % 0.3 < 0.2;
            double v = on ? amp * (Math.Sin(2 * Math.PI * 150 * t) + 0.5 * Math.Sin(2 * Math.PI * 300 * t) + 0.33 * Math.Sin(2 * Math.PI * 450 * t)) : 0;
            a[i] += (float)(v + rumble * Math.Sin(2 * Math.PI * 40 * t));
        }
        return a;
    }

    [Fact]
    public void Quiet_mic_gets_more_windows_gain_and_a_gate_between_noise_and_voice()
    {
        var r = MicAnalysis.Analyze(Noise(3, -70), Speech(7, -38, -70), Sr, new VoiceSettings(), windowsGainDb: 0, windowsMinDb: -40, windowsMaxDb: 30);
        Assert.True(r.HeardVoice);
        Assert.InRange(r.SpeechDb, -40, -36);
        Assert.InRange(r.WindowsGainDb, 15, 19);       // -38 → -20
        Assert.Equal(0, r.Settings.VolumeDb);
        Assert.True(r.Settings.GateOn);
        double noiseAfter = r.NoiseDb + r.WindowsGainDb;
        Assert.InRange(r.Settings.GateThresholdDb, noiseAfter + 5, -20 - 12);
        // micro faible : le bruit monte avec le gain (≈ -52 dB, 32 dB sous la voix) → réduction de bruit légère
        Assert.True(r.Settings.DenoiseOn);
        Assert.Equal(0.35, r.Settings.DenoiseAmount);
    }

    [Fact]
    public void Clipping_mic_is_turned_down()
    {
        var speech = Speech(7, -6, -60);
        for (int i = 0; i < speech.Length; i += 50) speech[i] = Math.Sign(speech[i]) * 1f; // crêtes à 0 dB
        var r = MicAnalysis.Analyze(Noise(3, -60), speech, Sr, new VoiceSettings(), 10, -40, 30);
        Assert.True(r.WindowsGainDb <= -6);
        Assert.Contains(r.Notes, n => n.Contains("saturait"));
    }

    [Fact]
    public void Noisy_room_turns_on_strong_denoise_and_rumble_turns_on_low_cut()
    {
        var r = MicAnalysis.Analyze(Noise(3, -42), Speech(7, -20, -42, rumbleDb: -24), Sr, new VoiceSettings(), 0, -40, 30);
        Assert.True(r.Settings.DenoiseOn);
        Assert.Equal(0.9, r.Settings.DenoiseAmount);
        Assert.True(r.Settings.LowCutOn);
        Assert.True(r.Settings.GateThresholdDb > -42);
    }

    [Fact]
    public void Mic_without_hardware_volume_uses_software_gain_and_keeps_the_voice_preset()
    {
        var preset = new VoiceSettings { PitchSemitones = -5, RobotOn = true };
        var r = MicAnalysis.Analyze(Noise(3, -75), Speech(7, -32, -75), Sr, preset); // pas de réglage Windows (min = max)
        Assert.Equal(0, r.WindowsGainDb);
        Assert.InRange(r.Settings.VolumeDb, 10, 13);
        Assert.Equal(-5, r.Settings.PitchSemitones);
        Assert.True(r.Settings.RobotOn);
    }

    [Fact]
    public void Good_mic_in_a_quiet_room_changes_almost_nothing()
    {
        var r = MicAnalysis.Analyze(Noise(3, -72), Speech(7, -21, -72), Sr, new VoiceSettings(), 0, -40, 30);
        Assert.Equal(0, r.WindowsGainDb);
        Assert.Equal(0, r.Settings.VolumeDb);
        Assert.False(r.Settings.DenoiseOn);
        Assert.False(r.Settings.LowCutOn);
        Assert.Contains(r.Notes, n => n.Contains("parfait"));
    }

    [Fact]
    public void Silence_only_is_reported_not_applied()
    {
        var current = new VoiceSettings { GateThresholdDb = -44 };
        var r = MicAnalysis.Analyze(Noise(3, -80), Noise(7, -80, 3), Sr, current, 0, -40, 30);
        Assert.False(r.HeardVoice);
        Assert.Same(current, r.Settings);
        Assert.NotEmpty(r.Notes);
    }
}
