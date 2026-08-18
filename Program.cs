using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;

var steamClient = new SteamClient();
var steamUser = steamClient.GetHandler<SteamUser>();

// Фактическое состояние авторизации Steam в текущем запуске.
bool steamLoggedOn = false;

Console.OutputEncoding = System.Text.Encoding.UTF8;

AppConfig config = ConfigManager.Load();

string? downloadDirectory =
    config.DownloadDirectory;

int parallelDownloads =
    config.ParallelDownloads;

// Выбранная ОС сохраняется между запусками.
// Возможные значения: windows, macos, linux, all.
string selectedOs =
    string.IsNullOrWhiteSpace(config.SelectedOs)
        ? "windows"
        : config.SelectedOs;

await ConnectToSteamAsync();

await MainMenuAsync();

steamClient.Disconnect();


// ============================================================
// ГЛАВНОЕ МЕНЮ
// ============================================================

async Task MainMenuAsync()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║          My Steam Downloader               ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        TokenStore.TryLoad(
            out string? storedUsername,
            out _,
            out _);

        Console.WriteLine(
            $"Steam:          {(steamLoggedOn
                ? $"✓ Авторизован ({storedUsername ?? "аккаунт"})"
                : "✗ Не авторизован")}");

        Console.WriteLine(
            $"Каталог:        {(string.IsNullOrEmpty(downloadDirectory) ? "не задан" : downloadDirectory)}");

        Console.WriteLine(
            $"Параллельность: {parallelDownloads}");

        Console.WriteLine(
            $"ОС:             {FormatDepotOs(selectedOs)}");    

        Console.WriteLine();
        Console.WriteLine("────────────────────────────────────────────");
        Console.WriteLine();
        Console.WriteLine("1. Steam");
        Console.WriteLine("2. Каталог");
        Console.WriteLine("3. Скачать");
        Console.WriteLine("4. Параллельность");
        Console.WriteLine("5. Операционная система");
        Console.WriteLine("6. Выход");
        Console.WriteLine();
        Console.WriteLine("────────────────────────────────────────────");
        Console.WriteLine();

        Console.Write("Выберите действие: ");

        string? choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                await SteamMenuAsync();
                break;

            case "2":
                CatalogMenu();
                break;

            case "3":
                await DownloadMenuAsync();
                break;

            case "4":
                ParallelMenu();
                break;

            case "5":
                OsMenu();
                break;

            case "6":
                return;

            default:
                Console.WriteLine();
                Console.WriteLine("✗ Неизвестный пункт.");
                await PauseAsync();
                break;
        }
    }
}


// ============================================================
// STEAM
// ============================================================

async Task SteamMenuAsync()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║                  Steam                     ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        TokenStore.TryLoad(
            out string? storedUsername,
            out _,
            out _);

        if (steamLoggedOn)
        {
            Console.WriteLine($"Статус: ✓ Авторизован");
            Console.WriteLine($"Аккаунт: {storedUsername ?? "аккаунт"}");
        }
        else
        {
            Console.WriteLine("Статус: ✗ Не авторизован");
        }

        Console.WriteLine();
        Console.WriteLine("1. Войти в Steam");
        Console.WriteLine("2. Удалить данные Steam");
        Console.WriteLine("3. Назад");
        Console.WriteLine();

        Console.Write("Выберите действие: ");

        string? choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                Console.Clear();
                await LoginToSteamAsync();
                await PauseAsync();
                break;

            case "2":
                Console.Clear();

                Console.WriteLine("=== Удаление данных Steam ===");
                Console.WriteLine();

                if (!TokenStore.TryLoad(
                    out string? steamUsername,
                    out _,
                    out _))
                {
                    Console.WriteLine("Сохранённых данных Steam нет.");
                    await PauseAsync();
                    break;
                }

                Console.WriteLine(
                    $"Будут удалены сохранённые данные аккаунта: {storedUsername}");

                Console.WriteLine();
                Console.Write("Удалить? (y/n): ");

                string? answer = Console.ReadLine();

                if (answer?.Equals("y", StringComparison.OrdinalIgnoreCase) == true)
                {
                    TokenStore.Delete();

                    Console.WriteLine();
                    Console.WriteLine("✓ Данные Steam удалены.");
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("Отмена.");
                }

                await PauseAsync();
                break;

            case "3":
                return;

            default:
                Console.WriteLine();
                Console.WriteLine("✗ Неизвестный пункт.");
                await PauseAsync();
                break;
        }
    }
}


// ============================================================
// КАТАЛОГ
// ============================================================

void CatalogMenu()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║                 Каталог                    ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine(
            $"Текущий каталог:");

        Console.WriteLine(
            $"  {(string.IsNullOrEmpty(downloadDirectory)
                ? "не задан"
                : downloadDirectory)}");

        Console.WriteLine();
        Console.WriteLine("1. Изменить каталог");
        Console.WriteLine("2. Назад");
        Console.WriteLine();

        Console.Write("Выберите действие: ");

        string? choice = Console.ReadLine();

        switch (choice)
        {
            case "1":

                Console.Clear();

                Console.WriteLine("=== Каталог загрузок ===");
                Console.WriteLine();

                Console.Write("Введите корневой каталог: ");

                string? path = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(path))
                {
                    Console.WriteLine();
                    Console.WriteLine("✗ Каталог не указан.");
                    Pause();
                    break;
                }

                path = Path.GetFullPath(path.Trim());

                try
                {
                    Directory.CreateDirectory(path);

                    downloadDirectory = path;

                    config.DownloadDirectory = downloadDirectory;
                    ConfigManager.Save(config);

                    Console.WriteLine();
                    Console.WriteLine("✓ Каталог сохранён:");
                    Console.WriteLine(downloadDirectory);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        $"✗ Не удалось создать каталог: {ex.Message}");
                }

                Pause();
                break;

            case "2":
                return;

            default:
                Console.WriteLine();
                Console.WriteLine("✗ Неизвестный пункт.");
                Pause();
                break;
        }
    }
}


