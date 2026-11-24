using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Islet.Services;

namespace Islet.ViewModels;

/// <summary>
/// Performance page: CPU, memory, GPU and disk as rings, plus network speed with a small graph.
/// Sampling only runs while <see cref="IsActive"/> (the page is on screen in the expanded islet).
/// </summary>
public sealed class PerformanceViewModel : ViewModelBase, IDisposable
{
    private const int HistoryLength = 30;          // seconds of network history in the graph
    private const double GraphWidth = 100;
    private const double GraphHeight = 26;
    private const double GraphFloor = 64 * 1024;   // below 64 KB/s the graph stays near the bottom

    private readonly PerformanceService _service;
    private readonly Queue<double> _downHistory = new();
    private readonly Queue<double> _upHistory = new();
    private bool _isActive;
    private PerfSample? _last;

    public PerformanceViewModel(Dispatcher dispatcher)
    {
        _service = new PerformanceService(dispatcher);
        _service.Sampled += OnSampled;
    }

    /// <summary>Set by the window: true while the expanded islet shows this page.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!Set(ref _isActive, value)) return;
            if (value) _service.Start();
            else _service.Stop();
        }
    }

    // ---- Rings ---------------------------------------------------------------

    public double CpuValue => _last?.Cpu ?? 0;
    public string CpuText => Percent(_last?.Cpu);

    public double MemoryValue => _last?.Memory ?? 0;
    public string MemoryText => Percent(_last?.Memory);

    public double GpuValue => _last?.Gpu ?? 0;
    public string GpuText => Percent(_last?.Gpu);

    public double DiskValue => _last?.Disk ?? 0;
    public string DiskText => Percent(_last?.Disk);

    // ---- Network -------------------------------------------------------------

    public string NetDownText => _last is null ? "—" : Speed(_last.NetDown);
    public string NetUpText => _last is null ? "—" : Speed(_last.NetUp);
    public PointCollection NetDownPoints { get; private set; } = new();
    public PointCollection NetUpPoints { get; private set; } = new();

    private void OnSampled(PerfSample sample)
    {
        if (!_isActive) return;
        _last = sample;

        Push(_downHistory, sample.NetDown);
        Push(_upHistory, sample.NetUp);
        double max = Math.Max(GraphFloor, Math.Max(_downHistory.Max(), _upHistory.Max()));
        NetDownPoints = ToPoints(_downHistory, max);
        NetUpPoints = ToPoints(_upHistory, max);

        foreach (var name in new[]
        {
            nameof(CpuValue), nameof(CpuText), nameof(MemoryValue), nameof(MemoryText),
            nameof(GpuValue), nameof(GpuText), nameof(DiskValue), nameof(DiskText),
            nameof(NetDownText), nameof(NetUpText), nameof(NetDownPoints), nameof(NetUpPoints),
        })
            OnPropertyChanged(name);
    }

    private static void Push(Queue<double> history, double value)
    {
        history.Enqueue(value);
        while (history.Count > HistoryLength) history.Dequeue();
    }

    /// <summary>Right-aligned line: the newest sample sits at the right edge.</summary>
    private static PointCollection ToPoints(Queue<double> history, double max)
    {
        var points = new PointCollection(history.Count + 2);
        double step = GraphWidth / (HistoryLength - 1);
        double x = GraphWidth - (history.Count - 1) * step;
        foreach (double v in history)
        {
            double y = GraphHeight - 1 - v / max * (GraphHeight - 2);
            points.Add(new Point(x, y));
            x += step;
        }
        points.Freeze();
        return points;
    }

    private static string Percent(double? v) => v is { } x ? $"{Math.Round(x * 100):0}%" : "—";

    private static double Gb(ulong bytes) => bytes / 1024.0 / 1024 / 1024;

    private static string Speed(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;   // Task Manager shows network in bits per second
        return bits switch
        {
            >= 1e9 => $"{bits / 1e9:0.0} Gbps",
            >= 1e6 => $"{bits / 1e6:0.0} Mbps",
            >= 1e3 => $"{bits / 1e3:0} Kbps",
            _ => $"{bits:0} bps",
        };
    }

    public void Dispose() => _service.Dispose();
}
