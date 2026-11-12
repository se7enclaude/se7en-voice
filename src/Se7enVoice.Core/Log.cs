namespace Se7enVoice.Core;

/// <summary>
/// Journal des erreurs (%AppData%\Se7enVoice\log.txt) pour comprendre un plantage ou une panne après coup.
/// Taille bornée : au-delà de 512 Ko, le fichier devient log.old.txt et un nouveau commence. Ne lève jamais d'exception.
/// </summary>
public static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppSettings.Folder, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(AppSettings.Folder, "log.old.txt"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // le journal ne doit jamais faire tomber l'appli
        }
    }

    public static void Error(string context, Exception ex) => Write($"{context} : {ex}");
}
