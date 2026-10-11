extern alias gateway;
extern alias mainapi;
extern alias examroom;
extern alias notification;
extern alias scheduler;

using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Easy.Data;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.Keycloak;

namespace Easy.IntegrationTests;

// One Keycloak per test run, importing the committed dev realm (deploy/keycloak).
public sealed class KeycloakFixture : IAsyncLifetime
{
    readonly KeycloakContainer container = new KeycloakBuilder("quay.io/keycloak/keycloak:26.8.0")
        .WithResourceMapping(new FileInfo("easy-realm.json"), "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .WithEnvironment("NOTIFICATION_CLIENT_SECRET", "test")
        .WithEnvironment("SCHEDULER_CLIENT_SECRET", "test")
        .Build();

    public string Issuer => new Uri(new Uri(container.GetBaseAddress()), "realms/easy").ToString();

    public Task InitializeAsync() => container.StartAsync();
    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    // The browser flow of the public client easy-web: authorization code with PKCE S256.
    // Dev users have their username as password.
    public async Task<string> LoginAsync(string user)
    {
        const string redirect = "http://localhost:8080/authentication/login-callback";
        string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        // Keycloak marks its login cookies Secure, which HttpClient's cookie store drops on plain http: carry them by hand.
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

        var page = await http.GetAsync($"{Issuer}/protocol/openid-connect/auth?client_id=easy-web&response_type=code&scope=openid"
            + $"&redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={challenge}&code_challenge_method=S256");
        string action = WebUtility.HtmlDecode(Regex.Match(await page.Content.ReadAsStringAsync(), "action=\"([^\"]+)\"").Groups[1].Value);
        var login = await http.SendAsync(new(HttpMethod.Post, action)
        {
            Content = Form(("username", user), ("password", user)),
            Headers = { { "Cookie", page.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]) } },
        });
        string code = QueryHelpers.ParseQuery(login.Headers.Location!.Query)["code"]!;

        return await TokenAsync(http, ("grant_type", "authorization_code"), ("client_id", "easy-web"), ("code", code),
            ("redirect_uri", redirect), ("code_verifier", verifier));
    }

    // Same realm and signing key, but issued to admin-cli: no easy-api audience.
    public async Task<string> WrongAudienceTokenAsync()
    {
        using var http = new HttpClient();
        return await TokenAsync(http, ("grant_type", "password"), ("client_id", "admin-cli"), ("username", "examiner"), ("password", "examiner"));
    }

    // A valid easy-api token that lives one second, from a throwaway client created through the admin API.
    public async Task<string> OneSecondTokenAsync()
    {
        using var http = new HttpClient();
        string admin = await TokenAsync(http, [("grant_type", "password"), ("client_id", "admin-cli"),
            ("username", KeycloakBuilder.DefaultUsername), ("password", KeycloakBuilder.DefaultPassword)], "master");
        http.DefaultRequestHeaders.Authorization = new("Bearer", admin);
        string clientId = $"expiry-{Guid.NewGuid():N}";
        var created = await http.PostAsJsonAsync(new Uri(new Uri(container.GetBaseAddress()), "admin/realms/easy/clients"), new
        {
            clientId,
            publicClient = true,
            directAccessGrantsEnabled = true,
            attributes = new Dictionary<string, string> { ["access.token.lifespan"] = "1" },
            protocolMappers = new[]
            {
                new { name = "aud", protocol = "openid-connect", protocolMapper = "oidc-audience-mapper",
                    config = new Dictionary<string, string> { ["included.client.audience"] = "easy-api", ["access.token.claim"] = "true" } },
                new { name = "flat roles", protocol = "openid-connect", protocolMapper = "oidc-usermodel-realm-role-mapper", // as on easy-web
                    config = new Dictionary<string, string> { ["claim.name"] = "roles", ["multivalued"] = "true", ["access.token.claim"] = "true" } },
            },
        });
        created.EnsureSuccessStatusCode();
        http.DefaultRequestHeaders.Authorization = null;
        return await TokenAsync(http, ("grant_type", "password"), ("client_id", clientId), ("username", "examiner"), ("password", "examiner"));
    }

    // Client credentials of the notification service account (secret set on the container above).
    public async Task<string> ServiceAccountTokenAsync()
    {
        using var http = new HttpClient();
        return await TokenAsync(http, ("grant_type", "client_credentials"), ("client_id", "easy-notification"), ("client_secret", "test"));
    }

    Task<string> TokenAsync(HttpClient http, params (string, string)[] fields) => TokenAsync(http, fields, "easy");

    async Task<string> TokenAsync(HttpClient http, (string, string)[] fields, string realm)
    {
        var response = await http.PostAsync(new Uri(new Uri(container.GetBaseAddress()), $"realms/{realm}/protocol/openid-connect/token"), Form(fields));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString()!;
    }

    static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
        new(fields.Select(f => KeyValuePair.Create(f.Key, f.Value)));
}

[Collection(nameof(Infra))] // MainApi needs Postgres and RabbitMQ at start-up
public class AuthTests(KeycloakFixture keycloak, Infra infra) : IClassFixture<KeycloakFixture>, IDisposable
{
    readonly List<IDisposable> hosts = [];

