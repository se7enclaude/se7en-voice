using Se7enVoice.Core.Dsp;

namespace Se7enVoice.Core.Presets;

/// <summary>Un preset de voix. <see cref="Icon"/> est un glyphe de la police Segoe Fluent Icons.</summary>
public sealed record Preset
{
    public string Name { get; init; } = "Preset";
    public string Tagline { get; init; } = "";
    public string Icon { get; init; } = "";
    /// <summary>Teinte d'accent Slipstream : volt, blaze, surge, boost, neon, hazard, violet, paper.</summary>
    public string Tone { get; init; } = "volt";
    /// <summary>Rayon de la bibliothèque (voir <see cref="PresetCategories"/>).</summary>
    public string Category { get; init; } = PresetCategories.Mine;
    public VoiceSettings Settings { get; init; } = new();
    public bool BuiltIn { get; init; }
}

public static class PresetCategories
{
    public const string Essentials = "Essentiels";
    public const string Movies = "Films & séries";
    public const string Creatures = "Créatures";
    public const string Machines = "Machines";
    public const string Places = "Lieux";
    public const string Fun = "Fun";
    public const string Mine = "Mes presets";

    public static IReadOnlyList<string> All { get; } = [Essentials, Movies, Creatures, Machines, Places, Fun];
}

/// <summary>
/// Voix fournies avec l'app : les grands classiques des changeurs de voix (Voicemod, Clownfish,
/// MorphVOX…) refaits avec la chaîne d'effets de se7en Voice.
/// </summary>
public static class BuiltInPresets
{
    private static Preset P(string category, string name, string tagline, char icon, string tone, VoiceSettings settings) =>
        new() { Category = category, Name = name, Tagline = tagline, Icon = icon.ToString(), Tone = tone, BuiltIn = true, Settings = settings };

    private const string E = PresetCategories.Essentials, M = PresetCategories.Movies, C = PresetCategories.Creatures,
        R = PresetCategories.Machines, L = PresetCategories.Places, F = PresetCategories.Fun;

