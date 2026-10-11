using Easy.Platform;

var builder = WebApplication.CreateBuilder(args).AddEasyPlatform();
var app = builder.Build().UseEasyPlatform();

app.Run();
