using System.Collections.ObjectModel;
using System.IO;
using Se7enVoice.Ai;
using Se7enVoice.Core.Ai;

namespace Se7enVoice.ViewModels;

/// <summary>Une voix IA dans la liste (carte cliquable).</summary>
public sealed class AiVoiceItem(AiVoiceInfo info) : Observable
{
    private bool _isSelected;
    public AiVoiceInfo Info { get; } = info;
    public string Name => Info.Name;
    public string Description => Info.Description;
    public bool IsCustom => Info.IsCustom;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>Voix IA : téléchargement des modèles, chargement sur la carte graphique, choix de la voix et de la hauteur.</summary>
public sealed partial class MainViewModel
{
    private RvcEngine? _rvc;
    private AiVoice? _ai;
    private bool _aiBusy, _aiAgain, _aiDownloading;
    private double _aiProgress;
    private string _aiStatus = "";
    private int _aiStatusTick;
    private CancellationTokenSource? _aiCancel;

    public ObservableCollection<AiVoiceItem> AiVoices { get; } = new(Core.Ai.AiVoices.All.Select(v => new AiVoiceItem(v)));

    public string AiCredits => Core.Ai.AiVoices.Credits;
    public string AiSizeText => $"{AiModels.TotalSize / 1_000_000} Mo";
    public bool AiReady => AiModels.Ready;
    public bool AiDownloading { get => _aiDownloading; private set { if (Set(ref _aiDownloading, value)) Raise(nameof(AiReady)); } }
    public double AiProgress { get => _aiProgress; private set => Set(ref _aiProgress, value); }
    public string AiStatus { get => _aiStatus; private set => Set(ref _aiStatus, value); }

    public bool AiOn
    {
        get => _settings.AiVoiceOn;
        set
        {
            if (_settings.AiVoiceOn == value) return;
            _settings.AiVoiceOn = value;
            MarkDirty();
            Raise();
            _ = ApplyAiAsync();
        }
    }

    public bool AiPitchAuto
    {
        get => _settings.AiPitchAuto;
        set
        {
            _settings.AiPitchAuto = value;
            MarkDirty();
            Raise();
            UpdateAiParams();
        }
    }

    public double AiSemitones
    {
        get => _settings.AiPitchSemitones;
        set
        {
            _settings.AiPitchSemitones = Math.Round(value);
            MarkDirty();
            Raise();
            UpdateAiParams();
        }
    }

    public void SelectAiVoice(AiVoiceItem item)
    {
        foreach (var v in AiVoices) v.IsSelected = v == item;
        _settings.AiVoiceId = item.Info.Id;
        MarkDirty();
        UpdateAiParams();
        if (!AiOn) AiOn = true;
        else _ = ApplyAiAsync(); // une voix importée a son propre générateur
    }

    public VoiceImport VoiceImport => _voiceImport ??= new VoiceImport(this);
    private VoiceImport? _voiceImport;

    /// <summary>Fin de l'import : la voix rejoint la liste et devient la voix choisie.</summary>
    public void AddCustomVoice(CustomAiVoice voice)
    {
        _settings.AiCustomVoices.Add(voice);
        var item = new AiVoiceItem(voice.ToInfo());
        AiVoices.Add(item);
        SelectAiVoice(item);
        Notify?.Invoke($"Voix « {voice.Name} » importée · elle est maintenant active");
    }

    public void DeleteCustomVoice(AiVoiceItem item)
    {
        if (!item.IsCustom) return;
        _settings.AiCustomVoices.RemoveAll(c => c.Id == item.Info.Id);
        AiVoices.Remove(item);
        bool wasSelected = _settings.AiVoiceId == item.Info.Id;
        if (wasSelected) _settings.AiVoiceId = Core.Ai.AiVoices.All[0].Id;
        MarkDirty();
        UpdateAiParams();
        _ = DeleteVoiceFileAsync(item.Info.File!, wasSelected);
        Notify?.Invoke($"Voix « {item.Name} » supprimée");
    }

    private async Task DeleteVoiceFileAsync(string file, bool wasSelected)
    {
        if (wasSelected) await ApplyAiAsync(); // libère d'abord le fichier en service
        for (int i = 0; i < 10; i++)
        {
            try { if (System.IO.File.Exists(file)) System.IO.File.Delete(file); return; }
            catch (IOException) { await Task.Delay(500); }
            catch (UnauthorizedAccessException) { await Task.Delay(500); }
        }
    }

    private AiVoiceInfo CurrentAiVoice =>
        AiVoices.FirstOrDefault(v => v.Info.Id == _settings.AiVoiceId)?.Info ?? Core.Ai.AiVoices.All[0];

