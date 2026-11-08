using Se7enVoice.Core.Updates;

namespace Se7enVoice.Tests;

public class ComponentStatusTests
{
    [Fact]
    public void Compares_driver_versions_digit_by_digit()
    {
        Assert.True(ComponentStatus.CompareVersions("3.3.1.7", "3.5.2.0") < 0);
        Assert.True(ComponentStatus.CompareVersions("3.10.0.0", "3.9.9.9") > 0);
        Assert.Equal(0, ComponentStatus.CompareVersions("3.5.2", "3.5.2.0"));
    }

    [Fact]
    public void Finds_the_newest_pack_and_the_driver_version_of_its_inf()
    {
        const string html = "<a href=\"https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip\">new</a>"
                            + " old: VBCABLE_Driver_Pack43.zip VBCABLE_Driver_Pack45.zip";
        Assert.Equal(46, ComponentStatus.LatestPack(html));
        Assert.Null(ComponentStatus.LatestPack("page sans lien"));
        Assert.Equal("3.5.2.0", ComponentStatus.InfDriverVersion("[Version]\r\nSignature=\"$Windows NT$\"\r\nDriverVer = 10/31/2026,3.5.2.0\r\n"));
        Assert.Equal("1.0.3.5", ComponentStatus.InfDriverVersion("DriverVer=09/02/2014,1.0.3.5"));
    }

    [Fact]
    public void Says_clearly_whether_seven_microphone_is_up_to_date()
    {
        var f = new ComponentsFile { CableLatestPack = 46, CableLatestDriver = "3.5.2.0", CableCheckedAt = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc) };
        Assert.Equal(ComponentState.UpdateAvailable, ComponentStatus.CableState(true, "3.3.1.7", f.CableLatestDriver));
        Assert.Equal("Mise à jour disponible : pilote 3.3.1.7 → 3.5.2.0 (Pack 46)", ComponentStatus.CableText(true, "3.3.1.7", f));
        Assert.Equal("À jour (pilote 3.5.2.0, Pack 46, vérifié le 9 nov. 2026)", ComponentStatus.CableText(true, "3.5.2.0", f));
        Assert.Equal("Pas installé", ComponentStatus.CableText(false, null, f));
        Assert.Contains("pas encore vérifiée", ComponentStatus.CableText(true, "3.3.1.7", new ComponentsFile()));
    }

    [Fact]
    public void Checks_at_most_every_12_hours()
    {
        var now = new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(ComponentStatus.Due(null, now));
        Assert.False(ComponentStatus.Due(now.AddHours(-11), now));
        Assert.True(ComponentStatus.Due(now.AddHours(-12), now));
    }

    private static readonly (string, long, string)[] Expected = [("a.onnx", 10, "aa"), ("b.onnx", 20, "bb")];

    [Fact]
    public void Ai_models_are_up_to_date_only_with_the_expected_fingerprints()
    {
        var at = new DateTime(2026, 11, 9, 10, 0, 0, DateTimeKind.Utc);
        var verified = new Dictionary<string, VerifiedFile> { ["a.onnx"] = new("aa", 10, at), ["b.onnx"] = new("bb", 20, at) };
        long? Both(string n) => n == "a.onnx" ? 10 : 20;
        Assert.Equal(ComponentState.UpToDate, ComponentStatus.AiState(Expected, verified, Both));
        Assert.Equal("Modèles à jour (voix-ia-1, vérifiés le 9 nov. 2026)", ComponentStatus.AiText(ComponentState.UpToDate, "voix-ia-1", verified));
        // une nouvelle version de se7en Voice attend d'autres modèles : mise à jour (et seulement de ce fichier)
        var older = new Dictionary<string, VerifiedFile>(verified) { ["b.onnx"] = new("old", 20, at) };
        Assert.Equal(ComponentState.UpdateAvailable, ComponentStatus.AiState(Expected, older, Both));
        // installés avant le suivi : pas encore vérifiés
        Assert.Equal(ComponentState.Unknown, ComponentStatus.AiState(Expected, new Dictionary<string, VerifiedFile>(), Both));
        Assert.Equal(ComponentState.Missing, ComponentStatus.AiState(Expected, verified, _ => null));
    }

    [Fact]
    public void State_file_survives_a_round_trip_and_a_broken_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "se7en-voice-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "composants.json");
        try
        {
            var f = new ComponentsFile { CableLatestPack = 46, CableLatestDriver = "3.5.2.0" };
            f.AiFiles["x.onnx"] = new VerifiedFile("abc", 5, DateTime.UtcNow);
            f.Save(path);
            var back = ComponentsFile.Load(path);
            Assert.Equal(46, back.CableLatestPack);
            Assert.Equal("abc", back.AiFiles["x.onnx"].Sha256);
            File.WriteAllText(path, "{ abîmé");
            Assert.Empty(ComponentsFile.Load(path).AiFiles);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
