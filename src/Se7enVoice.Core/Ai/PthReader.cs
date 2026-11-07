using System.IO.Compression;
using System.Text;

namespace Se7enVoice.Core.Ai;

/// <summary>Un tenseur lu dans un fichier PyTorch : forme et valeurs (converties en float, rangées en ligne).</summary>
public sealed record PthTensor(int[] Shape, float[] Data)
{
    public long Count => Data.LongLength;
}

/// <summary>
/// Lecteur SÛR des fichiers PyTorch « .pth » (format zip de torch.save) : il ne fait que décoder les données du pickle
/// (dictionnaires, listes, nombres, textes, tenseurs). Aucune fonction n'est appelée : seules quelques références
/// connues sont acceptées (OrderedDict, reconstruction de tenseur, types de stockage) ; tout autre « global » fait
/// échouer la lecture. Un fichier piégé ne peut donc rien exécuter.
/// </summary>
public static class PthReader
{
    private sealed record Global(string Module, string Name);
    private sealed record StorageRef(string Type, string Key, long Count);
    private sealed class Mark;
    private static readonly Mark TheMark = new();

    public static object? Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var pkl = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/data.pkl", StringComparison.Ordinal) || e.FullName == "data.pkl")
                  ?? throw new InvalidDataException("Ce n'est pas un fichier PyTorch récent (zip sans data.pkl).");
        string prefix = pkl.FullName[..^"data.pkl".Length];
        byte[] data;
        using (var s = pkl.Open()) data = ReadAll(s);
        var storages = new Dictionary<string, byte[]>();
        byte[] Storage(string key)
        {
            if (storages.TryGetValue(key, out var b)) return b;
            var e = zip.GetEntry(prefix + "data/" + key) ?? throw new InvalidDataException($"Stockage {key} absent.");
            using var s = e.Open();
            return storages[key] = ReadAll(s);
        }
        return new Unpickler(data, Storage).Load();
    }

    private static byte[] ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private sealed class Unpickler(byte[] d, Func<string, byte[]> storage)
    {
        private int _p;
        private readonly List<object?> _stack = [];
        private readonly Dictionary<long, object?> _memo = [];

        public object? Load()
        {
            while (true)
            {
                byte op = d[_p++];
                switch (op)
                {
                    case 0x80: _p++; break;                                   // PROTO
                    case 0x95: _p += 8; break;                                // FRAME
                    case (byte)'.': return Pop();                             // STOP
                    case (byte)'}': Push(new Dictionary<object, object?>()); break; // EMPTY_DICT
                    case (byte)']': Push(new List<object?>()); break;         // EMPTY_LIST
                    case (byte)')': Push(Array.Empty<object?>()); break;      // EMPTY_TUPLE
                    case (byte)'(': Push(TheMark); break;                     // MARK
                    case (byte)'N': Push(null); break;
                    case 0x88: Push(true); break;
                    case 0x89: Push(false); break;
                    case (byte)'J': Push((long)BitConverter.ToInt32(d, _p)); _p += 4; break; // BININT
                    case (byte)'K': Push((long)d[_p++]); break;               // BININT1
                    case (byte)'M': Push((long)BitConverter.ToUInt16(d, _p)); _p += 2; break; // BININT2
                    case 0x8a:                                                // LONG1
                    {
                        int n = d[_p++];
                        long v = 0;
                        for (int i = 0; i < n; i++) v |= (long)d[_p + i] << (8 * i);
                        if (n > 0 && n < 8 && (d[_p + n - 1] & 0x80) != 0) v -= 1L << (8 * n);
                        _p += n;
                        Push(v);
                        break;
                    }
                    case (byte)'G':                                           // BINFLOAT (big-endian)
                    {
                        var b = d.AsSpan(_p, 8).ToArray();
                        Array.Reverse(b);
                        Push(BitConverter.ToDouble(b));
                        _p += 8;
                        break;
                    }
                    case (byte)'X': { int n = BitConverter.ToInt32(d, _p); _p += 4; Push(Encoding.UTF8.GetString(d, _p, n)); _p += n; break; }
                    case 0x8c: { int n = d[_p++]; Push(Encoding.UTF8.GetString(d, _p, n)); _p += n; break; } // SHORT_BINUNICODE
                    case (byte)'C': { int n = d[_p++]; Push(d.AsSpan(_p, n).ToArray()); _p += n; break; }     // SHORT_BINBYTES
                    case (byte)'B': { int n = BitConverter.ToInt32(d, _p); _p += 4; Push(d.AsSpan(_p, n).ToArray()); _p += n; break; }
                    case (byte)'q': _memo[d[_p++]] = Peek(); break;           // BINPUT
                    case (byte)'r': _memo[BitConverter.ToUInt32(d, _p)] = Peek(); _p += 4; break; // LONG_BINPUT
                    case 0x94: _memo[_memo.Count] = Peek(); break;            // MEMOIZE
                    case (byte)'h': Push(_memo[d[_p++]]); break;              // BINGET
                    case (byte)'j': Push(_memo[BitConverter.ToUInt32(d, _p)]); _p += 4; break; // LONG_BINGET
                    case (byte)'c':                                           // GLOBAL
                    {
                        string module = Line(), name = Line();
                        Push(Allowed(module, name));
                        break;
                    }
                    case 0x93: { string name = (string)Pop()!, module = (string)Pop()!; Push(Allowed(module, name)); break; } // STACK_GLOBAL
                    case (byte)'t': Push(PopToMark().ToArray()); break;       // TUPLE
                    case 0x85: Push(new[] { Pop() }); break;                  // TUPLE1
                    case 0x86: { var b = Pop(); var a = Pop(); Push(new[] { a, b }); break; }
                    case 0x87: { var c = Pop(); var b = Pop(); var a = Pop(); Push(new[] { a, b, c }); break; }
                    case (byte)'a': { var v = Pop(); ((List<object?>)Peek()!).Add(v); break; } // APPEND
                    case (byte)'e': { var items = PopToMark(); ((List<object?>)Peek()!).AddRange(items); break; } // APPENDS
                    case (byte)'s': { var v = Pop(); var k = Pop()!; ((Dictionary<object, object?>)Peek()!)[k] = v; break; } // SETITEM
                    case (byte)'u':                                           // SETITEMS
                    {
                        var items = PopToMark();
                        var dict = (Dictionary<object, object?>)Peek()!;
                        for (int i = 0; i + 1 < items.Count; i += 2) dict[items[i]!] = items[i + 1];
                        break;
                    }
                    case (byte)'Q': Push(PersistentLoad(Pop())); break;       // BINPERSID
                    case (byte)'R': { var args = (object?[])Pop()!; var f = (Global)Pop()!; Push(Reduce(f, args)); break; } // REDUCE
                    case 0x81: { var args = (object?[])Pop()!; var cls = (Global)Pop()!; Push(Reduce(cls, args)); break; } // NEWOBJ
                    case (byte)'b': Pop(); break;                             // BUILD : état ignoré (attributs d'objets)
                    default: throw new InvalidDataException($"Instruction pickle non prise en charge : 0x{op:X2}.");
                }
            }
        }

        private static Global Allowed(string module, string name) => (module, name) switch
        {
            ("collections", "OrderedDict") or ("torch._utils", "_rebuild_tensor_v2") or ("torch._utils", "_rebuild_parameter") => new Global(module, name),
            ("torch", _) when name.EndsWith("Storage", StringComparison.Ordinal) => new Global(module, name),
            _ => throw new InvalidDataException($"Contenu refusé dans le fichier ({module}.{name}) : ce n'est pas un modèle de voix ordinaire."),
        };

        private object PersistentLoad(object? pid)
        {
            // ('storage', <type>, clé, emplacement, nombre d'éléments)
            if (pid is object?[] { Length: >= 5 } t && t[0] as string == "storage" && t[1] is Global g)
                return new StorageRef(g.Name, Convert.ToString(t[2])!, Convert.ToInt64(t[4]));
            throw new InvalidDataException("Référence de stockage inattendue.");
        }

        private object? Reduce(Global f, object?[] args)
        {
            switch (f.Name)
            {
                case "OrderedDict": return new Dictionary<object, object?>();
                case "_rebuild_parameter": return args[0];
                case "_rebuild_tensor_v2":
                {
                    var st = (StorageRef)args[0]!;
                    long offset = Convert.ToInt64(args[1]);
                    var shape = ((object?[])args[2]!).Select(Convert.ToInt32).ToArray();
                    var stride = ((object?[])args[3]!).Select(Convert.ToInt64).ToArray();
                    return Tensor(st, offset, shape, stride);
                }
                default: throw new InvalidDataException($"Appel refusé : {f.Name}.");
            }
        }

        private PthTensor Tensor(StorageRef st, long offset, int[] shape, long[] stride)
        {
            var raw = storage(st.Key);
            Func<long, float> get = st.Type switch
            {
                "HalfStorage" => i => (float)BitConverter.ToHalf(raw, (int)(i * 2)),
                "FloatStorage" => i => BitConverter.ToSingle(raw, (int)(i * 4)),
                "BFloat16Storage" => i => BitConverter.Int32BitsToSingle(BitConverter.ToUInt16(raw, (int)(i * 2)) << 16),
                "DoubleStorage" => i => (float)BitConverter.ToDouble(raw, (int)(i * 8)),
                _ => throw new InvalidDataException($"Type de nombres non pris en charge : {st.Type}."),
            };
            long count = shape.Aggregate(1L, (a, b) => a * b);
            var outData = new float[count];
            var idx = new int[shape.Length];
            for (long n = 0; n < count; n++)
            {
                long src = offset;
                for (int k = 0; k < shape.Length; k++) src += idx[k] * stride[k];
                outData[n] = get(src);
                for (int k = shape.Length - 1; k >= 0; k--)
                {
                    if (++idx[k] < shape[k]) break;
                    idx[k] = 0;
                }
            }
            return new PthTensor(shape, outData);
        }

        private string Line()
        {
            int start = _p;
            while (d[_p] != (byte)'\n') _p++;
            return Encoding.ASCII.GetString(d, start, _p++ - start);
        }

        private void Push(object? o) => _stack.Add(o);
        private object? Peek() => _stack[^1];

        private object? Pop()
        {
            var o = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return o;
        }

        private List<object?> PopToMark()
        {
            int i = _stack.LastIndexOf(TheMark);
            if (i < 0) throw new InvalidDataException("Pickle mal formé (marque absente).");
            var items = _stack.GetRange(i + 1, _stack.Count - i - 1);
            _stack.RemoveRange(i, _stack.Count - i);
            return items;
        }
    }
}

