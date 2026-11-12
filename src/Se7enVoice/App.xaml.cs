using System.Windows;
using System.Windows.Threading;
using Se7enVoice.Audio;
using Se7enVoice.Core;

namespace Se7enVoice;

public partial class App : Application
{
    private const string InstanceName = "se7enVoice.SingleInstance";
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Filet : toute erreur imprévue est écrite dans %AppData%\Se7enVoice\log.txt.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write((args.IsTerminating ? "Plantage" : "Erreur") + " (hors interface) : " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Erreur d'une tâche en arrière-plan", args.Exception);
            args.SetObserved();
        };

        // Processus admin lancé uniquement pour renommer VB-CABLE.
        if (e.Args.Contains(CableRenamer.Argument))
        {
            Shutdown(CableRenamer.RunElevated());
            return;
        }

        // Processus admin lancé pour installer « Seven Microphone » (installateur VB-CABLE + renommage).
        int install = Array.IndexOf(e.Args, CableInstaller.Argument);
        if (install >= 0 && install + 1 < e.Args.Length)
        {
            Shutdown(CableInstaller.RunElevated(e.Args[install + 1]));
            return;
        }

        // Appelé par Seventh OS avant la désinstallation : on retire le lancement avec Windows.
        if (e.Args.Contains("--uninstall-cleanup"))
        {
            try { Services.Autostart.Set(false); } catch (Exception) { }
            Shutdown(0);
            return;
        }

        // Une seule instance : si l'app tourne déjà, on la ramène au premier plan.
        _mutex = new Mutex(true, InstanceName, out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        bool minimized = e.Args.Contains("--minimized");
        if (minimized)
        {
            StartMain(true);
            return;
        }
        // Animation de lancement, puis la fenêtre principale (pas d'animation au démarrage avec Windows).
        var splash = new LaunchSplash();
        splash.Done += (_, _) => StartMain(false);
        splash.Show();
    }

    private void StartMain(bool minimized)
    {
        var window = new MainWindow(startMinimized: minimized);
        MainWindow = window;
        if (!minimized) window.Activate();

        var signal = _showSignal!;
        new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(window.ShowFromTray);
        }) { IsBackground = true, Name = "show-signal" }.Start();
    }

    /// <summary>
    /// Erreur imprévue sur le fil de l'interface : on la note et l'appli continue (le micro et la voix restent en
    /// service) plutôt que de tout fermer en pleine partie.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Erreur dans l'interface", e.Exception);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
