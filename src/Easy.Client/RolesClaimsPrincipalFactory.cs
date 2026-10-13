using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;

namespace Easy.Client;

// Keycloak's flat "roles" claim is a JSON array; the default factory turns it into one claim holding the array text.
// Split it so IsInRole and AuthorizeView(Roles=...) work.
public class RolesClaimsPrincipalFactory(IAccessTokenProviderAccessor accessor) : AccountClaimsPrincipalFactory<RemoteUserAccount>(accessor)
{
    public override async ValueTask<ClaimsPrincipal> CreateUserAsync(RemoteUserAccount account, RemoteAuthenticationUserOptions options)
    {
        var user = await base.CreateUserAsync(account, options);
        if (user.Identity is ClaimsIdentity identity && account is not null // null when logged out
            && account.AdditionalProperties.TryGetValue("roles", out var value) && value is JsonElement { ValueKind: JsonValueKind.Array } roles)
        {
            identity.RemoveClaim(identity.FindFirst(options.RoleClaim!));
            foreach (var role in roles.EnumerateArray())
                identity.AddClaim(new Claim(options.RoleClaim!, role.GetString()!));
        }
        return user;
    }
}