    /// <summary>Premier usage : télécharge les modèles puis allume la voix IA.</summary>
    public async Task DownloadAiAsync()
    {
        if (AiDownloading) return;
        _aiCancel = new CancellationTokenSource();
        AiDownloading = true;
        AiStatus = "Téléchargement des modèles…";
        try
        {
            await AiModels.DownloadAsync(new Progress<double>(p => AiProgress = p), _aiCancel.Token);
            Raise(nameof(AiReady));
            AiOn = true;
        }
        catch (OperationCanceledException)
        {
            AiStatus = "Téléchargement annulé.";
        }
        catch (Exception ex)
        {
            AiStatus = "Téléchargement impossible : " + ex.Message;
            Notify?.Invoke("Voix IA : téléchargement impossible. Vérifiez votre connexion et réessayez.");
        }
        finally
        {
            AiDownloading = false;
        }
    }

    public void CancelAiDownload() => _aiCancel?.Cancel();

    /// <summary>Met la voix IA en route (ou l'arrête) selon le réglage, à la fréquence actuelle du micro.</summary>
    private async Task ApplyAiAsync()
    {
        if (_aiBusy) { _aiAgain = true; return; }
        _aiBusy = true;
        try
        {
          do
          {
            _aiAgain = false;
            if (!AiOn || !Engine.IsRunning || !AiModels.Ready)
            {
                StopAi();
                if (AiOn && !AiModels.Ready) AiStatus = "";
                return;
            }
            if (_rvc == null)
            {
                AiStatus = "Chargement sur la carte graphique…";
                _rvc = await Task.Run(() => RvcEngine.Load(AiModels.Folder));
            }
            // générateur de la voix choisie : voix fournies, ou voix importée (son propre fichier)
            var voice = CurrentAiVoice;
            if (_rvc.GeneratorFile != voice.File)
            {
                AiStatus = voice.IsCustom ? $"Chargement de « {voice.Name} »…" : "Chargement des voix…";
                var rvc = _rvc;
                RvcGenerator g;
                try { g = await Task.Run(() => rvc.OpenGenerator(voice)); }
                catch (Exception ex) when (voice.IsCustom)
                {
                    // voix importée illisible : on revient aux voix fournies
                    _settings.AiVoiceId = Core.Ai.AiVoices.All[0].Id;
                    MarkDirty();
                    UpdateAiParams();
                    Notify?.Invoke($"La voix « {voice.Name} » n'a pas pu être chargée : {ex.Message}");
                    _aiAgain = true;
                    continue;
                }
                bool sameRate = g.Rate == rvc.OutRate;
                if (!sameRate) StopAi(); // la sortie change de fréquence : on recrée la conversion
                rvc.UseGenerator(g);
            }
            if (_ai == null || Engine.Ai == null)
            {
                StopAi();
                _ai = new AiVoice(_rvc, Engine.SampleRate);
                UpdateAiParams();
                Engine.Ai = _ai;
            }
          } while (_aiAgain && AiOn && Engine.IsRunning);
        }
        catch (Exception ex)
        {
            StopAi();
            AiStatus = "Voix IA indisponible : " + ex.Message;
            Notify?.Invoke("La voix IA n'a pas pu démarrer : " + ex.Message);
        }
        finally
        {
            _aiBusy = false;
        }
        Raise(nameof(LatencyText));
    }

    private void StopAi()
    {
        Engine.Ai = null;
        _ai?.Dispose();
        _ai = null;
        if (!AiOn) AiStatus = "";
    }

    private void UpdateAiParams()
    {
        var info = CurrentAiVoice;
        foreach (var v in AiVoices) v.IsSelected = v.Info.Id == info.Id;
        if (_ai == null) return;
        _ai.Sid = info.Sid;
        _ai.TargetHz = info.TargetHz;
        _ai.ManualSemitones = _settings.AiPitchAuto ? null : _settings.AiPitchSemitones;
    }

    /// <summary>Appelé par le minuteur des vu-mètres : met à jour la ligne d'état de la voix IA (2 fois par seconde).</summary>
    private void TickAi()
    {
        if (++_aiStatusTick % 15 != 0) return;
        var ai = _ai;
        if (ai == null || !AiOn) return;
        string pitch = AiPitchAuto ? $"hauteur auto {ai.Semitones:+0;-0;0}" : $"hauteur {ai.Semitones:+0;-0;0}";
        AiStatus = ai.TooSlow
            ? $"{ai.Device} trop lent ({ai.ComputeMs:0} ms par bloc) : la voix va hacher. Fermez les jeux ou utilisez les voix classiques."
            : $"{ai.Device} · {ai.ComputeMs:0} ms · retard ≈ {ai.LatencyMs:0} ms · {pitch}";
    }

    private void DisposeAi()
    {
        StopAi();
        _rvc?.Dispose();
        _rvc = null;
    }
}
