extern alias mainapi;

using Easy.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Easy.IntegrationTests;

[Collection(nameof(Infra))]
public class MainApiTests(Infra infra)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/_framework/blazor.webassembly.js")]
    public async Task Serves_the_blazor_client(string path)
    {
        using var mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>());

        var response = await mainApi.CreateClient().GetAsync(path);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Migrations_apply_on_empty_database()
    {
        using var mainApi = infra.Configure(new WebApplicationFactory<mainapi::Program>());

        (await mainApi.CreateClient().GetAsync("/health")).EnsureSuccessStatusCode();

        using var scope = mainApi.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyDb>().Database;
        Assert.NotEmpty(await db.GetAppliedMigrationsAsync());
        Assert.Empty(await db.GetPendingMigrationsAsync());
    }
}
