using SteamKit2;
using SteamKit2.CDN;

public static class OwnManifestDownloader
{
    private sealed class ManifestData
    {
        public uint DepotId { get; init; }
        public ulong ManifestId { get; init; }
        public required byte[] DepotKey { get; init; }
    }

    public static async Task<string> DownloadAsync(
        SteamClient steamClient,
        uint appId,
        string baseDirectory,
        string username)
    {
        if (!steamClient.IsConnected)
            throw new InvalidOperationException("SteamClient не подключён к Steam.");

        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("Не указана папка сохранения.", nameof(baseDirectory));

        if (string.IsNullOrWhiteSpace(username))
            username = "account";

        Directory.CreateDirectory(baseDirectory);

        string safeUsername = MakeSafeFileName(username);
        string folderName = $"Manifest_{appId}_{safeUsername}";

        string outputDirectory =
            GetUniqueDirectory(
                baseDirectory,
                folderName);

        Directory.CreateDirectory(outputDirectory);

        var steamApps =
            steamClient.GetHandler<SteamApps>();

        var steamContent =
            steamClient.GetHandler<SteamContent>();

        Console.WriteLine();
        Console.WriteLine("Получаем список DLC из Steam...");

        var appIdsToProcess = new List<uint>
        {
            appId
        };

        var dlcNames =
            new Dictionary<uint, string>();

        try
        {
            List<SteamDlcInfo> dlcs =
                await SteamDlcResolver.GetDlcsAsync(
                    steamClient,
                    appId);

            foreach (SteamDlcInfo dlc in dlcs)
            {
                if (dlc.AppId == 0 ||
                    dlc.AppId == appId ||
                    appIdsToProcess.Contains(dlc.AppId))
                {
                    continue;
                }

                appIdsToProcess.Add(dlc.AppId);
                dlcNames[dlc.AppId] = dlc.Name;
            }

            Console.WriteLine(
                $"✓ DLC найдено: {appIdsToProcess.Count - 1}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"⚠ Не удалось получить список DLC: {ex.Message}");

            Console.WriteLine(
                "  Будут обработаны только manifest основной игры.");
        }

        Console.WriteLine();
        Console.WriteLine("Получаем список CDN-серверов...");

        var cdnServers =
            (await steamContent.GetServersForSteamPipe(
                null,
                null))
            .OrderBy(server => server.WeightedLoad)
            .ToList();

        if (cdnServers.Count == 0)
        {
            TryDeleteEmptyDirectory(outputDirectory);

            throw new InvalidOperationException(
                "Steam не вернул CDN-серверы.");
        }

        Console.WriteLine(
            $"✓ CDN-серверов найдено: {cdnServers.Count}");

        var manifestData =
            new List<ManifestData>();

        // Не скачиваем один и тот же depot/manifest повторно,
        // если он одновременно указан у игры и у DLC.
        var downloadedManifests =
            new HashSet<(uint DepotId, ulong ManifestId)>();

        // В Lua добавим AppID основной игры и всех найденных DLC.
        var luaAppIds =
            new List<uint>();

        int preferredServerIndex = 0;
        int foundApps = 0;

        foreach (uint currentAppId in appIdsToProcess)
        {
            string title;

            if (currentAppId == appId)
            {
                title = $"Основная игра ({currentAppId})";
            }
            else if (dlcNames.TryGetValue(
                         currentAppId,
                         out string? dlcName) &&
                     !string.IsNullOrWhiteSpace(dlcName))
            {
                title = $"DLC: {dlcName} ({currentAppId})";
            }
            else
            {
                title = $"DLC ({currentAppId})";
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine(title);
            Console.WriteLine("========================================");

            bool found =
                await ProcessAppAsync(
                    steamApps,
                    steamContent,
                    steamClient,
                    cdnServers,
                    currentAppId,
                    appId,
                    manifestData,
                    downloadedManifests,
                    luaAppIds,
                    outputDirectory,
                    preferredServerIndex,
                    newIndex => preferredServerIndex = newIndex);

            if (found)
                foundApps++;
        }

        if (foundApps == 0)
        {
            TryDeleteEmptyDirectory(outputDirectory);

            throw new InvalidOperationException(
                "Ни основная игра, ни DLC не были найдены в Steam PICS.");
        }

        CreateLuaFile(
            appId,
            luaAppIds,
            manifestData,
            outputDirectory);

        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("ГОТОВО");
        Console.WriteLine("========================================");
        Console.WriteLine(
            $"✓ Обработано AppID: {foundApps}/{appIdsToProcess.Count}");
        Console.WriteLine(
            $"✓ Успешно скачано manifest: {manifestData.Count}");

        return outputDirectory;
    }

    private static async Task<bool> ProcessAppAsync(
        SteamApps steamApps,
        SteamContent steamContent,
        SteamClient steamClient,
        List<Server> cdnServers,
        uint currentAppId,
        uint rootAppId,
        List<ManifestData> manifestData,
        HashSet<(uint DepotId, ulong ManifestId)> downloadedManifests,
        List<uint> luaAppIds,
        string outputDirectory,
        int preferredServerIndex,
        Action<int> setPreferredServerIndex)
    {
        try
        {
            Console.WriteLine(
                $"Получаем PICS для AppID {currentAppId}...");

            var request =
                new SteamApps.PICSRequest(
                    currentAppId);

            var job =
                steamApps.PICSGetProductInfo(
                    request,
                    null,
                    false);

            var result =
                await job;

            foreach (var callback in result.Results)
            {
                if (!callback.Apps.TryGetValue(
                        currentAppId,
                        out var appInfo))
                {
                    continue;
                }

                if (!luaAppIds.Contains(currentAppId))
                    luaAppIds.Add(currentAppId);

                var app =
                    appInfo.KeyValues;

                var depots =
                    app["depots"];

                if (depots == null)
                {
                    Console.WriteLine(
                        "⚠ Depot'ы для этого AppID не найдены.");

                    return true;
                }

                int depotsFound = 0;

                foreach (var depotNode in depots.Children)
                {
                    if (!uint.TryParse(
                            depotNode.Name,
                            out uint depotId))
                    {
                        continue;
                    }

                    string? manifestText =
                        depotNode["manifests"]?["public"]?["gid"]?.Value;

                    if (!ulong.TryParse(
                            manifestText,
                            out ulong manifestId))
                    {
                        continue;
                    }

                    depotsFound++;

                    string os =
                        depotNode["config"]?["oslist"]?.Value
                        ?? "unknown";

                    Console.WriteLine();
                    Console.WriteLine(
                        $"Depot: {depotId} ({os})");

                    if (downloadedManifests.Contains(
                            (depotId, manifestId)))
                    {
                        Console.WriteLine(
                            "  ↪ Этот manifest уже был обработан ранее.");

                        continue;
                    }

                    int newIndex =
                        await DownloadDepotManifestAsync(
                            steamContent,
                            steamApps,
                            steamClient,
                            cdnServers,
                            preferredServerIndex,
                            depotId,
                            currentAppId,
                            rootAppId,
                            manifestId,
                            manifestData,
                            outputDirectory);

                    preferredServerIndex =
                        newIndex;

                    setPreferredServerIndex(
                        newIndex);

                    if (manifestData.Any(
                            x =>
                                x.DepotId == depotId &&
                                x.ManifestId == manifestId))
                    {
                        downloadedManifests.Add(
                            (depotId, manifestId));
                    }
                }

                if (depotsFound == 0)
                {
                    Console.WriteLine(
                        "ℹ Public manifest у этого AppID не найден.");
                }

                return true;
            }

            Console.WriteLine(
                $"⚠ AppID {currentAppId} не найден в PICS.");

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"⚠ Ошибка обработки AppID {currentAppId}: " +
                $"{ex.GetType().Name}: {ex.Message}");

            return false;
        }
    }

    private static async Task<int> DownloadDepotManifestAsync(
        SteamContent steamContent,
        SteamApps steamApps,
        SteamClient steamClient,
        List<Server> servers,
        int preferredServerIndex,
        uint depotId,
        uint ownerAppId,
        uint rootAppId,
        ulong manifestId,
        List<ManifestData> manifestData,
        string outputDirectory)
    {
        try
        {
            byte[]? depotKey =
                await TryGetDepotKeyAsync(
                    steamApps,
                    depotId,
                    ownerAppId);

            // У некоторых shared depot ключ может выдаваться через
            // AppID основной игры, поэтому делаем запасную попытку.
            if ((depotKey == null ||
                 depotKey.Length == 0) &&
                ownerAppId != rootAppId)
            {
                depotKey =
                    await TryGetDepotKeyAsync(
                        steamApps,
                        depotId,
                        rootAppId);
            }

            if (depotKey == null ||
                depotKey.Length == 0)
            {
                Console.WriteLine(
                    "  ✗ Depot Key не получен. " +
                    "Возможно, у выбранного аккаунта нет доступа.");

                return preferredServerIndex;
            }

            ulong manifestRequestCode =
                await steamContent.GetManifestRequestCode(
                    depotId,
                    ownerAppId,
                    manifestId,
                    "public",
                    null);

            if (manifestRequestCode == 0 &&
                ownerAppId != rootAppId)
            {
                manifestRequestCode =
                    await steamContent.GetManifestRequestCode(
                        depotId,
                        rootAppId,
                        manifestId,
                        "public",
                        null);
            }

            if (manifestRequestCode == 0)
            {
                Console.WriteLine(
                    "  ✗ Manifest Request Code не получен.");

                return preferredServerIndex;
            }

            using var cdnClient =
                new Client(
                    steamClient);

            for (int offset = 0;
                 offset < servers.Count;
                 offset++)
            {
                int index =
                    (preferredServerIndex + offset) %
                    servers.Count;

                var server =
                    servers[index];

                try
                {
                    string cdnAuthToken =
                        await GetCdnAuthTokenAsync(
                            steamContent,
                            ownerAppId,
                            depotId,
                            server.Host);

                    if (string.IsNullOrEmpty(cdnAuthToken) &&
                        ownerAppId != rootAppId)
                    {
                        cdnAuthToken =
                            await GetCdnAuthTokenAsync(
                                steamContent,
                                rootAppId,
                                depotId,
                                server.Host);
                    }

                    var manifest =
                        await cdnClient.DownloadManifestAsync(
                            depotId,
                            manifestId,
                            manifestRequestCode,
                            server,
                            depotKey,
                            null,
                            cdnAuthToken);

                    string outputPath =
                        Path.Combine(
                            outputDirectory,
                            $"{depotId}_{manifestId}.manifest");

                    manifest.SaveToFile(
                        outputPath);

                    Console.WriteLine(
                        "  ✓ Manifest скачан.");

                    manifestData.Add(
                        new ManifestData
                        {
                            DepotId = depotId,
                            ManifestId = manifestId,
                            DepotKey = depotKey
                        });

                    return index;
                }
                catch
                {
                    // CDN не подошёл — пробуем следующий.
                }
            }

            Console.WriteLine(
                "  ✗ Не удалось скачать manifest ни с одного CDN.");

            return preferredServerIndex;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"  ✗ Ошибка: {ex.GetType().Name}: {ex.Message}");

            return preferredServerIndex;
        }
    }

