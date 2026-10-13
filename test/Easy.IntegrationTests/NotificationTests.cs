extern alias mainapi;
extern alias notification;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Easy.Contracts;
using Easy.Data;
using mainapi::Easy.MainApi.Participants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Rebus.Bus;

namespace Easy.IntegrationTests;

// Spec 7 (issue 22): DocumentUploaded reaches the notification service, which mails the active examiners (S12).
[Collection(nameof(Infra))]
public class NotificationTests(KeycloakFixture keycloak, Infra infra) : IAsyncLifetime
{
    WebApplicationFactory<mainapi::Program> mainApi = null!;
    WebApplicationFactory<notification::Program> notifications = null!;
    Guid participant;

    public async Task InitializeAsync()
    {
        await infra.ClearMails();
        string database = infra.NewDatabase();
        mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>(), database).WithWebHostBuilder(b => b
            .UseSetting("Keycloak:Authority", keycloak.Issuer)
            .UseSetting("Keycloak:RequireHttpsMetadata", "false"));
        var keys = mainApi.Services.GetRequiredService<ParticipantKeys>(); // starts MainApi: migrations run
        using (var scope = mainApi.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
            var cycle = db.Cycles.Add(new() { Type = CycleType.Winter, Year = 2026 }).Entity;
            using var key = keys.Create();
            participant = db.Participants.Add(new()
            {
                CycleId = cycle.Id, DisplayNo = "WS2026-007", WrappedKey = key.Wrapped, KeyVersion = key.Version,
                CsvId = key.Encrypt("7"), Name = key.Encrypt("Erika"), ProjectTitle = key.Encrypt("Shop"),
            }).Entity.Id;
            db.Examiners.Add(new() { Subject = "a", DisplayName = "Aktiv", Email = "aktiv@easy.local" });
            db.Examiners.Add(new() { Subject = "b", DisplayName = "Inaktiv", Email = "inaktiv@easy.local", Active = false });
            await db.SaveChangesAsync();
        }
        notifications = infra.Configure(new WebApplicationFactory<notification::Program>(), database);
        notifications.Services.GetRequiredService<IBus>(); // starts the host: subscribes to DocumentUploaded
        await Task.Delay(TimeSpan.FromSeconds(1)); // let the subscription settle before publishing
    }

    public async Task DisposeAsync()
    {
        await notifications.DisposeAsync();
        await mainApi.DisposeAsync();
    }

    [Fact]
    public async Task DocumentUploaded_event_triggers_one_mail_in_mailpit()
    {
        var response = await mainApi.CreateClient().SendAsync(new(HttpMethod.Post, $"/api/participants/{participant}/documents")
        {
            Content = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF")) { Headers = { ContentType = new("application/pdf") } },
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await keycloak.LoginAsync("examiner")) },
        });
        response.EnsureSuccessStatusCode();

        var mail = Assert.Single(await infra.Mails(expected: 1));
        Assert.Equal("aktiv@easy.local", mail.GetProperty("To")[0].GetProperty("Address").GetString());
        Assert.Contains("Dokumentation", mail.GetProperty("Subject").GetString());
        Assert.Contains("WS2026-007", mail.GetProperty("Snippet").GetString());
    }

    [Fact]
    public async Task Upload_mail_goes_to_registered_examiners()
    {
        using (var scope = mainApi.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
            var registered = db.Examiners.Add(new() { Subject = "r", DisplayName = "Registriert", Email = "registriert@easy.local" }).Entity;
            db.Registrations.Add(new() { ParticipantId = participant, ExaminerId = registered.Id, Slot = 1, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await mainApi.Services.GetRequiredService<IBus>().Publish(new DocumentUploaded(Guid.NewGuid(), participant));

        await infra.Mails(expected: 1);
        await Task.Delay(TimeSpan.FromSeconds(2)); // give a mail to the unregistered examiner time to (not) arrive
        var mail = Assert.Single(await infra.Mails(expected: 1));
        Assert.Equal("registriert@easy.local", mail.GetProperty("To")[0].GetProperty("Address").GetString());
    }

    [Fact]
    public async Task Upload_mail_goes_to_all_active_examiners_when_nobody_is_registered()
    {
        using (var scope = mainApi.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyDb>();
            db.Examiners.Add(new() { Subject = "c", DisplayName = "Auch aktiv", Email = "auch@easy.local" });
            // Registered, but without an address (mirror before the first token with email): counts as nobody.
            var unreachable = db.Examiners.Add(new() { Subject = "n", DisplayName = "Ohne Mail", Email = "" }).Entity;
            db.Registrations.Add(new() { ParticipantId = participant, ExaminerId = unreachable.Id, Slot = 1, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        await mainApi.Services.GetRequiredService<IBus>().Publish(new DocumentUploaded(Guid.NewGuid(), participant));

        await infra.Mails(expected: 2);
        await Task.Delay(TimeSpan.FromSeconds(2)); // the inactive examiner gets none
        Assert.Equal(["aktiv@easy.local", "auch@easy.local"],
            (await infra.Mails(expected: 2)).Select(m => m.GetProperty("To")[0].GetProperty("Address").GetString()).Order());
    }

    [Fact]
    public async Task Redelivered_event_sends_one_mail()
    {
        var bus = mainApi.Services.GetRequiredService<IBus>();
        var uploaded = new DocumentUploaded(Guid.NewGuid(), participant);

        await bus.Publish(uploaded);
        await bus.Publish(uploaded);

        await infra.Mails(expected: 1);
        await Task.Delay(TimeSpan.FromSeconds(2)); // give the duplicate time to (not) send
        Assert.Single(await infra.Mails(expected: 1));
    }

}