// ============================================================
// ПАРАЛЛЕЛЬНОСТЬ
// ============================================================

void ParallelMenu()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║             Параллельность                 ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine(
            $"Текущее количество потоков: {parallelDownloads}");

        Console.WriteLine();
        Console.WriteLine("Допустимый диапазон: 1–200");
        Console.WriteLine();

        Console.Write("Новое значение или Enter для возврата: ");

        string? input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
            return;

        if (!int.TryParse(input, out int value))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Нужно ввести число.");
            Pause();
            continue;
        }

        if (value < 1 || value > 200)
        {
            Console.WriteLine();
            Console.WriteLine("✗ Значение должно быть от 1 до 200.");
            Pause();
            continue;
        }

        parallelDownloads = value;

        config.ParallelDownloads =
    parallelDownloads;

        ConfigManager.Save(config);

        Console.WriteLine();
        Console.WriteLine(
            $"✓ Параллельность установлена: {parallelDownloads}");

        Pause();

        return;
    }
}


// ============================================================
// ОПЕРАЦИОННАЯ СИСТЕМА
// ============================================================

void OsMenu()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║          Операционная система              ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine($"Текущий выбор: {FormatDepotOs(selectedOs)}");
        Console.WriteLine();

        Console.WriteLine("1. Windows");
        Console.WriteLine("2. macOS");
        Console.WriteLine("3. Linux");
        Console.WriteLine("4. Все ОС");
        Console.WriteLine("5. Назад");
        Console.WriteLine();

        Console.Write("Выберите ОС: ");

        string? choice = Console.ReadLine();

        string? newOs = choice switch
        {
            "1" => "windows",
            "2" => "macos",
            "3" => "linux",
            "4" => "all",
            "5" => null,
            _ => ""
        };

        if (choice == "5")
            return;

        if (string.IsNullOrEmpty(newOs))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Неизвестный пункт.");
            Pause();
            continue;
        }

        selectedOs = newOs;
        config.SelectedOs = selectedOs;
        ConfigManager.Save(config);

        Console.WriteLine();
        Console.WriteLine($"✓ Выбрана ОС: {FormatDepotOs(selectedOs)}");
        Pause();
        return;
    }
}


// ============================================================
// СКАЧИВАНИЕ
// ============================================================

