using System;
using System.Threading.Tasks;
using CpuThermals.Core;

namespace CpuThermals.ConsoleApp;

class Program
{
    static async Task Main(string[] args)
    {
        // --- Argument Parsing (Initial pass for logging) ---
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].ToLower() == "--log")
            {
                DebugLogger.IsLoggingEnabled = true;
            }
        }

        DebugLogger.Clear();
        DebugLogger.Log("=== CPU Thermal Resistance Calculator ===");

        // --- High-Rigor Production Defaults ---
        bool isSystemMode = false;
        bool usePreSoak = true;
        int? customPreSoakDuration = null;
        int? customLoops = null;
        int? customSoakSeconds = null; 
        int? customIdleSeconds = null; 
        
        // --- Full Argument Parsing ---
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--system":
                    isSystemMode = true;
                    break;
                case "--no-presoak":
                    usePreSoak = false;
                    break;
                case "--presoak-duration":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int ps)) { customPreSoakDuration = ps; i++; }
                    break;
                case "--loops":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int l)) { customLoops = l; i++; }
                    break;
                case "--soak":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int s)) { customSoakSeconds = s; i++; }
                    break;
                case "--idle":
                case "--cooldown":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int idle)) { customIdleSeconds = idle; i++; }
                    break;
                case "--log":
                    // Already handled above
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return;
            }
        }

        // --- Final Parameter Resolution ---
        int soakSeconds = customSoakSeconds ?? (isSystemMode ? 120 : 30);
        int idleSeconds = customIdleSeconds ?? (isSystemMode ? 30 : 10);
        int loops = customLoops ?? (isSystemMode ? 1 : 7);
        int preSoakDuration = customPreSoakDuration ?? 30;
        
        using var manager = new ThermalTestManager();
        
        manager.ProgressUpdated += (s, e) =>
        {
            string loopStr = e.TotalLoops > 1 ? $" [Loop {e.CurrentLoop}/{e.TotalLoops}]" : "";
            string dataStr = e.CurrentData != null 
                ? $" | {e.CurrentData.Temperature:F1}°C | {e.CurrentData.Power:F1}W | {e.CurrentData.ClockSpeed:F0}MHz{(e.CurrentData.IsThrottling ? " [THROTTLING]" : "")}" 
                : "";
            
            DebugLogger.LogProgress($"[{e.Phase}]{loopStr} Remaining: {e.RemainingTime.TotalSeconds:F0}s{dataStr}");
        };

        try
        {
            DebugLogger.Log("Initializing sensors and downloading dependencies...");
            await manager.EnsureReadyAsync();

            if (isSystemMode)
            {
                DebugLogger.Log("");
                DebugLogger.Log("Mode: SYSTEM RESISTANCE (Long Soak)");
                DebugLogger.Log($"- Idle: {idleSeconds}s");
                DebugLogger.Log($"- Soak: {soakSeconds}s");
                if (DebugLogger.IsLoggingEnabled) DebugLogger.Log("- Logging: Enabled (theta_nator.log)");
                DebugLogger.Log("");
                DebugLogger.Log("Press ENTER to start...");
                Console.ReadLine();

                var result = await manager.RunSystemTestAsync(TimeSpan.FromSeconds(idleSeconds), TimeSpan.FromSeconds(soakSeconds));
                DisplayResults(result);
            }
            else
            {
                DebugLogger.Log("");
                DebugLogger.Log("Mode: MOUNT RESISTANCE (Cyclical Step-Down)");
                DebugLogger.Log($"- Pre-Soak: {(usePreSoak ? $"{preSoakDuration}s" : "None")}");
                DebugLogger.Log($"- Loops: {loops}");
                DebugLogger.Log($"- Load per loop: {soakSeconds}s");
                DebugLogger.Log($"- Cooldown per loop: {idleSeconds}s");
                if (DebugLogger.IsLoggingEnabled) DebugLogger.Log("- Logging: Enabled (theta_nator.log)");
                DebugLogger.Log("");
                DebugLogger.Log("Press ENTER to start...");
                Console.ReadLine();

                var result = await manager.RunMountTestAsync(
                    loops, 
                    TimeSpan.FromSeconds(soakSeconds), 
                    TimeSpan.FromSeconds(idleSeconds), 
                    usePreSoak ? TimeSpan.FromSeconds(preSoakDuration) : TimeSpan.Zero);
                
                DisplayResults(result);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"\nAn error occurred: {ex.Message}");
            if (ex.InnerException != null) DebugLogger.Log($"Inner: {ex.InnerException.Message}");
        }

        DebugLogger.Log("");
        DebugLogger.Log("Press ENTER to exit.");
        Console.ReadLine();
    }

    static void PrintHelp()
    {
        Console.WriteLine("\nUsage:");
        Console.WriteLine("  ThetaNator.exe [options]");
        Console.WriteLine("\nOptions:");
        Console.WriteLine("  --system              Run System Resistance test (Long Soak) instead of Mount test.");
        Console.WriteLine("  --no-presoak          Skip the initial thermal equilibrium soak.");
        Console.WriteLine("  --presoak-duration <n> Duration of pre-soak in seconds (Default: 30s).");
        Console.WriteLine("  --loops <n>           Number of loops for Mount test (Default: 7).");
        Console.WriteLine("  --soak <n>            Duration in seconds for the load phase.");
        Console.WriteLine("  --idle <n>            Duration in seconds for idle/cooldown phase.");
        Console.WriteLine("  --log                 Enable file logging to 'theta_nator.log'.");
        Console.WriteLine("  --help                Show this help message.");
    }

    static void DisplayResults(ThermalTestResult result)
    {
        DebugLogger.Log("");
        DebugLogger.Log("=== Final Results ===");
        DebugLogger.Log($"Mode: {result.Mode}");
        DebugLogger.Log($"Avg Load: {result.AvgLoadTemp:F2}°C @ {result.AvgLoadPower:F2}W ({result.AvgLoadClock:F0}MHz)");
        
        if (result.Mode == ThermalTestMode.MountResistance)
        {
            DebugLogger.Log($"Avg Idle (across loops): {result.AvgIdleTemp:F2}°C @ {result.AvgIdlePower:F2}W");
            DebugLogger.Log("Loop Detail (Mount Resistance):");
            for (int i = 0; i < result.LoopResistances.Count; i++)
            {
                DebugLogger.Log($"  Loop {i+1}: {result.LoopResistances[i]:F4} °C/W");
            }
            DebugLogger.Log("--------------------");
            DebugLogger.Log($"STATISTICAL PRECISION (σ): {result.StandardDeviation:F5} °C/W");
        }
        else
        {
            DebugLogger.Log($"Baseline Idle: {result.AvgIdleTemp:F2}°C @ {result.AvgIdlePower:F2}W");
            DebugLogger.Log("--------------------");
        }

        if (result.DidThrottle)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            DebugLogger.Log("WARNING: Throttling detected! Result represents a 'capped' thermal state.");
            Console.ResetColor();
        }

        DebugLogger.Log($"THERMAL RESISTANCE: {result.ThermalResistance:F4} °C/W");
        DebugLogger.Log("====================");
    }
}