using System.Collections.Concurrent;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Messages;

namespace Easy.IntegrationTests;

// A throwaway Rebus endpoint on its own queue, subscribed to T: what a real consumer would receive, with rbs2-corr-id.
public sealed class EventConsumer<T> : IDisposable where T : class
{
    readonly BlockingCollection<Delivery> received = new();
    readonly BuiltinHandlerActivator activator = new();

    public record Delivery(T Event, string? CorrelationId);

    EventConsumer() =>
        activator.Handle<T>((_, context, message) =>
        {
            received.Add(new(message, context.Headers.GetValueOrDefault(Headers.CorrelationId)));
            return Task.CompletedTask;
        });

    public static async Task<EventConsumer<T>> Start(Infra infra)
    {
        var consumer = new EventConsumer<T>();
        var bus = Configure.With(consumer.activator)
            .Transport(t => t.UseRabbitMq(infra.Rabbit.GetConnectionString(), $"test-{Guid.NewGuid():N}"))
            .Start();
        await bus.Subscribe<T>();
        return consumer;
    }

    public Task<Delivery> Next(TimeSpan? timeout = null) => Task.Run(() =>
        received.TryTake(out var delivery, timeout ?? TimeSpan.FromSeconds(20)) ? delivery : throw new TimeoutException($"no {typeof(T).Name} received"));

    // Everything that arrives within the window, for asserting that nothing else came.
    public async Task<List<Delivery>> Drain(TimeSpan window)
    {
        await Task.Delay(window);
        var all = new List<Delivery>();
        while (received.TryTake(out var delivery)) all.Add(delivery);
        return all;
    }

    public void Dispose() => activator.Dispose();
}
