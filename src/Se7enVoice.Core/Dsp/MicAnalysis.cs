namespace Se7enVoice.Core.Dsp;

/// <summary>Ce que l'assistant micro propose de régler, avec les mesures qui l'expliquent.</summary>
public sealed record MicReport
{
    public bool HeardVoice { get; init; }
    public double NoiseDb { get; init; }        // bruit de fond pendant le silence (dBFS)
    public double SpeechDb { get; init; }       // niveau moyen de la voix (dBFS)
    public double PeakDb { get; init; }         // crête de la voix (dBFS)
    public int ClippedSamples { get; init; }    // échantillons écrêtés (saturation)
    public double LowShare { get; init; }       // part d'énergie sous 90 Hz (grondement, vibrations)
    /// <summary>Correction du volume du micro dans Windows (dB), 0 = ne pas toucher.</summary>
    public double WindowsGainDb { get; init; }
    /// <summary>Réglages proposés (volume, anti-bruit, réduction de bruit, coupe-bas).</summary>
    public required VoiceSettings Settings { get; init; }
    /// <summary>Phrases à montrer, dans l'ordre.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// Assistant micro : 3 s de silence puis quelques secondes de parole. On mesure le bruit de fond, le niveau et les
/// crêtes de la voix, le grondement grave, puis on en déduit le volume du micro dans Windows, le volume de sortie,
/// l'anti-bruit, la réduction de bruit et le coupe-bas. Les autres réglages (la voix choisie) ne sont pas touchés.
/// </summary>
public static class MicAnalysis
{
    public const double TargetSpeechDb = -20;   // niveau moyen visé pour la voix
    public const double MaxPeakDb = -3;         // crête à ne pas dépasser (marge avant saturation)