/// <summary>Ce qu'on sait d'un modèle de voix RVC importé, et ses poids prêts pour le moteur ONNX.</summary>
public sealed record RvcVoiceModel(int SampleRate, string Version, bool HasPitch, int Speakers, IReadOnlyList<(string Name, PthTensor Value)> Weights);

/// <summary>
/// Modèle RVC (« .pth » partagé, format léger de RVC) → poids nommés comme dans nos générateurs ONNX : préfixe
/// « net. », normalisation des poids repliée (w = g · v / ‖v‖), deux matrices transposées par l'export ; l'encodeur
/// postérieur (entraînement seulement) est ignoré.
/// </summary>
public static class RvcVoiceImport
{
    /// <summary>Noms donnés par l'export ONNX aux deux poids « Linear » (identiques pour nos générateurs 32, 40 et 48 kHz).</summary>
    public const string PhoneMatMul = "onnx::MatMul_4267", SourceMatMul = "onnx::MatMul_4956";

    public static RvcVoiceModel Load(string path)
    {
        if (PthReader.Read(path) is not Dictionary<object, object?> root)
            throw new InvalidDataException("Fichier non reconnu.");
        // modèle léger de RVC : {"weight": {...}, "sr": "40k", "f0": 1, "version": "v2"} ; point de contrôle : {"model": {...}}
        var weights = (root.GetValueOrDefault("weight") ?? root.GetValueOrDefault("model")) as Dictionary<object, object?>
                      ?? throw new InvalidDataException("Ce fichier ne contient pas de voix RVC.");
        string version = root.GetValueOrDefault("version") as string ?? "v1";
        bool f0 = root.GetValueOrDefault("f0") is not long f || f == 1;
        int rate = (root.GetValueOrDefault("sr") as string) switch
        {
            "32k" => 32000, "40k" => 40000, "48k" => 48000,
            _ => root.GetValueOrDefault("config") is List<object?> cfg && cfg.Count > 0 && cfg[^1] is long sr ? (int)sr : 0,
        };
        var t = weights.Where(kv => kv.Value is PthTensor).ToDictionary(kv => (string)kv.Key, kv => (PthTensor)kv.Value!);
        if (t.TryGetValue("enc_p.emb_phone.weight", out var phone) && phone.Shape.Length == 2 && phone.Shape[1] == 256) version = "v1";

        var list = new List<(string, PthTensor)>();
        foreach (var (k, v) in t)
        {
            if (k.StartsWith("enc_q.", StringComparison.Ordinal) || k.EndsWith(".weight_g", StringComparison.Ordinal)) continue;
            if (k.EndsWith(".weight_v", StringComparison.Ordinal))
            {
                string name = k[..^2];
                list.Add(("net." + name, Fold(t[name + "_g"], v)));
            }
            else if (k == "enc_p.emb_phone.weight") list.Add((PhoneMatMul, Transpose(v)));
            else if (k == "dec.m_source.l_linear.weight") list.Add((SourceMatMul, Transpose(v)));
            else list.Add(("net." + k, v));
        }
        int speakers = t.TryGetValue("emb_g.weight", out var emb) ? emb.Shape[0] : 0;
        return new RvcVoiceModel(rate, version, f0, speakers, list);
    }