async Task DownloadMenuAsync()
{
    Console.Clear();

    Console.WriteLine("╔════════════════════════════════════════════╗");
    Console.WriteLine("║                  Скачать                   ║");
    Console.WriteLine("╚════════════════════════════════════════════╝");
    Console.WriteLine();

    if (string.IsNullOrEmpty(downloadDirectory))
    {
        Console.WriteLine("✗ Сначала необходимо указать каталог.");
        Console.WriteLine();
        await PauseAsync();
        return;
    }

    Console.WriteLine($"Каталог загрузки:");
    Console.WriteLine($"  {downloadDirectory}");
    Console.WriteLine();

    Console.Write("Папка с Lua и manifest: ");

    string? sourceDirectory = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(sourceDirectory))
    {
        Console.WriteLine();
        Console.WriteLine("✗ Папка не указана.");
        await PauseAsync();
        return;
    }

    sourceDirectory =
        Path.GetFullPath(sourceDirectory.Trim());

    if (!Directory.Exists(sourceDirectory))
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✗ Папка не найдена: {sourceDirectory}");

        await PauseAsync();
        return;
    }

    // --------------------------------------------------------
    // Lua
    // --------------------------------------------------------

    var luaFiles = Directory
        .GetFiles(
            sourceDirectory,
            "*.lua",
            SearchOption.TopDirectoryOnly)
        .ToList();

    if (luaFiles.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine("✗ В папке не найден Lua-файл.");
        await PauseAsync();
        return;
    }

    if (luaFiles.Count > 1)
    {
        Console.WriteLine();
        Console.WriteLine(
            "✗ В папке найдено несколько Lua-файлов.");

        foreach (var lua in luaFiles)
            Console.WriteLine($"  {Path.GetFileName(lua)}");

        await PauseAsync();
        return;
    }

    string luaPath = luaFiles[0];

    Console.WriteLine();
    Console.WriteLine(
        $"✓ Lua: {Path.GetFileName(luaPath)}");

    // --------------------------------------------------------
    // Читаем Lua
    // --------------------------------------------------------

    List<SteamDepotInfo> depots;

    try
    {
        depots =
            SteamLuaParser.ParseAll(luaPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✗ Ошибка чтения Lua: {ex.Message}");

        await PauseAsync();
        return;
    }

    if (depots.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine("✗ В Lua не найдено ни одного Depot.");

        await PauseAsync();
        return;
    }

    Console.WriteLine(
        $"✓ Depot найдено: {depots.Count}");

    uint appId = depots[0].AppId;

    Console.WriteLine();
    Console.WriteLine($"✓ AppID: {appId}");

    // --------------------------------------------------------
    // Полный список Lua
    // --------------------------------------------------------

    var allLuaDepots =
        depots.ToDictionary(
            x => x.DepotId,
            x => x);

    // --------------------------------------------------------
    // Manifest'ы читаем заранее.
    //
    // Это важно: если Steam не указал oslist у depot'а,
    // мы сможем определить платформу по содержимому manifest
    // ещё ДО выбора DLC.
    // --------------------------------------------------------

    var manifestPaths = Directory
        .GetFiles(
            sourceDirectory,
            "*.manifest",
            SearchOption.TopDirectoryOnly)
        .ToList();

    if (manifestPaths.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine(
            "✗ В папке не найдено manifest-файлов.");

        await PauseAsync();
        return;
    }

    // depotId -> путь к manifest.
    var localManifestPaths =
        new Dictionary<uint, (string Path, ulong ManifestId)>();

    foreach (string manifestPath in manifestPaths)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(manifestPath);

        string[] parts =
            fileName.Split('_');

        if (parts.Length < 2 ||
            !uint.TryParse(
                parts[0],
                out uint depotId) ||
            !ulong.TryParse(
                parts[1],
                out ulong manifestId))
        {
            Console.WriteLine(
                $"⚠ Пропускаем manifest с неправильным именем: " +
                $"{Path.GetFileName(manifestPath)}");

            continue;
        }

        if (!allLuaDepots.TryGetValue(
                depotId,
                out SteamDepotInfo? luaDepot))
        {
            Console.WriteLine(
                $"⚠ Manifest {Path.GetFileName(manifestPath)} " +
                $"не относится к depot из Lua.");

            continue;
        }

        if (luaDepot.ManifestId != manifestId)
        {
            Console.WriteLine(
                $"⚠ ManifestID не совпадает для depot {depotId}: " +
                $"{Path.GetFileName(manifestPath)}");

            continue;
        }

        localManifestPaths[depotId] =
            (manifestPath, manifestId);
    }

    Console.WriteLine();
    Console.WriteLine(
        $"✓ Подготовлено локальных manifest: " +
        $"{localManifestPaths.Count}/{manifestPaths.Count}");

    // --------------------------------------------------------
    // Получаем DLC из Steam.
    // --------------------------------------------------------

    List<SteamDlcInfo> allDlcs = new();
    List<SteamDlcInfo> availableDlcs = new();

    try
    {
        Console.WriteLine();
        Console.WriteLine("Получаем список DLC из Steam...");

        allDlcs =
            await SteamDlcResolver.GetDlcsAsync(
                steamClient,
                appId);

        Console.WriteLine(
            $"✓ Steam вернул DLC: {allDlcs.Count}");
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"⚠ Не удалось получить список DLC: {ex.Message}");

        allDlcs = new List<SteamDlcInfo>();
    }

    // --------------------------------------------------------
    // ОС основной игры.
    //
    // Получаем её заранее, чтобы использовать не только для
    // основной игры, но и при формировании общего списка.
    // --------------------------------------------------------

    Dictionary<uint, string> depotOs = new();

    if (downloadDirectory != null)
    {
        try
        {
            depotOs =
                await SteamDepotOsResolver.GetDepotOsAsync(
                    steamClient,
                    appId);

            Console.WriteLine(
                $"✓ Получена информация об ОС для " +
                $"{depotOs.Count} depot основной игры.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"⚠ Не удалось получить информацию об ОС основной игры: " +
                $"{ex.Message}");

            // Не прекращаем работу: для depot'ов без информации
            // попробуем определить платформу по manifest.
        }
    }

    // --------------------------------------------------------
    // Собираем известные Steam OS для всех depot'ов.
    //
    // Для DLC SteamDlcResolver уже возвращает DepotOs.
    // --------------------------------------------------------

    var steamOsByDepot =
        new Dictionary<uint, string>(
            depotOs);

    foreach (SteamDlcInfo dlc in allDlcs)
    {
        foreach (var pair in dlc.DepotOs)
        {
            steamOsByDepot[pair.Key] = pair.Value;
        }
    }

    // --------------------------------------------------------
    // Определяем платформу каждого локального depot'а.
    //
    // 1. Если Steam явно указал OS — доверяем Steam.
    // 2. Если oslist пустой — анализируем manifest.
    // --------------------------------------------------------

    var depotPlatforms =
        new Dictionary<uint, DepotPlatform>();

    var depotPlatformDetails =
        new Dictionary<uint, string>();

    foreach (var pair in localManifestPaths)
    {
        uint depotId = pair.Key;
        string manifestPath = pair.Value.Path;

        string? steamOs = null;

        if (steamOsByDepot.TryGetValue(
                depotId,
                out string? knownOs))
        {
            steamOs = knownOs;
        }

        // Явная OS от Steam — самый надёжный источник.
        if (!string.IsNullOrWhiteSpace(steamOs) &&
            !steamOs.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            string normalized =
                steamOs.Trim().ToLowerInvariant();

            DepotPlatform platform;

            if (normalized.Contains("windows") ||
                normalized == "win")
            {
                platform = DepotPlatform.Windows;
            }
            else if (normalized.Contains("linux"))
            {
                platform = DepotPlatform.Linux;
            }
            else if (normalized.Contains("macos") ||
                     normalized == "mac" ||
                     normalized == "macosx")
            {
                platform = DepotPlatform.MacOS;
            }
            else
            {
                platform = DepotPlatform.Shared;
            }

            depotPlatforms[depotId] = platform;
            depotPlatformDetails[depotId] =
                $"Steam: {FormatDepotOs(steamOs)}";

            continue;
        }

        // oslist пустой — читаем manifest.
        try
        {
            DepotManifest manifest;

            await using (
                var stream =
                    File.OpenRead(manifestPath))
            {
                manifest =
                    DepotManifest.Deserialize(stream);
            }

            // Некоторые manifest'ы хранят имена файлов в зашифрованном
            // виде. Для анализа платформы по именам их сначала нужно
            // расшифровать ключом соответствующего depot.
            if (manifest.FilenamesEncrypted)
            {
                if (!allLuaDepots.TryGetValue(
                        depotId,
                        out SteamDepotInfo? luaDepotForKey) ||
                    string.IsNullOrWhiteSpace(luaDepotForKey.DepotKey))
                {
                    throw new IOException(
                        $"Нет depot key для расшифровки manifest {depotId}.");
                }

                byte[] manifestDepotKey =
                    Convert.FromHexString(luaDepotForKey.DepotKey);

                manifest.DecryptFilenames(manifestDepotKey);
            }

            var fileNames =
                (manifest.Files ?? new List<DepotManifest.FileData>())
                    .Select(file => file.FileName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

            DepotPlatform detected =
                SteamDepotPlatformResolver
                    .DetectFromFileNames(fileNames);

            depotPlatforms[depotId] = detected;

            depotPlatformDetails[depotId] =
                detected == DepotPlatform.Unknown
                    ? "Manifest: неизвестно"
                    : $"Manifest: {FormatDepotPlatform(detected)}";
        }
        catch (Exception ex)
        {
            depotPlatforms[depotId] =
                DepotPlatform.Unknown;

            depotPlatformDetails[depotId] =
                $"Manifest: ошибка анализа ({ex.Message})";
        }
    }

    bool IsDepotAllowedForSelectedOs(uint depotId)
    {
        if (selectedOs == "all")
            return true;

        if (!depotPlatforms.TryGetValue(
                depotId,
                out DepotPlatform platform))
        {
            // Нет локального manifest — нечего скачивать.
            return false;
        }

        return SteamDepotPlatformResolver.IsCompatible(
            platform,
            selectedOs);
    }

    // DLC может иметь depot'ы, перечисленные Steam,
    // а иногда manifest находится под AppID самого DLC.
    var knownDlcDepotIds =
        allDlcs
            .SelectMany(dlc =>
                dlc.DepotIds
                    .Append(dlc.AppId))
            .ToHashSet();

    // Depot'ы основной игры.
    var gameLuaDepots =
        depots
            .Where(x => !knownDlcDepotIds.Contains(x.DepotId))
            .ToList();

    // Если Steam вообще не вернул DLC — считаем весь Lua
    // принадлежащим основной игре.
    if (allDlcs.Count == 0)
    {
        gameLuaDepots = depots.ToList();
    }

    // --------------------------------------------------------
    // DLC показываем только если:
    // 1. depot реально есть в Lua;
    // 2. у него есть подходящий manifest;
    // 3. depot совместим с выбранной ОС.
    // --------------------------------------------------------

    bool IsDlcDepotAllowed(
        SteamDlcInfo dlc,
        uint depotId)
    {
        if (!allLuaDepots.ContainsKey(depotId))
            return false;

        if (!localManifestPaths.ContainsKey(depotId))
            return false;

        if (selectedOs == "all")
            return true;

        return IsDepotAllowedForSelectedOs(depotId);
    }

    availableDlcs =
        allDlcs
            .Where(dlc =>
                dlc.DepotIds
                    .Append(dlc.AppId)
                    .Distinct()
                    .Any(id =>
                        IsDlcDepotAllowed(
                            dlc,
                            id)))
            .ToList();

    // --------------------------------------------------------
    // Что именно скачиваем.
    // Сначала спрашиваем режим, а уже после этого показываем
    // только те depot'ы, которые относятся к выбранному режиму.
    // --------------------------------------------------------

    string downloadMode;

    if (availableDlcs.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("ЧТО СКАЧИВАТЬ");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine("1. Только игру");
        Console.WriteLine("2. Только DLC");
        Console.WriteLine("3. Игру и DLC");
        Console.WriteLine();

        while (true)
        {
            Console.Write("Выберите вариант: ");

            string? modeInput = Console.ReadLine();

            downloadMode = modeInput switch
            {
                "1" => "game",
                "2" => "dlc",
                "3" => "both",
                _ => ""
            };

            if (!string.IsNullOrEmpty(downloadMode))
                break;

            Console.WriteLine();
            Console.WriteLine(
                "✗ Неправильный выбор. Введите 1, 2 или 3.");
            Console.WriteLine();
        }
    }
    else
    {
        downloadMode = "game";

        Console.WriteLine();
        Console.WriteLine(
            "✓ Подходящих DLC в папке для выбранной ОС не найдено.");
        Console.WriteLine(
            "  Будет скачана только игра.");
    }

    bool downloadGame =
        downloadMode == "game" ||
        downloadMode == "both";

    bool downloadDlc =
        downloadMode == "dlc" ||
        downloadMode == "both";

    // --------------------------------------------------------
    // Диагностика depot'ов основной игры.
    // Показываем её только если выбрана игра.
    // --------------------------------------------------------

    if (downloadGame)
    {
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("DEPOT'Ы ОСНОВНОЙ ИГРЫ");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine($"ОС: {FormatDepotOs(selectedOs)}");
        Console.WriteLine();

        foreach (SteamDepotInfo depot in gameLuaDepots)
        {
            if (!depotPlatforms.TryGetValue(
                    depot.DepotId,
                    out DepotPlatform platform))
            {
                Console.WriteLine(
                    $"  {depot.DepotId}: неизвестно " +
                    "[нет подходящего manifest]");
                continue;
            }

            bool allowed =
                IsDepotAllowedForSelectedOs(depot.DepotId);

            Console.WriteLine(
                $"  {depot.DepotId}: " +
                $"{FormatDepotPlatform(platform)} " +
                $"[{depotPlatformDetails[depot.DepotId]}]" +
                $" {(allowed ? "✓" : "✗")}");
        }

        Console.WriteLine();

        int compatibleGameDepots =
            gameLuaDepots.Count(depot =>
                IsDepotAllowedForSelectedOs(depot.DepotId));

        Console.WriteLine(
            $"✓ Подходящих depot основной игры: " +
            $"{compatibleGameDepots}/{gameLuaDepots.Count}");

        if (compatibleGameDepots == 0 && !downloadDlc)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Для выбранной ОС depot'ов основной игры не найдено.");

            await PauseAsync();
            return;
        }
    }

    // --------------------------------------------------------
    // Диагностика depot'ов DLC.
    // Показываем её только если выбрано DLC.
    // --------------------------------------------------------

    if (downloadDlc)
    {
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("DEPOT'Ы DLC");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine($"ОС: {FormatDepotOs(selectedOs)}");
        Console.WriteLine();

        foreach (SteamDlcInfo dlc in allDlcs)
        {
            var dlcDepotIds =
                dlc.DepotIds
                    .Append(dlc.AppId)
                    .Distinct()
                    .Where(id => allLuaDepots.ContainsKey(id))
                    .ToList();

            if (dlcDepotIds.Count == 0)
                continue;

            int compatibleCount =
                dlcDepotIds.Count(id => IsDlcDepotAllowed(dlc, id));

            Console.WriteLine(dlc.Name);

            foreach (uint depotId in dlcDepotIds)
            {
                if (!depotPlatforms.TryGetValue(
                        depotId,
                        out DepotPlatform platform))
                {
                    Console.WriteLine(
                        $"  {depotId}: неизвестно " +
                        "[нет подходящего manifest] ✗");
                    continue;
                }

                bool allowed =
                    IsDlcDepotAllowed(dlc, depotId);

                Console.WriteLine(
                    $"  {depotId}: " +
                    $"{FormatDepotPlatform(platform)} " +
                    $"[{depotPlatformDetails[depotId]}]" +
                    $" {(allowed ? "✓" : "✗")}");
            }

            Console.WriteLine(
                $"  Подходящих depot: " +
                $"{compatibleCount}/{dlcDepotIds.Count}");
            Console.WriteLine();
        }
    }

    // --------------------------------------------------------
    // Фильтруем depot'ы основной игры.
    // --------------------------------------------------------

    if (downloadGame)
    {
        depots =
            gameLuaDepots
                .Where(depot =>
                    IsDepotAllowedForSelectedOs(
                        depot.DepotId))
                .ToList();
    }
    else
    {
        depots = new List<SteamDepotInfo>();
    }

    // --------------------------------------------------------
    // Выбор DLC.
    // --------------------------------------------------------

    var selectedDlcDepotIds =
        new HashSet<uint>();

    if (downloadDlc)
    {
        if (availableDlcs.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Для выбранной ОС нет доступных DLC.");

            if (!downloadGame)
            {
                await PauseAsync();
                return;
            }
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("ДОСТУПНЫЕ DLC");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine(
                $"ОС: {FormatDepotOs(selectedOs)}");
            Console.WriteLine();

            for (int i = 0; i < availableDlcs.Count; i++)
            {
                Console.WriteLine(
                    $"{i + 1}. {availableDlcs[i].Name}");
            }

            Console.WriteLine();
            Console.WriteLine(
                "Введите номера DLC через пробел или запятую.");
            Console.WriteLine("0 — не скачивать DLC.");
            Console.WriteLine("Например: 1 3");
            Console.WriteLine();

            while (true)
            {
                Console.Write("Какие DLC скачать: ");

                string? dlcInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(dlcInput))
                {
                    Console.WriteLine(
                        "✗ Неправильный ввод DLC. Введите номера ещё раз.");
                    continue;
                }

                string normalizedInput =
                    dlcInput
                        .Replace(',', ' ')
                        .Replace(';', ' ');

                string[] tokens =
                    normalizedInput.Split(
                        new[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length == 1 && tokens[0] == "0")
                    break;

                var selectedNumbers =
                    new HashSet<int>();

                bool valid = true;

                foreach (string token in tokens)
                {
                    if (!int.TryParse(
                            token,
                            out int number) ||
                        number < 1 ||
                        number > availableDlcs.Count)
                    {
                        valid = false;
                        break;
                    }

                    selectedNumbers.Add(number);
                }

                if (!valid ||
                    selectedNumbers.Count == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "✗ Неправильный ввод DLC.");
                    Console.WriteLine(
                        "  Используйте номера из списка, " +
                        "например: 1 3");
                    Console.WriteLine();
                    continue;
                }

                foreach (int number in selectedNumbers)
                {
                    SteamDlcInfo dlc =
                        availableDlcs[number - 1];

                    foreach (uint depotId in
                             dlc.DepotIds
                                .Append(dlc.AppId)
                                .Distinct())
                    {
                        if (IsDlcDepotAllowed(
                                dlc,
                                depotId))
                        {
                            selectedDlcDepotIds.Add(
                                depotId);
                        }
                    }
                }

                Console.WriteLine();
                Console.WriteLine("✓ Выбраны DLC:");

                foreach (int number in selectedNumbers)
                {
                    Console.WriteLine(
                        $"  {number}. " +
                        $"{availableDlcs[number - 1].Name}");
                }

                break;
            }
        }
    }

    // --------------------------------------------------------
    // Объединяем depot'ы игры и выбранных DLC.
    // --------------------------------------------------------

    var downloadDepots =
        new List<SteamDepotInfo>(depots);

    foreach (uint depotId in selectedDlcDepotIds)
    {
        if (downloadDepots.Any(
                x => x.DepotId == depotId))
        {
            continue;
        }

        if (allLuaDepots.TryGetValue(
                depotId,
                out SteamDepotInfo? dlcDepot))
        {
            downloadDepots.Add(dlcDepot);
        }
    }

    Console.WriteLine();
    Console.WriteLine(
        $"✓ Depot'ов основной игры: {depots.Count}");
    Console.WriteLine(
        $"✓ Depot'ов DLC добавлено: " +
        $"{downloadDepots.Count - depots.Count}");
    Console.WriteLine();

    if (downloadDepots.Count == 0)
    {
        Console.WriteLine(
            "✗ Нечего скачивать: не выбрана ни игра, ни DLC.");

        await PauseAsync();
        return;
    }

    // --------------------------------------------------------
    // Название игры
    // --------------------------------------------------------

    Console.WriteLine("Получаем название игры...");

    string? gameName =
        await SteamAppInfo.GetGameNameAsync(appId);

    if (string.IsNullOrWhiteSpace(gameName))
    {
        Console.WriteLine();
        Console.WriteLine(
            "⚠ Не удалось автоматически определить название игры.");

        Console.Write("Введите название папки игры: ");

        gameName = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(gameName))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Название не указано.");

            await PauseAsync();
            return;
        }
    }

    foreach (char c in Path.GetInvalidFileNameChars())
    {
        gameName = gameName.Replace(c, '_');
    }

    Console.WriteLine();
    Console.WriteLine($"✓ Игра: {gameName}");

    string gameDirectory =
        Path.Combine(
            downloadDirectory!,
            gameName);

    Directory.CreateDirectory(gameDirectory);

    Console.WriteLine();
    Console.WriteLine("Папка игры:");
    Console.WriteLine($"  {gameDirectory}");

    var downloads =
        new List<(SteamDepotInfo Depot, string ManifestPath)>();

    foreach (string manifestPath in manifestPaths)
    {
        string fileName =
            Path.GetFileNameWithoutExtension(manifestPath);

        string[] parts =
            fileName.Split('_');

        if (parts.Length < 2 ||
            !uint.TryParse(
                parts[0],
                out uint depotId) ||
            !ulong.TryParse(
                parts[1],
                out ulong manifestId))
        {
            Console.WriteLine(
                $"⚠ Пропускаем: {Path.GetFileName(manifestPath)}");

            continue;
        }

        var depot =
            downloadDepots.FirstOrDefault(
                x => x.DepotId == depotId);

        if (depot == null)
        {
            Console.WriteLine(
                $"⚠ Depot {depotId} отсутствует в Lua.");

            continue;
        }

        if (depot.ManifestId != manifestId)
        {
            Console.WriteLine(
                $"⚠ ManifestID не совпадает: " +
                $"{Path.GetFileName(manifestPath)}");

            continue;
        }

        downloads.Add(
            (depot, manifestPath));
    }

    if (downloads.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine(
            "✗ Подходящих manifest не найдено.");

        await PauseAsync();
        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        $"✓ Подготовлено manifest: {downloads.Count}");

    Console.WriteLine();
    Console.WriteLine("Папка игры:");
    Console.WriteLine($"  {gameDirectory}");

    Console.WriteLine();
    Console.WriteLine(
        $"Параллельность: {parallelDownloads}");

    Console.WriteLine();

    // --------------------------------------------------------
    // CDN-серверы получаем отдельно для каждого manifest.
    // Это важно: Steam может вернуть другой подходящий CDN
    // для следующего depot.
    // --------------------------------------------------------

    Console.WriteLine("Начинаем загрузку...");
    Console.WriteLine();

    // --------------------------------------------------------
    // Общий размер
    // --------------------------------------------------------

    ulong totalExpected = 0;
    long totalFiles = 0;

    foreach (var download in downloads)
    {
        DepotManifest manifest;

        await using (
            var stream =
                File.OpenRead(
                    download.ManifestPath))
        {
            manifest =
                DepotManifest.Deserialize(stream);
        }

        foreach (var file in manifest.Files)
        {
            if (file.Chunks != null &&
                file.Chunks.Count > 0)
            {
                totalExpected +=
                    file.TotalSize;

                totalFiles++;
            }
        }
    }

    var session =
        new DownloadSession
        {
            TotalExpected = totalExpected,
            TotalFiles = totalFiles
        };

    // --------------------------------------------------------
    // Скачивание
    // --------------------------------------------------------

    Console.WriteLine("========== STEAM DEBUG ==========");
    Console.WriteLine($"[DEBUG] SteamClient.IsConnected: {steamClient.IsConnected}");
    Console.WriteLine($"[DEBUG] Steam logged on in this run: {steamLoggedOn}");
    Console.WriteLine($"[DEBUG] SteamUser handler: {steamUser != null}");
    Console.WriteLine("=================================");
    Console.WriteLine();

    if (!steamLoggedOn)
    {
        Console.WriteLine("✗ Steam-аккаунт не авторизован в текущем запуске.");
        Console.WriteLine("  Сначала откройте меню «1. Steam» → «1. Войти в Steam».");
        await PauseAsync();
        return;
    }

    int completedManifests = 0;

    bool downloadFailed = false;
    string? downloadError = null;

    for (int i = 0;
         i < downloads.Count;
         i++)
    {
        var download = downloads[i];

        try
        {
            await CdnTest.RunAsync(
                steamClient,
                download.Depot,
                download.ManifestPath,
                gameDirectory,
                session,
                i + 1,
                downloads.Count,
                parallelDownloads);

            completedManifests++;
        }
        catch (Exception ex)
        {
            downloadFailed = true;
            downloadError = ex.Message;

            Console.WriteLine($"[DEBUG] Exception type: {ex.GetType().FullName}");
            Console.WriteLine($"[DEBUG] Full exception:\n{ex}");

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("========================================");
            Console.WriteLine("✗ ОШИБКА ЗАГРУЗКИ");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine($"Manifest: {i + 1}/{downloads.Count}");
            Console.WriteLine($"Причина: {ex.Message}");
            Console.WriteLine();

            break;
        }
    }

    session.Stopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine();
    Console.WriteLine("========================================");

    if (downloadFailed)
    {
        Console.WriteLine("✗ ЗАГРУЗКА ОСТАНОВЛЕНА");
    }
    else
    {
        Console.WriteLine("✓ ЗАГРУЗКА ЗАВЕРШЕНА");
    }

    Console.WriteLine("========================================");

    Console.WriteLine(
        $"Manifest: {completedManifests}/{downloads.Count}");

    Console.WriteLine(
        $"Файлов: {session.CompletedFiles}/{session.TotalFiles}");

    Console.WriteLine(
        $"Размер: {FormatSize((ulong)Math.Max(0, session.TotalDownloaded))}");

    Console.WriteLine(
        $"Время: {FormatTime(session.Stopwatch.Elapsed)}");

    if (session.Stopwatch.Elapsed.TotalSeconds > 0)
    {
        double speed =
            session.TotalDownloaded /
            session.Stopwatch.Elapsed.TotalSeconds;

        Console.WriteLine(
            $"Средняя скорость: " +
            $"{FormatSize((ulong)Math.Max(0, speed))}/s");
    }

    if (downloadFailed)
    {
        Console.WriteLine();
        Console.WriteLine($"Причина: {downloadError}");
    }

    Console.WriteLine();
    Console.WriteLine("Папка игры:");
    Console.WriteLine(gameDirectory);

    await PauseAsync();
}


