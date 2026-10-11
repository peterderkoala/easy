using Easy.Platform;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform(validateTokens: false); // S09: services validate, the gateway only routes
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build().UseEasyPlatform();

app.MapReverseProxy();

app.Run();
