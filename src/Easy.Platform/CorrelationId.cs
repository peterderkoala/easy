using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace Easy.Platform;

// S11: one ID per operation. Taken from the request or generated, written back to the request
// so the gateway forwards it, returned to the caller, and pushed into the log context.
public static partial class CorrelationId
{
    public const string Header = "X-Correlation-ID";

    public static async Task Handle(HttpContext context, RequestDelegate next)
    {
        string id = context.Request.Headers[Header].ToString();
        if (!Valid().IsMatch(id)) // client input ends up in logs: no free text
            id = Guid.NewGuid().ToString();

        context.Request.Headers[Header] = id;
        context.Response.Headers[Header] = id;
        using (LogContext.PushProperty("CorrelationId", id))
            await next(context);
    }

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    private static partial Regex Valid();
}
