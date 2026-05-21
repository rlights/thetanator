using System;
using System.Linq;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace CpuThermals.Core;

public class CpuSensorData
{
    public float? Temperature { get; set; }
    public float? Power { get; set; }
    public float? ClockSpeed { get; set; }
    public bool IsThrottling { get; set; }
    public string CpuName { get; set; } = "Unknown CPU";
}

public class HardwareMonitor : IDisposable
{
    private readonly Computer _computer;
    private IHardware? _cpu;
    private ISensor? _tempSensor;
    private ISensor? _powerSensor;
    private ISensor? _clockSensor;
    private readonly List<ISensor> _throttleSensors = new();

    public HardwareMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true
        };
        _computer.Open();
        InitializeCpu();
    }

    private void InitializeCpu()
    {
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (_cpu == null)
        {
            throw new Exception("No CPU hardware detected.");
        }

        _cpu.Update();
        
        // Find Temperature Sensor
        _tempSensor = _cpu.Sensors.FirstOrDefault(s => 
            s.SensorType == SensorType.Temperature && 
            (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core (Max)", StringComparison.OrdinalIgnoreCase)));

        // Find Power Sensor
        _powerSensor = _cpu.Sensors.FirstOrDefault(s => 
            s.SensorType == SensorType.Power && 
            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));

        // Find Clock Speed (First core as proxy)
        _clockSensor = _cpu.Sensors.FirstOrDefault(s => 
            s.SensorType == SensorType.Clock && 
            s.Name.Contains("Core #1", StringComparison.OrdinalIgnoreCase));

        // Find Throttling/Limit sensors
        // Looking for "Throttling", "Limit", or Boolean flags
        _throttleSensors.AddRange(_cpu.Sensors.Where(s => 
            s.Name.Contains("Throttling", StringComparison.OrdinalIgnoreCase) || 
            s.Name.Contains("Limit", StringComparison.OrdinalIgnoreCase)));
    }

    public CpuSensorData GetCurrentData()
    {
        if (_cpu == null) throw new ObjectDisposedException(nameof(HardwareMonitor));

        _cpu.Update();

        return new CpuSensorData
        {
            CpuName = _cpu.Name,
            Temperature = _tempSensor?.Value,
            Power = _powerSensor?.Value,
            ClockSpeed = _clockSensor?.Value,
            // Throttling is true if any limit sensor is active (usually > 0)
            IsThrottling = _throttleSensors.Any(s => s.Value > 0)
        };
    }

    public void Dispose()
    {
        _computer.Close();
    }
}