    private static async Task<byte[]?> TryGetDepotKeyAsync(
        SteamApps steamApps,
        uint depotId,
        uint appId)
    {
        try
        {
            var keyResult =
                await steamApps.GetDepotDecryptionKey(
                    depotId,
                    appId);

            if (keyResult == null ||
                keyResult.DepotKey == null ||
                keyResult.DepotKey.Length == 0)
            {
                return null;
            }

            return keyResult.DepotKey;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> GetCdnAuthTokenAsync(
        SteamContent steamContent,
        uint appId,
        uint depotId,
        string host)
    {
        try
        {
            var authResult =
                await steamContent.GetCDNAuthToken(
                    appId,
                    depotId,
                    host);

            if (authResult != null &&
                authResult.Result == EResult.OK &&
                !string.IsNullOrEmpty(authResult.Token))
            {
                return authResult.Token;
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static void CreateLuaFile(
        uint rootAppId,
        List<uint> appIds,
        List<ManifestData> manifestData,
        string outputDirectory)
    {
        string outputPath =
            Path.Combine(
                outputDirectory,
                $"{rootAppId}.lua");

        using var writer =
            new StreamWriter(
                outputPath,
                false,
                new System.Text.UTF8Encoding(false));

        // Основная игра всегда первая.
        writer.WriteLine(
            $"addappid({rootAppId})");

        foreach (ManifestData data
                 in manifestData
                     .GroupBy(x => new
                     {
                         x.DepotId,
                         x.ManifestId
                     })
                     .Select(group => group.First()))
        {
            string depotKey =
                Convert
                    .ToHexString(data.DepotKey)
                    .ToLowerInvariant();

            writer.WriteLine(
                $"addappid({data.DepotId},0,\"{depotKey}\")");

            writer.WriteLine(
                $"setManifestid({data.DepotId},\"{data.ManifestId}\")");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"✓ Lua-файл создан: {Path.GetFileName(outputPath)}");
    }

    private static string GetUniqueDirectory(
        string parentDirectory,
        string baseName)
    {
        string candidate =
            Path.Combine(
                parentDirectory,
                baseName);

        if (!Directory.Exists(candidate))
            return candidate;

        int index = 1;

        while (true)
        {
            candidate =
                Path.Combine(
                    parentDirectory,
                    $"{baseName}_{index}");

            if (!Directory.Exists(candidate))
                return candidate;

            index++;
        }
    }

    private static string MakeSafeFileName(
        string value)
    {
        foreach (char invalid
                 in Path.GetInvalidFileNameChars())
        {
            value =
                value.Replace(
                    invalid,
                    '_');
        }

        value = value.Trim();

        return string.IsNullOrWhiteSpace(value)
            ? "account"
            : value;
    }

    private static void TryDeleteEmptyDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
        }
    }
}