extern alias mainapi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Easy.Contracts;
using Easy.Data;
using mainapi::Easy.MainApi.Participants;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Easy.IntegrationTests;

// Spec 13 (issue 45): a registered examiner saves, submits and withdraws their examiner grading; blinding through one
// visibility function (S06, ADR 0005). Keycloak has two users: "examiner" and "admin" (who holds the examiner role too).
[Collection(nameof(Infra))]
public class ExaminerGradingTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    string examiner = null!, admin = null!;
    Guid cycle, participant;

    const string Secret = "Geheime Bemerkung von A";

    public async Task InitializeAsync()
    {
        mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>()).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false"));
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>(); // starts MainApi: migrations run
        await Db(async db =>
        {
            cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026, Weights = [15, 10, 40, 25, 10] }).Entity.Id;
            using var key = keys.Create();
            participant = db.Participants.Add(new()
            {
                CycleId = cycle, DisplayNo = "WS2026-001", WrappedKey = key.Wrapped, KeyVersion = key.Version,
                CsvId = key.Encrypt("1"), Name = key.Encrypt("Erika Muster"), ProjectTitle = key.Encrypt("Shop"),
                DocumentationStatus = DocumentationStatus.Submitted,
            }).Entity.Id;
            return await db.SaveChangesAsync();
        });
        examiner = await Login("examiner");
        admin = await Login("admin");
    }

    public async Task DisposeAsync() => await mainApi.DisposeAsync();

    [Fact]
    public async Task Registered_examiner_saves_partial_draft()
    {
        await Register(examiner);

        var response = await Save(examiner, Draft(("fiae-2026:1.1", 4, "Ziel klar beschrieben")), pages: [3, null, 7, null, null]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var own = Own(await Read(examiner));
        Assert.Equal("Draft", own.GetProperty("status").GetString());
        Assert.Equal(4, own.GetProperty("points").GetProperty("fiae-2026:1.1").GetInt32());
        Assert.Equal("Ziel klar beschrieben", own.GetProperty("remarks").GetProperty("fiae-2026:1.1").GetString());
        Assert.Equal([3, null, 7, null, null], own.GetProperty("pages").EnumerateArray().Select(p => p.ValueKind == JsonValueKind.Null ? (int?)null : p.GetInt32()));
        Assert.Equal((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32(), own.GetProperty("version").GetUInt32());
    }

    [Fact]
    public async Task Unregistered_examiner_cannot_save()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(examiner, Draft(("fiae-2026:1.1", 4, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Mine}/submit", examiner)).StatusCode);
        Assert.Empty(await Db(db => db.ExaminerGradings.ToListAsync()));
    }

    [Fact]
    public async Task Points_outside_criterion_range_are_rejected()
    {
        await Register(examiner);

        Assert.Equal(HttpStatusCode.BadRequest, (await Save(examiner, Draft(("fiae-2026:4.1", 3, null)))).StatusCode); // max 2
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(examiner, Draft(("fiae-2026:1.1", -1, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(examiner, Draft(("fiae-2026:9.9", 1, null)))).StatusCode); // unknown key
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(examiner, new { remarks = new Dictionary<string, string> { ["fiae-2025:1.1"] = "Alter Schlüssel" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save(examiner, Draft(("fiae-2026:1.1", 1, null)), pages: [1, 2])).StatusCode); // one page per section
        Assert.Equal(HttpStatusCode.OK, (await Save(examiner, Draft(("fiae-2026:4.1", 2, null)))).StatusCode);
    }

    [Fact]
    public async Task Stale_save_returns_409()
    {
        await Register(examiner);
        var first = await (await Save(examiner, Draft(("fiae-2026:1.1", 1, null)))).Content.ReadFromJsonAsync<JsonElement>();
        uint version = first.GetProperty("version").GetUInt32();

        Assert.Equal(HttpStatusCode.OK, (await Save(examiner, Draft(("fiae-2026:1.1", 2, null)), version: version)).StatusCode); // tab 1
        Assert.Equal(HttpStatusCode.Conflict, (await Save(examiner, Draft(("fiae-2026:1.1", 3, null)), version: version)).StatusCode); // tab 2, stale
        Assert.Equal(HttpStatusCode.Conflict, (await Save(examiner, Draft(("fiae-2026:1.1", 3, null)))).StatusCode); // no version, row exists

        Assert.Equal(2, Own(await Read(examiner)).GetProperty("points").GetProperty("fiae-2026:1.1").GetInt32());
    }

    [Fact]
    public async Task Concurrent_saves_one_wins_the_other_gets_409()
    {
        await Register(examiner);

        // Same base version (none) in parallel: the DB rejects the loser (unique key or xmin), or it reads the winner's row.
        var first = await Task.WhenAll(Save(examiner, Draft(("fiae-2026:1.1", 1, null))), Save(examiner, Draft(("fiae-2026:1.1", 2, null))));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], first.Select(r => r.StatusCode).Order());

        uint version = await Version(examiner);
        var second = await Task.WhenAll(Save(examiner, Draft(("fiae-2026:1.1", 3, null)), version: version), Save(examiner, Draft(("fiae-2026:1.1", 4, null)), version: version));
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], second.Select(r => r.StatusCode).Order());
    }

    [Fact]
    public async Task Submit_without_documentation_is_rejected()
    {
        await Db(db => db.Participants.Where(p => p.Id == participant).ExecuteUpdateAsync(s => s.SetProperty(p => p.DocumentationStatus, (DocumentationStatus?)null)));
        await Register(examiner);
        await Save(examiner, Complete());

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, $"{Mine}/submit", examiner)).StatusCode);
        Assert.Null(await Db(db => db.Participants.Where(p => p.Id == participant).Select(p => p.DocumentationStatus).SingleAsync()));
    }

    [Fact]
    public async Task Remarks_are_stored_encrypted_and_audited()
    {
        await Register(examiner);
        var version = (await (await Save(examiner, Draft(("fiae-2026:1.1", 4, "Erste Bemerkung")))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetUInt32();
        await Save(examiner, Draft(("fiae-2026:1.1", 5, "Erste Bemerkung")), version: version); // same remark: no new remark entry

        var stored = await Db(db => db.CriterionGradings.SingleAsync());
        Assert.DoesNotContain("Erste Bemerkung", System.Text.Encoding.UTF8.GetString(stored.Remark!));
        var entries = await Db(db => db.AuditEntries.Where(e => e.Entity == nameof(CriterionGrading)).OrderBy(e => e.Id).ToListAsync());
        Assert.Equal([("Points", null, "4"), ("Remark", null, null), ("Points", "4", "5")], entries.Select(e => (e.Field, e.Old, e.Field == "Remark" ? null : e.New)));
        string criterion = $"{await ExaminerId("examiner")}/fiae-2026:1.1";
        Assert.All(entries, e => Assert.Equal((participant, criterion), (e.ParticipantId, e.EntityKey)));
        var remark = entries.Single(e => e.Field == "Remark");
        using var key = mainApi.Services.GetRequiredService<ParticipantKeys>().Open(await Db(db => db.Participants.SingleAsync()));
        Assert.Equal("Erste Bemerkung", key.Decrypt(Convert.FromBase64String(remark.New!))); // encrypted under the participant key
    }

    [Fact]
    public async Task Submit_requires_all_points_and_remarks_of_ten_characters()
    {
        await Register(examiner);
        var complete = Complete();
        complete.Remarks["fiae-2026:5.2"] = "Zu kurz.."; // 9 characters
        await Save(examiner, complete);

        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Mine}/submit", examiner)).StatusCode);
        complete.Points.Remove("fiae-2026:5.2");
        complete.Remarks["fiae-2026:5.2"] = "Lang genug.";
        await Save(examiner, complete, version: await Version(examiner));
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Post, $"{Mine}/submit", examiner)).StatusCode);

        await Save(examiner, Complete(), version: await Version(examiner));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"{Mine}/submit", examiner)).StatusCode);
        Assert.Equal("Submitted", Own(await Read(examiner)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Save(examiner, Complete(), version: await Version(examiner))).StatusCode); // withdraw first
    }

    [Fact]
    public async Task First_submit_sets_documentation_in_progress()
    {
        await Register(examiner);
        await Save(examiner, Complete());
        Assert.Equal("Submitted", (await Read(examiner)).GetProperty("status").GetString());

        await Send(HttpMethod.Post, $"{Mine}/submit", examiner);

        Assert.Equal("InProgress", (await Read(examiner)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Withdraw_returns_grading_to_draft()
    {
        await Register(examiner);
        await Save(examiner, Complete());
        await Send(HttpMethod.Post, $"{Mine}/submit", examiner);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"{Mine}/withdraw", examiner)).StatusCode);

        var read = await Read(examiner);
        Assert.Equal("Draft", Own(read).GetProperty("status").GetString());
        Assert.Equal("InProgress", read.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Save(examiner, Complete(), version: await Version(examiner))).StatusCode);
    }

    [Fact]
    public async Task Unregister_after_submit_is_rejected()
    {
        await Register(examiner);
        await Save(examiner, Complete());
        await Send(HttpMethod.Post, $"{Mine}/submit", examiner);

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, Registration, examiner)).StatusCode);

        Assert.Equal(1, await Db(db => db.Registrations.CountAsync()));
        Assert.Equal(14, await Db(db => db.CriterionGradings.CountAsync()));
    }

    [Fact]
    public async Task Unregister_before_submit_discards_draft()
    {
        await Register(examiner);
        await Save(examiner, Complete());

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Delete, Registration, examiner)).StatusCode);

        Assert.Empty(await Db(db => db.ExaminerGradings.ToListAsync()));
        Assert.Empty(await Db(db => db.CriterionGradings.ToListAsync()));
    }

    [Fact]
    public async Task Deactivation_releases_drafts_but_keeps_submitted_gradings()
    {
        await Register(examiner);
        await Register(admin);
        await Save(examiner, Complete());
        await Save(admin, Complete());
        await Send(HttpMethod.Post, $"{Mine}/submit", examiner);

        await Send(HttpMethod.Post, $"/api/examiners/{await ExaminerId("examiner")}/deactivate", admin);
        await Send(HttpMethod.Post, $"/api/examiners/{await ExaminerId("admin")}/deactivate", admin);

        Assert.Equal([await ExaminerId("examiner")], await Db(db => db.Registrations.Select(r => r.ExaminerId).ToListAsync()));
        Assert.Equal([ExaminerGradingStatus.Submitted], await Db(db => db.ExaminerGradings.Select(g => g.Status).ToListAsync()));
        Assert.Equal(14, await Db(db => db.CriterionGradings.CountAsync()));
    }

    [Fact]
    public async Task Upload_after_first_submit_returns_409()
    {
        await Register(examiner);
        await Save(examiner, Complete());
        await Send(HttpMethod.Post, $"{Mine}/submit", examiner);

        var upload = new HttpRequestMessage(HttpMethod.Post, $"/api/participants/{participant}/documents")
        {
            Content = new ByteArrayContent("%PDF-1.7 neu"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } },
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", examiner) },
        };

        Assert.Equal(HttpStatusCode.Conflict, (await mainApi.CreateClient().SendAsync(upload)).StatusCode);
        Assert.Empty(await Db(db => db.Documents.ToListAsync()));
    }

    [Fact]
    public async Task Cycle_weights_locked_after_first_submit()
    {
        await Register(examiner);
        await Save(examiner, Complete());
        var weights = new { weights = new[] { 15, 10, 40, 20, 15 } };
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Put, $"/api/cycles/{cycle}/weights", admin, weights)).StatusCode); // a draft does not lock

        await Send(HttpMethod.Post, $"{Mine}/submit", examiner);

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Put, $"/api/cycles/{cycle}/weights", admin, new { weights = new[] { 15, 10, 40, 25, 10 } })).StatusCode);
        var stored = await Db(async db => (await db.Cycles.SingleAsync()).Weights);
        Assert.Equal([15, 10, 40, 20, 15], stored);
    }

    [Fact]
    public async Task Admin_not_grading_sees_all_drafts()
    {
        await Register(examiner);
        await Save(examiner, Draft(("fiae-2026:1.1", 4, Secret)));

        var grading = Assert.Single((await Read(admin)).GetProperty("gradings").EnumerateArray());

        Assert.Equal(("Dev Examiner", "Draft", false), (grading.GetProperty("examiner").GetString(), grading.GetProperty("status").GetString(), grading.GetProperty("mine").GetBoolean()));
        Assert.Equal(4, grading.GetProperty("points").GetProperty("fiae-2026:1.1").GetInt32());
        Assert.Equal(Secret, grading.GetProperty("remarks").GetProperty("fiae-2026:1.1").GetString());
    }

    [Fact]
    public async Task Admin_who_is_registered_is_blinded()
    {
        await Register(examiner);
        await Register(admin);
        await Save(examiner, Draft(("fiae-2026:1.1", 4, Secret)));

        var read = await Read(admin);

        var other = read.GetProperty("gradings").EnumerateArray().Single(g => !g.GetProperty("mine").GetBoolean());
        Assert.Equal(("Dev Examiner", "Draft"), (other.GetProperty("examiner").GetString(), other.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, other.GetProperty("points").ValueKind);
        Assert.Equal(JsonValueKind.Null, other.GetProperty("remarks").ValueKind);
        Assert.DoesNotContain(Secret, read.GetRawText());
    }

    [Fact]
    public async Task Other_examiner_sees_status_only()
    {
        await Register(admin);
        await Save(admin, Draft(("fiae-2026:1.1", 4, Secret)));

        var read = await Read(examiner);

        var grading = Assert.Single(read.GetProperty("gradings").EnumerateArray());
        Assert.Equal(("Dev Admin", "Draft"), (grading.GetProperty("examiner").GetString(), grading.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Null, grading.GetProperty("points").ValueKind);
        Assert.DoesNotContain(Secret, read.GetRawText());
    }

    [Fact]
    public async Task Blinding_rest_read_and_hub_messages_carry_no_foreign_points()
    {
        await Register(examiner); // A
        await Register(admin); // B, blinded as a registered examiner
        var messages = new List<string>();
        var submitted = new TaskCompletionSource();
        await using var b = new HubConnectionBuilder()
            .WithUrl(new Uri(mainApi.Server.BaseAddress, "hubs/grading"), o =>
            {
                o.HttpMessageHandlerFactory = _ => mainApi.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
                o.AccessTokenProvider = () => Task.FromResult<string?>(admin);
            })
            .Build();
        // Every IGradingClient method, so no message B could receive goes unrecorded.
        foreach (var method in typeof(IGradingClient).GetMethods())
            b.On(method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray(), args =>
            {
                lock (messages) messages.Add(JsonSerializer.Serialize(args));
                if (args[2] is ExaminerGradingEvent.Submitted) submitted.TrySetResult();
                return Task.CompletedTask;
            });
        await b.StartAsync();
        await b.InvokeAsync("JoinDocumentation", participant);

        await Save(examiner, Draft(("fiae-2026:1.1", 4, Secret)));
        var complete = Complete();
        complete.Remarks["fiae-2026:1.1"] = Secret;
        await Save(examiner, complete, version: await Version(examiner));
        await Send(HttpMethod.Post, $"{Mine}/submit", examiner);
        await submitted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var read = await Read(admin);
        var a = read.GetProperty("gradings").EnumerateArray().Single(g => !g.GetProperty("mine").GetBoolean());
        Assert.Equal(JsonValueKind.Null, a.GetProperty("points").ValueKind);
        Assert.DoesNotContain(Secret, read.GetRawText());
        Assert.Equal(
            [$"[\"{participant}\",\"Dev Examiner\",{(int)ExaminerGradingEvent.Draft}]", $"[\"{participant}\",\"Dev Examiner\",{(int)ExaminerGradingEvent.Draft}]",
             $"[\"{participant}\",\"Dev Examiner\",{(int)ExaminerGradingEvent.Submitted}]"],
            messages); // IDs, display name and status only
    }

    record Grading(Dictionary<string, int> Points, Dictionary<string, string> Remarks);

    static Grading Draft((string Key, int Points, string? Remark) criterion) =>
        new(new() { [criterion.Key] = criterion.Points }, criterion.Remark is null ? [] : new() { [criterion.Key] = criterion.Remark });

    static Grading Complete()
    {
        var criteria = GradingMatrix.Sections.SelectMany(s => s.Criteria).ToList();
        return new(criteria.ToDictionary(c => c.Key, c => c.Max), criteria.ToDictionary(c => c.Key, _ => "Nachvollziehbar begründet."));
    }

    Task<HttpResponseMessage> Save(string token, Grading grading, uint? version = null, int?[]? pages = null) =>
        Save(token, new { version, points = grading.Points, remarks = grading.Remarks, pages });

    Task<HttpResponseMessage> Save(string token, object body) => Send(HttpMethod.Put, Mine, token, body);

    async Task<uint> Version(string token) => Own(await Read(token)).GetProperty("version").GetUInt32();

    static JsonElement Own(JsonElement read) => read.GetProperty("gradings").EnumerateArray().Single(g => g.GetProperty("mine").GetBoolean());

    async Task<JsonElement> Read(string token)
    {
        var response = await Send(HttpMethod.Get, $"/api/participants/{participant}/grading", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    string Mine => $"/api/participants/{participant}/grading/mine";
    string Registration => $"/api/participants/{participant}/registration";

    async Task Register(string token) => (await Send(HttpMethod.Post, Registration, token)).EnsureSuccessStatusCode();

    Task<Guid> ExaminerId(string user) => Db(db => db.Examiners.Where(e => e.Email == $"{user}@easy.local").Select(e => e.Id).SingleAsync());

    async Task<string> Login(string user)
    {
        string token = await keycloak.LoginAsync(user);
        (await Send(HttpMethod.Post, "/api/me", token)).EnsureSuccessStatusCode();
        return token;
    }

    Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return mainApi.CreateClient().SendAsync(request);
    }

    async Task<T> Db<T>(Func<EasyDb, Task<T>> query)
    {
        using var scope = mainApi.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<EasyDb>());
    }
}
