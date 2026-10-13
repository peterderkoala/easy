using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Easy.Platform;

// S09 zero trust: every host except the gateway validates Keycloak access tokens itself
// (signature, issuer, audience easy-api, lifetime). The "Keycloak" config section binds onto JwtBearerOptions:
// Authority is the issuer the browser sees; MetadataAddress optionally points discovery and keys at an
// in-container URL; RequireHttpsMetadata=false only for the plain-http dev Keycloak.
public static class Auth
{
    public static void Add(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            builder.Configuration.GetSection("Keycloak").Bind(options);
            options.Audience = "easy-api";
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30); // default 5 min would double the 5-min token lifetime
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            options.TokenValidationParameters.RoleClaimType = "roles"; // flat claim from the realm's role mapper
            options.Events = new()
            {
                // Browsers cannot set headers on WebSockets, so SignalR sends ?access_token=. Accept it on hub paths only.
                OnMessageReceived = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/hubs"))
                        context.Token = context.Request.Query["access_token"];
                    return Task.CompletedTask;
                },
            };
        });
        builder.Services.AddAuthorization();
    }

    // Authenticated probe: proves the host accepted the token and resolves the roles.
    public static void Map(WebApplication app)
    {
        if (app.Services.GetService<IAuthenticationSchemeProvider>() is null)
            return; // gateway
        app.MapGet("/whoami", (ClaimsPrincipal user) =>
            new { name = user.Identity?.Name, roles = new[] { "examiner", "admin" }.Where(user.IsInRole) }).RequireAuthorization();
    }
}
