using Se7enVoice.Core.Dsp;

namespace Se7enVoice.Core.Presets;

/// <summary>Un preset de voix. <see cref="Icon"/> est un glyphe de la police Segoe Fluent Icons.</summary>
public sealed record Preset
{
    public string Name { get; init; } = "Preset";
    public string Tagline { get; init; } = "";
    public string Icon { get; init; } = "";
    /// <summary>Teinte d'accent Slipstream : volt, blaze, surge, boost, neon, hazard.</summary>
    public string Tone { get; init; } = "volt";
    public VoiceSettings Settings { get; init; } = new();
    public bool BuiltIn { get; init; }
}

public static class BuiltInPresets
{
    public static IReadOnlyList<Preset> All { get; } =
    [
        new() { Name = "Normal", Tagline = "Ta vraie voix", Icon = "", Tone = "paper", BuiltIn = true, Settings = new() },
        new() { Name = "Voix grave", Tagline = "Grosse voix posée", Icon = "", Tone = "surge", BuiltIn = true,
            Settings = new() { PitchSemitones = -5, EqOn = true, EqLowDb = 3 } },
        new() { Name = "Chipmunk", Tagline = "Petit écureuil", Icon = "", Tone = "volt", BuiltIn = true,
            Settings = new() { PitchSemitones = 9, EqOn = true, EqLowDb = -4 } },
        new() { Name = "Robot", Tagline = "Bip boup", Icon = "", Tone = "surge", BuiltIn = true,
            Settings = new() { RobotOn = true, RobotFrequency = 50, RobotMix = 1, PitchSemitones = -1, CompressorOn = true, CompressorAmount = 0.5 } },
        new() { Name = "Alien", Tagline = "Venu d'ailleurs", Icon = "", Tone = "boost", BuiltIn = true,
            Settings = new() { PitchSemitones = 5, RobotOn = true, RobotFrequency = 420, RobotMix = 0.6,
                               EchoOn = true, EchoTimeMs = 120, EchoFeedback = 0.3, EchoMix = 0.25 } },
        new() { Name = "Démon", Tagline = "Sorti des enfers", Icon = "", Tone = "hazard", BuiltIn = true,
            Settings = new() { PitchSemitones = -9, DistortionOn = true, DistortionDrive = 0.3, EqOn = true, EqLowDb = 4,
                               ReverbOn = true, ReverbSize = 0.5, ReverbMix = 0.25 } },
        new() { Name = "Radio", Tagline = "Talkie-walkie", Icon = "", Tone = "blaze", BuiltIn = true,
            Settings = new() { RadioOn = true, CompressorOn = true, CompressorAmount = 0.7 } },
        new() { Name = "Grotte", Tagline = "Écho… écho…", Icon = "", Tone = "violet", BuiltIn = true,
            Settings = new() { EchoOn = true, EchoTimeMs = 380, EchoFeedback = 0.5, EchoMix = 0.4, ReverbOn = true, ReverbSize = 0.6, ReverbMix = 0.2 } },
        new() { Name = "Stade", Tagline = "Grosse salle", Icon = "", Tone = "neon", BuiltIn = true,
            Settings = new() { ReverbOn = true, ReverbSize = 0.95, ReverbMix = 0.45 } },
        new() { Name = "Voix aiguë", Tagline = "Plus haut, plus clair", Icon = "", Tone = "neon", BuiltIn = true,
            Settings = new() { PitchSemitones = 4, EqOn = true, EqLowDb = -3, EqHighDb = 3 } },
        new() { Name = "Géant", Tagline = "Très très grave", Icon = "", Tone = "blaze", BuiltIn = true,
            Settings = new() { PitchSemitones = -12, EqOn = true, EqLowDb = 2, ReverbOn = true, ReverbSize = 0.4, ReverbMix = 0.15 } },
        new() { Name = "Streamer", Tagline = "Voix pro et nette", Icon = "", Tone = "boost", BuiltIn = true,
            Settings = new() { CompressorOn = true, CompressorAmount = 0.6, EqOn = true, EqLowDb = 2, EqMidDb = -1, EqHighDb = 3 } },
    ];
}
