using System.Text;

namespace Se7enVoice.Core.Ai;

/// <summary>
/// Écrit une copie d'un modèle ONNX dont certains poids (initialiseurs float32 stockés en « raw_data ») sont remplacés,
/// octet pour octet, sans réécrire le reste du fichier : mêmes noms, mêmes formes, mêmes tailles.
/// (Remplacer les poids au chargement — SessionOptions.AddInitializer — n'est pas fiable sur DirectML : la carte
/// graphique garde une partie des poids d'origine.)
/// </summary>
public static class OnnxPatch
{
    /// <summary>Position et taille des données brutes de chaque initialiseur float32 du graphe.</summary>
    public static Dictionary<string, (long Offset, int Length, long[] Dims)> Index(byte[] model)
    {
        var result = new Dictionary<string, (long, int, long[])>();
        foreach (var (field, start, len) in Fields(model, 0, model.Length))
        {
            if (field != 7) continue;                                    // ModelProto.graph
            foreach (var (gf, gs, gl) in Fields(model, start, start + len))
            {
                if (gf != 5) continue;                                   // GraphProto.initializer
                string? name = null;
                long raw = -1;
                int rawLen = 0, type = 0;
                var dims = new List<long>();
                int p = gs, end = gs + gl;
                while (p < end)
                {
                    ulong key = Varint(model, ref p);
                    int f = (int)(key >> 3), wire = (int)(key & 7);
                    if (wire == 0)
                    {
                        ulong v = Varint(model, ref p);
                        if (f == 1) dims.Add((long)v);
                        else if (f == 2) type = (int)v;
                        continue;
                    }
                    if (wire == 2)
                    {
                        int l = (int)Varint(model, ref p);
                        if (f == 8) name = Encoding.UTF8.GetString(model, p, l);
                        else if (f == 9) { raw = p; rawLen = l; }
                        else if (f == 1) { int q = p; while (q < p + l) dims.Add((long)Varint(model, ref q)); } // dims « packed »
                        p += l;
                        continue;
                    }
                    p += wire switch { 1 => 8, 5 => 4, _ => throw new InvalidDataException("Modèle ONNX illisible.") };
                }
                if (name != null && raw >= 0 && type == 1) result[name] = (raw, rawLen, dims.ToArray());
            }
        }
        return result;
    }

    /// <summary>
    /// Copie <paramref name="model"/> en remplaçant les poids donnés. Lève une exception si un poids manque ou n'a pas
    /// la forme attendue (voix d'une autre architecture). La table des voix (« net.emb_g.weight ») est ajustée au
    /// nombre de lignes du modèle : seule la voix 0 sert pour une voix importée.
    /// </summary>
    public static byte[] Apply(byte[] model, IEnumerable<(string Name, PthTensor Value)> weights)
    {
        var index = Index(model);
        var copy = (byte[])model.Clone();
        foreach (var (name, value) in weights)
        {
            if (!index.TryGetValue(name, out var slot))
                throw new InvalidDataException($"Poids inattendu dans la voix ({name}) : architecture différente.");
            var data = value.Data;
            int want = slot.Length / 4;
            if (data.Length != want)
            {
                if (name != "net.emb_g.weight" || slot.Dims.Length != 2 || value.Shape.Length != 2 || value.Shape[1] != slot.Dims[1])
                    throw new InvalidDataException($"Le poids {name} n'a pas la taille attendue : architecture différente.");
                var resized = new float[want];
                Array.Copy(data, resized, Math.Min(data.Length, want));
                data = resized;
            }
            Buffer.BlockCopy(data, 0, copy, (int)slot.Offset, slot.Length);
        }
        return copy;
    }

    private static IEnumerable<(int Field, int Start, int Length)> Fields(byte[] d, int start, int end)
    {
        int p = start;
        while (p < end)
        {
            ulong key = Varint(d, ref p);
            int f = (int)(key >> 3), wire = (int)(key & 7);
            switch (wire)
            {
                case 0: Varint(d, ref p); break;
                case 1: p += 8; break;
                case 5: p += 4; break;
                case 2:
                    int l = (int)Varint(d, ref p);
                    yield return (f, p, l);
                    p += l;
                    break;
                default: throw new InvalidDataException("Modèle ONNX illisible.");
            }
        }
    }

    private static ulong Varint(byte[] d, ref int p)
    {
        ulong v = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte b = d[p++];
            v |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80) return v;
        }
    }
}
