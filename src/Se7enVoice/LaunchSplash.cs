using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Se7enVoice;

/// <summary>
/// Animation de lancement « la voix », silencieuse, environ 2,3 s : un égaliseur danse comme une voix qui parle,
/// se calme et s'aplatit en une ligne lumineuse ; de cette ligne monte le mot SE7EN, VOICE s'écrit dessous, et
/// deux ondes partent d'un micro au centre. Un clic la passe.
/// </summary>
public sealed class LaunchSplash : Window
{
    private static readonly Color Silver = Color.FromRgb(0xE6, 0xE7, 0xEB);
    private static readonly Color Glow = Color.FromRgb(0x8E, 0xB8, 0xFF);
    private const double W = 560, H = 330;
    private const int Bars = 36;
    private readonly Canvas _stage = new() { Width = W, Height = H, ClipToBounds = true };
    private readonly Rectangle[] _bars = new Rectangle[Bars];
    private readonly double[] _phase = new double[Bars];
    private readonly DateTime _start = DateTime.Now;
    private readonly Random _rnd = new(7);
    private bool _closing;

    public LaunchSplash()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = W;
        Height = H;
        Cursor = Cursors.Hand;
        Content = new Border
        {
            CornerRadius = new CornerRadius(26),
            Background = new LinearGradientBrush(Color.FromRgb(0x16, 0x19, 0x22), Color.FromRgb(0x08, 0x09, 0x0D), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xE6, 0xE7, 0xEB)),
            BorderThickness = new Thickness(1),
            Child = _stage,
        };
        var display = Application.Current?.TryFindResource("FontDisplay") as FontFamily ?? new FontFamily("Segoe UI Black");

        // l'égaliseur
        double bw = 7, gap = 5, total = Bars * (bw + gap) - gap, x0 = (W - total) / 2;
        for (int i = 0; i < Bars; i++)
        {
            _phase[i] = _rnd.NextDouble() * Math.PI * 2;
            var r = new Rectangle
            {
                Width = bw, Height = 4, RadiusX = 3.5, RadiusY = 3.5,
                Fill = new LinearGradientBrush(Glow, Silver, 90),
            };
            Canvas.SetLeft(r, x0 + i * (bw + gap));
            Canvas.SetTop(r, H / 2 - 2);
            _stage.Children.Add(r);
            _bars[i] = r;
        }
        CompositionTarget.Rendering += OnFrame;

        // le mot qui monte de la ligne
        var word = new TextBlock
        {
            Text = "SE7EN", FontFamily = display, FontSize = 62, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Silver),
            Width = W, TextAlignment = TextAlignment.Center, Opacity = 0, RenderTransform = new TranslateTransform(0, 26),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Glow, BlurRadius = 24, ShadowDepth = 0, Opacity = .6 },
        };
        Canvas.SetTop(word, 96);
        _stage.Children.Add(word);
        var at = TimeSpan.FromMilliseconds(1250);
        word.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = at });
        ((TranslateTransform)word.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(520)) { BeginTime = at, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        var voice = new TextBlock { Text = "V O I C E", FontFamily = display, FontSize = 17, Foreground = new SolidColorBrush(Glow), Width = W, TextAlignment = TextAlignment.Center, Opacity = 0 };
        Canvas.SetTop(voice, 196);
        _stage.Children.Add(voice);
        voice.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { BeginTime = at + TimeSpan.FromMilliseconds(260) });

        // ondes du micro
        for (int k = 0; k < 2; k++)
        {
            var ring = new Ellipse { Width = 120, Height = 120, Stroke = new SolidColorBrush(Color.FromArgb(0x90, Glow.R, Glow.G, Glow.B)), StrokeThickness = 1.5, Opacity = 0,
                RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(.3, .3) };
            Canvas.SetLeft(ring, W / 2 - 60);
            Canvas.SetTop(ring, 134 - 60);
            _stage.Children.Insert(0, ring);
            var b = at + TimeSpan.FromMilliseconds(120 + k * 260);
            var st = (ScaleTransform)ring.RenderTransform;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.3, 3.2, TimeSpan.FromMilliseconds(900)) { BeginTime = b, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.3, 3.2, TimeSpan.FromMilliseconds(900)) { BeginTime = b, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = b };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(.9, TimeSpan.Zero));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(900)));
            ring.BeginAnimation(OpacityProperty, fade);
        }

        var foot = new TextBlock { Text = "Cliquer pour passer", FontSize = 10, Foreground = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), Width = W, TextAlignment = TextAlignment.Center };
        Canvas.SetTop(foot, H - 34);
        _stage.Children.Add(foot);

        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        var end = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2350) };
        end.Tick += (_, _) => { end.Stop(); Finish(); };
        end.Start();
        MouseLeftButtonDown += (_, _) => Finish();
        KeyDown += (_, _) => Finish();
        Closed += (_, _) => CompositionTarget.Rendering -= OnFrame;
    }

    /// <summary>Raised once the splash has faded out.</summary>
    public event EventHandler? Done;

    // 0–0,9 s : la voix parle (barres qui dansent) ; 0,9–1,25 s : elle se tait, tout s'aplatit ; ensuite la ligne
    // descend vers le bas du mot puis s'efface.
    private void OnFrame(object? sender, EventArgs e)
    {
        double t = (DateTime.Now - _start).TotalSeconds;
        double energy = t < .15 ? t / .15 : t < .9 ? 1 : Math.Max(0, 1 - (t - .9) / .35);
        double lineY = t < 1.25 ? H / 2 : H / 2 + Math.Min(1, (t - 1.25) / .5) * 30;
        double fadeLine = t < 1.6 ? 1 : Math.Max(0, 1 - (t - 1.6) / .4);
        for (int i = 0; i < Bars; i++)
        {
            double centre = 1 - Math.Abs(i - (Bars - 1) / 2.0) / (Bars / 2.0); // plus haut au centre
            double syll = .55 + .45 * Math.Sin(t * 9 + _phase[i]) * Math.Sin(t * 3.3 + i * .4);
            double h = 4 + energy * (18 + 110 * centre * centre) * Math.Abs(syll);
            _bars[i].Height = h;
            Canvas.SetTop(_bars[i], lineY - h / 2);
            _bars[i].Opacity = fadeLine;
        }
    }

    private void Finish()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => { Close(); Done?.Invoke(this, EventArgs.Empty); };
        BeginAnimation(OpacityProperty, fade);
    }
}
