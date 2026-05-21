using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace CpuThermals.Core;

public class StressTestRunner : IDisposable
{
    private Process? _primeProcess;
    private readonly string _baseDir;
    private readonly string _toolsDir;
    private readonly string _primeExePath;
    // URL to the Windows Service version (Headless)
    private const string P95_URL = "https://download.mersenne.ca/mirror/gimps/v30/30.19/p95v3019b20.win64.service.zip";

    public StressTestRunner()
    {
        _baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _toolsDir = Path.Combine(_baseDir, "tools", "prime95");
        _primeExePath = Path.Combine(_toolsDir, "ntprime64.exe");
    }

    public async Task EnsureDownloadedAsync()
    {
        if (File.Exists(_primeExePath))
        {
            DebugLogger.Log("Prime95 (Headless) already exists locally.");
        }
        else
        {
            Directory.CreateDirectory(_toolsDir);
            string zipPath = Path.Combine(_toolsDir, "p95_service.zip");

            DebugLogger.Log($"Prime95 (Headless) not found. Downloading from {P95_URL}...");

            using (var client = new HttpClient())
            {
                var response = await client.GetAsync(P95_URL);
                response.EnsureSuccessStatusCode();
                using (var fs = new FileStream(zipPath, FileMode.Create))
                {
                    await response.Content.CopyToAsync(fs);
                }
            }

            DebugLogger.Log("Extracting Headless Prime95...");
            ZipFile.ExtractToDirectory(zipPath, _toolsDir, true);
            File.Delete(zipPath);
            DebugLogger.Log("Prime95 Headless setup complete.");
        }

        // Optimization: Prepare the INI once during initialization, not in every loop.
        PreparePrimeIni();
    }

    public void Start()
    {
        if (_primeProcess != null && !_primeProcess.HasExited) return;

        if (!File.Exists(_primeExePath))
        {
            throw new FileNotFoundException("Prime95 executable missing. Call EnsureDownloadedAsync first.");
        }

        // Optimization: Removed PreparePrimeIni() from here as it now runs in EnsureDownloadedAsync()

        DebugLogger.Log($"Starting Prime95 (Headless) with working directory: {_toolsDir}");
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = _primeExePath,
            Arguments = $"-t -W\"{_toolsDir}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        _primeProcess = Process.Start(startInfo);
        if (_primeProcess != null)
        {
            DebugLogger.Log("Prime95 process started. Setting priority to BelowNormal.");
            _primeProcess.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
    }

    private void PreparePrimeIni()
    {
        string iniPath = Path.Combine(_toolsDir, "prime.ini");
        
        DebugLogger.Log("Writing prime.ini configuration...");
        // Small FFTs (Max Heat), hit all logical cores
        string iniContent = $@"
[Prime95]
TortureThreads={Environment.ProcessorCount}
MinTortureFFT=4
MaxTortureFFT=1024
TortureMem=0
TortureTime=15
";
        File.WriteAllText(iniPath, iniContent);
    }

    public void Stop()
    {
        if (_primeProcess != null && !_primeProcess.HasExited)
        {
            DebugLogger.Log("Stopping Prime95...");
            _primeProcess.Kill(true);
            _primeProcess.WaitForExit();
            DebugLogger.Log("Prime95 stopped.");
        }
    }

    public void Dispose()
    {
        Stop();
        _primeProcess?.Dispose();
    }
}