    public static TheoryData<string> Services => ["main-api", "exam-room", "notification", "scheduler"];

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Valid_token_is_accepted_by_each_host(string service)
    {
        var response = await WhoAmI(Host(service), await keycloak.LoginAsync("examiner"));

        response.EnsureSuccessStatusCode();
        Assert.Equal("examiner", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString());
    }

    [Theory]
    [MemberData(nameof(Services))]
    public async Task Token_with_wrong_audience_is_rejected_by_each_host(string service)
    {
        var response = await WhoAmI(Host(service), await keycloak.WrongAudienceTokenAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("audience", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        // Without skew the one-second token is expired after two seconds; the default skew would hide that for minutes.
        var client = Host("main-api", s => s.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            o => o.TokenValidationParameters.ClockSkew = TimeSpan.Zero));
        string token = await keycloak.OneSecondTokenAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        var response = await WhoAmI(client, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("expired", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Admin_has_examiner_role()
    {
        var response = await WhoAmI(Host("main-api"), await keycloak.LoginAsync("admin"));

        response.EnsureSuccessStatusCode();
        var roles = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles").EnumerateArray().Select(r => r.GetString());
        Assert.Equal(["examiner", "admin"], roles);
    }

    [Fact]
    public async Task Gateway_does_not_validate_tokens_services_do()
    {
        var mainApi = Track(infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => Keycloak(b)));
        mainApi.UseKestrel(0);
        mainApi.StartServer();
        // The gateway knows no Keycloak at all: it forwards whatever bearer token it gets.
        var gateway = Track(new WebApplicationFactory<gateway::Program>().WithWebHostBuilder(b => b
            .UseSetting("ReverseProxy:Clusters:main-api:Destinations:main-api:Address", mainApi.ClientOptions.BaseAddress.ToString())));
        string token = await keycloak.LoginAsync("examiner");

        Assert.Null(gateway.Services.GetService<IAuthenticationSchemeProvider>());
        Assert.Equal(HttpStatusCode.OK, (await WhoAmI(gateway.CreateClient(), token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WhoAmI(gateway.CreateClient(), "not-a-token")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WhoAmI(mainApi.CreateClient(), "not-a-token")).StatusCode);
    }

    [Fact]
    public async Task First_login_creates_examiner_mirror_with_role()
    {
        var mainApi = Track(infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => Keycloak(b)));
        string token = await keycloak.LoginAsync("admin");

        for (int i = 0; i < 2; i++) // a second login must not add a row
            (await mainApi.CreateClient().SendAsync(new(HttpMethod.Post, "/api/me")
                { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } })).EnsureSuccessStatusCode();

        using var scope = mainApi.Services.CreateScope();
        var examiner = Assert.Single(await scope.ServiceProvider.GetRequiredService<EasyDb>().Examiners.ToListAsync());
        Assert.Equal(ExaminerRole.Admin, examiner.Role);
        Assert.Equal("Dev Admin", examiner.DisplayName);
        Assert.Equal("admin@easy.local", examiner.Email);
        Assert.True(examiner.Active);
        Assert.False(string.IsNullOrEmpty(examiner.Subject));
    }

    [Fact]
    public async Task Service_account_cannot_register_as_examiner()
    {
        var client = Host("main-api");

        var response = await client.SendAsync(new(HttpMethod.Post, "/api/me")
            { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await keycloak.ServiceAccountTokenAsync()) } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Query_string_token_is_ignored_outside_hub_paths()
    {
        string token = await keycloak.LoginAsync("examiner");

        var response = await Host("main-api").GetAsync($"/whoami?access_token={token}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    static Task<HttpResponseMessage> WhoAmI(HttpClient client, string token) =>
        client.SendAsync(new(HttpMethod.Get, "/whoami") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) } });

    HttpClient Host(string service, Action<IServiceCollection>? services = null) => service switch
    {
        "main-api" => Client(infra.Configure(new WebApplicationFactory<mainapi::Program>()), services),
        "exam-room" => Client(new WebApplicationFactory<examroom::Program>(), services),
        "notification" => Client(infra.Configure(new WebApplicationFactory<notification::Program>()), services), // Rebus endpoint since spec 7
        "scheduler" => Client(new WebApplicationFactory<scheduler::Program>(), services),
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    HttpClient Client<T>(WebApplicationFactory<T> factory, Action<IServiceCollection>? services) where T : class
    {
        Track(factory);
        return Track(factory.WithWebHostBuilder(b => Keycloak(b).ConfigureServices(services ?? (_ => { })))).CreateClient();
    }

    IWebHostBuilder Keycloak(IWebHostBuilder builder) => builder
        .UseSetting("Keycloak:Authority", keycloak.Issuer)
        .UseSetting("Keycloak:RequireHttpsMetadata", "false");

    T Track<T>(T host) where T : IDisposable
    {
        hosts.Add(host);
        return host;
    }

    public void Dispose() => hosts.ForEach(h => h.Dispose());
}
