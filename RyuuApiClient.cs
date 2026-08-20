using System.IO.Compression;
using System.Net.Http.Headers;

public sealed class RyuuApiClient
{
    private readonly HttpClient http;

    public RyuuApiClient(string authKey)
    {
        if (string.IsNullOrWhiteSpace(authKey))
            throw new ArgumentException(
                "Ryuu Auth Key не указан.",
                nameof(authKey));

        http = new HttpClient();

        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "MySteamDownloader",
                "1.0"));

        http.DefaultRequestHeaders.Add(
            "X-Auth-Key",
            authKey);
    }

    public async Task<string> DownloadManifestPackAsync(
        uint appId,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        string zipPath = Path.Combine(
            outputDirectory,
            $"{appId}.zip");

        string url =
            $"https://generator.ryuu.lol/api/download/{appId}";

        Console.WriteLine();
        Console.WriteLine("Получаем manifest-пакет через Ryuu...");
        Console.WriteLine($"AppID: {appId}");

        using HttpResponseMessage response =
            await http.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);

        Console.WriteLine(
            $"HTTP: {(int)response.StatusCode}");

        response.EnsureSuccessStatusCode();

        await using (Stream input =
            await response.Content.ReadAsStreamAsync())
        await using (FileStream output =
            File.Create(zipPath))
        {
            await input.CopyToAsync(output);
        }

        Console.WriteLine(
            $"✓ ZIP скачан: " +
            $"{new FileInfo(zipPath).Length:N0} bytes");

        Console.WriteLine("Распаковываем...");

        ZipFile.ExtractToDirectory(
            zipPath,
            outputDirectory,
            overwriteFiles: true);

        Console.WriteLine("✓ Архив распакован.");

        File.Delete(zipPath);

        Console.WriteLine("✓ Временный ZIP удалён.");

        return outputDirectory;
    }
}