using Easy.Platform;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform();
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build().UseEasyPlatform();

app.MapReverseProxy();

app.Run();
