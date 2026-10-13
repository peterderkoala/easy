using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Easy.Platform;

// Wiring every host shares: logging, correlation ID, token validation (not in the gateway), /health (S16, Compose healthchecks grep for "Healthy").
public static class Hosting
{
    public static WebApplicationBuilder AddEasyPlatform(this WebApplicationBuilder builder, bool validateTokens = true)
    {
        if (validateTokens) Auth.Add(builder);
        builder.Services.AddSerilog(config => Logging.Configure(config, builder.Environment.ApplicationName));
        builder.Services.AddHealthChecks();
        builder.Services.AddHttpContextAccessor().AddSingleton<IInterceptor, AuditInterceptor>(); // picked up by AddEasyData
        return builder;
    }

    public static WebApplication UseEasyPlatform(this WebApplication app)
    {
        app.Use(CorrelationId.Handle);
        Auth.Map(app);
        app.MapHealthChecks("/health", new()
        {
            ResponseWriter = (context, report) => context.Response.WriteAsync($"{report.Status} {app.Environment.ApplicationName}"),
        });
        return app;
    }
}
