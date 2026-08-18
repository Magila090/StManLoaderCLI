using System.Text.RegularExpressions;

public class SteamDepotInfo
{
    public uint AppId { get; init; }
    public uint DepotId { get; init; }
    public ulong ManifestId { get; init; }
    public string DepotKey { get; init; } = "";
}

public static class SteamLuaParser
{
    public static List<SteamDepotInfo> ParseAll(string luaPath)
    {
        if (!File.Exists(luaPath))
            throw new FileNotFoundException(
                "Lua-файл не найден.",
                luaPath);

        string text = File.ReadAllText(luaPath);

        // =====================================================
        // AppID
        //
        // Первый addappid без ключа:
        //
        // addappid(541570)
        // =====================================================

        var appMatch = Regex.Match(
            text,
            @"addappid\s*\(\s*(\d+)\s*\)",
            RegexOptions.IgnoreCase);

        if (!appMatch.Success)
            throw new Exception(
                "Не удалось найти AppID в Lua-файле.");

        uint appId = uint.Parse(
            appMatch.Groups[1].Value);

        // =====================================================
        // Читаем все depot + ключи
        //
        // addappid(541574,0,"KEY")
        // =====================================================

        var depotMatches = Regex.Matches(
            text,
            @"addappid\s*\(\s*(\d+)\s*,\s*0\s*,\s*[""']([0-9a-fA-F]{64})[""']\s*\)",
            RegexOptions.IgnoreCase);

        if (depotMatches.Count == 0)
            throw new Exception(
                "Не удалось найти depot'ы с ключами в Lua-файле.");

        var depots = new Dictionary<uint, SteamDepotInfo>();

        foreach (Match match in depotMatches)
        {
            uint depotId = uint.Parse(
                match.Groups[1].Value);

            string depotKey =
                match.Groups[2].Value;

            depots[depotId] = new SteamDepotInfo
            {
                AppId = appId,
                DepotId = depotId,
                DepotKey = depotKey
            };
        }

        // =====================================================
        // Читаем все ManifestID
        //
        // setManifestid(541574,"7789652124095821521")
        // =====================================================

        var manifestMatches = Regex.Matches(
            text,
            @"setManifestid\s*\(\s*(\d+)\s*,\s*[""'](\d+)[""']\s*\)",
            RegexOptions.IgnoreCase);

        foreach (Match match in manifestMatches)
        {
            uint depotId = uint.Parse(
                match.Groups[1].Value);

            ulong manifestId = ulong.Parse(
                match.Groups[2].Value);

            if (depots.TryGetValue(
                    depotId,
                    out var depot))
            {
                depots[depotId] = new SteamDepotInfo
                {
                    AppId = depot.AppId,
                    DepotId = depot.DepotId,
                    DepotKey = depot.DepotKey,
                    ManifestId = manifestId
                };
            }
        }

        // =====================================================
        // Проверяем
        // =====================================================

        foreach (var depot in depots.Values)
        {
            if (depot.ManifestId == 0)
            {
                throw new Exception(
                    $"Для DepotID {depot.DepotId} " +
                    $"не найден ManifestID.");
            }
        }

        return depots.Values
            .OrderBy(x => x.DepotId)
            .ToList();
    }

    // Старый метод оставляем,
    // чтобы другой код проекта не сломался.
    public static SteamDepotInfo Parse(string luaPath)
    {
        var depots = ParseAll(luaPath);

        if (depots.Count == 0)
            throw new Exception(
                "В Lua не найдено ни одного depot.");

        return depots[0];
    }
}