// ============================================================
// АВТОРИЗАЦИЯ
// ============================================================

async Task ConnectToSteamAsync()
{
    Console.WriteLine(
        "Подключаемся к Steam...");

    steamClient.Connect();

    while (!steamClient.IsConnected)
        await Task.Delay(100);

    Console.WriteLine(
        "✓ Подключение к Steam установлено.");

    // При каждом запуске автоматически пробуем восстановить
    // авторизацию по сохранённому refresh-токену.
    if (TokenStore.TryLoad(
        out string? savedUsername,
        out _,
        out string? savedRefreshToken))
    {
        Console.WriteLine();
        Console.WriteLine(
            $"Найдена сохранённая авторизация: {savedUsername}");
        Console.WriteLine(
            "Пробуем войти автоматически...");

        bool success =
            await LoginWithSavedTokenAsync(
                savedUsername!,
                savedRefreshToken!);

        if (success)
        {
            Console.WriteLine(
                "✓ Автоматический вход выполнен.");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine(
                "⚠ Автоматический вход не удался.");
            Console.WriteLine(
                "  Программа продолжит работу без авторизации.");
        }
    }
    else
    {
        Console.WriteLine(
            "ℹ Сохранённой авторизации нет.");
    }
}


// ============================================================
// АВТОМАТИЧЕСКИЙ ВХОД ПО СОХРАНЁННОМУ ТОКЕНУ
// ============================================================

