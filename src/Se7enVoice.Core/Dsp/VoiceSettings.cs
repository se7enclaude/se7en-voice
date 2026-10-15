namespace Se7enVoice.Core.Dsp;

/// <summary>
/// Tous les réglages de la voix. Objet immuable : l'interface en crée une copie modifiée
/// (avec <c>with</c>) et le thread audio l'applique au début du bloc suivant.
/// Les champs ajoutés après la 1.0 ont des valeurs par défaut « éteint » : les anciens presets
/// et réglages se rechargent à l'identique.
/// </summary>
public sealed record VoiceSettings
{
    // Base
    public double VolumeDb { get; init; } = 0;          // -12 .. +20
    public double PitchSemitones { get; init; } = 0;    // -12 .. +12
    /// <summary>« Voix plus grave » : pitch léger + poitrine + présence, 0..1.</summary>
    public double Depth { get; init; } = 0;

    // Timbre (formants)
    /// <summary>Gabarit de la voix en demi-tons : négatif = plus grand/adulte, positif = plus petit/féminin. -12..+12</summary>
    public double FormantSemitones { get; init; }
    /// <summary>Garde le timbre naturel quand on change la hauteur (pas d'effet « chipmunk »).</summary>
    public bool KeepFormants { get; init; }

    // Noise gate
    public bool GateOn { get; init; } = true;
    public double GateThresholdDb { get; init; } = -50; // -80 .. -20

    // Suppression de bruit spectrale
    public bool DenoiseOn { get; init; }
    public double DenoiseAmount { get; init; } = 0.6;   // 0..1

    // Coupe-bas
    public bool LowCutOn { get; init; }
    public double LowCutHz { get; init; } = 90;         // 20..300

    // Égaliseur 5 bandes
    public bool EqOn { get; init; }
    public double EqLowDb { get; init; }
    public double EqLowMidDb { get; init; }
    public double EqMidDb { get; init; }
    public double EqPresenceDb { get; init; }
    public double EqHighDb { get; init; }

    // De-esser
    public bool DeEsserOn { get; init; }
    public double DeEsserAmount { get; init; } = 0.5;   // 0..1

    // Compresseur
    public bool CompressorOn { get; init; }
    public double CompressorAmount { get; init; } = 0.5; // 0..1

    // Autotune
    public bool AutotuneOn { get; init; }
    public int AutotuneKey { get; init; }               // 0 = Do … 11 = Si
    public int AutotuneScale { get; init; }             // 0 chromatique, 1 majeure, 2 mineure, 3 pentatonique
    public double AutotuneSpeed { get; init; } = 1;     // 0..1

    // Harmoniseur
    public bool HarmonyOn { get; init; }
    public double HarmonySemitones { get; init; } = -12; // -12..+12
    public double HarmonyMix { get; init; } = 0.5;       // 0..1

    // Robot / alien (modulation en anneau)
    public bool RobotOn { get; init; }
    public double RobotFrequency { get; init; } = 60;   // Hz 20..800
    public double RobotMix { get; init; } = 1;

    // Chuchotement
    public bool WhisperOn { get; init; }
    public double WhisperAmount { get; init; } = 0.8;   // 0..1

    // Vibrato
    public bool VibratoOn { get; init; }
    public double VibratoRate { get; init; } = 5;       // Hz 0.5..12
    public double VibratoDepth { get; init; } = 0.3;    // 0..1

    // Distorsion
    public bool DistortionOn { get; init; }
    public double DistortionDrive { get; init; } = 0.4; // 0..1

    // 8-bit
    public bool BitcrushOn { get; init; }
    public double BitcrushBits { get; init; } = 8;      // 2..16
    public double BitcrushRateHz { get; init; } = 8000; // 1000..24000

    // Radio / talkie
    public bool RadioOn { get; init; }

    // Filtre passe-bande
    public bool FilterOn { get; init; }
    public double FilterLowHz { get; init; } = 300;     // 20..4000
    public double FilterHighHz { get; init; } = 3400;   // 300..20000

    // Chorus
    public bool ChorusOn { get; init; }
    public double ChorusRate { get; init; } = 0.8;      // Hz
    public double ChorusDepth { get; init; } = 0.5;     // 0..1
    public double ChorusMix { get; init; } = 0.5;       // 0..1

    // Écho
    public bool EchoOn { get; init; }
    public double EchoTimeMs { get; init; } = 280;      // 40..1000
    public double EchoFeedback { get; init; } = 0.4;    // 0..0.9
    public double EchoMix { get; init; } = 0.35;        // 0..1

    // Réverbération
    public bool ReverbOn { get; init; }
    public double ReverbSize { get; init; } = 0.7;      // 0..1
    public double ReverbMix { get; init; } = 0.3;       // 0..1
}
