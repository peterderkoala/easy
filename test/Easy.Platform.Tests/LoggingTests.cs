using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Serilog;

namespace Easy.Platform.Tests;

public class LoggingTests
{
    [Fact]
    public async Task Log_line_is_json_with_service_name_and_correlation_id()
    {
        var line = await LogOneLineDuringRequest(context => context.Request.Headers[CorrelationId.Header] = "abc-123");

        Assert.Equal("Easy.Test", line.GetProperty("Service").GetString());
        Assert.Equal("abc-123", line.GetProperty("CorrelationId").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Max Mustermann")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123x")]
    public async Task Missing_or_invalid_correlation_id_is_replaced_and_returned(string? sent)
    {
        HttpContext? request = null;
        var line = await LogOneLineDuringRequest(context =>
        {
            request = context;
            if (sent is not null)
                context.Request.Headers[CorrelationId.Header] = sent;
        });

        var id = line.GetProperty("CorrelationId").GetString();
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(id, request!.Response.Headers[CorrelationId.Header]);
    }

    static async Task<JsonElement> LogOneLineDuringRequest(Action<HttpContext> arrange)
    {
        var output = new StringWriter();
        using var logger = Logging.Configure(new LoggerConfiguration(), "Easy.Test", output).CreateLogger();
        var context = new DefaultHttpContext();
        arrange(context);

        await CorrelationId.Handle(context, _ =>
        {
            logger.Information("hello {Thing}", 1);
            return Task.CompletedTask;
        });

        return JsonDocument.Parse(output.ToString().Trim()).RootElement;
    }
}