async Task<bool> LoginWithSavedTokenAsync(
    string username,
    string refreshToken)
{
    try
    {
        steamLoggedOn = false;

        Console.WriteLine(
            $"[DEBUG] Автоматический вход для: {username}");

        // Сохранённый refresh token используется SteamKit2
        // как AccessToken для password-less входа.
        steamUser.LogOn(
    new SteamUser.LogOnDetails
    {
        Username = username,
        AccessToken = refreshToken,
        ShouldRememberPassword = true
    });

        Console.WriteLine(
            "[DEBUG] Ожидаем ответ Steam об автоматическом входе...");

        using var timeout =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(20));

        while (!timeout.IsCancellationRequested)
        {
            var callbackTask =
                steamClient.WaitForCallbackAsync();

            var completed =
                await Task.WhenAny(
                    callbackTask,
                    Task.Delay(
                        Timeout.Infinite,
                        timeout.Token));

            if (completed != callbackTask)
                break;

            var callback =
                await callbackTask;

            if (callback is SteamUser.LoggedOnCallback logOn)
            {
                if (logOn.Result == EResult.OK)
                {
                    steamLoggedOn = true;

                    Console.WriteLine(
                        "✓ Steam сообщил об успешной авторизации.");

                    return true;
                }

                Console.WriteLine(
                    $"✗ Steam отклонил сохранённый токен: {logOn.Result}");

                if (logOn.Result == EResult.InvalidPassword ||
                    logOn.Result == EResult.InvalidSignature ||
                    logOn.Result == EResult.AccessDenied ||
                    logOn.Result == EResult.Expired)
                {
                    TokenStore.Delete();

                    Console.WriteLine(
                        "⚠ Недействительный токен удалён.");
                }

                return false;
            }
        }

        Console.WriteLine(
            "✗ Steam не ответил на автоматический вход за 20 секунд.");

        return false;
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"✗ Ошибка автоматического входа: " +
            $"{ex.GetType().Name}: {ex.Message}");

        steamLoggedOn = false;
        return false;
    }
}