    /// <summary>Normalisation des poids : w = g · v / ‖v‖, la norme portant sur toutes les dimensions sauf la première.</summary>
    public static PthTensor Fold(PthTensor g, PthTensor v)
    {
        int rows = v.Shape[0];
        int per = (int)(v.Count / rows);
        var w = new float[v.Count];
        for (int r = 0; r < rows; r++)
        {
            double n = 0;
            for (int i = 0; i < per; i++) n += v.Data[r * per + i] * (double)v.Data[r * per + i];
            double scale = g.Data[r] / Math.Max(Math.Sqrt(n), 1e-12);
            for (int i = 0; i < per; i++) w[r * per + i] = (float)(v.Data[r * per + i] * scale);
        }
        return new PthTensor(v.Shape, w);
    }

    public static PthTensor Transpose(PthTensor m)
    {
        int a = m.Shape[0], b = m.Shape[1];
        var o = new float[m.Count];
        for (int i = 0; i < a; i++)
            for (int j = 0; j < b; j++) o[j * a + i] = m.Data[i * b + j];
        return new PthTensor([b, a], o);
    }

    /// <summary>Message d'erreur en clair si le modèle ne peut pas servir (null s'il convient).</summary>
    public static string? Problem(RvcVoiceModel m) =>
        m.Version != "v2" ? "C'est une voix RVC v1 : seules les voix RVC v2 sont prises en charge."
        : !m.HasPitch ? "Cette voix n'utilise pas la hauteur (modèle « sans f0 ») : elle n'est pas prise en charge."
        : m.SampleRate is not (32000 or 40000 or 48000) ? "Fréquence de la voix inconnue (32k, 40k ou 48k attendus)."
        : m.Weights.Count < 300 ? "Ce fichier ne contient pas une voix RVC complète."
        : null;
}
