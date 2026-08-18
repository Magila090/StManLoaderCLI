using SteamKit2;

public static class SteamDepotOsResolver
{
    public static async Task<Dictionary<uint, string>> GetDepotOsAsync(
        SteamClient steamClient,
        uint appId)
    {
        var steamApps = steamClient.GetHandler<SteamApps>();

        if (steamApps == null)
            throw new InvalidOperationException(
                "SteamApps handler недоступен.");

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

            var depotsNode =
                appInfo.KeyValues["depots"];

            if (depotsNode == null)
            {
                throw new IOException(
                    "В Steam App Info отсутствует секция depots.");
            }

            var resultMap =
                new Dictionary<uint, string>();

            foreach (var depotNode in depotsNode.Children)
            {
                if (!uint.TryParse(
                        depotNode.Name,
                        out uint depotId))
                {
                    continue;
                }

                string osList = "";

                var config =
                    depotNode["config"];

                if (config != null)
                {
                    var osNode =
                        config["oslist"];

                    if (osNode != null)
                    {
                        osList =
                            osNode.AsString();
                    }
                }

                resultMap[depotId] =
                    osList ?? "";
            }

            return resultMap;
        }

        throw new IOException(
            $"AppID {appId} отсутствует в PICS-ответе Steam.");
    }
}