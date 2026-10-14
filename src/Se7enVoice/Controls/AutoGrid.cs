using System.Windows;
using System.Windows.Controls.Primitives;

namespace Se7enVoice.Controls;

/// <summary>Grille dont le nombre de colonnes s'adapte à la largeur (tuiles toujours alignées bord à bord).</summary>
public sealed class AutoGrid : UniformGrid
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AutoGrid),
        new FrameworkPropertyMetadata(180.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (!double.IsInfinity(constraint.Width))
        {
            int cols = Math.Max(1, (int)(constraint.Width / MinItemWidth));
            if (Columns != cols) Columns = cols;
        }
        return base.MeasureOverride(constraint);
    }
}
