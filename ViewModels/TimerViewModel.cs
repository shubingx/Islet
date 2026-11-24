using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;

namespace Islet.ViewModels;

/// <summary>
/// Timer page: a countdown (with presets and ±1 min) and a stopwatch. Both keep their own state,
/// so switching tabs doesn't stop the other one. Time is measured with Stopwatch, not by counting
/// ticks, so it stays accurate even when the UI thread is busy.
/// </summary>
public sealed class TimerViewModel : ViewModelBase
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(10);

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(50) };

    private bool _isStopwatchMode;

    // Countdown
    private TimeSpan _duration = TimeSpan.FromMinutes(5);
    private TimeSpan _cdBase;                 // time left when the countdown was last (re)started
    private readonly Stopwatch _cdRun = new();
    private bool _cdStarted;                  // running or paused (not reset)

    // Stopwatch
    private TimeSpan _swBase;                 // time accumulated before the last pause
    private readonly Stopwatch _swRun = new();

    public TimerViewModel()
    {
        _tick.Tick += (_, _) => OnTick();
    }

    /// <summary>The countdown reached zero. Argument: the duration that was set.</summary>
    public event Action<TimeSpan>? Finished;

    // ---- Mode --------------------------------------------------------------

    public bool IsStopwatchMode
    {
        get => _isStopwatchMode;
        set
        {
            if (!Set(ref _isStopwatchMode, value)) return;
            OnPropertyChanged(nameof(IsCountdownMode));
            RaiseState();
        }
    }

    public bool IsCountdownMode => !IsStopwatchMode;

    // ---- Values ------------------------------------------------------------

    private TimeSpan Remaining
    {
        get
        {
            if (!_cdStarted) return _duration;
            var left = _cdBase - _cdRun.Elapsed;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    private TimeSpan SwElapsed => _swBase + _swRun.Elapsed;

    /// <summary>Whether the page's current mode is running.</summary>
    public bool IsRunning => IsStopwatchMode ? _swRun.IsRunning : _cdRun.IsRunning;

    public string PlayGlyph => IsRunning ? "" : "";

    public string DisplayText => IsStopwatchMode ? FormatStopwatch(SwElapsed) : FormatCountdown(Remaining);

    /// <summary>Countdown: how much has passed (0→1). Stopwatch: position within the current minute.</summary>
    public double Progress => IsStopwatchMode
        ? SwElapsed.TotalSeconds % 60 / 60
        : _cdStarted && _duration > TimeSpan.Zero ? Math.Clamp(1 - Remaining / _duration, 0, 1) : 0;

    public string StatusText
    {
        get
        {
            if (IsStopwatchMode)
                return _swRun.IsRunning ? "計時中" : SwElapsed > TimeSpan.Zero ? "已暫停" : "碼錶";
            if (!_cdStarted) return "設定時間";
            return _cdRun.IsRunning
                ? $"{(DateTime.Now + Remaining).ToString("HH:mm", CultureInfo.InvariantCulture)} 結束"
                : "已暫停";
        }
    }

    // ---- Collapsed islet ----------------------------------------------------

    /// <summary>Show the timer instead of the clock on the collapsed islet.</summary>
    public bool IdleActive => _cdStarted || _swRun.IsRunning || _swBase > TimeSpan.Zero;
    public bool IdleInactive => !IdleActive;

    /// <summary>Countdown wins when both are in use. No hundredths here: the capsule is small.</summary>
    public string IdleText => _cdStarted ? FormatCountdown(Remaining) : FormatShort(SwElapsed);

    // ---- Commands ----------------------------------------------------------

    public void Toggle()
    {
        if (IsStopwatchMode)
        {
            if (_swRun.IsRunning) { _swBase += _swRun.Elapsed; _swRun.Reset(); }
            else _swRun.Start();
        }
        else if (_cdRun.IsRunning)
        {
            _cdBase = Remaining;
            _cdRun.Reset();
        }
        else
        {
            if (!_cdStarted) { _cdBase = _duration; _cdStarted = true; }
            _cdRun.Restart();
        }
        UpdateTicker();
        RaiseState();
    }

    public void Reset()
    {
        if (IsStopwatchMode)
        {
            _swRun.Reset();
            _swBase = TimeSpan.Zero;
        }
        else
        {
            _cdRun.Reset();
            _cdStarted = false;
        }
        UpdateTicker();
        RaiseState();
    }

    /// <summary>Preset button: set a new duration and reset the countdown.</summary>
    public void SetDuration(TimeSpan duration)
    {
        _duration = Clamp(duration);
        _cdRun.Reset();
        _cdStarted = false;
        UpdateTicker();
        RaiseState();
    }

    /// <summary>±N minutes. Before starting it changes the duration; while counting it changes the time left.</summary>
    public void Adjust(int minutes)
    {
        var delta = TimeSpan.FromMinutes(minutes);
        if (!_cdStarted)
        {
            _duration = Clamp(_duration + delta);
        }
        else
        {
            var left = Remaining + delta;
            if (left <= TimeSpan.Zero) return;          // -1 with under a minute left: ignore
            if (left > MaxDuration) left = MaxDuration;
            // Keep the progress bar sensible: the total grows along with the time left.
            _duration = Clamp(_duration + (left - Remaining));
            _cdBase = left;
            if (_cdRun.IsRunning) _cdRun.Restart();
        }
        RaiseState();
    }

    // ---- Internals ---------------------------------------------------------

    private void OnTick()
    {
        if (_cdRun.IsRunning && _cdBase - _cdRun.Elapsed <= TimeSpan.Zero)
        {
            var finished = _duration;
            _cdRun.Reset();
            _cdStarted = false;
            UpdateTicker();
            RaiseState();
            Finished?.Invoke(finished);
            return;
        }
        RaiseValues();
    }

    private void UpdateTicker()
    {
        bool needed = _cdRun.IsRunning || _swRun.IsRunning;
        if (needed && !_tick.IsEnabled) _tick.Start();
        else if (!needed && _tick.IsEnabled) _tick.Stop();
    }

    private void RaiseValues()
    {
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IdleText));
        OnPropertyChanged(nameof(StatusText));
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(IdleActive));
        OnPropertyChanged(nameof(IdleInactive));
        RaiseValues();
    }

    private static TimeSpan Clamp(TimeSpan t) => t < MinDuration ? MinDuration : t > MaxDuration ? MaxDuration : t;

    /// <summary>Rounded up, so a fresh 5:00 shows 5:00 and the last second shows 0:01.</summary>
    private static string FormatCountdown(TimeSpan t) => FormatShort(TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds)));

    private static string FormatShort(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private static string FormatStopwatch(TimeSpan t) =>
        t.TotalHours >= 1 ? FormatShort(t) : $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 10:00}";

    public static string DescribeDuration(TimeSpan t)
    {
        int h = (int)t.TotalHours, m = t.Minutes;
        if (h > 0) return m > 0 ? $"{h} 小時 {m} 分鐘" : $"{h} 小時";
        return $"{(int)t.TotalMinutes} 分鐘";
    }
}
