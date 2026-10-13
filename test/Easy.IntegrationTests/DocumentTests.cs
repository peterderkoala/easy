extern alias gateway;
extern alias mainapi;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Amazon.S3;
using Easy.Data;
using mainapi::Easy.MainApi.Documents;
using mainapi::Easy.MainApi.Participants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Easy.IntegrationTests;

// Spec 7 (issue 22): PDF upload through MainApi into SeaweedFS with SSE-C (S06, ADR 0002).
[Collection(nameof(Infra))]
public class DocumentTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nsecret participant documentation\n%%EOF");

    readonly List<IDisposable> hosts = [];
    string token = null!;

    public async Task InitializeAsync() => token = await keycloak.LoginAsync("examiner");

    public Task DisposeAsync()
    {
        hosts.ForEach(h => h.Dispose());
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Pdf_upload_stores_encrypted_object_and_document_row()
    {
        var mainApi = MainApi();
        Guid participant = await SeedParticipant(mainApi);

        var response = await Upload(mainApi.CreateClient(), participant, Pdf);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (document, stored) = await Db(mainApi, async db =>
            (await db.Documents.SingleAsync(), await db.Participants.SingleAsync()));
        Assert.Equal((participant, Pdf.Length), (document.ParticipantId, document.Size));
        Assert.Equal(DocumentStore.Key(participant, document.Id), document.ObjectKey);
        Assert.Equal(DocumentationStatus.Submitted, stored.DocumentationStatus);
        using (var key = mainApi.Services.GetRequiredService<ParticipantKeys>().Open(stored))
            Assert.Equal(Pdf, await mainApi.Services.GetRequiredService<DocumentStore>().Get(document.ObjectKey, key, default));
        using var http = new HttpClient();
        Assert.DoesNotContain("secret participant", await http.GetStringAsync($"{infra.SeaweedFiler}/buckets/easy-documents/{document.ObjectKey}"));
    }

    [Fact]
    public async Task Non_pdf_is_rejected()
    {
        var mainApi = MainApi();
        Guid participant = await SeedParticipant(mainApi);

        var response = await Upload(mainApi.CreateClient(), participant, Encoding.ASCII.GetBytes("<html>not a pdf</html>"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(await Db(mainApi, db => db.Documents.ToListAsync()));
    }

    [Fact]
    public async Task File_over_limit_is_rejected_at_each_layer()
    {
        byte[] big = [.. Pdf, .. new byte[2000]];

        // MainApi itself (TestServer has no Kestrel limits).
        var mainApi = MainApi(limit: 1000);
        Guid participant = await SeedParticipant(mainApi);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(mainApi.CreateClient(), participant, big)).StatusCode);
        // Kestrel of MainApi: the limit is set, and a real oversized request is refused.
        Assert.Equal(1000, mainApi.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize);
        var onKestrel = Track(MainApi(limit: 1000).WithWebHostBuilder(b => b.UseSetting("urls", "http://127.0.0.1:0")));
        onKestrel.UseKestrel();
        onKestrel.StartServer();
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(onKestrel.CreateClient(), participant, big)).StatusCode);
        // Gateway route, in front of a MainApi that would accept the file.
        // UseKestrel(0) on a derived factory still binds the default port 5000; a urls setting gives each its own port.
        var roomy = Track(MainApi(limit: 1_000_000).WithWebHostBuilder(b => b.UseSetting("urls", "http://127.0.0.1:0")));
        roomy.UseKestrel();
        roomy.StartServer();
        var gateway = Track(new WebApplicationFactory<gateway::Program>().WithWebHostBuilder(b => b
            .UseSetting("urls", "http://127.0.0.1:0")
            .UseSetting("Upload:MaxBytes", "1000")
            .UseSetting("ReverseProxy:Clusters:main-api:Destinations:main-api:Address", roomy.ClientOptions.BaseAddress.ToString())));
        gateway.UseKestrel();
        gateway.StartServer();
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(gateway.CreateClient(), participant, big)).StatusCode);

        Assert.Empty(await Db(mainApi, db => db.Documents.ToListAsync()));
    }

    [Fact]
    public void Limit_is_read_from_environment()
    {
        Environment.SetEnvironmentVariable("Upload__MaxBytes", "4321"); // safe: the Infra collection runs sequentially
        try
        {
            var mainApi = MainApi();
            var gateway = Track(new WebApplicationFactory<gateway::Program>());

            Assert.Equal(4321, mainApi.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize);
            var routes = gateway.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Routes;
            Assert.Equal(4321, routes.Single(r => r.RouteId == "main-api-upload").MaxRequestBodySize);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Upload__MaxBytes", null);
        }
    }

    [Fact]
    public async Task Forget_deletes_participant_objects()
    {
        var mainApi = MainApi();
        Guid participant = await SeedParticipant(mainApi);
        (await Upload(mainApi.CreateClient(), participant, Pdf)).EnsureSuccessStatusCode();
        var store = mainApi.Services.GetRequiredService<DocumentStore>();
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>();
        var (document, p) = await Db(mainApi, async db => (await db.Documents.SingleAsync(), await db.Participants.SingleAsync()));
        using var key = keys.Open(p);

        await keys.Forget(p, store, default);

        var missing = await Assert.ThrowsAnyAsync<AmazonS3Exception>(() => store.Get(document.ObjectKey, key, default));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    WebApplicationFactory<mainapi::Program> MainApi(long? limit = null) =>
        Track(infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b =>
        {
            b.UseSetting("Keycloak:Authority", keycloak.Issuer).UseSetting("Keycloak:RequireHttpsMetadata", "false");
            if (limit is not null) b.UseSetting("Upload:MaxBytes", limit.ToString());
        }));

    static async Task<Guid> SeedParticipant(WebApplicationFactory<mainapi::Program> mainApi)
    {
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>();
        return await Db(mainApi, async db =>
        {
            var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026 }).Entity;
            using var key = keys.Create();
            var p = db.Participants.Add(new()
            {
                CycleId = cycle.Id, DisplayNo = "WS2026-001", WrappedKey = key.Wrapped, KeyVersion = key.Version,
                CsvId = key.Encrypt("1"), Name = key.Encrypt("Erika"), ProjectTitle = key.Encrypt("Shop"),
            }).Entity;
            await db.SaveChangesAsync();
            return p.Id;
        });
    }

    Task<HttpResponseMessage> Upload(HttpClient client, Guid participant, byte[] content) =>
        client.SendAsync(new(HttpMethod.Post, $"/api/participants/{participant}/documents")
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new("application/pdf") } },
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        });

    static async Task<T> Db<T>(WebApplicationFactory<mainapi::Program> mainApi, Func<EasyDb, Task<T>> query)
    {
        using var scope = mainApi.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EasyDb>());
    }

    T Track<T>(T host) where T : IDisposable
    {
        hosts.Add(host);
        return host;
    }
}
