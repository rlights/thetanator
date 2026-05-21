using System;
using System.IO;

namespace CpuThermals.Core;

public static class DebugLogger
{
    private static readonly string LogPath = "theta_nator.log";
    public static bool IsLoggingEnabled { get; set; } = false;

    /// Logs a message to the console and optionally to a file. 
    /// Ensures that any carriage-return progress lines are cleared first.
    public static void Log(string message)
    {
        string timestampedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        
        // Clear the current console line in case a \r progress line was active
        try 
        {
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
        } 
        catch 
        {
            // Fallback if WindowWidth is not available
            Console.Write("\r                                                                                \r");
        }
        
        Console.WriteLine(timestampedMessage);
        
        // File Output (Optional)
        if (IsLoggingEnabled)
        {
            try
            {
                File.AppendAllText(LogPath, timestampedMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    /// Specifically for real-time updating lines that use \r
    public static void LogProgress(string message)
    {
        try 
        {
            // Pad with spaces to ensure previous longer messages are wiped
            string paddedMessage = message.PadRight(Console.WindowWidth - 1);
            Console.Write("\r" + paddedMessage);
        }
        catch 
        {
            Console.Write("\r" + message);
        }
    }

    public static void Clear()
    {
        if (!IsLoggingEnabled) return;

        try
        {
            if (File.Exists(LogPath)) File.Delete(LogPath);
        }
        catch { }
    }
}