using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Se7enVoice.Core.Ai;

namespace Se7enVoice.Tests;

public class PthReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Reads_a_torch_file_like_the_ones_rvc_shares()
    {
        var root = (Dictionary<object, object?>)PthReader.Read(Fixture("voix_test.pth"))!;
        Assert.Equal("48k", root["sr"]);
        Assert.Equal("v2", root["version"]);
        Assert.Equal(1L, root["f0"]);
        var w = (Dictionary<object, object?>)root["weight"]!;
        var expected = JsonDocument.Parse(File.ReadAllText(Fixture("voix_test.json"))).RootElement;
        foreach (var name in new[] { "enc_p.emb_phone.weight", "dec.ups.0.weight_v", "emb_g.weight", "dec.conv_pre.bias", "view.weight" })
        {
            var t = (PthTensor)w[name]!;
            var e = expected.GetProperty(name);
            Assert.Equal(e.GetProperty("shape").EnumerateArray().Select(x => x.GetInt32()), t.Shape);
            var data = e.GetProperty("data").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            Assert.Equal(data.Length, t.Data.Length);
            for (int i = 0; i < data.Length; i++) Assert.Equal(data[i], t.Data[i], 3);   // demi-précision ; « view » vérifie les pas (stride)
        }
    }

    [Fact]
    public void Conversion_folds_weight_norm_transposes_and_drops_training_parts()
    {
        var m = RvcVoiceImport.Load(Fixture("voix_test.pth"));
        Assert.Equal(48000, m.SampleRate);
        Assert.Equal("v2", m.Version);
        Assert.True(m.HasPitch);
        Assert.Equal(5, m.Speakers);
        var byName = m.Weights.ToDictionary(x => x.Name, x => x.Value);
        Assert.DoesNotContain(byName.Keys, k => k.Contains("enc_q") || k.EndsWith("weight_g") || k.EndsWith("weight_v"));
        var expected = JsonDocument.Parse(File.ReadAllText(Fixture("voix_test.json"))).RootElement;
        var folded = expected.GetProperty("_folded").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        var w = byName["net.dec.ups.0.weight"];
        for (int i = 0; i < folded.Length; i++) Assert.Equal(folded[i], w.Data[i], 3);
        var phone = byName[RvcVoiceImport.PhoneMatMul];
        Assert.Equal([3, 4], phone.Shape);                                          // (4,3) transposée
        Assert.Contains("complète", RvcVoiceImport.Problem(m)); // fichier de test : trop peu de poids pour une vraie voix
    }

    [Fact]
    public void A_booby_trapped_file_is_refused_without_running_anything()
    {
        // pickle qui appellerait os.system("calc") : doit être refusé à la lecture du « global »
        string path = Path.Combine(Path.GetTempPath(), $"piege-{Guid.NewGuid():N}.pth");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("piege/data.pkl");
            using var s = e.Open();
            var pkl = new List<byte> { 0x80, 2 };
            pkl.AddRange(Encoding.ASCII.GetBytes("cos\nsystem\n"));
            pkl.Add((byte)'X'); pkl.AddRange(BitConverter.GetBytes(4)); pkl.AddRange(Encoding.ASCII.GetBytes("calc"));
            pkl.Add(0x85); pkl.Add((byte)'R'); pkl.Add((byte)'.');
            s.Write(pkl.ToArray());
        }
        var ex = Assert.Throws<InvalidDataException>(() => PthReader.Read(path));
        Assert.Contains("os.system", ex.Message);
        File.Delete(path);
    }
}
