# ThetaNator: CPU-to-Cooler thermal resistance tester

**Quantify your cpu-to-cooler junction performance & put your thermal paste skills to the test!  
ThetaNator measures the thermal resistance of your CPU cooler interface using artificial load and thermodynamic step-response analysis.**

## Project Overview
This tool provides a repeatable, objective, statistically rigorous metric for cooler junction efficiency, by correlating CPU Package Power (SVID) with Package Temperature. It implements a specialized **Step-Response Methodology** to isolate specific parts of the cooling stack, either measuring the cooler junction alone or the effectiveness of the entire cooling system.  
The tool is theoretically valid for both air and liquid cooling systems, but is designed mainly for liquid.

## Measurement Modes

### 1. Mount Resistance (Default Mode)  
This mode focuses on the "dry" thermal resistance of the junction between the Silicon Die, IHS, and Cooler / Coldplate.  
There are a variety of complicating/confounding factors that can affect this measurement, mainly background processes and OS work causing fluctuations in cpu temperature and power consumption. Additionally a cold start can skew the data.  

The default parameters are therefore chosen to eliminate as many of these factors as possible:
*   **Pre-Soak (Default 30s):** Heats up your cooler to a reasonably steady state before the first measurement, including heat soaked into the entire mass of the cooling system (whether air or water), and establishes a thermal gradient across the copper and TIM before the first measurement loop. This ensures every loop starts from a fairly consistent "warm" state.
*   **Cyclical Loops (Default n=7):** Testing multiple times helps remove the impact of the background noise. 7 samples provides a statistically significant data set, letting us identify and exclude any outliers without making you sit through dozens of test cycles.
*   **Aligned Trough Methodology:** To defeat high or shifting background OS noise, the tool increases sensor polling to **50ms** during the critical cooldown transition. It scans a tight 1.5s window immediately after the load drops to find the exact moment the hardware-averaged temperature hits its local minimum before background tasks can interfere. By using both the Power and Temperature readings from this exact time point, we achieve maximum thermodynamic precision. Probably.
*   **Statistical Precision (σ):** The tool calculates the **Standard Deviation** across all loops. 
    *   **Low σ (< 0.01):** High confidence. The measurement environment was stable and the thermal interface is behaving predictably.
    *   **High σ (> 0.02):** Lower confidence. This suggests significant background OS jitter or unstable ambient conditions during the test. Consider closing background apps and re-running, or re-run with a higher loop count.

### 2. System Resistance (`--system`)
This is designed for looking at the effectiveness of your cooling system as a whole. Great for figuring out that you need to clean your air intake (actually the inspiration for this project).
* Measures the overall dissipation capability of the entire loop (fans, radiators, ambient airflow).
* This is done with a single long soak (default 120s).
* Best for benchmarking different fan curves or checking for radiator dust buildup. Actually, just go clean it anyway. Trust me...

## Build instructions
.NET 10 SDK must be installed on the system. Built with 10.0.204 LTS.

To compile to a single self contained exe:  
`dotnet publish CpuThermals.Console -c Release -o ./releases/full /p:SelfContained=true /p:DebugType=None /p:DebugSymbols=false /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true`

Compile to single exe without dotnet runtime bundled:  
`dotnet publish CpuThermals.Console -c Release -o ./releases/lite /p:DebugType=None /p:DebugSymbols=false /p:PublishSingleFile=true`

To run directly from source:  
`dotnet build`  
`dotnet run --project CpuThermals.Console`

## Usage
Run the standalone executable as Administrator.

```
# Default rigorous test (presoak + 7 loops)
.\ThetaNator.exe

# Custom test parameters with logging enabled
.\ThetaNator.exe --loops 10 --soak 60 --log

# System Resistance Test (Long soak)
.\ThetaNator.exe --system --soak 300
```

### CLI Options:
*   `--system`: Run System Resistance test instead of Mount test.
*   `--log`: Enable file logging to `theta_nator.log`.
*   `--no-presoak`: Skip the initial thermal equilibrium soak.
*   `--presoak-duration <n>`: Duration of pre-soak in seconds (Default: 30s).
*   `--loops <n>`: Number of cycles (Default: 7).
*   `--soak <n>`: Seconds of load per cycle.
*   `--idle <n>`: Seconds of cooldown/baseline per cycle.
*   `--help`: Show usage info.

## Technical Details
*   **Engine:** .NET 10 (LTS)
*   **Sensors:** LibreHardwareMonitorLib (requires admin privileges, sorry)
*   **Load Generator:** Headless Prime95 (currently v30.19, fetched at runtime if not present)
*   **Logging:** Results and raw sensor data can be saved to `theta_nator.log` though this is not enabled by default

Yes, a robot helped write this. Don't hold it against me. Genuine feedback & contributions encouraged <3

## Comparative results
Open an issue if you want to contribute your measurements as an example data point!
* My 13900KF with a chonky water loop (EK Quantum Velocity² & EK Quantum Surface X360M) gets about 0.21°C/W - i should probably repaste it.
