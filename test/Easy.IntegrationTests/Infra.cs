using System.Net;
using System.Net.Sockets;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Easy.IntegrationTests;

// One Postgres and one RabbitMQ for the whole run. Each test takes its own database.
public sealed class Infra : IAsyncLifetime
{
    // ponytail: every test has its own database and so its own Npgsql pool, whose idle connections outlive the test host,
    // so the default 100 runs out; raised instead of clearing pools per test. Clear them on dispose if 500 runs out too.
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17").WithCommand("-c", "max_connections=500").Build();

    // Fixed host port, so a stopped and restarted broker comes back at the same address.
    public RabbitMqContainer Rabbit { get; } = new RabbitMqBuilder("rabbitmq:4").WithPortBinding(FreePort(), 5672).Build();

    // Same release as compose (ADR 0002). Port 8888 is the filer, which serves the stored (encrypted) bytes without S3.
    public IContainer Seaweed { get; } = new ContainerBuilder("chrislusf/seaweedfs:4.48")
        .WithCommand("server", "-s3", "-dir=/data")
        .WithEnvironment("AWS_ACCESS_KEY_ID", "test-access")
        .WithEnvironment("AWS_SECRET_ACCESS_KEY", "test-secret")
        .WithPortBinding(8333, true)
        .WithPortBinding(8888, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8333).ForStatusCodeMatching(c => (int)c < 500))
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8888).ForStatusCodeMatching(c => (int)c < 500)))
        .Build();

    public IContainer Mailpit { get; } = new ContainerBuilder("axllent/mailpit:v1.31.5")
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/livez")))
        .Build();

    public string SeaweedS3 => $"http://{Seaweed.Hostname}:{Seaweed.GetMappedPublicPort(8333)}";
    public string SeaweedFiler => $"http://{Seaweed.Hostname}:{Seaweed.GetMappedPublicPort(8888)}";
    public string MailpitApi => $"http://{Mailpit.Hostname}:{Mailpit.GetMappedPublicPort(8025)}";

    public async Task ClearMails()
    {
        using var http = new HttpClient();
        await http.DeleteAsync($"{MailpitApi}/api/v1/messages");
    }

    // Polls Mailpit until at least `expected` mails arrived, or 20 s passed.
    public async Task<List<JsonElement>> Mails(int expected)
    {
        using var http = new HttpClient();
        for (var until = DateTime.UtcNow.AddSeconds(20); ; await Task.Delay(200))
        {
            var mails = (await http.GetFromJsonAsync<JsonElement>($"{MailpitApi}/api/v1/messages")).GetProperty("messages").EnumerateArray().ToList();
            if (mails.Count >= expected || DateTime.UtcNow > until)
                return mails;
        }
    }

    // Created up front: hosts other than MainApi (Notification) expect an existing database.
    public string NewDatabase()
    {
        string name = $"t{Guid.NewGuid():N}";
        using var connection = new NpgsqlConnection(Postgres.GetConnectionString());
        connection.Open();
        using var create = new NpgsqlCommand($"CREATE DATABASE {name}", connection);
        create.ExecuteNonQuery();
        return new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    // A throwaway master secret (ADR 0001) for hosts that encrypt participant data.
    public string MasterSecretFile { get; } = Path.GetTempFileName();

    // Points a host (MainApi needs both at start-up) at the containers, on a fresh database and its own input queue,
    // so hosts of parallel tests never consume each other's messages.
    // Pass a database to put several hosts (MainApi and Notification) on the same one.
    public WebApplicationFactory<T> Configure<T>(WebApplicationFactory<T> factory, string? database = null) where T : class =>
        factory.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", database ?? NewDatabase())
            .UseSetting("Rebus:Rabbit", Rabbit.GetConnectionString())
            .UseSetting("Rebus:Queue", $"test-{Guid.NewGuid():N}")
            .UseSetting("Rebus:ErrorQueue", $"test-error-{Guid.NewGuid():N}")
            .UseSetting("MasterSecretFile", MasterSecretFile)
            .UseSetting("Seaweed:Endpoint", SeaweedS3)
            .UseSetting("Seaweed:AccessKey", "test-access")
            .UseSetting("Seaweed:SecretKey", "test-secret")
            .UseSetting("Smtp:Host", Mailpit.Hostname)
            .UseSetting("Smtp:Port", Mailpit.GetMappedPublicPort(1025).ToString()));

    // Messages in the host's error queue (Rebus:ErrorQueue); declares it like Rebus does if it does not exist yet.
    public async Task<uint> ErrorQueueCount(IServiceProvider host)
    {
        string queue = host.GetRequiredService<IConfiguration>()["Rebus:ErrorQueue"]!;
        await using var connection = await new ConnectionFactory { Uri = new(Rabbit.GetConnectionString()) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        return (await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false)).MessageCount;
    }

    public Task InitializeAsync()
    {
        File.WriteAllText(MasterSecretFile, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        return Task.WhenAll(Postgres.StartAsync(), Rabbit.StartAsync(), Seaweed.StartAsync(), Mailpit.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Rabbit.DisposeAsync();
        await Seaweed.DisposeAsync();
        await Mailpit.DisposeAsync();
        File.Delete(MasterSecretFile);
    }

    static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition(nameof(Infra))]
public class InfraCollection : ICollectionFixture<Infra>, ICollectionFixture<KeycloakFixture>; // one Keycloak for the run: it is the slowest container to start
