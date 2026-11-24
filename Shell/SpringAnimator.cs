using System.Windows.Media;

namespace Islet.Shell;

/// <summary>
/// Damped-spring animator driven by the WPF render loop. Animates several values
/// at once (width, height, corner radius) toward a target with a slight overshoot,
/// which gives the islet its elastic "iOS" feel. Retargeting mid-flight keeps the
/// current velocity, so interrupted animations stay smooth.
/// </summary>
public sealed class SpringAnimator
{
    private readonly double[] _value;
    private readonly double[] _target;
    private readonly double[] _velocity;

    /// <summary>Spring stiffness; higher = faster. Can be changed between animations.</summary>
    public double Stiffness { get; set; }

    /// <summary>Damping; higher = less bounce. Can be changed between animations.</summary>
    public double Damping { get; set; }
    private TimeSpan _last;
    private bool _running;

    public event Action<double[]>? Updated;
    public event Action? Completed;

    public SpringAnimator(double[] initial, double stiffness = 320, double damping = 24)
    {
        _value = (double[])initial.Clone();
        _target = (double[])initial.Clone();
        _velocity = new double[initial.Length];
        Stiffness = stiffness;
        Damping = damping;
    }

    public void SetTarget(params double[] target)
    {
        Array.Copy(target, _target, _target.Length);
        if (_running) return;

        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        double dt = _last == TimeSpan.Zero ? 1.0 / 60 : (now - _last).TotalSeconds;
        _last = now;
        if (dt <= 0) return;
        dt = Math.Min(dt, 1.0 / 30);

        // Integrate in small fixed sub-steps so stiff springs stay stable even when a frame is late.
        const double maxStep = 1.0 / 240;
        int steps = (int)Math.Ceiling(dt / maxStep);
        double h = dt / steps;

        bool settled = true;
        for (int i = 0; i < _value.Length; i++)
        {
            for (int s = 0; s < steps; s++)
            {
                double accel = -Stiffness * (_value[i] - _target[i]) - Damping * _velocity[i];
                _velocity[i] += accel * h;
                _value[i] += _velocity[i] * h;
            }

            if (Math.Abs(_value[i] - _target[i]) > 0.3 || Math.Abs(_velocity[i]) > 3)
                settled = false;
        }

        if (!settled)
        {
            Updated?.Invoke(_value);
            return;
        }

        for (int i = 0; i < _value.Length; i++)
        {
            _value[i] = _target[i];
            _velocity[i] = 0;
        }

        Updated?.Invoke(_value);
        CompositionTarget.Rendering -= OnRendering;
        _running = false;
        Completed?.Invoke();
    }
}
