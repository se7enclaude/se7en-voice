using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Se7enVoice.Core.Presets;
using Se7enVoice.Services;
using Se7enVoice.ViewModels;
using Forms = System.Windows.Forms;

namespace Se7enVoice;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _trayMute;
    private GlobalHotkeys? _hotkeys;
    private bool _exiting, _trayHintShown;
    private readonly System.Windows.Threading.DispatcherTimer _spectrumTimer;

    public MainWindow(bool startMinimized)
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.PresetApplied += ShowPresetToast;
        _vm.Notify += ShowToast;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Muted) or "") _trayMute!.Checked = _vm.Muted;
        };

        // Analyseur de spectre : seulement quand la page Studio est à l'écran.
        _spectrumTimer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(33), System.Windows.Threading.DispatcherPriority.Render,
            (_, _) => { if (IsVisible && WindowState != WindowState.Minimized && PageVoice.IsVisible) Spectrum.Update(_vm.Engine.CopyScope, _vm.Engine.SampleRate); },
            Dispatcher);
        IsVisibleChanged += (_, _) => UpdateMixerVisible();
        StateChanged += (_, _) => UpdateMixerVisible();

        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };

        // Icône près de l'horloge
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir se7en Voice", null, (_, _) => ShowFromTray());
        _trayMute = new Forms.ToolStripMenuItem("Muet", null, (_, _) => _vm.Muted = !_vm.Muted);
        menu.Items.Add(_trayMute);
        menu.Items.Add("Voix suivante", null, (_, _) => _vm.StepPreset(+1));
        menu.Items.Add("A/B : voix sans effets", null, (_, _) => _vm.Bypass = !_vm.Bypass);
        menu.Items.Add("Enregistrer / arrêter", null, (_, _) => _vm.ToggleRecording());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => ExitApp());
        _tray = new Forms.NotifyIcon
        {
            Text = "se7en Voice",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowFromTray(); };

        SourceInitialized += (_, _) => RegisterHotkeys();
        Loaded += (_, _) => OfferCableSetup();
        Closing += OnClosing;

        if (startMinimized)
        {
            _trayHintShown = true;
            // On initialise la fenêtre (pour les raccourcis) sans l'afficher.
            new WindowInteropHelper(this).EnsureHandle();
        }
        else
        {
            Show();
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))?.Stream;
        return stream != null ? new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize) : System.Drawing.SystemIcons.Application;
    }

    private void RegisterHotkeys()
    {
        _hotkeys = new GlobalHotkeys(new WindowInteropHelper(this).Handle);
        var failed = new List<string>();
        void Reg(Key k, string label, Action a) { if (!_hotkeys.Register(k, a)) failed.Add(label); }

        Reg(Key.M, "Ctrl+Alt+M", () => _vm.Muted = !_vm.Muted);
        Reg(Key.Right, "Ctrl+Alt+→", () => _vm.StepPreset(+1));
        Reg(Key.Left, "Ctrl+Alt+←", () => _vm.StepPreset(-1));
        Reg(Key.N, "Ctrl+Alt+N", () => { var n = _vm.Presets.FirstOrDefault(); if (n != null) _vm.ApplyPreset(n); });
        Reg(Key.D0, "Ctrl+Alt+0", _vm.StopSounds);
        Reg(Key.B, "Ctrl+Alt+B", () => _vm.Bypass = !_vm.Bypass);
        Reg(Key.R, "Ctrl+Alt+R", _vm.ToggleRecording);
        for (int i = 0; i < 9; i++)
        {
            int index = i;
            Reg(Key.F1 + i, $"Ctrl+Alt+F{i + 1}", () => _vm.ApplyFavorite(index));
        }
        for (int i = 0; i < 9; i++)
        {
            int index = i;
            Reg(Key.D1 + i, $"Ctrl+Alt+{i + 1}", () => _vm.PlaySoundAt(index));
        }
        if (failed.Count > 0)
            ShowToast("Raccourcis déjà pris par une autre app : " + string.Join(", ", failed));
    }

    // ---------- Fenêtre ----------

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting && _vm.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                _tray.ShowBalloonTip(4000, "se7en Voice tourne toujours",
                    "Votre voix reste active. Clic droit sur l'icône près de l'horloge pour quitter.", Forms.ToolTipIcon.Info);
            }
            return;
        }
        _hotkeys?.Dispose();
        _spectrumTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _vm.Dispose();
        Application.Current.Shutdown();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Sans bordure Windows, une fenêtre maximisée déborde un peu de l'écran.
        BorderThickness = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    // ---------- Onglets ----------

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PageVoice == null) return;
        PageVoice.Visibility = TabVoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageEffects.Visibility = TabEffects.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSounds.Visibility = TabSounds.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = TabSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageMixer.Visibility = TabMixer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateMixerVisible();
        if (TabMixer.IsChecked == true) _vm.Mixer.Scan();
    }

    private void UpdateMixerVisible()
    {
        if (_vm == null) return;
        bool shown = IsVisible && WindowState != WindowState.Minimized;
        _vm.MixerVisible = shown && TabMixer.IsChecked == true;
        if (shown) _spectrumTimer.Start(); else _spectrumTimer.Stop();
    }

    private void CableBadge_Click(object sender, RoutedEventArgs e) => TabSettings.IsChecked = true;

    // ---------- Presets ----------

    private void PresetTile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem item) _vm.ApplyPreset(item);
    }

    private void QuickVoice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem item) _vm.ApplyPreset(item);
    }

    private void GoVoice_Click(object sender, RoutedEventArgs e) => TabVoice.IsChecked = true;

    private void GoSettings_Click(object sender, RoutedEventArgs e) => TabSettings.IsChecked = true;

    private void FixOutput_Click(object sender, RoutedEventArgs e) => _vm.FixWindowsOutput();

    private void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => TabVoice.IsChecked = true;

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem item) _vm.ToggleFavorite(item);
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Content: string f }) _vm.PresetFilter = f;
    }

    private void Filter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Content: string f } rb && f == _vm.PresetFilter) rb.IsChecked = true;
    }

    private void ResetEffects_Click(object sender, RoutedEventArgs e) => _vm.ResetEffects();

    private void Record_Click(object sender, RoutedEventArgs e) => _vm.ToggleRecording();

    private void CaptureKey_Click(object sender, RoutedEventArgs e) => _vm.StartKeyCapture();

    private void OpenRecordings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_vm.RecordingsFolder);
            Process.Start(new ProcessStartInfo(_vm.RecordingsFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowToast("Impossible d'ouvrir le dossier : " + ex.Message); }
    }

    private void ChooseRecordings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Dossier des enregistrements", InitialDirectory = _vm.RecordingsFolder };
        if (dlg.ShowDialog(this) == true) _vm.RecordingsFolder = dlg.FolderName;
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        PresetNameBox.Text = _vm.CurrentPreset is { IsUser: true } p ? p.Name : "";
        SaveDialog.Visibility = Visibility.Visible;
        PresetNameBox.Focus();
    }

    private void ConfirmSave_Click(object sender, RoutedEventArgs e)
    {
        var name = PresetNameBox.Text.Trim();
        if (name.Length == 0) { PresetNameBox.Focus(); return; }
        if (_vm.Presets.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            ShowToast("Ce nom est déjà pris par une voix de base · choisissez-en un autre");
            return;
        }
        SaveDialog.Visibility = Visibility.Collapsed;
        _vm.SaveCurrentAs(name);
        TabVoice.IsChecked = true;
    }

    private void CancelSave_Click(object sender, RoutedEventArgs e) => SaveDialog.Visibility = Visibility.Collapsed;

    private void PresetNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ConfirmSave_Click(sender, e);
        else if (e.Key == Key.Escape) CancelSave_Click(sender, e);
    }

    private void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PresetItem item) return;
        var dlg = new SaveFileDialog
        {
            FileName = item.Name + ".json",
            Filter = "Preset se7en Voice (*.json)|*.json",
            Title = "Exporter le preset pour un ami",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            PresetStore.Export(item.Preset, dlg.FileName);
            ShowToast("Voix exportée · envoyez le fichier à un ami, il n'aura qu'à cliquer sur « Importer »");
        }
        catch (Exception ex) { ShowToast("Export impossible : " + ex.Message); }
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem item) _vm.DeletePreset(item);
    }

    private void ImportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Preset se7en Voice (*.json)|*.json", Multiselect = true, Title = "Importer des presets" };
        if (dlg.ShowDialog(this) != true) return;
        int ok = dlg.FileNames.Count(_vm.ImportPreset);
        ShowToast(ok > 0 ? $"{ok} voix ajoutée(s) · dans « Mes presets »" : "Ce fichier n'est pas un preset se7en Voice.");
    }

    // ---------- Soundboard ----------

    private void AddSounds_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Sons|*.mp3;*.wav;*.ogg;*.m4a;*.aac;*.wma;*.flac",
            Multiselect = true,
            Title = "Ajouter des sons",
        };
        if (dlg.ShowDialog(this) == true) _vm.AddSounds(dlg.FileNames);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var presets = files.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();
        int imported = presets.Count(_vm.ImportPreset);
        if (imported > 0) ShowToast($"{imported} voix importée(s)");
        var sounds = files.Except(presets).ToList();
        if (sounds.Count > 0)
        {
            _vm.AddSounds(sounds);
            TabSounds.IsChecked = true;
        }
    }

    private void PlaySound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SoundItem s) _vm.PlaySound(s);
    }

    private void RemoveSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SoundItem s) _vm.RemoveSound(s);
    }

    private void StopSounds_Click(object sender, RoutedEventArgs e) => _vm.StopSounds();

    // ---------- Micro virtuel ----------

    private async void InstallCable_Click(object sender, RoutedEventArgs e)
    {
        CableDialog.Visibility = Visibility.Collapsed;
        await _vm.InstallCableAsync();
    }

    private void CableDialogInstall_Click(object sender, RoutedEventArgs e) => InstallCable_Click(sender, e);

    private void CableDialogLater_Click(object sender, RoutedEventArgs e) => CableDialog.Visibility = Visibility.Collapsed;

    /// <summary>Premier lancement sans micro virtuel : on propose l'installation une fois.</summary>
    private void OfferCableSetup()
    {
        _vm.FinishPendingCableSetup();
        if (_vm.CableFound || _vm.CableRebootPending || _vm.CableSetupOffered) return;
        _vm.CableSetupOffered = true;
        CableDialog.Visibility = Visibility.Visible;
    }

    private void DownloadCable_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });

    private void CheckCable_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshDevices(restart: true);
        ShowToast(_vm.CableFound
            ? "Micro virtuel trouvé · choisissez « " + _vm.CableName + " » dans Discord"
            : "Micro virtuel toujours absent · redémarrez le PC après l'installation");
    }

    private void RenameCable_Click(object sender, RoutedEventArgs e)
    {
        _vm.CableRenameOffered = true;
        _vm.RenameCable();
    }

    // ---------- Effets visuels ----------

    /// <summary>Notification « titre · détail » : le titre en capitales espacées, le détail en dessous (sans titre : « Info »).</summary>
    private void ShowToast(string message)
    {
        int cut = message.IndexOf(" · ", StringComparison.Ordinal);
        ToastTitle.Text = Controls.Caps.Space(cut > 0 ? message[..cut] : "Info");
        ToastText.Text = cut > 0 ? message[(cut + 3)..] : message;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3400))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(3700)), new CubicEase { EasingMode = EasingMode.EaseIn }));
        Toast.BeginAnimation(OpacityProperty, anim);
        if (!IsVisible) _tray.ShowBalloonTip(3000, "se7en Voice", message, Forms.ToolTipIcon.None);
    }

    private void ShowPresetToast(PresetItem p)
    {
        if (IsVisible) ShowToast($"Voix appliquée · « {p.Name} » est active sur votre micro");
    }
}
