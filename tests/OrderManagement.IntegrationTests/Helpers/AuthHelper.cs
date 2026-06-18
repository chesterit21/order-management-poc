using System.Net.Http.Json;
using System.Text.Json;

namespace OrderManagement.IntegrationTests.Helpers;

public static class AuthHelper
{
    public static async Task<string> LoginAsync(
        HttpClient client,
        string username,
        string password = "Password123!")
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                username,
                password
            });

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        return document.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("Access token was missing.");
    }
}