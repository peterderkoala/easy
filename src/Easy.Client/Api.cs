using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Easy.Client;

// MainApi calls with the caller's access token (S05). Null when no token is available: the framework redirects to login.
public static class Api
{
    public static async Task<string?> Token(this IAccessTokenProvider tokens) =>
        (await tokens.RequestAccessToken()).TryGetToken(out var token) ? token.Value : null;

    public static async Task<HttpResponseMessage?> Send(this HttpClient http, IAccessTokenProvider tokens, HttpRequestMessage request)
    {
        if (await tokens.Token() is not { } token)
            return null;
        request.Headers.Authorization = new("Bearer", token);
        return await http.SendAsync(request);
    }

    // The detail of a problem response, else the status code.
    public static async Task<string> Problem(this HttpResponseMessage response) =>
        (response.Content.Headers.ContentType?.MediaType == "application/problem+json"
            ? (await response.Content.ReadFromJsonAsync<ProblemBody>())?.Detail : null) ?? $"Fehler ({(int)response.StatusCode})";

    record ProblemBody(string? Detail);
}
