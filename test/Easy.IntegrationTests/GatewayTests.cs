extern alias gateway;
extern alias mainapi;
extern alias examroom;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Easy.IntegrationTests;

public class GatewayTests
{
    [Theory]
    [InlineData("/health", "Easy.Gateway")]
    [InlineData("/health/main-api", "Easy.MainApi")]
    [InlineData("/health/exam-room", "Easy.ExamRoom")]
    public async Task Gateway_routes_health_of_each_host(string path, string host)
    {
        using var mainApi = StartOnKestrel(new WebApplicationFactory<mainapi::Program>());
        using var examRoom = StartOnKestrel(new WebApplicationFactory<examroom::Program>());
        using var gateway = new WebApplicationFactory<gateway::Program>().WithWebHostBuilder(b => b
            .UseSetting("ReverseProxy:Clusters:main-api:Destinations:main-api:Address", BaseAddress(mainApi))
            .UseSetting("ReverseProxy:Clusters:exam-room:Destinations:exam-room:Address", BaseAddress(examRoom)));

        var response = await gateway.CreateClient().GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.Equal($"Healthy {host}", await response.Content.ReadAsStringAsync());
    }

    static WebApplicationFactory<T> StartOnKestrel<T>(WebApplicationFactory<T> factory) where T : class
    {
        factory.UseKestrel(0);
        factory.StartServer();
        return factory;
    }

    static string BaseAddress<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.ClientOptions.BaseAddress.ToString();
}