    public static MicReport Analyze(ReadOnlySpan<float> silence, ReadOnlySpan<float> speech, int sampleRate, VoiceSettings current,
                                    double windowsGainDb = 0, double windowsMinDb = 0, double windowsMaxDb = 0)
    {
        int frame = Math.Max(1, sampleRate / 50); // 20 ms
        var silenceDb = FrameLevels(silence, frame);
        var speechDb = FrameLevels(speech, frame);
        double noise = Percentile(silenceDb, 0.6);
        if (double.IsNegativeInfinity(noise) || silenceDb.Count == 0) noise = -90;

        // trames de voix : nettement au-dessus du bruit de fond
        var voiced = speechDb.Where(d => d > noise + 10 && d > -60).OrderByDescending(d => d).ToList();
        bool heard = voiced.Count >= Math.Max(5, speechDb.Count * 0.15);
        double peak = Peak(speech);
        int clipped = 0;
        foreach (var v in speech) if (Math.Abs(v) >= 0.985f) clipped++;

        var notes = new List<string>();
        if (!heard)
        {
            notes.Add(peak < -45
                ? "On ne vous a presque pas entendu : vérifiez que c'est le bon micro, qu'il n'est pas coupé (bouton sur le casque) et parlez plus près."
                : "Votre voix ne ressort pas du bruit de fond : rapprochez-vous du micro ou réduisez le bruit autour (ventilo, musique), puis recommencez.");
            return new MicReport
            {
                HeardVoice = false, NoiseDb = noise, SpeechDb = voiced.Count > 0 ? voiced.Average() : -90, PeakDb = peak,
                ClippedSamples = clipped, Settings = current, Notes = notes,
            };
        }

        // niveau « actif » : la moitié la plus forte des trames de voix (les fins de mots ne comptent pas)
        double speechLevel = voiced.Take(Math.Max(1, voiced.Count / 2)).Average();
        double low = LowShare(speech, sampleRate);

        // 1) volume du micro dans Windows : viser -20 dB de moyenne sans dépasser -3 dB en crête
        double wanted = Math.Min(TargetSpeechDb - speechLevel, MaxPeakDb - peak);
        if (clipped > sampleRate / 1000) wanted = Math.Min(wanted, -6); // ça sature : on baisse franchement
        double hw = 0;
        if (windowsMaxDb > windowsMinDb && Math.Abs(wanted) >= 3)
            hw = Math.Clamp(wanted, windowsMinDb - windowsGainDb, windowsMaxDb - windowsGainDb);
        hw = Math.Round(hw);
        // le gain matériel monte (ou baisse) le bruit et la voix ensemble
        double n2 = noise + hw, s2 = speechLevel + hw, p2 = peak + hw;

        // 2) le reste en volume logiciel (plafonné par les crêtes)
        double soft = Math.Clamp(Math.Min(TargetSpeechDb - s2, MaxPeakDb - p2), -12, 20);
        soft = Math.Abs(soft) < 1.5 ? 0 : Math.Round(soft);
        double n3 = n2 + soft, s3 = s2 + soft;

        // 3) anti-bruit : entre le bruit et la voix, plus près du bruit
        double snr = s3 - n3;
        double gate = Math.Round(Math.Clamp(n3 + Math.Max(6, snr * 0.35), -80, -20));
        gate = Math.Min(gate, Math.Round(s3 - 12));

        // 4) réduction de bruit selon le bruit de fond restant
        // (elle abîme un peu la voix : seulement si le bruit s'entend vraiment)
        bool denoise = snr < 35 || n3 > -55;
        double amount = snr < 20 || n3 > -44 ? 0.9 : snr < 28 || n3 > -50 ? 0.6 : 0.35;

        // 5) coupe-bas si le grave (vibrations, ventilo, « pop ») pèse lourd
        bool lowCut = low > 0.18 || current.LowCutOn;
        double lowCutHz = low > 0.35 ? 120 : 90;

        var settings = current with
        {
            VolumeDb = soft, GateOn = true, GateThresholdDb = gate,
            DenoiseOn = denoise, DenoiseAmount = denoise ? amount : current.DenoiseAmount,
            LowCutOn = lowCut, LowCutHz = lowCut ? (current.LowCutOn ? Math.Max(current.LowCutHz, lowCutHz) : lowCutHz) : current.LowCutHz,
        };

        if (clipped > sampleRate / 1000) notes.Add("Votre micro saturait (son qui grésille sur les cris) : son volume est baissé.");
        if (hw > 0) notes.Add($"Volume du micro dans Windows monté de {hw:0} dB : votre voix était trop faible.");
        else if (hw < 0 && clipped <= sampleRate / 1000) notes.Add($"Volume du micro dans Windows baissé de {-hw:0} dB : votre voix était trop forte.");
        if (soft > 0) notes.Add($"Voix renforcée de {soft:0} dB" + (hw > 0 || windowsMaxDb <= windowsMinDb ? " (le micro est au maximum de ce qu'il peut)." : "."));
        else if (soft < 0) notes.Add($"Voix baissée de {-soft:0} dB.");
        if (hw == 0 && soft == 0) notes.Add("Niveau de votre voix : parfait, rien à changer.");
        notes.Add($"Anti-bruit réglé à {gate:0} dB : le micro se coupe entre vos phrases, pas pendant.");
        notes.Add(denoise
            ? $"Réduction de bruit {(amount >= 0.9 ? "forte" : amount >= 0.6 ? "moyenne" : "légère")} : il y a du bruit de fond ({n3:0} dB)."
            : "Pièce calme : pas besoin de réduction de bruit (votre voix reste plus naturelle).");
        if (lowCut && !current.LowCutOn) notes.Add($"Coupe-bas à {lowCutHz:0} Hz : il y avait du grondement grave (vibrations, ventilo, souffle).");
        if (snr < 20) notes.Add("Votre voix est proche du bruit de fond : si possible, rapprochez le micro de votre bouche.");

        return new MicReport
        {
            HeardVoice = true, NoiseDb = noise, SpeechDb = speechLevel, PeakDb = peak, ClippedSamples = clipped, LowShare = low,
            WindowsGainDb = hw, Settings = settings, Notes = notes,
        };
    }

    /// <summary>Niveau RMS (dBFS) de chaque trame.</summary>
    public static List<double> FrameLevels(ReadOnlySpan<float> s, int frame)
    {
        var list = new List<double>();
        for (int start = 0; start + frame <= s.Length; start += frame)
        {
            double sum = 0;
            for (int i = start; i < start + frame; i++) sum += s[i] * (double)s[i];
            double rms = Math.Sqrt(sum / frame);
            list.Add(rms > 1e-9 ? 20 * Math.Log10(rms) : -180);
        }
        return list;
    }

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0) return double.NegativeInfinity;
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(int)Math.Clamp(Math.Round(p * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }

    private static double Peak(ReadOnlySpan<float> s)
    {
        float m = 0;
        foreach (var v in s) m = Math.Max(m, Math.Abs(v));
        return m > 1e-9 ? 20 * Math.Log10(m) : -180;
    }

    /// <summary>Part d'énergie sous 90 Hz (filtre passe-bas d'ordre 2 contre l'énergie totale).</summary>
    public static double LowShare(ReadOnlySpan<float> s, int sampleRate)
    {
        var lp = new Biquad();
        lp.SetLowPass(sampleRate, 90);
        double total = 0, low = 0;
        foreach (var v in s)
        {
            double y = lp.Process(v);
            total += v * (double)v;
            low += y * y;
        }
        return total > 1e-12 ? Math.Min(1, low / total) : 0;
    }
}
