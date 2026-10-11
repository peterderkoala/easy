# Draft upstream issue: OutboxForwarder.Dispose throws when the bus is disposed concurrently

Target: rebus-org/Rebus.PostgreSql (not filed yet; link it in easy#25 once filed).
Versions: Rebus 8.9.5, Rebus.PostgreSql 9.1.1, Rebus.ServiceProvider 10.7.2, .NET 10.

---

**Title:** `OutboxForwarder.Dispose()` is not idempotent; throws `ObjectDisposedException` when the bus is disposed twice concurrently

**What happens**

Hosting Rebus with `AddRebus(...)` plus `.Outbox(o => o.StoreInPostgreSql(...))` in an ASP.NET Core app that is tested with
`WebApplicationFactory<Program>` (Program ends with `app.Run()`), disposing the factory intermittently throws:

```
System.ObjectDisposedException: The CancellationTokenSource has been disposed.
   at System.Threading.CancellationTokenSource.Cancel()
   at Rebus.PostgreSql.Outbox.OutboxForwarder.Dispose()
   at Rebus.Bus.RebusBus.Dispose()   (via the Disposed event handler in RebusConfigurer.Start)
   at Rebus.ServiceProvider.Internals.RebusBackgroundService.StopAsync(...)
   at Microsoft.Extensions.Hosting.Internal.Host.StopAsync(...)
   at Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1.DisposeAsync()
```

A variant throws from `TplAsyncTask.Dispose()` (`_tokenSource.Cancel()`), reached through `OutboxForwarder.Dispose()`.

**Why**

1. `WebApplicationFactory.Dispose` calls `IHost.StopAsync`. That fires `ApplicationStopping`, which wakes `WaitForShutdownAsync`
   inside the app's own `app.Run()` on a thread-pool thread, and that calls `IHost.StopAsync` again. `Host.StopAsync` has no
   guard against concurrent calls, so `RebusBackgroundService.StopAsync` (and `bus.Dispose()`) runs on two threads at once.
2. `RebusBus.Dispose()` guards re-entry with plain `bool _disposing` / `_disposed` fields, so two threads can both pass the guard
   and both raise `Disposed`. The handler in `RebusConfigurer.Start()` disposes every tracked instance each time it runs.
3. `OutboxForwarder.Dispose()` (`Rebus.PostgreSql/PostgreSql/Outbox/OutboxForwarder.cs`, lines 99-105 at tag 9.1.1) is not idempotent:

   ```csharp
   public void Dispose()
   {
       _cancellationTokenSource.Cancel();      // throws once the CTS below has been disposed
       _forwarder?.Dispose();
       _cleaner?.Dispose();
       _cancellationTokenSource?.Dispose();
   }
   ```

   `TplAsyncTask.Dispose()` (Rebus core) has the same check-then-act gap between `IsCancellationRequested` and `_tokenSource.Cancel()`.

Sequential double disposal is fine (`RebusBus._disposed` stops it). The failure needs the concurrent case, which is why it is intermittent.

**Minimal repro**

- Web app: `builder.Services.AddRebus(c => c.Transport(t => t.UseInMemoryTransport(new(), "q")).Outbox(o => o.StoreInPostgreSql(cs, "outbox")));` and `app.Run();`.
- xUnit test: create `new WebApplicationFactory<Program>()`, call `CreateClient()`, dispose it; loop 100 times. (Not run standalone yet: in our suite of ~40 such factories it failed 1-2 times per run.)

**Suggested fix**

Make `OutboxForwarder.Dispose()` idempotent and thread-safe, e.g.

```csharp
int _disposed;
public void Dispose()
{
    if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
    _cancellationTokenSource.Cancel();
    _forwarder?.Dispose();
    _cancellationTokenSource.Dispose();
}
```

(Also, `_cleaner` is never assigned.) Optionally make `RebusBus.Dispose()` use `Interlocked` for its guard in Rebus core.

---

**Our workaround (easy):** `AddEasyMessaging` wraps Rebus' `IHostedService` so concurrent `StopAsync` calls share one stop
(`src/Easy.Platform/Messaging.cs`, `StopOnce`). Production is unaffected: `app.Run()` stops the host once, and later container
disposal hits the already-disposed bus.
