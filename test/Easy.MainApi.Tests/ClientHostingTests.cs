using Microsoft.AspNetCore.Mvc.Testing;

namespace Easy.MainApi.Tests;

public class ClientHostingTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/_framework/blazor.webassembly.js")]
    public async Task Serves_the_blazor_client(string path)
    {
        using var factory = new WebApplicationFactory<Program>();

        var response = await factory.CreateClient().GetAsync(path);

        response.EnsureSuccessStatusCode();
    }
}
