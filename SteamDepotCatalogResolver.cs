using SteamKit2;

public sealed class SteamExpectedDepotInfo
{
    public uint AppId { get; init; }
    public uint DepotId { get; init; }
    public ulong PublicManifestId { get; init; }
    public string OsList { get; init; } = "";

    public bool IsShared => string.IsNullOrWhiteSpace(OsList);
}

public static class SteamDepotCatalogResolver
{
    public static async Task<List<SteamExpectedDepotInfo>> GetDepotsAsync(
        SteamClient steamClient,
        uint appId)
    {
        var steamApps = steamClient.GetHandler<SteamApps>();

        if (steamApps == null)
            throw new InvalidOperationException(
                "SteamApps handler недоступен.");

        var request = new SteamApps.PICSRequest(appId);

        var job = steamApps.PICSGetProductInfo(
            request,
            null,
            false);

        var result = await job;

        if (result == null ||
            result.Failed ||
            result.Results.Count == 0)
        {
            throw new IOException(
                $"Steam не вернул PICS App Info для AppID {appId}.");
        }

        foreach (var callback in result.Results)
        {
            if (!callback.Apps.TryGetValue(
                    appId,
                    out var appInfo))
            {
                continue;
            }

            var depotsNode = appInfo.KeyValues["depots"];

            if (depotsNode == null)
                return new List<SteamExpectedDepotInfo>();

            var resultList =
                new List<SteamExpectedDepotInfo>();

            foreach (var depotNode in depotsNode.Children)
            {
                if (!uint.TryParse(
                        depotNode.Name,
                        out uint depotId))
                {
                    continue;
                }

                string osList = "";

                var config = depotNode["config"];
                if (config != null)
                {
                    var osNode = config["oslist"];
                    if (osNode != null)
                        osList = osNode.AsString() ?? "";
                }

                var manifests = depotNode["manifests"];
                if (manifests == null)
                {
                    // Depot без собственного manifest может быть
                    // depotfromapp/shared content. Такой depot не
                    // является отдельным обязательным manifest'ом.
                    continue;
                }

                var publicNode = manifests["public"];
                if (publicNode == null)
                    continue;

                string manifestValue =
                    publicNode["gid"]?.AsString()
                    ?? publicNode.AsString()
                    ?? "";

                if (!ulong.TryParse(
                        manifestValue,
                        out ulong manifestId) ||
                    manifestId == 0)
                {
                    continue;
                }

                resultList.Add(
                    new SteamExpectedDepotInfo
                    {
                        AppId = appId,
                        DepotId = depotId,
                        PublicManifestId = manifestId,
                        OsList = osList
                    });
            }

            return resultList
                .OrderBy(x => x.DepotId)
                .ToList();
        }

        throw new IOException(
            $"AppID {appId} отсутствует в PICS-ответе Steam.");
    }
}