    public static IReadOnlyList<Preset> All { get; } =
    [
        // ---------- Essentiels ----------
        P(E, "Normal", "Ta vraie voix", '', "paper", new()),
        P(E, "Streamer", "Voix pro et nette", '', "boost", new()
        {
            LowCutOn = true, LowCutHz = 90, DenoiseOn = true, DenoiseAmount = 0.5, CompressorOn = true, CompressorAmount = 0.6,
            EqOn = true, EqLowDb = 2, EqMidDb = -1, EqPresenceDb = 2, EqHighDb = 2, DeEsserOn = true, DeEsserAmount = 0.4,
        }),
        P(E, "Voix radio FM", "Grosse voix d'animateur", '', "volt", new()
        {
            LowCutOn = true, LowCutHz = 70, Depth = 0.25, CompressorOn = true, CompressorAmount = 0.8,
            EqOn = true, EqLowDb = 4, EqLowMidDb = -2, EqPresenceDb = 3, EqHighDb = 1, DeEsserOn = true, DeEsserAmount = 0.5,
        }),
        P(E, "Podcast", "Chaleureuse et posée", '', "surge", new()
        {
            LowCutOn = true, LowCutHz = 80, DenoiseOn = true, DenoiseAmount = 0.4, CompressorOn = true, CompressorAmount = 0.5,
            EqOn = true, EqLowDb = 2, EqLowMidDb = -1.5, EqPresenceDb = 1.5, DeEsserOn = true, DeEsserAmount = 0.5,
        }),
        P(E, "Voix grave", "Grosse voix naturelle", '', "surge", new()
        {
            PitchSemitones = -4, KeepFormants = true, FormantSemitones = -2, EqOn = true, EqLowDb = 3,
        }),
        P(E, "Voix aiguë", "Plus haut, plus clair", '', "neon", new()
        {
            PitchSemitones = 4, KeepFormants = true, FormantSemitones = 1, EqOn = true, EqLowDb = -3, EqHighDb = 3,
        }),
        P(E, "Voix de fille", "Homme → femme", '', "neon", new()
        {
            PitchSemitones = 5, KeepFormants = true, FormantSemitones = 2.5, LowCutOn = true, LowCutHz = 140,
            EqOn = true, EqLowDb = -3, EqLowMidDb = -2, EqPresenceDb = 2, EqHighDb = 2,
        }),
        P(E, "Voix de garçon", "Femme → homme", '', "surge", new()
        {
            PitchSemitones = -5, KeepFormants = true, FormantSemitones = -2.5, EqOn = true, EqLowDb = 3, EqLowMidDb = 1, EqHighDb = -1,
        }),
        P(E, "Autotune", "Façon T-Pain", '', "volt", new()
        {
            AutotuneOn = true, AutotuneSpeed = 1, AutotuneScale = 0, CompressorOn = true, CompressorAmount = 0.5,
            ChorusOn = true, ChorusDepth = 0.25, ChorusMix = 0.25, ReverbOn = true, ReverbSize = 0.5, ReverbMix = 0.15,
        }),
        P(E, "Autotune pop", "Chant juste et doux", '', "boost", new()
        {
            AutotuneOn = true, AutotuneSpeed = 0.55, AutotuneScale = 1, CompressorOn = true, CompressorAmount = 0.5,
            ReverbOn = true, ReverbSize = 0.6, ReverbMix = 0.2, DeEsserOn = true,
        }),

        // ---------- Films & séries ----------
        P(M, "Dark Vador", "Je suis ton père", '', "hazard", new()
        {
            PitchSemitones = -4, KeepFormants = true, FormantSemitones = -3, Depth = 0.3, DistortionOn = true, DistortionDrive = 0.08,
            EqOn = true, EqLowMidDb = 4, EqHighDb = -3, CompressorOn = true, CompressorAmount = 0.7, ReverbOn = true, ReverbSize = 0.3, ReverbMix = 0.15,
        }),
        P(M, "Bane", "Voix derrière le masque", '', "blaze", new()
        {
            PitchSemitones = -3, KeepFormants = true, FormantSemitones = -2, FilterOn = true, FilterLowHz = 110, FilterHighHz = 2600,
            DistortionOn = true, DistortionDrive = 0.1, EqOn = true, EqLowMidDb = 5, CompressorOn = true, CompressorAmount = 0.8,
        }),
        P(M, "Ghostface", "Quel est ton film d'horreur préféré ?", '', "hazard", new()
        {
            PitchSemitones = -6, KeepFormants = true, FormantSemitones = -2, FilterOn = true, FilterLowHz = 220, FilterHighHz = 4200,
            DistortionOn = true, DistortionDrive = 0.18, CompressorOn = true, CompressorAmount = 0.8, EqOn = true, EqPresenceDb = 3,
        }),
        P(M, "Anonymous", "Nous sommes légion", '', "violet", new()
        {
            PitchSemitones = -8, RobotOn = true, RobotFrequency = 30, RobotMix = 0.25, CompressorOn = true, CompressorAmount = 0.7,
            EqOn = true, EqLowDb = 2, ReverbOn = true, ReverbSize = 0.3, ReverbMix = 0.12,
        }),
        P(M, "Venom", "Nous sommes Venom", '', "violet", new()
        {
            PitchSemitones = -7, KeepFormants = true, FormantSemitones = -4, HarmonyOn = true, HarmonySemitones = -12, HarmonyMix = 0.45,
            DistortionOn = true, DistortionDrive = 0.2, EqOn = true, EqLowDb = 4, ReverbOn = true, ReverbSize = 0.35, ReverbMix = 0.15,
        }),
        P(M, "Stormtrooper", "Talkie de l'Empire", '', "paper", new()
        {
            FilterOn = true, FilterLowHz = 450, FilterHighHz = 3200, DistortionOn = true, DistortionDrive = 0.22,
            CompressorOn = true, CompressorAmount = 0.8, ReverbOn = true, ReverbSize = 0.25, ReverbMix = 0.12,
        }),
        P(M, "GLaDOS", "IA un peu inquiétante", '', "surge", new()
        {
            PitchSemitones = 2, KeepFormants = true, FormantSemitones = 2, AutotuneOn = true, AutotuneSpeed = 1,
            ChorusOn = true, ChorusDepth = 0.2, ChorusMix = 0.25, EqOn = true, EqPresenceDb = 3, ReverbOn = true, ReverbSize = 0.4, ReverbMix = 0.15,
        }),
        P(M, "Minion", "Banana !", '', "volt", new()
        {
            PitchSemitones = 8, KeepFormants = true, FormantSemitones = 4, ChorusOn = true, ChorusDepth = 0.3, ChorusMix = 0.3,
        }),
        P(M, "Chipmunk", "Petit écureuil", '', "volt", new() { PitchSemitones = 9, EqOn = true, EqLowDb = -4 }),
        P(M, "Optimus", "Robot géant héroïque", '', "blaze", new()
        {
            PitchSemitones = -3, KeepFormants = true, FormantSemitones = -2, ChorusOn = true, ChorusRate = 0.3, ChorusDepth = 0.4, ChorusMix = 0.4,
            RobotOn = true, RobotFrequency = 80, RobotMix = 0.2, CompressorOn = true, CompressorAmount = 0.7, ReverbOn = true, ReverbSize = 0.4, ReverbMix = 0.15,
        }),

        // ---------- Créatures ----------
        P(C, "Démon", "Sorti des enfers", '', "hazard", new()
        {
            PitchSemitones = -9, HarmonyOn = true, HarmonySemitones = -12, HarmonyMix = 0.35, DistortionOn = true, DistortionDrive = 0.3,
            EqOn = true, EqLowDb = 4, ReverbOn = true, ReverbSize = 0.5, ReverbMix = 0.25,
        }),
        P(C, "Monstre", "Grrr…", '', "blaze", new()
        {
            PitchSemitones = -10, KeepFormants = true, FormantSemitones = -5, DistortionOn = true, DistortionDrive = 0.15, EqOn = true, EqLowDb = 5,
        }),
        P(C, "Géant", "Très très grave", '', "blaze", new()
        {
            PitchSemitones = -12, EqOn = true, EqLowDb = 2, ReverbOn = true, ReverbSize = 0.4, ReverbMix = 0.15,
        }),
        P(C, "Orc", "Lok'tar ogar !", '', "boost", new()
        {
            PitchSemitones = -6, KeepFormants = true, FormantSemitones = -4, DistortionOn = true, DistortionDrive = 0.12, EqOn = true, EqLowMidDb = 3,
        }),
        P(C, "Alien", "Venu d'ailleurs", '', "boost", new()
        {
            PitchSemitones = 5, RobotOn = true, RobotFrequency = 420, RobotMix = 0.6, EchoOn = true, EchoTimeMs = 120, EchoFeedback = 0.3, EchoMix = 0.25,
        }),
        P(C, "Zombie", "Cerveaaaux…", '', "boost", new()
        {
            PitchSemitones = -5, KeepFormants = true, FormantSemitones = -2, WhisperOn = true, WhisperAmount = 0.35,
            DistortionOn = true, DistortionDrive = 0.25, VibratoOn = true, VibratoRate = 2.5, VibratoDepth = 0.25,
        }),
        P(C, "Fantôme", "Hoooouuu…", '', "violet", new()
        {
            PitchSemitones = 2, WhisperOn = true, WhisperAmount = 0.75, VibratoOn = true, VibratoRate = 3, VibratoDepth = 0.25,
            ReverbOn = true, ReverbSize = 0.92, ReverbMix = 0.5, EchoOn = true, EchoTimeMs = 420, EchoFeedback = 0.35, EchoMix = 0.2,
        }),
        P(C, "Elfe", "Voix de la forêt", '', "boost", new()
        {
            PitchSemitones = 4, KeepFormants = true, FormantSemitones = 3, ChorusOn = true, ChorusDepth = 0.4, ChorusMix = 0.35,
            ReverbOn = true, ReverbSize = 0.7, ReverbMix = 0.25,
        }),
        P(C, "Gobelin", "Petit et fourbe", '', "boost", new()
        {
            PitchSemitones = 5, KeepFormants = true, FormantSemitones = 7, DistortionOn = true, DistortionDrive = 0.08, EqOn = true, EqPresenceDb = 4,
        }),

        // ---------- Machines ----------
        P(R, "Robot", "Bip boup", '', "surge", new()
        {
            RobotOn = true, RobotFrequency = 50, RobotMix = 1, PitchSemitones = -1, CompressorOn = true, CompressorAmount = 0.5,
        }),
        P(R, "Cyborg", "Mi-homme mi-machine", '', "neon", new()
        {
            RobotOn = true, RobotFrequency = 140, RobotMix = 0.45, ChorusOn = true, ChorusDepth = 0.5, ChorusMix = 0.4,
            CompressorOn = true, CompressorAmount = 0.6, ReverbOn = true, ReverbSize = 0.3, ReverbMix = 0.12,
        }),
        P(R, "Rétro 8-bit", "Console des années 80", '', "volt", new()
        {
            PitchSemitones = 2, BitcrushOn = true, BitcrushBits = 6, BitcrushRateHz = 7000, EqOn = true, EqPresenceDb = 3,
        }),
        P(R, "Radio", "Talkie-walkie", '', "blaze", new() { RadioOn = true, CompressorOn = true, CompressorAmount = 0.7 }),
        P(R, "Téléphone", "Allô ?", '', "paper", new()
        {
            FilterOn = true, FilterLowHz = 320, FilterHighHz = 3400, CompressorOn = true, CompressorAmount = 0.7, DistortionOn = true, DistortionDrive = 0.04,
        }),
        P(R, "Mégaphone", "Attention, attention !", '', "hazard", new()
        {
            FilterOn = true, FilterLowHz = 550, FilterHighHz = 4200, DistortionOn = true, DistortionDrive = 0.35, CompressorOn = true, CompressorAmount = 0.8,
            EchoOn = true, EchoTimeMs = 160, EchoFeedback = 0.2, EchoMix = 0.15,
        }),
        P(R, "Vieille radio", "Années 40, ça grésille", '', "blaze", new()
        {
            FilterOn = true, FilterLowHz = 250, FilterHighHz = 2800, BitcrushOn = true, BitcrushBits = 10, BitcrushRateHz = 11000,
            DistortionOn = true, DistortionDrive = 0.15, CompressorOn = true, CompressorAmount = 0.6,
        }),
        P(R, "Astronaute", "Houston, on a un problème", '', "surge", new()
        {
            RadioOn = true, CompressorOn = true, CompressorAmount = 0.8, EchoOn = true, EchoTimeMs = 90, EchoFeedback = 0.15, EchoMix = 0.12,
        }),

        // ---------- Lieux ----------
        P(L, "Grotte", "Écho… écho…", '', "violet", new()
        {
            EchoOn = true, EchoTimeMs = 380, EchoFeedback = 0.5, EchoMix = 0.4, ReverbOn = true, ReverbSize = 0.6, ReverbMix = 0.2,
        }),
        P(L, "Stade", "Grosse salle", '', "neon", new() { ReverbOn = true, ReverbSize = 0.95, ReverbMix = 0.45 }),
        P(L, "Cathédrale", "Réverbération immense", '', "violet", new()
        {
            ReverbOn = true, ReverbSize = 1, ReverbMix = 0.55, EqOn = true, EqHighDb = -2,
        }),
        P(L, "Salle de bain", "Petite pièce carrelée", '', "surge", new()
        {
            ReverbOn = true, ReverbSize = 0.35, ReverbMix = 0.35, EqOn = true, EqPresenceDb = 2,
        }),
        P(L, "Montagne", "Ohééé… ééé…", '', "boost", new()
        {
            EchoOn = true, EchoTimeMs = 650, EchoFeedback = 0.45, EchoMix = 0.35, ReverbOn = true, ReverbSize = 0.5, ReverbMix = 0.12,
        }),
        P(L, "Sous l'eau", "Blub blub", '', "surge", new()
        {
            FilterOn = true, FilterLowHz = 80, FilterHighHz = 550, ChorusOn = true, ChorusRate = 0.6, ChorusDepth = 0.8, ChorusMix = 0.6,
            VibratoOn = true, VibratoRate = 1.8, VibratoDepth = 0.35,
        }),
        P(L, "Derrière le mur", "Chez le voisin", '', "paper", new()
        {
            FilterOn = true, FilterLowHz = 60, FilterHighHz = 450, EqOn = true, EqLowDb = 3,
        }),

        // ---------- Fun ----------
        P(F, "Hélium", "Ballon de baudruche", '', "neon", new()
        {
            PitchSemitones = 3, KeepFormants = true, FormantSemitones = 7,
        }),
        P(F, "Bébé", "Areuh areuh", '', "neon", new()
        {
            PitchSemitones = 7, KeepFormants = true, FormantSemitones = 5, VibratoOn = true, VibratoRate = 6, VibratoDepth = 0.12,
        }),
        P(F, "Papy", "Voix tremblotante", '', "paper", new()
        {
            PitchSemitones = -1, KeepFormants = true, FormantSemitones = -1, VibratoOn = true, VibratoRate = 6.5, VibratoDepth = 0.4,
            WhisperOn = true, WhisperAmount = 0.15, EqOn = true, EqLowDb = -3, EqHighDb = -2,
        }),
        P(F, "Chuchotement", "Psst…", '', "violet", new()
        {
            WhisperOn = true, WhisperAmount = 1, EqOn = true, EqLowDb = -6, EqPresenceDb = 3, CompressorOn = true, CompressorAmount = 0.6,
        }),
        P(F, "Chorale", "Toute une foule", '', "boost", new()
        {
            HarmonyOn = true, HarmonySemitones = 7, HarmonyMix = 0.4, ChorusOn = true, ChorusDepth = 0.7, ChorusMix = 0.6,
            ReverbOn = true, ReverbSize = 0.8, ReverbMix = 0.3,
        }),
        P(F, "Clone", "Toi en double", '', "surge", new()
        {
            ChorusOn = true, ChorusRate = 0.4, ChorusDepth = 0.9, ChorusMix = 0.8, EchoOn = true, EchoTimeMs = 60, EchoFeedback = 0.1, EchoMix = 0.3,
        }),
    ];
}
