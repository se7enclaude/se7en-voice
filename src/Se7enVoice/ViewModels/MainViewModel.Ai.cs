using System.Collections.ObjectModel;
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
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>Voix IA : téléchargement des modèles, chargement sur la carte graphique, choix de la voix et de la hauteur.</summary>
public sealed partial class MainViewModel
{
    private RvcEngine? _rvc;
    private AiVoice? _ai;
    private bool _aiBusy, _aiDownloading;
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
    }

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
        if (_aiBusy) return;
        _aiBusy = true;
        try
        {
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
            if (_ai == null || Engine.Ai == null)
            {
                StopAi();
                _ai = new AiVoice(_rvc, Engine.SampleRate);
                UpdateAiParams();
                Engine.Ai = _ai;
            }
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
        var info = Core.Ai.AiVoices.Find(_settings.AiVoiceId) ?? Core.Ai.AiVoices.All[0];
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
