using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CpuThermals.Core;

public enum TestPhase
{
    NotStarted,
    IdleBaseline,
    PreSoak,
    RampUp,
    Soak,
    Cooldown,
    Completed
}

public enum ThermalTestMode
{
    MountResistance, // Mode 1: Cyclical step-down with Aligned Trough
    SystemResistance // Mode 2: Long soak step-up
}

public class TestProgressArgs : EventArgs
{
    public TestPhase Phase { get; set; }
    public int CurrentLoop { get; set; }
    public int TotalLoops { get; set; }
    public CpuSensorData? CurrentData { get; set; }
    public TimeSpan RemainingTime { get; set; }
}

public class ThermalTestResult
{
    public ThermalTestMode Mode { get; set; }
    public float AvgIdleTemp { get; set; }
    public float AvgIdlePower { get; set; }
    public float AvgLoadTemp { get; set; }
    public float AvgLoadPower { get; set; }
    public float AvgLoadClock { get; set; }
    public bool DidThrottle { get; set; }
    public float ThermalResistance { get; set; } // Mean °C/W
    public float StandardDeviation { get; set; } // Precision metric
    public List<float> LoopResistances { get; set; } = new(); 
}

public class ThermalTestManager : IDisposable
{
    private readonly HardwareMonitor _monitor;
    private readonly StressTestRunner _runner;
    private TestPhase _currentPhase = TestPhase.NotStarted;
    private int _currentLoop = 0;
    private int _totalLoops = 1;
    private bool _throttleDetected = false;

    public event EventHandler<TestProgressArgs>? ProgressUpdated;

    public ThermalTestManager()
    {
        _monitor = new HardwareMonitor();
        _runner = new StressTestRunner();
    }

    public async Task EnsureReadyAsync()
    {
        await _runner.EnsureDownloadedAsync();
    }

    public async Task<ThermalTestResult> RunMountTestAsync(int loops, TimeSpan loadDuration, TimeSpan cooldownDuration, TimeSpan preSoakDuration, CancellationToken ct = default)
    {
        _totalLoops = loops;
        _throttleDetected = false;
        var loopResults = new List<float>();
        
        List<CpuSensorData> allLoadData = new();
        List<CpuSensorData> allIdleData = new();

        // 1. Optional Pre-Soak
        if (preSoakDuration > TimeSpan.Zero)
        {
            DebugLogger.Log($"Starting Pre-Soak ({preSoakDuration.TotalSeconds}s) to establish thermal equilibrium...");
            _currentPhase = TestPhase.PreSoak;
            _runner.Start();
            await CollectDataAsync(preSoakDuration, 250, ct); // Normal polling for soak
            _runner.Stop();
            DebugLogger.Log("Pre-Soak complete. Cooling down briefly...");
            await Task.Delay(5000, ct); 
        }

        for (int i = 1; i <= loops; i++)
        {
            _currentLoop = i;
            DebugLogger.Log($"--- Loop {i}/{loops} ---");

            // 1. Ramp Up
            _currentPhase = TestPhase.RampUp;
            _runner.Start();
            await Task.Delay(2000, ct); 

            // 2. Load Soak
            _currentPhase = TestPhase.Soak;
            var loadData = await CollectDataAsync(loadDuration, 250, ct);
            allLoadData.AddRange(loadData);

            // 3. Aligned Trough Search (High-Frequency)
            DebugLogger.Log("Killing load and searching for thermal trough (50ms polling)...");
            
            // Capture Load Baseline (final 2s of LoadData)
            var loadBaselineWindow = loadData.Skip(Math.Max(0, loadData.Count - 8)).ToList(); // 8 * 250ms = 2s
            float baselineTemp = loadBaselineWindow.Average(d => d.Temperature ?? 0);
            float baselinePower = loadBaselineWindow.Average(d => d.Power ?? 0);

            _runner.Stop();
            _currentPhase = TestPhase.Cooldown;
            
            // Collect 1.5s of high-frequency data
            var troughSearchData = await CollectDataAsync(TimeSpan.FromSeconds(1.5), 50, ct);
            allIdleData.AddRange(troughSearchData);

            // Find the Trough (local minimum temperature)
            var troughFrame = troughSearchData
                .Where(d => d.Temperature.HasValue)
                .OrderBy(d => d.Temperature)
                .FirstOrDefault();

            if (troughFrame != null)
            {
                float dT = baselineTemp - (troughFrame.Temperature ?? 0);
                float dP = baselinePower - (troughFrame.Power ?? 0);
                float res = dP > 1 ? dT / dP : 0;
                loopResults.Add(res);
                DebugLogger.Log($"Loop {i} Trough: {troughFrame.Temperature:F2}°C | Resistance: {res:F4} °C/W");
            }

            // Finish the rest of the cooldown at normal speed
            var remainingCooldown = cooldownDuration - TimeSpan.FromSeconds(1.5);
            if (remainingCooldown > TimeSpan.Zero)
            {
                allIdleData.AddRange(await CollectDataAsync(remainingCooldown, 250, ct));
            }
        }

        _currentPhase = TestPhase.Completed;

        float mean = loopResults.Any() ? loopResults.Average() : 0;
        float stdDev = 0;
        if (loopResults.Count > 1)
        {
            float sumOfSquares = loopResults.Select(r => (r - mean) * (r - mean)).Sum();
            stdDev = (float)Math.Sqrt(sumOfSquares / (loopResults.Count - 1));
        }

        return new ThermalTestResult
        {
            Mode = ThermalTestMode.MountResistance,
            AvgLoadTemp = allLoadData.Any() ? allLoadData.Average(d => d.Temperature ?? 0) : 0,
            AvgLoadPower = allLoadData.Any() ? allLoadData.Average(d => d.Power ?? 0) : 0,
            AvgLoadClock = allLoadData.Any() ? allLoadData.Average(d => d.ClockSpeed ?? 0) : 0,
            AvgIdleTemp = allIdleData.Any() ? allIdleData.Average(d => d.Temperature ?? 0) : 0,
            AvgIdlePower = allIdleData.Any() ? allIdleData.Average(d => d.Power ?? 0) : 0,
            DidThrottle = _throttleDetected,
            LoopResistances = loopResults,
            ThermalResistance = mean,
            StandardDeviation = stdDev
        };
    }

