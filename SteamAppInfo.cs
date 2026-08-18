using System.Net.Http.Json;

public static class SteamAppInfo
{
    private static readonly HttpClient Http = new HttpClient();

    public static async Task<string?> GetGameNameAsync(uint appId)
    {
        try
        {
            string url =
                $"https://store.steampowered.com/api/appdetails?appids={appId}";

            var response =
                await Http.GetFromJsonAsync<Dictionary<string, SteamAppDetailsResponse>>(url);

            if (response == null ||
                !response.TryGetValue(
                    appId.ToString(),
                    out var app))
            {
                return null;
            }

            if (!app.Success ||
                app.Data == null ||
                string.IsNullOrWhiteSpace(app.Data.Name))
            {
                return null;
            }

            return app.Data.Name;
        }
        catch
        {
            return null;
        }
    }

    private class SteamAppDetailsResponse
    {
        public bool Success { get; set; }
        public SteamAppData? Data { get; set; }
    }

    private class SteamAppData
    {
        public string? Name { get; set; }
    }
}