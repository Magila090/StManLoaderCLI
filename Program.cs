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
    // Определяем ОС depot'ов через Steam PICS.
    // --------------------------------------------------------

    Console.WriteLine();
    Console.WriteLine("Получаем информацию об ОС depot'ов из Steam...");

    Dictionary<uint, string> depotOs;

    try
    {
        depotOs =
            await SteamDepotOsResolver.GetDepotOsAsync(
                steamClient,
                appId);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✗ Не удалось получить информацию об ОС: {ex.Message}");

        await PauseAsync();
        return;
    }

    Console.WriteLine(
        $"✓ Получена информация об ОС для {depotOs.Count} depot.");

    // --------------------------------------------------------
    // ОС берём из настройки главного меню.
    // Никаких дополнительных вопросов при скачивании.
    // --------------------------------------------------------

    Console.WriteLine();
    Console.WriteLine("========================================");
    Console.WriteLine("ВЫБРАННАЯ ОПЕРАЦИОННАЯ СИСТЕМА");
    Console.WriteLine("========================================");
    Console.WriteLine();
    Console.WriteLine($"✓ ОС: {FormatDepotOs(selectedOs)}");
    Console.WriteLine();

    Console.WriteLine();
    Console.WriteLine("Depot'ы из Steam:");

    foreach (var depot in depots)
    {
        string os =
            depotOs.TryGetValue(
                depot.DepotId,
                out string? value)
                ? value
                : "all";

        Console.WriteLine(
            $"  {depot.DepotId}: {FormatDepotOs(os)}");
    }

    Console.WriteLine();

    // Depot без oslist являются общими и подходят для любой ОС.
    if (selectedOs != "all")
    {
        var filteredDepots =
            depots
                .Where(depot =>
                {
                    if (!depotOs.TryGetValue(
                            depot.DepotId,
                            out string? osList) ||
                        string.IsNullOrWhiteSpace(osList) ||
                        osList.Equals(
                            "all",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    return DepotOsMatches(
                        osList,
                        selectedOs);
                })
                .ToList();

        Console.WriteLine(
            $"✓ Подходящих depot для {FormatDepotOs(selectedOs)}: " +
            $"{filteredDepots.Count}/{depots.Count}");

        if (filteredDepots.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Для выбранной ОС depot'ов не найдено.");

            await PauseAsync();
            return;
        }

        depots = filteredDepots;
    }

    Console.WriteLine();
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

    // --------------------------------------------------------
    // Manifest
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
            depots.FirstOrDefault(
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