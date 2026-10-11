using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Easy.Platform;

// Wiring every host shares: logging, correlation ID, /health (S16, Compose healthchecks grep for "Healthy").
public static class Hosting
{
    public static WebApplicationBuilder AddEasyPlatform(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog(config => Logging.Configure(config, builder.Environment.ApplicationName));
        builder.Services.AddHealthChecks();
        return builder;
    }

    public static WebApplication UseEasyPlatform(this WebApplication app)
    {
        app.Use(CorrelationId.Handle);
        app.MapHealthChecks("/health", new()
        {
            ResponseWriter = (context, report) => context.Response.WriteAsync($"{report.Status} {app.Environment.ApplicationName}"),
        });
        return app;
    }
}
