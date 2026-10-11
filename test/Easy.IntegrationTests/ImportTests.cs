extern alias mainapi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Easy.Data;
using mainapi::Easy.MainApi.Imports;
using mainapi::Easy.MainApi.Participants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;

namespace Easy.IntegrationTests;

// Spec 6 (issue 21): admin uploads a CSV, MainApi imports it asynchronously through its own queue (S02).
[Collection(nameof(Infra))]
public class ImportTests(KeycloakFixture keycloak, Infra infra) : IClassFixture<KeycloakFixture>, IAsyncLifetime
{
    const string Header = "Prüfungsteilnehmer/-in;Prüfl.-Nr.;Ausbildungsstätte;Status;Projektbezeichnung";
    const string ValidCsv = $"{Header}\nErika Muster;4711;Firma;egal;Webshop\nMax Mustermann;4712;Firma / Backhaus IT;egal;CRM\n";

    WebApplicationFactory<mainapi::Program> mainApi = null!;
    string admin = null!;

    public async Task InitializeAsync()
    {
        mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false"));
        admin = await keycloak.LoginAsync("admin");
    }

    public async Task DisposeAsync() => await mainApi.DisposeAsync();

    [Fact]
    public async Task Valid_csv_creates_cycle_and_participants()
    {
        var job = await Import(ValidCsv);

        Assert.Equal("Succeeded", job.GetProperty("status").GetString());
        var cycle = await Db(db => db.Cycles.SingleAsync());
        Assert.Equal((CycleType.Winter, 2026), (cycle.Type, cycle.Year));
        Assert.Equal(cycle.Id, job.GetProperty("cycleId").GetGuid());
        var participants = await Db(db => db.Participants.OrderBy(p => p.DisplayNo).ToListAsync());
        Assert.Equal(["WS2026-001", "WS2026-002"], participants.Select(p => p.DisplayNo));
        Assert.All(participants, p => Assert.Equal(ApplicationStatus.Submitted, p.ApplicationStatus));
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>();
        using (var key = keys.Open(participants[0]))
            Assert.Equal(("Erika Muster", "4711", "Webshop"), (key.Decrypt(participants[0].Name), key.Decrypt(participants[0].CsvId), key.Decrypt(participants[0].ProjectTitle)));
        Assert.Equal(keys.BlindIndex(cycle.Id, "4712"), participants[1].CsvIdHash);
    }

    [Fact]
    public async Task Invalid_csv_imports_nothing_and_lists_row_errors()
    {
        var job = await Import($"{Header}\nErika;4711;F;S;Shop\n;4712;F;S;CRM\nMax;12a;F;S;App\n");

        Assert.Equal("Failed", job.GetProperty("status").GetString());
        var errors = job.GetProperty("errors").EnumerateArray()
            .Select(e => (e.GetProperty("row").GetInt32(), e.GetProperty("column").GetString(), e.GetProperty("message").GetString()));
        Assert.Equal([(3, "Prüfungsteilnehmer/-in", "Required."), (4, "Prüfl.-Nr.", "Not an integer.")], errors);
        Assert.Empty(await Db(db => db.Cycles.ToListAsync()));
        Assert.Empty(await Db(db => db.Participants.ToListAsync()));
    }

    [Fact]
    public async Task Duplicate_id_in_file_is_rejected()
    {
        var job = await Import($"{Header}\nErika;4711;F;S;Shop\nMax;4711;F;S;CRM\n");

        Assert.Equal("Failed", job.GetProperty("status").GetString());
        Assert.Contains("Duplicate of row 2.", job.GetProperty("errors").GetRawText());
        Assert.Empty(await Db(db => db.Participants.ToListAsync()));
    }

    [Fact]
    public async Task Same_id_in_another_cycle_is_allowed()
    {
        Assert.Equal("Succeeded", (await Import(ValidCsv)).GetProperty("status").GetString());
        Assert.Equal("Succeeded", (await Import(ValidCsv, year: 2027)).GetProperty("status").GetString());

        Assert.Equal(2, await Db(db => db.Cycles.CountAsync()));
        Assert.Equal(4, await Db(db => db.Participants.CountAsync()));
    }

    [Fact]
    public async Task Redelivered_import_message_creates_one_cycle()
    {
        var jobId = Guid.NewGuid();
        await Db(async db =>
        {
            db.ImportJobs.Add(new() { Id = jobId, CycleType = CycleType.Summer, Year = 2026, Status = ImportStatus.Queued });
            return await db.SaveChangesAsync();
        });
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>();
        var bus = mainApi.Services.GetRequiredService<IBus>();

        await bus.SendLocal(ImportCycle.Create(jobId, Encoding.UTF8.GetBytes(ValidCsv), keys));
        await bus.SendLocal(ImportCycle.Create(jobId, Encoding.UTF8.GetBytes(ValidCsv), keys));

        await Poll(jobId);
        await Task.Delay(TimeSpan.FromSeconds(2)); // give the second message time to be handled
        Assert.Equal(1, await Db(db => db.Cycles.CountAsync()));
        Assert.Equal(["SS2026-001", "SS2026-002"], await Db(db => db.Participants.OrderBy(p => p.DisplayNo).Select(p => p.DisplayNo).ToListAsync()));
    }

    [Fact]
    public async Task Stored_name_is_ciphertext_in_database()
    {
        await Import(ValidCsv);

        var names = await Db(db => db.Database.SqlQueryRaw<byte[]>("SELECT name AS \"Value\" FROM participants").ToListAsync());
        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.DoesNotContain(Convert.ToHexString(Encoding.UTF8.GetBytes("Muster")), Convert.ToHexString(n)));
    }

    [Fact]
    public async Task Non_admin_cannot_import()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Upload(ValidCsv, token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(ValidCsv, token: await keycloak.LoginAsync("examiner"))).StatusCode);
        Assert.Empty(await Db(db => db.ImportJobs.ToListAsync()));
    }

    async Task<JsonElement> Import(string csv, int year = 2026)
    {
        var response = await Upload(csv, admin, year);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await Poll((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
    }

    Task<HttpResponseMessage> Upload(string csv, string? token, int year = 2026)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Winter"), "type" },
            { new StringContent(year.ToString()), "year" },
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "import.csv" },
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return mainApi.CreateClient().SendAsync(request);
    }

    // The admin page polls the job until it leaves "Queued".
    async Task<JsonElement> Poll(Guid jobId)
    {
        var client = mainApi.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        for (var until = DateTime.UtcNow.AddSeconds(20); DateTime.UtcNow < until; await Task.Delay(200))
        {
            var job = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{jobId}");
            if (job.GetProperty("status").GetString() != "Queued")
                return job;
        }
        throw new TimeoutException("import job still queued");
    }

    async Task<T> Db<T>(Func<EasyDb, Task<T>> query)
    {
        using var scope = mainApi.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EasyDb>());
    }
}
