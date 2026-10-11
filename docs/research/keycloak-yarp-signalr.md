# Keycloak OIDC through YARP, SignalR and Blazor WASM on .NET 10

Resolves issue #4. Context: CONCEPT.md S05 (Authentication), S09 (backend, real-time, zero-trust validation `[DECIDED]`), S16 (deployment). Researched 2026-10-10 against primary docs only. Facts the docs do not state are marked **inferred**.

## Short answer

1. Blazor WASM is a public OIDC client using Authorization Code + PKCE, via `Microsoft.AspNetCore.Components.WebAssembly.Authentication` (`AddOidcAuthentication`). Keycloak needs no special adapter.
2. Each service validates the JWT itself with `Microsoft.AspNetCore.Authentication.JwtBearer` (authority = Keycloak realm URL, signing keys from discovery). This fits the S09 zero-trust decision. YARP does not need to validate.
3. Browser SignalR cannot send an `Authorization` header on WebSocket/SSE, so the token travels as `?access_token=`. The hub service must read it in `JwtBearerEvents.OnMessageReceived`. YARP forwards WebSockets by default.
4. Keycloak puts realm roles in `realm_access.roles` and client roles in `resource_access.<client>.roles` (nested JSON). ASP.NET does not turn these into role claims by itself. Needs a flat-claim Keycloak mapper or a claims transformation (server) and a custom account factory (WASM).
5. Current versions: .NET 10 packages `10.0.12`, `Yarp.ReverseProxy 2.3.0`, Keycloak `26.8.0`.

## Versions and packages

| Piece | Package / version | Source |
|---|---|---|
| WASM OIDC | `Microsoft.AspNetCore.Components.WebAssembly.Authentication` 10.0.x (latest on NuGet: 10.0.12) | [Blazor standalone with Authentication library](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/standalone-with-authentication-library?view=aspnetcore-10.0); NuGet flat-container index |
| Service JWT validation | `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.x (latest 10.0.12) | [JwtBearerOptions.MapInboundClaims](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer.jwtbeareroptions.mapinboundclaims?view=aspnetcore-10.0); NuGet |
| SignalR .NET client (only if a non-browser client is needed) | `Microsoft.AspNetCore.SignalR.Client` 10.0.x | NuGet |
| Gateway | `Yarp.ReverseProxy` 2.3.0 (latest stable on NuGet; docs now live under ASP.NET Core docs "fundamentals/servers/yarp") | NuGet; [YARP getting started](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/getting-started?view=aspnetcore-10.0) |
| IdP | Keycloak 26.8.0 (latest in release notes) | [Keycloak release notes](https://www.keycloak.org/docs/latest/release_notes/) |

Blazor's auth library supports only the PKCE authorization-code flow (via MSAL.js), and the docs advise against other flows: [standalone-with-authentication-library](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/standalone-with-authentication-library?view=aspnetcore-10.0).

## 1. Blazor WASM as public client (S05)

- Register with `AddOidcAuthentication(options => builder.Configuration.Bind("Local", options.ProviderOptions))`; `wwwroot/appsettings.json` needs `Local:Authority` and `Local:ClientId`. Default scopes are `openid` and `profile`. Extra scopes go in `options.ProviderOptions.DefaultScopes`. (Same source.)
- Redirect URIs default to `/authentication/login-callback` and `/authentication/logout-callback`; they must be registered on the IdP. Some IdPs allow loopback redirects without a port (RFC 8252 section 7.3) (same source).
- Keycloak side: client with Client authentication OFF (public), standard flow ON, PKCE method `S256` set explicitly. Keycloak does not enforce PKCE unless the client sends the parameters or a method is configured: [Server Admin Guide, "PKCE method"](https://www.keycloak.org/docs/latest/server_admin/index.html). Set valid redirect URIs and Web Origins (Web Origins handles CORS, same guide).
- Authority is the realm URL, `https://<host>/realms/<realm>` (**inferred** from Keycloak convention; the `securing_apps` page returned 404 at `/docs/latest/securing_apps/`, so not verified in a fetched primary page).
- Tokens to APIs: `BaseAddressAuthorizationMessageHandler` only adds tokens for URIs inside the app base URI. For other origins use a custom `AuthorizationMessageHandler` with `ConfigureHandler(authorizedUrls: [...], scopes: [...])`: [Additional security scenarios](https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/additional-scenarios?view=aspnetcore-10.0). With the gateway as the single address and main-api serving the static files behind it (S09/S16), the base-address handler suffices. Cross-origin calls would need server CORS (same page).
- S05 says login goes directly to Keycloak "regardless of which server delivers the frontend files". That matches: login is a browser redirect to the authority, the gateway is not on the login path.

## 2. JWT validation in each service (S09 zero trust)

