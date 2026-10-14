namespace Se7enVoice.Core.Dsp;

/// <summary>
/// Tous les réglages de la voix. Objet immuable : l'interface en crée une copie modifiée
/// (avec <c>with</c>) et le thread audio l'applique au début du bloc suivant.
/// </summary>
public sealed record VoiceSettings
{
    // Base
    public double VolumeDb { get; init; } = 0;          // -12 .. +20
    public double PitchSemitones { get; init; } = 0;    // -12 .. +12
    /// <summary>« Voix plus grave » : pitch léger + poitrine + présence, 0..1.</summary>
    public double Depth { get; init; } = 0;

    // Noise gate
    public bool GateOn { get; init; } = true;
    public double GateThresholdDb { get; init; } = -50; // -80 .. -20

    // Égaliseur 3 bandes
    public bool EqOn { get; init; }
    public double EqLowDb { get; init; }
    public double EqMidDb { get; init; }
    public double EqHighDb { get; init; }

    // Compresseur
    public bool CompressorOn { get; init; }
    public double CompressorAmount { get; init; } = 0.5; // 0..1

    // Robot / alien (modulation en anneau)
    public bool RobotOn { get; init; }
    public double RobotFrequency { get; init; } = 60;   // Hz 20..800
    public double RobotMix { get; init; } = 1;

    // Distorsion
    public bool DistortionOn { get; init; }
    public double DistortionDrive { get; init; } = 0.4; // 0..1

    // Radio / talkie
    public bool RadioOn { get; init; }

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