// ============================================================
// ВХОД В STEAM
// ============================================================

async Task<bool> LoginToSteamAsync()
{
    if (steamLoggedOn)
    {
        Console.WriteLine(
            "✓ Вы уже авторизованы в Steam.");
        return true;
    }

    if (TokenStore.TryLoad(
        out string? savedUsername,
        out _,
        out string? savedRefreshToken))
    {
        Console.WriteLine(
            $"Найдена сохранённая авторизация: {savedUsername}");

        Console.WriteLine(
            "Пробуем войти без пароля...");

        if (await LoginWithSavedTokenAsync(
            savedUsername!,
            savedRefreshToken!))
        {
            Console.WriteLine();
            Console.WriteLine(
                "✓ Вход по сохранённому токену успешен.");
            return true;
        }

        Console.WriteLine();
        Console.WriteLine(
            "Сохранённый токен не подошёл.");
    }

    Console.WriteLine();
    Console.WriteLine("=== Первичная авторизация ===");
    Console.WriteLine();

    Console.Write("Steam login: ");

    string? username =
        Console.ReadLine();

    if (string.IsNullOrWhiteSpace(username))
    {
        Console.WriteLine(
            "Логин не указан.");

        return false;
    }

    Console.Write("Steam password: ");

    string password =
        ReadPassword();

    Console.WriteLine();
    Console.WriteLine(
        "Начинаем авторизацию...");

    var auth =
        steamClient.Authentication;

    var authSession =
        await auth.BeginAuthSessionViaCredentialsAsync(
            new AuthSessionDetails
            {
                Username = username,
                Password = password,
                IsPersistentSession = true
            });

    Console.WriteLine(
        "✓ Сессия авторизации создана.");

    Console.WriteLine(
        "Ожидаем подтверждение Steam Guard...");

    var pollResult =
        await authSession.PollingWaitForResultAsync();

    if (pollResult == null)
    {
        Console.WriteLine(
            "✗ Авторизация не завершилась.");

        return false;
    }

    Console.WriteLine();
    Console.WriteLine(
        "✓ Авторизация успешна!");

    steamLoggedOn = true;

    if (!string.IsNullOrEmpty(
            pollResult.AccessToken) &&
        !string.IsNullOrEmpty(
            pollResult.RefreshToken))
    {
        TokenStore.Save(
            username,
            pollResult.AccessToken,
            pollResult.RefreshToken);

        Console.WriteLine(
            "✓ Авторизация сохранена.");
    }

    return true;
}


