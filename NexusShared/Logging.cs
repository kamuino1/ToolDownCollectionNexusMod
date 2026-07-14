using Serilog;

namespace NexusShared;

// Serilog setup: writes to the console (plain, same look as before) and a rolling daily file.
public static class Logging
{
    // filePathPattern e.g. "...\logs\collection-.log" -> Serilog appends the date: collection-20260714.log
    public static void Setup(string filePathPattern)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                filePathPattern,
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    // Log one line. The message is passed as a property (not a template), so any '{' '}' in
    // mod names / exception text is printed verbatim and never parsed by Serilog.
    public static void Line(string message) => Log.Information("{Line}", message);

    // Flush + release the file handle (call at the end of Main)
    public static void Close() => Log.CloseAndFlush();
}
