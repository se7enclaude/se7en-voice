using System.IO;
using Se7enVoice.Ai;
using Se7enVoice.Core.Ai;

namespace Se7enVoice.ViewModels;

/// <summary>
/// « Importer une voix » : une voix RVC v2 (.pth) entraînée sur votre voix ou celle d'un ami qui est d'accord.
/// Le fichier est lu sans exécuter quoi que ce soit, vérifié, puis transformé en générateur pour la carte graphique.
/// </summary>
public sealed class VoiceImport(MainViewModel vm) : Observable
{
    private RvcVoiceModel? _model;
    private string _name = "", _file = "", _info = "", _error = "", _status = "";
    private bool _open, _busy, _high, _consent;
    private double _progress;

    public bool IsOpen { get => _open; private set => Set(ref _open, value); }
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) Raise(nameof(CanImport)); } }
    public bool HasModel => _model != null;
    public string FileName { get => _file; private set => Set(ref _file, value); }
    public string Info { get => _info; private set => Set(ref _info, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public string Name { get => _name; set { if (Set(ref _name, value)) Raise(nameof(CanImport)); } }
    /// <summary>Voix aiguë (≈ 210 Hz) plutôt que grave (≈ 115 Hz) : sert à la hauteur automatique.</summary>
    public bool IsHigh { get => _high; set { if (Set(ref _high, value)) Raise(nameof(IsLow)); } }
    public bool IsLow { get => !_high; set => IsHigh = !value; }
    /// <summary>« Cette voix est la mienne, ou la personne m'a donné son accord. » Obligatoire.</summary>
    public bool Consent { get => _consent; set { if (Set(ref _consent, value)) Raise(nameof(CanImport)); } }
    public bool CanImport => HasModel && Consent && !Busy && Name.Trim().Length > 0;

    /// <summary>Ouvre la fenêtre avec le fichier choisi et le vérifie (≈ 1 s).</summary>
    public async Task OpenAsync(string path)
    {
        _model = null;
        Raise(nameof(HasModel));
        FileName = Path.GetFileName(path);
        Name = Clean(Path.GetFileNameWithoutExtension(path));
        Consent = false;
        IsHigh = false;
        Error = Info = Status = "";
        Progress = 0;
        IsOpen = true;
        Busy = true;
        Status = "Vérification du fichier…";
        try
        {
            _model = await Task.Run(() => AiVoiceImporter.Inspect(path));
            Info = $"Voix RVC v2 · {_model.SampleRate / 1000} kHz · prête à importer";
            Status = "";
        }
        catch (Exception ex)
        {
            Error = ex is InvalidDataException or FileNotFoundException ? ex.Message : "Ce fichier n'a pas pu être lu : " + ex.Message;
            Status = "";
        }
        finally
        {
            Busy = false;
            Raise(nameof(HasModel));
            Raise(nameof(CanImport));
        }
    }

    public async Task ImportAsync()
    {
        if (!CanImport || _model is not { } model) return;
        Busy = true;
        Error = "";
        try
        {
            if (!AiModels.SkeletonReady(model.SampleRate))
            {
                Status = $"Téléchargement du modèle {model.SampleRate / 1000} kHz (une seule fois, ≈ 110 Mo)…";
                await AiModels.DownloadSkeletonAsync(model.SampleRate, new Progress<double>(p => Progress = p), CancellationToken.None);
            }
            Status = "Préparation de la voix…";
            Progress = 0;
            string id = "perso-" + Guid.NewGuid().ToString("N")[..10];
            string file = await Task.Run(() => AiVoiceImporter.Build(model, id));
            var voice = new CustomAiVoice
            {
                Id = id,
                Name = Name.Trim(),
                File = file,
                Rate = model.SampleRate,
                TargetHz = IsHigh ? 210 : 115,
            };
            _model = null;
            IsOpen = false;
            vm.AddCustomVoice(voice);
        }
        catch (Exception ex)
        {
            Error = "Import impossible : " + ex.Message;
        }
        finally
        {
            Busy = false;
            Status = "";
        }
    }

    public void Close()
    {
        if (Busy) return;
        _model = null;
        IsOpen = false;
    }

    private static string Clean(string s)
    {
        s = s.Replace('_', ' ').Replace('-', ' ').Trim();
        return s.Length > 24 ? s[..24].Trim() : s;
    }
}
