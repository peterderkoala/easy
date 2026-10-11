using System.Text.Json;
using Easy.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Easy.MainApi.Tests;

public class ClientRolesTests
{
    [Fact]
    public async Task Flat_roles_claim_becomes_one_role_per_entry()
    {
        var account = new RemoteUserAccount
        {
            AdditionalProperties = new Dictionary<string, object> { ["preferred_username"] = "admin", ["roles"] = JsonDocument.Parse("""["examiner","admin"]""").RootElement },
        };
        var options = new RemoteAuthenticationUserOptions { AuthenticationType = "oidc", NameClaim = "preferred_username", RoleClaim = "roles" };

        var user = await new RolesClaimsPrincipalFactory(null!).CreateUserAsync(account, options);

        Assert.True(user.IsInRole("examiner"));
        Assert.True(user.IsInRole("admin"));
    }
}
