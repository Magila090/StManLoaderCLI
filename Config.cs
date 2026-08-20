using System.Text.Json;

public class AppConfig
{
    public string? DownloadDirectory { get; set; }

    public int ParallelDownloads { get; set; } = 20;

    public string SelectedOs { get; set; } = "windows";

    public bool DeleteManifestsAfterDownload { get; set; } = true;

    public string? ManifestKeepDirectory { get; set; }
}

public static class ConfigManager
{
    private static readonly string ConfigPath =
        Path.Combine(
            AppContext.BaseDirectory,
            "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            string json =
                File.ReadAllText(ConfigPath);

            var config =
                JsonSerializer.Deserialize<AppConfig>(json);

            return config ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        var options =
            new JsonSerializerOptions
            {
                WriteIndented = true
            };

        string json =
            JsonSerializer.Serialize(
                config,
                options);

        File.WriteAllText(
            ConfigPath,
            json);
    }
}