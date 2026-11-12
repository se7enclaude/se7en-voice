using Se7enVoice.Core.Ai;

namespace Se7enVoice.Tests;

public class OnnxPatchTests
{
    private static byte[] Model() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "petit_modele.onnx"));

    private static float[] Read(byte[] model, string name)
    {
        var s = OnnxPatch.Index(model)[name];
        var f = new float[s.Length / 4];
        Buffer.BlockCopy(model, (int)s.Offset, f, 0, s.Length);
        return f;
    }

    [Fact]
    public void Finds_the_weights_of_the_model()
    {
        var idx = OnnxPatch.Index(Model());
        Assert.Equal([2L, 3L], idx["net.emb_g.weight"].Dims);
        Assert.Equal([1.5f, -2.5f, 3.5f], Read(Model(), "net.dec.bias"));
        Assert.Equal([0f, 1, 2, 3, 4, 5], Read(Model(), "net.emb_g.weight"));
    }

    [Fact]
    public void Replaces_weights_byte_for_byte_and_fits_the_voice_table()
    {
        var original = Model();
        var patched = OnnxPatch.Apply(original,
        [
            ("net.dec.bias", new PthTensor([3], [7f, 8f, 9f])),
            ("net.emb_g.weight", new PthTensor([1, 3], [10f, 11f, 12f])),   // une seule voix : complétée par des zéros
        ]);
        Assert.Equal(original.Length, patched.Length);
        Assert.Equal([7f, 8f, 9f], Read(patched, "net.dec.bias"));
        Assert.Equal([10f, 11f, 12f, 0, 0, 0], Read(patched, "net.emb_g.weight"));
        Assert.Equal([1.5f, -2.5f, 3.5f], Read(original, "net.dec.bias"));        // l'original n'est pas touché
    }

    [Fact]
    public void Patching_in_place_gives_the_same_bytes_as_a_copy()
    {
        (string, PthTensor)[] weights =
        [
            ("net.dec.bias", new PthTensor([3], [7f, 8f, 9f])),
            ("net.emb_g.weight", new PthTensor([1, 3], [10f, 11f, 12f])),
        ];
        var copy = OnnxPatch.Apply(Model(), weights);
        var inPlace = Model();
        OnnxPatch.ApplyInPlace(inPlace, weights);
        Assert.Equal(copy, inPlace);
    }

    [Fact]
    public void Refuses_a_voice_of_another_architecture()
    {
        Assert.Throws<InvalidDataException>(() => OnnxPatch.Apply(Model(), [("net.inconnu", new PthTensor([3], [1f, 2f, 3f]))]));
        Assert.Throws<InvalidDataException>(() => OnnxPatch.Apply(Model(), [("net.dec.bias", new PthTensor([4], [1f, 2f, 3f, 4f]))]));
    }
}
