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

    public MainWindow(bool startMinimized)
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.PresetApplied += ShowOnomatopoeia;
        _vm.Notify += ShowToast;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.Muted) or "") _trayMute!.Checked = _vm.Muted;
        };

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
                    "Ta voix reste active. Clic droit sur l'icône près de l'horloge pour quitter.", Forms.ToolTipIcon.Info);
            }
            return;
        }
        _hotkeys?.Dispose();
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
    }

    private void CableBadge_Click(object sender, RoutedEventArgs e) => TabSettings.IsChecked = true;

    // ---------- Presets ----------

    private void PresetTile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PresetItem item) _vm.ApplyPreset(item);
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
            ShowToast("Ce nom est déjà pris par une voix de base, choisis-en un autre.");
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
            ShowToast("Preset exporté ! Envoie le fichier à ton ami, il n'a qu'à cliquer sur IMPORTER.");
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
        ShowToast(ok > 0 ? $"{ok} preset(s) ajouté(s) dans « Mes presets »." : "Ce fichier n'est pas un preset se7en Voice.");
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
        if (imported > 0) ShowToast($"{imported} preset(s) importé(s).");
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

    private void DownloadCable_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true });

    private void CheckCable_Click(object sender, RoutedEventArgs e)
    {
        _vm.RefreshDevices(restart: true);
        ShowToast(_vm.CableFound
            ? "Micro virtuel trouvé ! Choisis « " + _vm.CableName + " » dans Discord."
            : "Toujours pas trouvé. Pense à redémarrer le PC après l'installation.");
    }

    private void RenameCable_Click(object sender, RoutedEventArgs e)
    {
        _vm.CableRenameOffered = true;
        _vm.RenameCable();
    }

    // ---------- Effets visuels ----------

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        var anim = new DoubleAnimationUsingKeyFrames();
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4200))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4500)), new CubicEase { EasingMode = EasingMode.EaseIn }));
        Toast.BeginAnimation(OpacityProperty, anim);
        if (!IsVisible) _tray.ShowBalloonTip(3000, "se7en Voice", message, Forms.ToolTipIcon.None);
    }

    private static string WordFor(PresetItem p) => p.Name switch
    {
        "Normal" => "OK!",
        "Robot" => "BZZT!",
        "Alien" => "ZWOOP!",
        "Démon" => "GRAAH!",
        "Chipmunk" => "SKWEE!",
        "Radio" => "KSSHH!",
        "Grotte" => "OHÉÉÉ!",
        "Stade" or "Géant" => "BOOOM!",
        "Voix grave" => "WHOMP!",
        "Streamer" => "LIVE!",
        _ => "ZAP!",
    };

    private void ShowOnomatopoeia(PresetItem p)
    {
        if (!IsVisible) return;
        Onomatopoeia.Text = WordFor(p);
        var snap = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
        var dur = TimeSpan.FromMilliseconds(320);

        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))));
        Onomatopoeia.BeginAnimation(OpacityProperty, opacity);

        var scale = new DoubleAnimation(0.4, 1, dur) { EasingFunction = snap };
        OnoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        OnoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
        OnoRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, new DoubleAnimation(-14, -8, dur) { EasingFunction = snap });
    }
}
