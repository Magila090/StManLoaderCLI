using SteamKit2;

public sealed class SteamDlcInfo
{
    public uint AppId { get; set; }

    public string Name { get; set; } = "";

    public List<uint> DepotIds { get; set; } = new();

    // OS для depot'ов этого DLC: windows / macos / linux / пусто = общий.
    public Dictionary<uint, string> DepotOs { get; set; } = new();
}

public static class SteamDlcResolver
{
    public static async Task<List<SteamDlcInfo>> GetDlcsAsync(
        SteamClient steamClient,
        uint appId)
    {
        var steamApps =
            steamClient.GetHandler<SteamApps>();

        if (steamApps == null)
            throw new InvalidOperationException(
                "SteamApps handler недоступен.");

        // ----------------------------------------------------
        // Получаем App Info основной игры
        // ----------------------------------------------------

        var request =
            new SteamApps.PICSRequest(appId);

        var job =
            steamApps.PICSGetProductInfo(
                request,
                null,
                false);

        var result = await job;

        if (result == null ||
            result.Failed ||
            result.Results.Count == 0)
        {
            throw new IOException(
                "Steam не вернул PICS App Info.");
        }

        foreach (var callback in result.Results)
        {
            if (!callback.Apps.TryGetValue(
                    appId,
                    out var appInfo))
            {
                continue;
            }

            var extended =
                appInfo.KeyValues["extended"];

            if (extended == null)
                return new List<SteamDlcInfo>();

            var listNode =
                extended["listofdlc"];

            if (listNode == null)
                return new List<SteamDlcInfo>();

            string list =
                listNode.AsString();

            if (string.IsNullOrWhiteSpace(list))
                return new List<SteamDlcInfo>();

            var dlcIds =
                new List<uint>();

            foreach (string value in list.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (uint.TryParse(
                        value.Trim(),
                        out uint dlcAppId))
                {
                    dlcIds.Add(dlcAppId);
                }
            }

            if (dlcIds.Count == 0)
                return new List<SteamDlcInfo>();

            // ------------------------------------------------
            // Получаем информацию обо всех DLC
            // ------------------------------------------------

            var requests =
                dlcIds.Select(
                    id => new SteamApps.PICSRequest(id))
                .ToList();

            var dlcJob =
    steamApps.PICSGetProductInfo(
        requests,
        Array.Empty<SteamApps.PICSRequest>(),
        false);

            var dlcResult =
                await dlcJob;

            if (dlcResult == null ||
                dlcResult.Failed)
            {
                throw new IOException(
                    "Steam не вернул информацию о DLC.");
            }

            var resultList =
                new List<SteamDlcInfo>();

            foreach (var dlcCallback in dlcResult.Results)
            {
                foreach (var pair in dlcCallback.Apps)
                {
                    uint dlcAppId =
                        pair.Key;

                    var dlcInfo =
                        pair.Value;

                    var common =
                        dlcInfo.KeyValues["common"];

                    string name =
                        dlcAppId.ToString();

                    if (common != null)
                    {
                        var nameNode =
                            common["name"];

                        if (nameNode != null)
                        {
                            string steamName =
                                nameNode.AsString();

                            if (!string.IsNullOrWhiteSpace(
                                    steamName))
                            {
                                name = steamName;
                            }
                        }
                    }

                    var depotIds =
                        new List<uint>();

                    var depotOs =
                        new Dictionary<uint, string>();

                    var depots =
                        dlcInfo.KeyValues["depots"];

                    if (depots != null)
                    {
                        foreach (var depotNode
                            in depots.Children)
                        {
                            if (!uint.TryParse(
                                    depotNode.Name,
                                    out uint depotId))
                            {
                                continue;
                            }

                            depotIds.Add(depotId);

                            string osList = "";

                            var config =
                                depotNode["config"];

                            if (config != null)
                            {
                                var osNode =
                                    config["oslist"];

                                if (osNode != null)
                                    osList = osNode.AsString();
                            }

                            depotOs[depotId] = osList ?? "";
                        }
                    }

                    // В некоторых играх Steam использует AppID самого DLC
                    // как идентификатор контента, для которого Lua содержит
                    // отдельный manifest.
                    //
                    // Пример Sally Face:
                    // DLC AppID = 567990
                    // Внутри PICS:
                    //   567991 = macOS
                    //   567992 = Linux
                    //   567993 = Windows
                    //
                    // Поэтому учитываем и сам DLC AppID.

                    if (!depotIds.Contains(dlcAppId))
                    {
                        depotIds.Insert(0, dlcAppId);
                    }

                    resultList.Add(
                        new SteamDlcInfo
                        {
                            AppId = dlcAppId,
                            Name = name,
                            DepotIds = depotIds,
                            DepotOs = depotOs
                        });
                }
            }

            return resultList
                .OrderBy(x => x.AppId)
                .ToList();
        }

        throw new IOException(
            $"AppID {appId} отсутствует в PICS-ответе Steam.");
    }
}