- Use `AddAuthentication().AddJwtBearer(...)` with `Authority` = realm URL and `Audience` set. Signing keys come from the discovery document (**inferred**: standard JwtBearer behavior; not stated on the pages fetched).
- Audience: Keycloak adds audiences to the access token via the `Audience` / `Audience Resolve` mappers. The default client scope `roles` contains an Audience Resolve mapper that adds the client ID of every client for which the token holds at least one client role. So define client roles on a resource-server client (for example `easy-api`) and `aud` will contain it: [Server Admin Guide, "Audience resolve"](https://www.keycloak.org/docs/latest/server_admin/index.html). Alternative: an explicit Audience mapper per service.
- Confidential clients per S05 (main API, exam room, Hangfire) are not needed for pure JWT validation (**inferred**, standard resource-server pattern). They are only needed if a service itself calls Keycloak (client credentials, introspection) or Hangfire does a login.
- Claim mapping: `JwtBearerOptions.MapInboundClaims` defaults to `true` (maps short JWT claim names to legacy `ClaimTypes.*`), while `JsonWebTokenHandler.MapInboundClaims` defaults to `false`: [JwtBearerOptions.MapInboundClaims](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer.jwtbeareroptions.mapinboundclaims?view=aspnetcore-10.0), [JsonWebTokenHandler.MapInboundClaims](https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.jsonwebtokens.jsonwebtokenhandler.mapinboundclaims?view=msal-web-dotnet-latest). Set it explicitly to `false` and configure `TokenValidationParameters.NameClaimType` / `RoleClaimType`: [Map, customize, and transform claims](https://learn.microsoft.com/aspnet/core/security/authentication/claims?view=aspnetcore-10.0#name-claim-and-role-claim-mapping). Since .NET 8 JwtBearer uses `JsonWebTokenHandler`: [breaking change note](https://learn.microsoft.com/aspnet/core/breaking-changes/8/securitytoken-events?view=aspnetcore-10.0).

## 3. SignalR with Keycloak tokens

- Browsers cannot set `Authorization` on WebSocket and Server-Sent Events, so SignalR sends the token as `access_token` query parameter for those transports. Non-browser clients (.NET client) use the header: [SignalR authn and authz](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0#authenticate-users-connecting-to-a-signalr-hub).
- Server: hook `JwtBearerEvents.OnMessageReceived`, read `context.Request.Query["access_token"]`, set `context.Token` only when the path targets a hub. Same page has the sample.
- Client (standalone Blazor WASM): get the token with `IAccessTokenProvider.RequestAccessToken()` and pass `options.AccessTokenProvider = () => Task.FromResult(token?.Value)` to `HubConnectionBuilder.WithUrl`: [Secure Blazor WebAssembly, "Secure a SignalR hub"](https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/?view=aspnetcore-10.0#secure-a-signalr-hub). SignalR calls the token function before every HTTP request, so renewal belongs inside that function (authn-and-authz page). Prefer a function that calls `RequestAccessToken()` each time rather than capturing one string (**inferred** from that behavior).
- Logging risk: ASP.NET Core logs request URLs including `access_token`. Raise `Microsoft.AspNetCore.Hosting` to Warning or filter the value: [Security considerations in SignalR, "Access token logging"](https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0). The same applies to gateway and any access logs (**inferred**).
- Token expiry on long connections: by default an established connection keeps working after the token expires. `CloseOnAuthenticationExpiration = true` in `MapHub` options closes it at expiry and the client must reconnect (authn-and-authz page). Built-in in-place refresh (`EnableAuthenticationRefresh`) is documented only for the ASP.NET Core 11+ monikers, not 10. For .NET 10 plan "close on expiry and reconnect" or accept long-lived connections; this matters for the exam room hub (S07). Keycloak token lifetimes are realm settings (not researched).
- Hub authorization: `[Authorize]` on hub class or methods (Blazor security page above).
- WebSocket origin: CORS does not protect WebSockets; use WebSocket origin restriction: [SignalR security](https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0).

## 4. YARP in front (S09, S16)

- WebSocket (and SPDY) proxying is enabled by default for direct forwarding and the full pipeline. HTTP/2 WebSockets (RFC 8441) work with YARP 2.0+ on .NET 7+ with Kestrel. Inbound and outbound HTTP versions may differ: [YARP WebSockets](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/websockets?view=aspnetcore-10.0).
- Idle timeout: cluster `ActivityTimeout` (default 100 s) still applies after the handshake. If neither side sends data or keep-alive frames within it, YARP closes the connection. ASP.NET Core WebSocket `KeepAliveInterval` defaults to 2 minutes, longer than 100 s. Send keep-alives faster than `ActivityTimeout` or raise `ActivityTimeout` (same page). SignalR's own keep-alive default was not verified here.
- Auth: YARP does no authentication unless enabled per route. `AuthorizationPolicy` per route: custom policy name, `default` (authenticated user), or `anonymous`. The fallback policy is null by default. Needs `UseAuthentication`/`UseAuthorization` before `MapReverseProxy`: [YARP authn/authz](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0). For the S09 "optional coarse check for a token's presence": JwtBearer in the gateway plus `RequireAuthenticatedUser` on routes does full signature validation (more than presence). A strict presence-only check would be custom middleware (**inferred**, no doc). Either way services re-validate (decided).
- Credentials flow: bearer tokens in headers flow to destinations by default, and the destination must verify them itself (same page, "Flowing Credentials"). The query string is forwarded too (**inferred**; no documented stripping).
- The SignalR authn page notes a YARP-fronted BFF can add the `Authorization` header to the proxied WebSocket request so the backend reads the header instead of the query string. That needs the gateway to hold the token (BFF pattern), which conflicts with the browser-direct public-client design in S05. Not recommended here.
- SignalR routes: route the hub path prefix so `/negotiate` goes with it. `/refresh` matters only with the ASP.NET Core 11 authentication refresh feature.

## 5. Role mapping (admin / examiner, S05)

Keycloak ([Server Admin Guide, token role mappings](https://www.keycloak.org/docs/latest/server_admin/index.html)):

- Realm roles appear in `realm_access.roles`, client roles in `resource_access.<clientId>.roles`.
- Default role mappers add roles to the access token and the introspection endpoint, not the ID token. Mapper options include "Add to ID token", "Add to access token", multivalued, and "Token claim name" (configurable, also per client via the dedicated client scope).
- "Full scope allowed" is on by default and puts every role the user holds into every token. The guide marks the switch deprecated and recommends turning it off and declaring role scope mappings per client. Recommended for production.
- S05: "Admins are always also examiners". Model as a composite role (admin includes examiner) or assign both roles. Composite roles were not verified in the fetched text (**inferred**).

ASP.NET:

- With `MapInboundClaims = false` and `RoleClaimType = "roles"`, a flat claim named `roles` works directly. Keycloak's default nesting does not. Options: (a) a Keycloak User Realm Role / User Client Role mapper with Token claim name `roles` so the claim is flat, or (b) an `IClaimsTransformation` / `OnTokenValidated` handler that parses `realm_access`. Option (a) keeps every service simple and consistent.
- Messages carrying claims over Rebus (S09): carry the validated role list, not the raw token (design point, not a doc claim).

Blazor WASM:

- The WASM `ClaimsPrincipal` is built by `AccountClaimsPrincipalFactory<TAccount>` from the account object. A `RemoteUserAccount` subclass with `[JsonPropertyName(...)]` properties plus a factory subclass adds claims, registered via `AddOidcAuthentication<RemoteAuthenticationState, CustomUserAccount>`: [Additional security scenarios, "Customize the user"](https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/additional-scenarios?view=aspnetcore-10.0). The docs' example maps an array claim (`amr`); roles follow the same pattern.
- Caveat (**inferred**, verify in a spike): these properties come from ID token / user info, not the access token. For UI role checks (`AuthorizeView Roles="admin"`) the role mapper must also have "Add to ID token" on, or the factory must decode the access token (the docs' `User.razor` shows decoding it).
- UI role checks are cosmetic; services enforce (S09).

## 6. Effects on CONCEPT.md (for the main session; not edited here)

- S05 "Keycloak status ambiguous": nothing found that blocks Keycloak. Every integration point has first-party documentation.
- S05 client layout: confidential clients are not needed for plain JWT validation; keep them only for service accounts or Hangfire login.
- S07/S09: exam room connections outlive tokens. On .NET 10 use `CloseOnAuthenticationExpiration` plus reconnect, or accept; in-place refresh is .NET 11+.
- S09 gateway: `access_token` in query strings means gateway and service access logs need filtering.
- S16: YARP `ActivityTimeout` vs keep-alive needs a setting for the exam room route.

## Open points for a spike (not answered by docs read)

1. Shared API audience vs per-service audience, and the mapper config that yields it.
2. Composite roles (admin includes examiner) in issued tokens.
3. Role claim shape in the WASM user (ID vs access token) on a real Keycloak 26.8 realm.
4. Access token lifespan and silent renew for public clients in WASM.
5. SignalR default keep-alive vs YARP 100 s timeout.
6. Keycloak hosting and HA (S16): out of scope.

## Sources

- https://learn.microsoft.com/en-us/aspnet/core/blazor/security/webassembly/standalone-with-authentication-library?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/additional-scenarios?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/blazor/security/webassembly/?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/signalr/security?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/security/authentication/claims?view=aspnetcore-10.0
- https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.jwtbearer.jwtbeareroptions.mapinboundclaims?view=aspnetcore-10.0
- https://learn.microsoft.com/dotnet/api/microsoft.identitymodel.jsonwebtokens.jsonwebtokenhandler.mapinboundclaims?view=msal-web-dotnet-latest
- https://learn.microsoft.com/aspnet/core/breaking-changes/8/securitytoken-events?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/websockets?view=aspnetcore-10.0
- https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0
- https://www.keycloak.org/docs/latest/server_admin/index.html
- https://www.keycloak.org/docs/latest/release_notes/
- https://api.nuget.org/v3-flatcontainer/ (package version indexes)
