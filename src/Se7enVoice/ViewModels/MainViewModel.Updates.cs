using System.Windows.Threading;
using Se7enVoice.Ai;
using Se7enVoice.Audio;
using Se7enVoice.Core.Updates;

namespace Se7enVoice.ViewModels;

/// <summary>
/// Composants à jour : modèles de la voix IA (empreintes vérifiées) et Seven Microphone (pilote VB-CABLE comparé au
/// dernier pack publié). Vérifié au lancement puis toutes les 12 h, une notification par nouvelle version.
/// </summary>
public sealed partial class MainViewModel
{
    private DispatcherTimer? _updatesTimer;
    private bool _checkingComponents;
    private string? _cableDriver;

    /// <summary>« À jour (pilote 3.5.2.0, Pack 46, vérifié le 9 nov. 2026) » ou « Mise à jour disponible : … ».</summary>
    public string CableUpdateText => ComponentStatus.CableText(CableFound, _cableDriver, ComponentsStore.Current);

    public bool CableUpdateAvailable =>
        ComponentStatus.CableState(CableFound, _cableDriver, ComponentsStore.Current.CableLatestDriver) == ComponentState.UpdateAvailable;

    public bool CableUpToDate =>
        ComponentStatus.CableState(CableFound, _cableDriver, ComponentsStore.Current.CableLatestDriver) == ComponentState.UpToDate;

    /// <summary>« Modèles à jour (voix-ia-1, vérifiés le 9 nov. 2026) ».</summary>
    public string AiModelsText => AiModels.StatusText;

    public bool CheckingComponents { get => _checkingComponents; private set => Set(ref _checkingComponents, value); }

    /// <summary>Appelé une fois la fenêtre prête : première vérification 20 s après le lancement, puis toutes les heures
    /// (le réseau n'est interrogé que si la dernière vérification date de plus de 12 h).</summary>
    public void StartComponentChecks()
    {
        if (_updatesTimer != null) return;
        _updatesTimer = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, async (_, _) =>
        {
            _updatesTimer!.Interval = TimeSpan.FromHours(1);
            await CheckComponentsAsync(force: false);
        }, Dispatcher);
        _updatesTimer.Start();
        RefreshComponentTexts();
    }

    /// <summary>Bouton « Vérifier les mises à jour ».</summary>
    public Task CheckComponentsNowAsync() => CheckComponentsAsync(force: true);

    private async Task CheckComponentsAsync(bool force)
    {
        if (CheckingComponents) return;
        CheckingComponents = true;
        try
        {
            await Task.Run(async () =>
            {
                try { await AiModels.VerifyInstalledAsync(); } catch (Exception) { /* fichier occupé : prochaine fois */ }
                await CableUpdates.CheckAsync(force);
            });
            RefreshComponentTexts();
            var st = ComponentsStore.Current;
            if (CableUpdateAvailable && st.CableNotifiedPack != st.CableLatestPack)
            {
                ComponentsStore.Update(c => c.CableNotifiedPack = c.CableLatestPack);
                Notify?.Invoke($"Mise à jour de Seven Microphone disponible · pilote {st.CableLatestDriver} (Pack {st.CableLatestPack}), dans Paramètres");
            }
        }
        finally
        {
            CheckingComponents = false;
        }
    }

    private void RefreshComponentTexts()
    {
        _cableDriver = CableFound ? CableUpdates.InstalledDriverVersion() : null;
        Raise(nameof(CableUpdateText));
        Raise(nameof(CableUpdateAvailable));
        Raise(nameof(CableUpToDate));
        Raise(nameof(AiModelsText));
        Raise(nameof(AiReady));
    }
}
