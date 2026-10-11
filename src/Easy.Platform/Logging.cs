using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Easy.Platform;

// S16: compact JSON to stdout, enriched with service name and correlation ID. Never log participant data.
public static class Logging
{
    public static LoggerConfiguration Configure(LoggerConfiguration config, string service, TextWriter? output = null) =>
        config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Yarp", LogEventLevel.Warning) // Information logs full URLs, incl. ?access_token= on hubs
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", service)
            .WriteTo.Sink(new JsonLineSink(output ?? Console.Out));

    sealed class JsonLineSink(TextWriter output) : ILogEventSink
    {
        readonly CompactJsonFormatter formatter = new();

        public void Emit(LogEvent logEvent)
        {
            lock (output)
                formatter.Format(logEvent, output);
        }
    }
}
