using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Easy.IntegrationTests;

// One Postgres and one RabbitMQ for the whole run. Each test takes its own database.
public sealed class Infra : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("postgres:17").Build();

    // Fixed host port, so a stopped and restarted broker comes back at the same address.
    public RabbitMqContainer Rabbit { get; } = new RabbitMqBuilder("rabbitmq:4").WithPortBinding(FreePort(), 5672).Build();

    public string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = $"t{Guid.NewGuid():N}" }.ConnectionString;

    // Points a host (MainApi needs both at start-up) at the containers, on a fresh database.
    public WebApplicationFactory<T> Configure<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", NewDatabase())
            .UseSetting("Rebus:Rabbit", Rabbit.GetConnectionString()));

    public Task InitializeAsync() => Task.WhenAll(Postgres.StartAsync(), Rabbit.StartAsync());

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await Rabbit.DisposeAsync();
    }

    static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition(nameof(Infra))]
public class InfraCollection : ICollectionFixture<Infra>;
