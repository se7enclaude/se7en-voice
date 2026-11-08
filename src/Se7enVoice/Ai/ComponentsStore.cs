using System.IO;
using Se7enVoice.Core.Updates;

namespace Se7enVoice.Ai;

/// <summary>Le fichier %LocalAppData%\Se7enVoice\composants.json, partagé par les modèles IA et Seven Microphone.</summary>
public static class ComponentsStore
{
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Se7enVoice", "composants.json");

    private static readonly object Lock = new();
    private static ComponentsFile? _current;

    public static ComponentsFile Current
    {
        get { lock (Lock) return _current ??= ComponentsFile.Load(Path); }
    }

    public static void Update(Action<ComponentsFile> change)
    {
        lock (Lock)
        {
            _current ??= ComponentsFile.Load(Path);
            change(_current);
            try { _current.Save(Path); } catch (IOException) { /* réessayé à la prochaine modification */ }
        }
    }
}
