using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Islet.Services;

/// <summary>One reading. A null value means that counter isn't available on this PC.</summary>
public sealed record PerfSample(
    double? Cpu,            // 0–1
    double? Memory,         // 0–1
    ulong MemoryUsed,       // bytes
    ulong MemoryTotal,      // bytes
    double? Gpu,            // 0–1, busiest engine (same as Task Manager)
    double? Disk,           // 0–1, busiest physical disk's active time
    double NetDown,         // bytes/s
    double NetUp);          // bytes/s

/// <summary>
/// Reads the same numbers Task Manager shows, through the Windows performance counters (PDH).
/// Sampling runs on a background thread and only while <see cref="Start"/> is in effect, so the
/// islet costs nothing when the performance page isn't on screen. Results arrive on the UI thread.
/// </summary>
public sealed class PerformanceService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly object _lock = new();
    private Timer? _timer;
    private int _busy;
    private bool _opened;

    private IntPtr _query;
    private IntPtr _cpu, _gpu, _disk, _netDown, _netUp;

    public PerformanceService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public event Action<PerfSample>? Sampled;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);

    public void Start()
    {
        lock (_lock)
        {
            if (_timer is not null) return;
            // Rate counters need two collections; the first tick only primes them.
            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, Interval);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;   // previous sample still running
        try
        {
            bool primed = _opened;
            EnsureOpen();
            if (_query == IntPtr.Zero) return;
            if (PdhCollectQueryData(_query) != 0) return;
            if (!primed) return;

            var sample = Read();
            _dispatcher.BeginInvoke(() => Sampled?.Invoke(sample));
        }
        catch (Exception ex)
        {
            // A counter going away mid-read (GPU driver reset, USB disk unplugged) just skips a tick.
            App.Log($"效能取樣失敗：{ex}");
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private void EnsureOpen()
    {
        if (_opened) return;
        _opened = true;
        if (PdhOpenQuery(null, IntPtr.Zero, out _query) != 0) { _query = IntPtr.Zero; return; }

        // "% Processor Utility" is what Task Manager uses on Windows 10+; it can pass 100 when the
        // CPU boosts, so it's clamped. Older systems fall back to "% Processor Time".
        _cpu = Add(@"\Processor Information(_Total)\% Processor Utility");
        if (_cpu == IntPtr.Zero) _cpu = Add(@"\Processor(_Total)\% Processor Time");

        _gpu = Add(@"\GPU Engine(*)\Utilization Percentage");
        _disk = Add(@"\PhysicalDisk(*)\% Idle Time");
        _netDown = Add(@"\Network Interface(*)\Bytes Received/sec");
        _netUp = Add(@"\Network Interface(*)\Bytes Sent/sec");
    }

    private IntPtr Add(string path) =>
        PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero;

    private PerfSample Read()
    {
        double? cpu = ReadSingle(_cpu) is { } c ? Clamp01(c / 100) : null;

        double? gpu = null;
        if (ReadArray(_gpu) is { } engines)
        {
            // Instance names look like "pid_1234_luid_0x0_0xC2E3_phys_0_eng_3_engtype_3D".
            // Add up every process on the same engine, then report the busiest engine.
            var perEngine = new Dictionary<string, double>();
            foreach (var (name, value) in engines)
            {
                int i = name.IndexOf("luid_", StringComparison.Ordinal);
                string key = i >= 0 ? name[i..] : name;
                perEngine[key] = perEngine.GetValueOrDefault(key) + value;
            }
            gpu = Clamp01((perEngine.Count > 0 ? perEngine.Values.Max() : 0) / 100);
        }

        double? disk = null;
        if (ReadArray(_disk) is { } disks)
        {
            double busiest = 0;
            foreach (var (name, idle) in disks)
                if (name != "_Total") busiest = Math.Max(busiest, 100 - idle);
            disk = Clamp01(busiest / 100);
        }

        double down = ReadArray(_netDown)?.Sum(x => x.Value) ?? 0;
        double up = ReadArray(_netUp)?.Sum(x => x.Value) ?? 0;

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        double? memory = null;
        ulong used = 0, total = 0;
        if (GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
        {
            total = mem.ullTotalPhys;
            used = total - mem.ullAvailPhys;
            memory = Clamp01((double)used / total);
        }

        return new PerfSample(cpu, memory, used, total, gpu, disk, down, up);
    }

    private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;

    // ---- PDH helpers ---------------------------------------------------------

    private static double? ReadSingle(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var v) != 0) return null;
        return v.CStatus <= 1 ? v.DoubleValue : null;
    }

    private static List<(string Name, double Value)>? ReadArray(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        const uint format = PDH_FMT_DOUBLE | PDH_FMT_NOCAP100;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            uint size = 0;
            uint status = PdhGetFormattedCounterArray(counter, format, ref size, out _, IntPtr.Zero);
            if (status != PDH_MORE_DATA) return status == 0 ? new() : null;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArray(counter, format, ref size, out uint count, buffer);
                if (status == PDH_MORE_DATA) continue;   // instances changed in between; try again
                if (status != 0) return null;

                // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then the value (status at +8, double at +16).
                const int itemSize = 24;
                var result = new List<(string, double)>((int)count);
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buffer + i * itemSize;
                    if ((uint)Marshal.ReadInt32(item, 8) > 1) continue;   // PDH_CSTATUS_* other than valid/new
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                    double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
                    result.Add((name, value));
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        return null;
    }

    public void Dispose()
    {
        Stop();
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
    }

    // ---- Interop -------------------------------------------------------------

    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type,
        out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize,
        out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
