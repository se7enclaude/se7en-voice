using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Se7enVoice.Controls;

/// <summary>Un réglage d'effet : libellé, valeur affichée et slider Slipstream, en une ligne de XAML.</summary>
public sealed class ParamSlider : Control
{
    public static readonly DependencyProperty LabelProperty = Dp(nameof(Label), "");
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ParamSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ParamSlider)d).UpdateText()));
    public static readonly DependencyProperty MinimumProperty = Dp(nameof(Minimum), 0.0);
    public static readonly DependencyProperty MaximumProperty = Dp(nameof(Maximum), 1.0);
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(nameof(Format), typeof(string), typeof(ParamSlider),
        new PropertyMetadata("{0:P0}", (d, _) => ((ParamSlider)d).UpdateText()));
    public static readonly DependencyProperty ToneProperty = Dp(nameof(Tone), (Brush?)null);
    public static readonly DependencyProperty OriginProperty = Dp(nameof(Origin), double.NaN);
    public static readonly DependencyProperty TickProperty = Dp(nameof(Tick), 0.0);
    public static readonly DependencyProperty ValueTextProperty = Dp(nameof(ValueText), "");

    // Une valeur liée égale à la valeur par défaut ne déclenche pas de changement : on calcule le texte au chargement.
    public ParamSlider() => Loaded += (_, _) => UpdateText();

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    /// <summary>Format .NET de la valeur affichée, ex. « {0:+0;-0;0} dB ».</summary>
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public Brush? Tone { get => (Brush?)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    /// <summary>Valeur « zéro » d'où part le remplissage (NaN = depuis la gauche).</summary>
    public double Origin { get => (double)GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    /// <summary>Pas d'aimantation (0 = libre).</summary>
    public double Tick { get => (double)GetValue(TickProperty); set => SetValue(TickProperty, value); }
    public string ValueText { get => (string)GetValue(ValueTextProperty); private set => SetValue(ValueTextProperty, value); }

    private void UpdateText()
    {
        try { ValueText = string.Format(System.Globalization.CultureInfo.CurrentCulture, Format, Value); }
        catch (FormatException) { ValueText = Value.ToString("0.##"); }
    }

    private static DependencyProperty Dp<T>(string name, T def) =>
        DependencyProperty.Register(name, typeof(T), typeof(ParamSlider), new PropertyMetadata(def));
}
