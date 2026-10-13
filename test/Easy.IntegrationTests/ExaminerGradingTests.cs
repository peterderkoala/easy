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
        await using var b = Hub(admin);
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

    [Fact]
    public async Task Summary_view_unblinds_only_the_opener()
    {
        await Register(examiner); // A
        await Register(admin); // B
        await Save(examiner, Draft(("fiae-2026:1.1", 4, Secret)));
        await Save(admin, Draft(("fiae-2026:1.1", 2, "Bemerkung von B")));
        Assert.Equal(JsonValueKind.Null, Foreign(await Read(admin)).GetProperty("points").ValueKind); // blinded before

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, SummaryView, admin)).StatusCode);

        var a = Foreign(await Read(admin)); // B reads A, drafts included
        Assert.Equal(4, a.GetProperty("points").GetProperty("fiae-2026:1.1").GetInt32());
        Assert.Equal(Secret, a.GetProperty("remarks").GetProperty("fiae-2026:1.1").GetString());
        var b = Foreign(await Read(examiner)); // A still blinded
        Assert.Equal(JsonValueKind.Null, b.GetProperty("points").ValueKind);
        Assert.Equal(JsonValueKind.Null, b.GetProperty("remarks").ValueKind);
        Assert.True((await Read(admin)).GetProperty("summaryView").GetBoolean()); // the split view hides "open" (issue 52)
        Assert.False((await Read(examiner)).GetProperty("summaryView").GetBoolean());
    }

    [Fact]
    public async Task Opening_the_summary_view_is_audited()
    {
        await Register(examiner);

        // Idempotent, in parallel (the unique key decides) and afterwards.
        var parallel = await Task.WhenAll(Send(HttpMethod.Post, SummaryView, examiner), Send(HttpMethod.Post, SummaryView, examiner));
        Assert.All(parallel, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, SummaryView, examiner)).StatusCode);

        var entry = Assert.Single(await Db(db => db.AuditEntries.Where(e => e.Entity == "SummaryView").ToListAsync()));
        Assert.Equal((participant, $"{await ExaminerId("examiner")}"), (entry.ParticipantId, entry.EntityKey));
        Assert.Single(await Db(db => db.SummaryViews.ToListAsync()));
    }

    [Fact]
    public async Task Other_examiner_cannot_open_summary_view()
    {
        await Register(examiner);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, SummaryView, admin)).StatusCode); // not registered
        Assert.Empty(await Db(db => db.SummaryViews.ToListAsync()));
    }

    [Fact]
    public async Task Summary_view_of_a_forgotten_participant_is_rejected()
    {
        await Register(examiner);
        await Db(db => db.Participants.Where(p => p.Id == participant).ExecuteUpdateAsync(s => s.SetProperty(p => p.KeyForgotten, true)));

        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Post, SummaryView, examiner)).StatusCode);
        Assert.Empty(await Db(db => db.SummaryViews.ToListAsync()));
    }

    [Fact]
    public async Task Summary_view_is_closed_once_graded()
    {
        await Register(examiner);
        await Db(db => db.Participants.Where(p => p.Id == participant).ExecuteUpdateAsync(s => s.SetProperty(p => p.DocumentationStatus, DocumentationStatus.Graded)));

        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, SummaryView, examiner)).StatusCode);
    }

    [Fact]
    public async Task Summary_view_opened_event_reaches_the_group()
    {
        await Register(examiner);
        await Register(admin);
        var opened = new TaskCompletionSource<(Guid, string)>();
        await using var a = Hub(examiner);
        a.On<Guid, string>(nameof(IGradingClient.SummaryViewOpened), (p, name) => opened.TrySetResult((p, name)));
        await a.StartAsync();
        await a.InvokeAsync("JoinDocumentation", participant);

        await Send(HttpMethod.Post, SummaryView, admin);

        Assert.Equal((participant, "Dev Admin"), await opened.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    // Spec 16 (issue 48). Weights 15/10/40/25/10, thresholds 4 (section) and 10 (total), seeded by the migration.

    [Fact]
    public async Task Second_submit_within_thresholds_grades_and_stores_score()
    {
        using var consumer = await EventConsumer<GradeChanged>.Start(infra);
        await Pair();
        await SubmitSections(examiner, 5, 5, 5, 5, 5); // 50.0
        Assert.Equal("InProgress", (await Read(examiner)).GetProperty("status").GetString());

        await SubmitSections(admin, 6, 5, 5, 5, 5); // 51.5

        Assert.Equal("Graded", (await Read(examiner)).GetProperty("status").GetString());
        var grade = await Db(db => db.Grades.SingleAsync(g => g.ParticipantId == participant && g.Source == GradeSource.Documentation));
        Assert.Equal(50.8m, grade.Value); // 50.75
        Assert.Single(await Db(db => db.AuditEntries.Where(e => e.Entity == nameof(Grade)).ToListAsync()));
        var delivery = await consumer.Next(); // through the outbox
        Assert.Equal((participant, "Documentation"), (delivery.Event.ParticipantId, delivery.Event.Source));
    }

    [Fact]
    public async Task Score_rounds_half_away_from_zero()
    {
        await Pair();
        await SubmitSections(examiner, 6, 5, 5, 5, 1); // 47.5
        await SubmitSections(admin, 5, 5, 5, 5, 0); // 45.0

        Assert.Equal(46.3m, await Score()); // 46.25
    }

    [Fact]
    public async Task Section_gap_of_five_is_a_deviation_and_four_is_not()
    {
        await Pair();
        await SubmitSections(examiner, 5, 0, 5, 5, 5);
        await SubmitSections(admin, 5, 5, 5, 5, 5); // section 2: gap 5, total gap 5

        Assert.Equal(["fiae-2026:2"], await DeviatingSections());
        Assert.Equal("InProgress", (await Read(examiner)).GetProperty("status").GetString());

        await Withdraw(admin);
        await SubmitSections(admin, 5, 4, 5, 5, 5, version: await Version(admin)); // gap 4

        Assert.Null(await Db(db => db.Deviations.SingleOrDefaultAsync()));
        Assert.Equal("Graded", (await Read(examiner)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Total_gap_above_ten_is_a_deviation_and_ten_is_not()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 5, 5, 5);
        await SubmitSections(admin, 5, 8, 7, 5, 5); // total gap 3 + 8 = 11, sections within

        Assert.Equal(["total"], await DeviatingSections());

        await Withdraw(admin);
        await SubmitSections(admin, 5, 5, 7, 5, 7, version: await Version(admin)); // 8 + 2 = 10

        Assert.Equal("Graded", (await Read(examiner)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deviation_stores_sections_and_thresholds_used()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 0, 5, 5);
        await SubmitSections(admin, 5, 5, 5, 5, 5); // section 3: gap 5, total gap 20

        var deviation = await Db(db => db.Deviations.SingleAsync());
        Assert.Equal(["fiae-2026:3", "total"], deviation.Sections);
        Assert.Equal((participant, 4, 10), (deviation.ParticipantId, deviation.SectionThreshold, deviation.TotalThreshold));
    }

    [Fact]
    public async Task Threshold_change_does_not_alter_stored_deviation()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 0, 5, 5);
        await SubmitSections(admin, 5, 5, 5, 5, 5);

        var settings = await Send(HttpMethod.Put, "/api/settings", admin, new { deviationSectionPoints = 8, deviationTotalPoints = 30, defaultWeights = new[] { 15, 10, 40, 25, 10 } });
        Assert.Equal(HttpStatusCode.NoContent, settings.StatusCode);

        var deviation = await Db(db => db.Deviations.SingleAsync());
        Assert.Equal(["fiae-2026:3", "total"], deviation.Sections);
        Assert.Equal((4, 10), (deviation.SectionThreshold, deviation.TotalThreshold));
    }

    [Fact]
    public async Task Withdraw_clears_deviation_and_resubmit_rechecks()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 0, 5, 5);
        await SubmitSections(admin, 5, 5, 5, 5, 5);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode);

        await Withdraw(admin);
        Assert.Null(await Db(db => db.Deviations.SingleOrDefaultAsync())); // acceptances go with it

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"{Mine}/submit", admin)).StatusCode); // unchanged: same deviation again
        var deviation = await Db(db => db.Deviations.SingleAsync());
        Assert.Equal(["fiae-2026:3", "total"], deviation.Sections);
        Assert.Empty(deviation.AcceptedBy);
    }

    [Fact]
    public async Task Both_accepting_the_deviation_grades_with_average_of_two()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 0, 5, 5); // 30.0
        await SubmitSections(admin, 5, 5, 5, 5, 5); // 50.0
        var events = new List<string>();
        var graded = new TaskCompletionSource();
        await using var hub = Hub(examiner);
        hub.On<Guid, string>(nameof(IGradingClient.DeviationAccepted), (_, name) => { lock (events) events.Add($"accepted {name}"); });
        hub.On<Guid>(nameof(IGradingClient.Graded), _ => { lock (events) events.Add("graded"); graded.TrySetResult(); });
        await hub.StartAsync();
        await hub.InvokeAsync("JoinDocumentation", participant);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode); // twice: no change
        Assert.Equal("InProgress", (await Read(examiner)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Accept, admin)).StatusCode);

        Assert.Equal("Graded", (await Read(examiner)).GetProperty("status").GetString());
        Assert.Equal(40.0m, await Score());
        await graded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["accepted Dev Examiner", "accepted Dev Admin", "graded"], events);
    }

    [Fact]
    public async Task Deviation_notice_hides_gaps_from_examiners_and_shows_them_to_admins()
    {
        // The admin must not grade here: an examiner from outside Keycloak takes the second place, submitted (S06).
        await Register(examiner);
        await SeedSubmittedGrader(5, 5, 5, 5, 5);
        var events = new List<string>();
        var deviated = new TaskCompletionSource();
        await using var hub = Hub(examiner);
        foreach (var method in typeof(IGradingClient).GetMethods())
            hub.On(method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray(), args =>
            {
                lock (events) events.Add($"{method.Name}{JsonSerializer.Serialize(args)}");
                if (method.Name == nameof(IGradingClient.DeviationFound)) deviated.TrySetResult();
                return Task.CompletedTask;
            });
        await hub.StartAsync();
        await hub.InvokeAsync("JoinDocumentation", participant);

        await SubmitSections(examiner, 5, 5, 0, 5, 5);
        await deviated.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var mine = (await Read(examiner)).GetProperty("deviation");
        Assert.Equal(["fiae-2026:3", "total"], mine.GetProperty("sections").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("gaps").ValueKind);
        var admins = (await Read(admin)).GetProperty("deviation");
        Assert.Equal(5, admins.GetProperty("gaps").GetProperty("fiae-2026:3").GetInt32());
        Assert.Equal(20m, admins.GetProperty("gaps").GetProperty("total").GetDecimal());
        Assert.Equal($"DeviationFound[\"{participant}\"]", events.Single(e => e.StartsWith(nameof(IGradingClient.DeviationFound)))); // no sections, no numbers
        Assert.DoesNotContain(events, e => e.Contains("fiae-2026"));
    }

    [Fact]
    public async Task Graded_documentation_rejects_every_write()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 5, 5, 5);
        await SubmitSections(admin, 5, 5, 5, 5, 5);
        Assert.Equal("Graded", (await Read(examiner)).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await Save(examiner, Complete(), version: await Version(examiner))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, $"{Mine}/submit", examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Withdraw(examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Delete, Registration, examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, SummaryView, examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, $"/api/participants/{participant}/comments", examiner, new { text = "Zu spät" })).StatusCode);
        Assert.Equal("Submitted", Own(await Read(examiner)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Graded_unblinds_both_examiners()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 5, 5, 5);
        await SubmitSections(admin, 6, 5, 5, 5, 5);

        Assert.Equal(0, Foreign(await Read(admin)).GetProperty("points").GetProperty("fiae-2026:1.2").GetInt32()); // examiner: 5 + 0
        Assert.Equal(1, Foreign(await Read(examiner)).GetProperty("points").GetProperty("fiae-2026:1.2").GetInt32()); // admin: 5 + 1
    }

    // Spec 17 (issue 49). The pair: "examiner" and a seeded grader (submitted 5/5/5/5/5); the third examiner: "admin".

    [Fact]
    public async Task Invite_only_on_deviation()
    {
        await Register(examiner);
        await SeedSubmittedGrader(5, 5, 5, 5, 5);
        var admin = await ExaminerId("admin");

        Assert.Equal(HttpStatusCode.Conflict, (await Invite(examiner, admin)).StatusCode); // no deviation yet
        Assert.Equal(HttpStatusCode.Forbidden, (await Invite(this.admin, admin)).StatusCode); // not registered

        await SubmitSections(examiner, 5, 5, 0, 5, 5);
        Assert.Equal(HttpStatusCode.NoContent, (await Invite(examiner, admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Invite(examiner, admin)).StatusCode); // one open at a time
        var invitation = await Db(db => db.ThirdExaminerInvitations.SingleAsync());
        Assert.Equal((admin, await ExaminerId("examiner"), ThirdExaminerStatus.Invited), (invitation.ExaminerId, invitation.InvitedBy, invitation.Status));
    }

    [Fact]
    public async Task Registered_pair_cannot_be_invited()
    {
        var seeded = await Deviated();

        Assert.Equal(HttpStatusCode.BadRequest, (await Invite(examiner, seeded)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Invite(examiner, await ExaminerId("examiner"))).StatusCode);
        await Db(db => db.Examiners.Where(e => e.Email == "admin@easy.local").ExecuteUpdateAsync(s => s.SetProperty(e => e.Active, false)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Invite(examiner, await ExaminerId("admin"))).StatusCode); // inactive
        Assert.Empty(await Db(db => db.ThirdExaminerInvitations.ToListAsync()));
    }

    [Fact]
    public async Task Invitation_freezes_both_gradings()
    {
        await Deviated();
        (await Invite(examiner, await ExaminerId("admin"))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Conflict, (await Withdraw(examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode);

        (await Send(HttpMethod.Post, $"{Third}/accept", admin)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Withdraw(examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode);
        Assert.Equal("Submitted", Own(await Read(examiner)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Decline_lifts_the_freeze()
    {
        await Deviated();
        (await Invite(examiner, await ExaminerId("admin"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Third}/decline", examiner)).StatusCode); // invitee only

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"{Third}/decline", admin)).StatusCode);

        Assert.Equal(ThirdExaminerStatus.Declined, (await Db(db => db.ThirdExaminerInvitations.SingleAsync())).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, $"{Third}/accept", admin)).StatusCode); // nothing to accept
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Invite(examiner, await ExaminerId("admin"))).StatusCode); // invite again
    }

    [Fact]
    public async Task Step_back_discards_draft_and_lifts_the_freeze()
    {
        await Deviated();
        (await Invite(examiner, await ExaminerId("admin"))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(admin, Draft(("fiae-2026:1.1", 4, null)))).StatusCode); // invited, not accepted
        (await Send(HttpMethod.Post, $"{Third}/accept", admin)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await Save(admin, Draft(("fiae-2026:1.1", 4, null)))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, $"{Third}/decline", admin)).StatusCode);

        var adminId = await ExaminerId("admin");
        Assert.False(await Db(db => db.ExaminerGradings.AnyAsync(g => g.ExaminerId == adminId)));
        Assert.Equal(HttpStatusCode.Forbidden, (await Save(admin, Draft(("fiae-2026:1.1", 4, null)))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Withdraw(examiner)).StatusCode);
    }

    [Fact]
    public async Task Third_examiner_starts_blind()
    {
        await Deviated();
        Assert.Equal(2, (await Read(admin)).GetProperty("gradings").EnumerateArray().Count(g => g.GetProperty("points").ValueKind == JsonValueKind.Object)); // admin not grading
        (await Invite(examiner, await ExaminerId("admin"))).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Post, $"{Third}/accept", admin)).EnsureSuccessStatusCode();
        await Save(admin, Draft(("fiae-2026:1.1", 4, Secret)));

        var read = await Read(admin);
        Assert.Equal(["fiae-2026:3", "total"], read.GetProperty("deviation").GetProperty("sections").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(JsonValueKind.Null, read.GetProperty("deviation").GetProperty("gaps").ValueKind);
        var others = read.GetProperty("gradings").EnumerateArray().Where(g => !g.GetProperty("mine").GetBoolean()).ToList();
        Assert.Equal(2, others.Count);
        Assert.All(others, g => Assert.Equal(JsonValueKind.Null, g.GetProperty("points").ValueKind));
        Assert.Equal(("Dev Admin", "Accepted"), (read.GetProperty("thirdExaminer").GetProperty("examiner").GetString(), read.GetProperty("thirdExaminer").GetProperty("status").GetString()));
        var thirds = (await Read(examiner)).GetProperty("gradings").EnumerateArray().Single(g => g.GetProperty("examiner").GetString() == "Dev Admin");
        Assert.Equal(JsonValueKind.Null, thirds.GetProperty("points").ValueKind); // the pair is blind to the third's draft
        Assert.DoesNotContain(Secret, (await Read(examiner)).GetRawText());

        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, SummaryView, admin)).StatusCode);
        Assert.Equal(2, (await Read(admin)).GetProperty("gradings").EnumerateArray().Count(g => !g.GetProperty("mine").GetBoolean() && g.GetProperty("points").ValueKind == JsonValueKind.Object));
    }

    [Fact]
    public async Task Third_submit_grades_with_average_of_three()
    {
        await Deviated(); // 30.0 and 50.0
        (await Invite(examiner, await ExaminerId("admin"))).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Post, $"{Third}/accept", admin)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Post, Accept, admin)).StatusCode); // not one of the pair

        await SubmitSections(admin, 6, 0, 0, 0, 0); // 9.0: far off, no further check

        Assert.Equal("Graded", (await Read(examiner)).GetProperty("status").GetString());
        Assert.Equal(29.7m, await Score()); // 89 / 3 = 29.67
        Assert.Equal(HttpStatusCode.Conflict, (await Send(HttpMethod.Post, $"{Third}/decline", admin)).StatusCode); // graded is final
    }

    [Fact]
    public async Task Deactivation_releases_invitation()
    {
        await Deviated();
        var adminId = await ExaminerId("admin");
        (await Invite(examiner, adminId)).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Post, $"{Third}/accept", admin)).EnsureSuccessStatusCode();
        await Save(admin, Draft(("fiae-2026:1.1", 4, null)));

        (await Send(HttpMethod.Post, $"/api/examiners/{adminId}/deactivate", admin)).EnsureSuccessStatusCode();

        Assert.Equal(ThirdExaminerStatus.Declined, (await Db(db => db.ThirdExaminerInvitations.SingleAsync())).Status);
        Assert.False(await Db(db => db.ExaminerGradings.AnyAsync(g => g.ExaminerId == adminId)));
        Assert.Equal(HttpStatusCode.NoContent, (await Send(HttpMethod.Post, Accept, examiner)).StatusCode); // freeze lifted
    }

    [Fact]
    public async Task Invitation_events_reach_the_group()
    {
        await Deviated();
        var events = new List<string>();
        var accepted = new TaskCompletionSource();
        await using var hub = Hub(examiner);
        hub.On<Guid, string>(nameof(IGradingClient.ThirdExaminerInvited), (_, name) => { lock (events) events.Add($"invited {name}"); });
        hub.On<Guid, string>(nameof(IGradingClient.ThirdExaminerDeclined), (_, name) => { lock (events) events.Add($"declined {name}"); });
        hub.On<Guid, string>(nameof(IGradingClient.ThirdExaminerAccepted), (_, name) => { lock (events) events.Add($"accepted {name}"); accepted.TrySetResult(); });
        await hub.StartAsync();
        await hub.InvokeAsync("JoinDocumentation", participant);

        (await Invite(examiner, await ExaminerId("admin"))).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Post, $"{Third}/decline", admin)).EnsureSuccessStatusCode();
        (await Invite(examiner, await ExaminerId("admin"))).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Post, $"{Third}/accept", admin)).EnsureSuccessStatusCode();

        await accepted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(["invited Dev Admin", "declined Dev Admin", "invited Dev Admin", "accepted Dev Admin"], events);
    }

    // Spec 20 (issue 52): what the split view needs beyond specs 13 to 17.

    [Fact]
    public async Task Read_carries_weights_and_the_score_once_graded()
    {
        await Pair();
        await SubmitSections(examiner, 5, 5, 5, 5, 5);
        var before = await Read(examiner);
        Assert.Equal([15, 10, 40, 25, 10], before.GetProperty("weights").EnumerateArray().Select(w => w.GetInt32()));
        Assert.Equal(JsonValueKind.Null, before.GetProperty("score").ValueKind);

        await SubmitSections(admin, 6, 5, 5, 5, 5);

        Assert.Equal(50.8m, (await Read(examiner)).GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task Examiner_list_shows_active_examiners_by_name_only()
    {
        var seeded = await SeedSubmittedGrader(5, 5, 5, 5, 5); // "Dritte Person"
        await Db(db => db.Examiners.Where(e => e.Id == seeded).ExecuteUpdateAsync(s => s.SetProperty(e => e.Active, false)));

        var response = await Send(HttpMethod.Get, "/api/examiners", examiner);

        response.EnsureSuccessStatusCode();
        var list = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        Assert.Equal(["Dev Admin", "Dev Examiner"], list!.Select(e => e.GetProperty("displayName").GetString()));
        Assert.Equal(await ExaminerId("admin"), list![0].GetProperty("id").GetGuid());
        Assert.All(list!, e => Assert.Equal(2, e.EnumerateObject().Count())); // no email, no subject
    }

    string Third => $"/api/participants/{participant}/third-examiner";

    Task<HttpResponseMessage> Invite(string token, Guid examinerId) => Send(HttpMethod.Post, Third, token, new { examinerId });

    // "examiner" (30.0) and a seeded grader (50.0) deviate in section 3 and the total. Returns the seeded grader's ID.
    async Task<Guid> Deviated()
    {
        await Register(examiner);
        var seeded = await SeedSubmittedGrader(5, 5, 5, 5, 5);
        await SubmitSections(examiner, 5, 5, 0, 5, 5);
        return seeded;
    }

    record Grading(Dictionary<string, int> Points, Dictionary<string, string> Remarks);

    string Accept => $"/api/participants/{participant}/grading/deviation/accept";

    async Task Pair()
    {
        await Register(examiner);
        await Register(admin);
    }

    // Section points (0 to 10) spread over the section's criteria, first criteria first; remarks long enough to submit.
    static Grading Sections(params int[] points)
    {
        var grading = new Grading([], []);
        foreach (var (section, total) in GradingMatrix.Sections.Zip(points))
        {
            int left = total;
            foreach (var c in section.Criteria)
            {
                grading.Points[c.Key] = Math.Min(c.Max, left);
                grading.Remarks[c.Key] = "Nachvollziehbar begründet.";
                left -= grading.Points[c.Key];
            }
        }
        return grading;
    }

    async Task SubmitSections(string token, int s1, int s2, int s3, int s4, int s5, uint? version = null)
    {
        (await Save(token, Sections(s1, s2, s3, s4, s5), version: version)).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Post, $"{Mine}/submit", token)).EnsureSuccessStatusCode();
    }

    Task<HttpResponseMessage> Withdraw(string token) => Send(HttpMethod.Post, $"{Mine}/withdraw", token);

    Task<decimal> Score() => Db(db => db.Grades.Where(g => g.ParticipantId == participant && g.Source == GradeSource.Documentation).Select(g => g.Value).SingleAsync());

    Task<string[]> DeviatingSections() => Db(db => db.Deviations.Select(d => d.Sections).SingleAsync());

    // A second registered examiner with a submitted grading, straight in the DB (Keycloak has two users only). Criterion
    // rows by SQL: the audit interceptor needs an HTTP request's actor.
    async Task<Guid> SeedSubmittedGrader(params int[] sections)
    {
        var id = Guid.NewGuid();
        await Db(async db =>
        {
            db.Examiners.Add(new() { Id = id, Subject = $"seed-{id}", DisplayName = "Dritte Person", Email = $"{id}@easy.local" });
            db.Registrations.Add(new() { ParticipantId = participant, ExaminerId = id, Slot = 2, CreatedAt = DateTimeOffset.UtcNow });
            db.ExaminerGradings.Add(new()
            {
                ParticipantId = participant, ExaminerId = id, Status = ExaminerGradingStatus.Submitted, Pages = new int?[5], UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.Participants.Single(p => p.Id == participant).DocumentationStatus = DocumentationStatus.InProgress;
            await db.SaveChangesAsync();
            foreach (var (key, points) in Sections(sections).Points)
                await db.Database.ExecuteSqlAsync($"INSERT INTO criterion_gradings (participant_id, examiner_id, criterion_key, points) VALUES ({participant}, {id}, {key}, {points})");
            return 0;
        });
        return id;
    }

    HubConnection Hub(string token) => new HubConnectionBuilder()
        .WithUrl(new Uri(mainApi.Server.BaseAddress, "hubs/grading"), o =>
        {
            o.HttpMessageHandlerFactory = _ => mainApi.Server.CreateHandler();
            o.Transports = HttpTransportType.LongPolling; // TestServer has no WebSockets
            o.AccessTokenProvider = () => Task.FromResult<string?>(token);
        })
        .Build();

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

    static JsonElement Foreign(JsonElement read) => read.GetProperty("gradings").EnumerateArray().Single(g => !g.GetProperty("mine").GetBoolean());

    static JsonElement Own(JsonElement read) => read.GetProperty("gradings").EnumerateArray().Single(g => g.GetProperty("mine").GetBoolean());

    async Task<JsonElement> Read(string token)
    {
        var response = await Send(HttpMethod.Get, $"/api/participants/{participant}/grading", token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    string Mine => $"/api/participants/{participant}/grading/mine";
    string SummaryView => $"/api/participants/{participant}/summary-view";
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
