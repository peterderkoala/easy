using Easy.Platform;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform(validateTokens: false); // S09: services validate, the gateway only routes
// S06: the upload route takes its body limit from the same Upload__MaxBytes as MainApi.
// A source of its own, so an appsettings reload cannot drop it.
builder.Configuration.AddInMemoryCollection([new("ReverseProxy:Routes:main-api-upload:MaxRequestBodySize", Upload.MaxBytes(builder.Configuration).ToString())]);
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build().UseEasyPlatform();

app.MapReverseProxy();

app.Run();
