using System.Text.Json;

namespace Se7enVoice.Core.Presets;

/// <summary>Presets de l'utilisateur, un fichier JSON par preset.</summary>
public sealed class PresetStore(string directory)
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Directory { get; } = directory;

    public List<Preset> LoadAll()
    {
        var list = new List<Preset>();
        if (!System.IO.Directory.Exists(Directory)) return list;
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            var preset = TryRead(file);
            if (preset != null) list.Add(preset);
        }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(Preset preset)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PathFor(preset.Name), JsonSerializer.Serialize(preset with { BuiltIn = false }, Json));
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    public static void Export(Preset preset, string file) =>
        File.WriteAllText(file, JsonSerializer.Serialize(preset with { BuiltIn = false }, Json));

    public static Preset? TryRead(string file)
    {
        try
        {
            var p = JsonSerializer.Deserialize<Preset>(File.ReadAllText(file), Json);
            return p == null || string.IsNullOrWhiteSpace(p.Name) ? null : p with { BuiltIn = false };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string PathFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        if (safe.Length == 0) safe = "preset";
        return Path.Combine(Directory, safe + ".json");
    }
}
