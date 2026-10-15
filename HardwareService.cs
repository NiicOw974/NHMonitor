using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace NHMonitor;

/// <summary>Valeurs mesurées à un instant donné. null = capteur indisponible.</summary>
public sealed record Snapshot(
    string CpuName, float? CpuLoad, float? CpuTemp,
    string? GpuName, float? GpuLoad, float? GpuTemp,
    float RamLoad, double RamUsedGb, double RamTotalGb,
    string? NetName, double? NetDownBps, double? NetUpBps);

/// <summary>Lit les capteurs matériels via LibreHardwareMonitor (CPU/GPU), l'API Win32 (RAM) et .NET (réseau).</summary>
public sealed class HardwareService : IDisposable
{
    private static readonly string[] CpuTempNames = ["CPU Package", "Core Max", "Core Average", "Core (Tctl/Tdie)", "CPU (Tctl/Tdie)", "Tctl/Tdie", "Tdie"];
    private static readonly string[] GpuLoadNames = ["GPU Core", "D3D 3D", "GPU"];
    private static readonly string[] GpuTempNames = ["GPU Core", "GPU Hot Spot", "GPU"];

    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware? _gpu;
    private readonly NetworkMonitor _network = new();

    public HardwareService()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
        _computer.Open();

        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // Priorité au GPU dédié, sinon le GPU intégré.
        _gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
            ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
            ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
    }

    public Snapshot Read()
    {
        Update(_cpu);
        Update(_gpu);

        float? cpuLoad = Find(_cpu, SensorType.Load, ["CPU Total"]);
        float? cpuTemp = Find(_cpu, SensorType.Temperature, CpuTempNames) ?? Max(_cpu, SensorType.Temperature);

        float? gpuLoad = Find(_gpu, SensorType.Load, GpuLoadNames) ?? Max(_gpu, SensorType.Load);
        float? gpuTemp = Find(_gpu, SensorType.Temperature, GpuTempNames) ?? Max(_gpu, SensorType.Temperature);

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double totalGb = mem.ullTotalPhys / 1024d / 1024 / 1024;
        double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1024d / 1024 / 1024;
        float ramLoad = totalGb > 0 ? (float)(usedGb / totalGb * 100) : 0;

        var (netName, netDown, netUp) = _network.Read();

        return new Snapshot(
            _cpu?.Name ?? "CPU", cpuLoad, cpuTemp,
            _gpu?.Name, gpuLoad, gpuTemp,
            ramLoad, usedGb, totalGb,
            netName, netDown, netUp);
    }

    private static void Update(IHardware? hw)
    {
        if (hw is null) return;
        try
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) sub.Update();
        }
        catch
        {
            // Un capteur défaillant ne doit pas faire planter l'application.
        }
    }

    private static IEnumerable<ISensor> Sensors(IHardware hw) =>
        hw.Sensors.Concat(hw.SubHardware.SelectMany(s => s.Sensors));

    private static float? Find(IHardware? hw, SensorType type, string[] names)
    {
        if (hw is null) return null;
        var sensors = Sensors(hw).Where(s => s.SensorType == type).ToList();
        foreach (var name in names)
        {
            var s = sensors.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (s is not null && Valid(type, s.Value)) return s.Value;
        }
        return null;
    }

    private static float? Max(IHardware? hw, SensorType type)
    {
        if (hw is null) return null;
        var values = Sensors(hw).Where(s => s.SensorType == type && Valid(type, s.Value)).Select(s => s.Value!.Value).ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    // Sans driver, LibreHardwareMonitor renvoie souvent 0 au lieu de null pour les températures.
    private static bool Valid(SensorType type, float? v) =>
        v is { } f && !float.IsNaN(f) && (type == SensorType.Temperature ? f > 0 : f >= 0);

    /// <summary>Rapport complet de LibreHardwareMonitor (tous les capteurs détectés), pour le diagnostic.</summary>
    public string GetReport()
    {
        Update(_cpu);
        Update(_gpu);
        return _computer.GetReport();
    }

    public void Dispose() => _computer.Close();

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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