// ============================================================
// ВСПОМОГАТЕЛЬНОЕ
// ============================================================

async Task PauseAsync()
{
    Console.WriteLine();
    Console.WriteLine(
        "Нажмите Enter для продолжения...");

    await Task.Run(
        () => Console.ReadLine());
}

void Pause()
{
    Console.WriteLine();
    Console.WriteLine(
        "Нажмите Enter для продолжения...");

    Console.ReadLine();
}

static bool DepotOsMatches(
    string osList,
    string selectedOs)
{
    string normalized =
        osList
            .Replace(',', ' ')
            .Replace(';', ' ')
            .Replace('|', ' ')
            .ToLowerInvariant();

    string[] values =
        normalized.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);

    return values.Any(value =>
        value == selectedOs ||
        (selectedOs == "macos" &&
         (value == "macosx" || value == "mac")) ||
        (selectedOs == "windows" &&
         value == "win") ||
        (selectedOs == "linux" &&
         value == "linux64"));
}

static string FormatDepotPlatform(DepotPlatform platform)
{
    if (platform == DepotPlatform.Unknown)
        return "неизвестно";

    if (platform == DepotPlatform.Shared)
        return "общий";

    var names = new List<string>();

    if ((platform & DepotPlatform.Windows) != 0)
        names.Add("Windows");

    if ((platform & DepotPlatform.Linux) != 0)
        names.Add("Linux");

    if ((platform & DepotPlatform.MacOS) != 0)
        names.Add("macOS");

    return names.Count == 0
        ? "неизвестно"
        : string.Join(", ", names);
}

static string FormatDepotOs(string osList)
{
    if (string.IsNullOrWhiteSpace(osList) ||
        osList.Equals(
            "all",
            StringComparison.OrdinalIgnoreCase))
    {
        return "общий";
    }

    return osList;
}

static string ReadPassword()
{
    var password =
        new System.Text.StringBuilder();

    while (true)
    {
        var key =
            Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Enter)
            break;

        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
                password.Length--;

            continue;
        }

        if (!char.IsControl(key.KeyChar))
        {
            password.Append(
                key.KeyChar);

            Console.Write("*");
        }
    }

    Console.WriteLine();

    return password.ToString();
}


static string FormatSize(ulong bytes)
{
    const double KB = 1024;
    const double MB = KB * 1024;
    const double GB = MB * 1024;

    if (bytes >= GB)
        return $"{bytes / GB:F2} GB";

    if (bytes >= MB)
        return $"{bytes / MB:F2} MB";

    if (bytes >= KB)
        return $"{bytes / KB:F2} KB";

    return $"{bytes} B";
}


static string FormatTime(TimeSpan time)
{
    if (time.TotalHours >= 1)
        return time.ToString(@"hh\:mm\:ss");

    return time.ToString(@"mm\:ss");
}