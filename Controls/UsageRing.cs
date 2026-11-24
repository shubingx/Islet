using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Islet.Controls;

/// <summary>
/// A ring that fills clockwise from the top. The fill glides to new values and changes colour
/// as load rises: normal → warn (≥ <see cref="WarnAt"/>) → critical (≥ <see cref="CriticalAt"/>).
/// </summary>
public sealed class UsageRing : FrameworkElement
{
    private readonly SolidColorBrush _arcBrush = new(Colors.White);
    private int _tier = -1;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageRing), new PropertyMetadata(0.0, OnValueChanged));

    /// <summary>0–1.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The value actually drawn; animated toward <see cref="Value"/>.</summary>
    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(UsageRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(UsageRing),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(UsageRing),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public static readonly DependencyProperty NormalColorProperty = RegisterColor(nameof(NormalColor), Colors.White);
    public static readonly DependencyProperty WarnColorProperty = RegisterColor(nameof(WarnColor), Color.FromRgb(0xFF, 0x9F, 0x0A));
    public static readonly DependencyProperty CriticalColorProperty = RegisterColor(nameof(CriticalColor), Color.FromRgb(0xFF, 0x45, 0x3A));

    public Color NormalColor { get => (Color)GetValue(NormalColorProperty); set => SetValue(NormalColorProperty, value); }
    public Color WarnColor { get => (Color)GetValue(WarnColorProperty); set => SetValue(WarnColorProperty, value); }
    public Color CriticalColor { get => (Color)GetValue(CriticalColorProperty); set => SetValue(CriticalColorProperty, value); }

    public double WarnAt { get; set; } = 0.60;
    public double CriticalAt { get; set; } = 0.85;

    private static DependencyProperty RegisterColor(string name, Color fallback) =>
        DependencyProperty.Register(name, typeof(Color), typeof(UsageRing),
            new PropertyMetadata(fallback, (d, _) => ((UsageRing)d).UpdateColor(animate: false, force: true)));

    public UsageRing()
    {
        Loaded += (_, _) => UpdateColor(animate: false, force: true);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (UsageRing)d;
        double to = Math.Clamp((double)e.NewValue, 0, 1);
        ring.BeginAnimation(ShownProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(450))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
        ring.UpdateColor(animate: true, force: false);
    }

    private void UpdateColor(bool animate, bool force)
    {
        double v = Value;
        int tier = v >= CriticalAt ? 2 : v >= WarnAt ? 1 : 0;
        if (tier == _tier && !force) return;
        _tier = tier;

        var color = tier switch { 2 => CriticalColor, 1 => WarnColor, _ => NormalColor };
        if (animate)
            _arcBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(color, TimeSpan.FromMilliseconds(300)));
        else
        {
            _arcBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _arcBrush.Color = color;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        double t = Thickness;
        double r = (size - t) / 2;
        if (r <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(TrackBrush, t), center, r, r);

        double shown = (double)GetValue(ShownProperty);
        if (shown <= 0.002) return;

        var pen = new Pen(_arcBrush, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (shown >= 0.999)
        {
            dc.DrawEllipse(null, pen, center, r, r);
            return;
        }

        double angle = shown * 2 * Math.PI;
        var start = new Point(center.X, center.Y - r);
        var end = new Point(center.X + r * Math.Sin(angle), center.Y - r * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(r, r), 0, isLargeArc: angle > Math.PI, SweepDirection.Clockwise,
                isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