    public async Task<ThermalTestResult> RunSystemTestAsync(TimeSpan idleDuration, TimeSpan soakDuration, CancellationToken ct = default)
    {
        _totalLoops = 1;
        _currentLoop = 1;
        _throttleDetected = false;

        // 1. Idle Baseline
        _currentPhase = TestPhase.IdleBaseline;
        var idleData = await CollectDataAsync(idleDuration, 250, ct);

        // 2. Ramp Up
        _currentPhase = TestPhase.RampUp;
        _runner.Start();
        await Task.Delay(5000, ct);

        // 3. Load Soak
        _currentPhase = TestPhase.Soak;
        var loadData = await CollectDataAsync(soakDuration, 250, ct);

        _runner.Stop();
        _currentPhase = TestPhase.Completed;

        float avgIT = idleData.Any() ? idleData.Average(d => d.Temperature ?? 0) : 0;
        float avgIP = idleData.Any() ? idleData.Average(d => d.Power ?? 0) : 0;
        float avgLT = loadData.Any() ? loadData.Average(d => d.Temperature ?? 0) : 0;
        float avgLP = loadData.Any() ? loadData.Average(d => d.Power ?? 0) : 0;

        float dT = avgLT - avgIT;
        float dP = avgLP - avgIP;

        return new ThermalTestResult
        {
            Mode = ThermalTestMode.SystemResistance,
            AvgIdleTemp = avgIT,
            AvgIdlePower = avgIP,
            AvgLoadTemp = avgLT,
            AvgLoadPower = avgLP,
            AvgLoadClock = loadData.Any() ? loadData.Average(d => d.ClockSpeed ?? 0) : 0,
            DidThrottle = _throttleDetected,
            ThermalResistance = dP > 1 ? dT / dP : 0,
            StandardDeviation = 0
        };
    }

    private async Task<List<CpuSensorData>> CollectDataAsync(TimeSpan duration, int pollIntervalMs, CancellationToken ct)
    {
        var dataList = new List<CpuSensorData>();
        DateTime startTime = DateTime.Now;

        while (DateTime.Now - startTime < duration)
        {
            ct.ThrowIfCancellationRequested();

            var data = _monitor.GetCurrentData();
            dataList.Add(data);

            if (data.IsThrottling) _throttleDetected = true;

            ProgressUpdated?.Invoke(this, new TestProgressArgs
            {
                Phase = _currentPhase,
                CurrentLoop = _currentLoop,
                TotalLoops = _totalLoops,
                CurrentData = data,
                RemainingTime = duration - (DateTime.Now - startTime)
            });

            await Task.Delay(pollIntervalMs, ct);
        }

        return dataList;
    }

    public void Dispose()
    {
        _monitor.Dispose();
        _runner.Dispose();
    }
}