using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;
using System.Text;
using QRCoder;

var steamClient = new SteamClient();
var steamUser = steamClient.GetHandler<SteamUser>();

// Фактическое состояние авторизации Steam в текущем запуске.
bool steamLoggedOn = false;

string? currentSteamUsername = null;

Console.OutputEncoding = System.Text.Encoding.UTF8;

AppConfig config = ConfigManager.Load();

const string ryuuAuthKey = "???"; // Вставьте свой ключ

string? downloadDirectory =
    config.DownloadDirectory;

string? ownManifestDirectory =
    config.OwnManifestDirectory;

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
        Console.WriteLine("║              StManLoaderCLI                ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine(
        $"Steam:          {(steamLoggedOn
            ? $"✓ Авторизован ({currentSteamUsername ?? "аккаунт"})"
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
        Console.WriteLine("6. Папка манифестов");
        Console.WriteLine("7. Исправить игру");
        Console.WriteLine("8. Собственные манифесты");
        Console.WriteLine("0. Выход");
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
                ManifestFolderMenu();
                break;
            
            case "7":
                GameRepair.RepairMenu();
                break;

            case "8":
                await OwnManifestMenuAsync();
                break;

            case "0":
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

async Task ResetSteamSessionAsync()
{
    try
    {
        if (steamLoggedOn)
        {
            steamUser.LogOff();
        }
    }
    catch
    {
    }

    steamLoggedOn = false;
    currentSteamUsername = null;

    if (steamClient.IsConnected)
    {
        steamClient.Disconnect();

        while (steamClient.IsConnected)
            await Task.Delay(100);
    }

    steamClient.Connect();

    while (!steamClient.IsConnected)
        await Task.Delay(100);
}


string? SelectSteamAccount(
    string title)
{
    var accounts =
        TokenStore.GetAccounts();

    if (accounts.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine(
            "✗ Сохранённых аккаунтов нет.");

        return null;
    }

    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine();

    for (int i = 0; i < accounts.Count; i++)
    {
        var account = accounts[i];

        string marker =
            account.IsActive
                ? " ← выбран"
                : "";

        Console.WriteLine(
            $"{i + 1}. {account.Username}{marker}");
    }

    Console.WriteLine();
    Console.WriteLine("0. Назад");
    Console.WriteLine();

    while (true)
    {
        Console.Write("Выберите аккаунт: ");

        string? input =
            Console.ReadLine();

        if (input == "0")
            return null;

        if (int.TryParse(
                input,
                out int number) &&
            number >= 1 &&
            number <= accounts.Count)
        {
            return accounts[number - 1].Username;
        }

        Console.WriteLine(
            "✗ Неправильный выбор.");
    }
}

async Task SwitchSteamAccountAsync()
{
    Console.Clear();

    string? username =
        SelectSteamAccount(
            "=== Переключение аккаунта ===");

    if (username == null)
        return;

    if (steamLoggedOn &&
        username.Equals(
            currentSteamUsername,
            StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        Console.WriteLine(
            "✓ Этот аккаунт уже используется.");

        await PauseAsync();
        return;
    }

    if (!TokenStore.TryLoad(
            username,
            out string? storedUsername,
            out _,
            out string? refreshToken))
    {
        Console.WriteLine();
        Console.WriteLine(
            "✗ Не удалось прочитать данные аккаунта.");

        await PauseAsync();
        return;
    }

    string? oldAccount =
        currentSteamUsername;

    await ResetSteamSessionAsync();

    TokenStore.SetActive(username);

    Console.WriteLine();
    Console.WriteLine(
        $"Входим как {storedUsername}...");

    bool success =
        await LoginWithSavedTokenAsync(
            storedUsername!,
            refreshToken!);

    if (success)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✓ Активный аккаунт: {storedUsername}");

        await PauseAsync();
        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        "✗ Переключиться не удалось.");

    // Если возможно — возвращаем предыдущий аккаунт.
    if (!string.IsNullOrWhiteSpace(oldAccount) &&
        TokenStore.TryLoad(
            oldAccount,
            out string? oldUsername,
            out _,
            out string? oldRefreshToken))
    {
        Console.WriteLine();
        Console.WriteLine(
            $"Возвращаем аккаунт {oldUsername}...");

        await ResetSteamSessionAsync();

        TokenStore.SetActive(oldUsername!);

        await LoginWithSavedTokenAsync(
            oldUsername!,
            oldRefreshToken!);
    }

    await PauseAsync();
}


async Task AddSteamAccountAsync()
{
    string? previousAccount =
        currentSteamUsername;

    await ResetSteamSessionAsync();

    bool success =
        await LoginToSteamAsync();

    if (success)
        return;

    // Пользователь отменил или произошла ошибка.
    // Возвращаем прежний аккаунт.
    if (!string.IsNullOrWhiteSpace(previousAccount) &&
        TokenStore.TryLoad(
            previousAccount,
            out string? username,
            out _,
            out string? refreshToken))
    {
        await ResetSteamSessionAsync();

        TokenStore.SetActive(username!);

        await LoginWithSavedTokenAsync(
            username!,
            refreshToken!);
    }
}

async Task DeleteSteamAccountAsync()
{
    Console.Clear();

    string? username =
        SelectSteamAccount(
            "=== Удаление аккаунта ===");

    if (username == null)
        return;

    Console.WriteLine();
    Console.WriteLine(
        $"Будут удалены данные аккаунта: {username}");

    Console.WriteLine();
    Console.Write(
        "Удалить? (y/n): ");

    string? answer =
        Console.ReadLine();

    if (!answer?.Equals(
            "y",
            StringComparison.OrdinalIgnoreCase) == true)
    {
        Console.WriteLine();
        Console.WriteLine("Отмена.");
        await PauseAsync();
        return;
    }

    bool deletingCurrent =
        steamLoggedOn &&
        username.Equals(
            currentSteamUsername,
            StringComparison.OrdinalIgnoreCase);

    if (deletingCurrent)
    {
        try
        {
            steamUser.LogOff();
        }
        catch
        {
        }

        steamLoggedOn = false;
        currentSteamUsername = null;
    }

    TokenStore.Delete(username);

    Console.WriteLine();
    Console.WriteLine(
        $"✓ Данные аккаунта {username} удалены.");

    // Если удалили текущий аккаунт —
    // автоматически подключаем новый выбранный,
    // если он остался.
    if (deletingCurrent &&
        TokenStore.TryLoad(
            out string? nextUsername,
            out _,
            out string? nextRefreshToken))
    {
        Console.WriteLine();
        Console.WriteLine(
            $"Подключаем следующий аккаунт: {nextUsername}");

        await ResetSteamSessionAsync();

        await LoginWithSavedTokenAsync(
            nextUsername!,
            nextRefreshToken!);
    }

    await PauseAsync();
}

async Task SteamMenuAsync()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine(
            "╔════════════════════════════════════════════╗");
        Console.WriteLine(
            "║                   Steam                    ║");
        Console.WriteLine(
            "╚════════════════════════════════════════════╝");
        Console.WriteLine();

        var accounts =
            TokenStore.GetAccounts();

        if (steamLoggedOn)
        {
            Console.WriteLine(
                "Статус: ✓ Авторизован");

            Console.WriteLine(
                $"Аккаунт: {currentSteamUsername ?? "аккаунт"}");
        }
        else
        {
            Console.WriteLine(
                "Статус: ✗ Не авторизован");
        }

        Console.WriteLine(
            $"Сохранённых аккаунтов: {accounts.Count}");

        Console.WriteLine();

        Console.WriteLine(
            "1. Добавить аккаунт");

        Console.WriteLine(
            "2. Переключить аккаунт");

        Console.WriteLine(
            "3. Удалить аккаунт");

        Console.WriteLine(
            "0. Назад");

        Console.WriteLine();

        Console.Write(
            "Выберите действие: ");

        string? choice =
            Console.ReadLine();

        switch (choice)
        {
            case "1":
                await AddSteamAccountAsync();
                break;

            case "2":
                await SwitchSteamAccountAsync();
                break;

            case "3":
                await DeleteSteamAccountAsync();
                break;

            case "0":
                return;

            default:
                Console.WriteLine();
                Console.WriteLine(
                    "✗ Неизвестный пункт.");

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
        Console.WriteLine("0. Назад");
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

            case "0":
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
        Console.WriteLine("0. Назад");
        Console.WriteLine();

        Console.Write("Выберите ОС: ");

        string? choice = Console.ReadLine();

        string? newOs = choice switch
        {
            "1" => "windows",
            "2" => "macos",
            "3" => "linux",
            "4" => "all",
            "0" => null,
            _ => ""
        };

        if (choice == "0")
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
// ПАПКА МАНИФЕСТОВ
// ============================================================

void ManifestFolderMenu()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine("╔════════════════════════════════════════════╗");
        Console.WriteLine("║             Папка манифестов               ║");
        Console.WriteLine("╚════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine(
            $"После успешного скачивания: {(config.DeleteManifestsAfterDownload ? "удалять" : "оставлять")}");

        if (!config.DeleteManifestsAfterDownload)
        {
            Console.WriteLine(
                $"Папка для хранения: {config.ManifestKeepDirectory ?? "не задана"}");
        }

        Console.WriteLine();
        Console.WriteLine("1. Удалять папку после скачивания");
        Console.WriteLine("2. Оставлять папку после скачивания");
        Console.WriteLine("3. Изменить папку хранения");
        Console.WriteLine("0. Назад");
        Console.WriteLine();
        Console.Write("Выберите действие: ");

        string? choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                config.DeleteManifestsAfterDownload = true;
                ConfigManager.Save(config);

                Console.WriteLine();
                Console.WriteLine("✓ Папка manifest будет удаляться после успешного скачивания.");
                Pause();
                break;

            case "2":
                if (string.IsNullOrWhiteSpace(config.ManifestKeepDirectory))
                {
                    Console.WriteLine();
                    Console.Write("Введите папку, куда сохранять manifest: ");
                    string? path = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        Console.WriteLine();
                        Console.WriteLine("✗ Папка не указана.");
                        Pause();
                        break;
                    }

                    try
                    {
                        path = Path.GetFullPath(path.Trim());
                        Directory.CreateDirectory(path);
                        config.ManifestKeepDirectory = path;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"✗ Не удалось создать папку: {ex.Message}");
                        Pause();
                        break;
                    }
                }

                config.DeleteManifestsAfterDownload = false;
                ConfigManager.Save(config);

                Console.WriteLine();
                Console.WriteLine("✓ Папка manifest будет сохраняться после успешного скачивания.");
                Console.WriteLine($"  Папка: {config.ManifestKeepDirectory}");
                Pause();
                break;

            case "3":
                Console.WriteLine();
                Console.WriteLine(
                    $"Текущая папка: {config.ManifestKeepDirectory ?? "не задана"}");
                Console.Write("Новая папка или Enter для отмены: ");

                string? newPath = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(newPath))
                    break;

                try
                {
                    newPath = Path.GetFullPath(newPath.Trim());
                    Directory.CreateDirectory(newPath);
                    config.ManifestKeepDirectory = newPath;
                    ConfigManager.Save(config);

                    Console.WriteLine();
                    Console.WriteLine("✓ Папка хранения сохранена:");
                    Console.WriteLine(newPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"✗ Не удалось создать папку: {ex.Message}");
                }

                Pause();
                break;

            case "0":
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
// СОБСТВЕННЫЕ MANIFEST
// ============================================================

async Task OwnManifestMenuAsync()
{
    while (true)
    {
        Console.Clear();

        Console.WriteLine(
            "╔════════════════════════════════════════════╗");
        Console.WriteLine(
            "║           Собственные манифесты            ║");
        Console.WriteLine(
            "╚════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.WriteLine(
            "Папка сохранения:");

        Console.WriteLine(
            $"  {(string.IsNullOrWhiteSpace(ownManifestDirectory)
                ? "не задана"
                : ownManifestDirectory)}");

        Console.WriteLine();

        Console.WriteLine(
            $"Аккаунт: {(steamLoggedOn
                ? currentSteamUsername ?? "аккаунт"
                : "не авторизован")}");

        Console.WriteLine();
        Console.WriteLine("1. Изменить папку сохранения");

        if (string.IsNullOrWhiteSpace(
                ownManifestDirectory))
        {
            Console.WriteLine(
                "2. Скачать манифесты [недоступно — укажите папку]");
        }
        else
        {
            Console.WriteLine(
                "2. Скачать манифесты");
        }

        Console.WriteLine("0. Назад");
        Console.WriteLine();

        Console.Write(
            "Выберите действие: ");

        string? choice =
            Console.ReadLine();

        switch (choice)
        {
            case "1":
                OwnManifestDirectoryMenu();
                break;

            case "2":
                await DownloadOwnManifestsAsync();
                break;

            case "0":
                return;

            default:
                Console.WriteLine();
                Console.WriteLine(
                    "✗ Неизвестный пункт.");

                await PauseAsync();
                break;
        }
    }
}

void OwnManifestDirectoryMenu()
{
    Console.Clear();

    Console.WriteLine(
        "=== Папка собственных манифестов ===");
    Console.WriteLine();

    Console.WriteLine(
        $"Текущая папка: {ownManifestDirectory ?? "не задана"}");

    Console.WriteLine();
    Console.Write(
        "Введите новую папку или Enter для отмены: ");

    string? input =
        Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        return;

    try
    {
        string path =
            Path.GetFullPath(
                input.Trim().Trim('"'));

        Directory.CreateDirectory(path);

        ownManifestDirectory =
            path;

        config.OwnManifestDirectory =
            path;

        ConfigManager.Save(config);

        Console.WriteLine();
        Console.WriteLine(
            "✓ Папка сохранена:");

        Console.WriteLine(
            path);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✗ Не удалось использовать папку: {ex.Message}");
    }

    Pause();
}

async Task DownloadOwnManifestsAsync()
{
    Console.Clear();

    Console.WriteLine(
        "╔════════════════════════════════════════════╗");
    Console.WriteLine(
        "║        Скачать собственные manifest       ║");
    Console.WriteLine(
        "╚════════════════════════════════════════════╝");
    Console.WriteLine();

    // --------------------------------------------------------
    // Папка
    // --------------------------------------------------------

    if (string.IsNullOrWhiteSpace(
            ownManifestDirectory))
    {
        Console.WriteLine(
            "✗ Сначала укажите папку сохранения.");

        await PauseAsync();
        return;
    }

    // --------------------------------------------------------
    // Steam
    // --------------------------------------------------------

    if (!steamLoggedOn ||
        string.IsNullOrWhiteSpace(
            currentSteamUsername))
    {
        Console.WriteLine(
            "✗ Steam-аккаунт не авторизован.");

        Console.WriteLine();
        Console.WriteLine(
            "Сначала войдите в Steam или выберите сохранённый аккаунт.");

        await PauseAsync();
        return;
    }

    Console.WriteLine(
        $"Аккаунт: {currentSteamUsername}");

    Console.WriteLine(
        $"Папка:   {ownManifestDirectory}");

    Console.WriteLine();

    // --------------------------------------------------------
    // AppID
    // --------------------------------------------------------

    Console.Write(
        "Введите AppID игры: ");

    string? input =
        Console.ReadLine();

    if (!uint.TryParse(
            input,
            out uint appId) ||
        appId == 0)
    {
        Console.WriteLine();
        Console.WriteLine(
            "✗ Неверный AppID.");

        await PauseAsync();
        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        $"Получаем manifest для AppID {appId}...");

    Console.WriteLine(
        $"От имени аккаунта: {currentSteamUsername}");

    try
    {
        string resultDirectory =
            await OwnManifestDownloader.DownloadAsync(
                steamClient,
                appId,
                ownManifestDirectory,
                currentSteamUsername);

        Console.WriteLine();
        Console.WriteLine(
            "========================================");

        Console.WriteLine(
            "✓ СКАЧИВАНИЕ ЗАВЕРШЕНО");

        Console.WriteLine(
            "========================================");

        Console.WriteLine();

        Console.WriteLine(
            "Manifest сохранены:");

        Console.WriteLine(
            resultDirectory);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            "✗ Не удалось скачать manifest:");

        Console.WriteLine(
            $"{ex.GetType().Name}: {ex.Message}");
    }

    await PauseAsync();
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

    Console.Write("Путь к папке или AppID: ");

string? sourceInput = Console.ReadLine();

if (string.IsNullOrWhiteSpace(sourceInput))
{
    Console.WriteLine();
    Console.WriteLine("✗ Путь или AppID не указан.");
    await PauseAsync();
    return;
}

sourceInput = sourceInput.Trim();

string sourceDirectory;
bool temporaryManifestDirectory = false;

// --------------------------------------------------------
// AppID или путь?
// --------------------------------------------------------

if (uint.TryParse(sourceInput, out uint requestedAppId))
{
    // Пользователь ввёл AppID.
    // Создаём временную папку рядом с каталогом загрузок.
    sourceDirectory =
        Path.Combine(
            downloadDirectory!,
            $"Manifest_{requestedAppId}");

    temporaryManifestDirectory = true;

    Console.WriteLine();
    Console.WriteLine($"✓ Обнаружен AppID: {requestedAppId}");
    Console.WriteLine(
        $"Временная папка manifest: {sourceDirectory}");

    try
    {
        // На всякий случай удаляем старую папку
        // с таким же AppID перед новой загрузкой.
        if (Directory.Exists(sourceDirectory))
        {
            Directory.Delete(
                sourceDirectory,
                recursive: true);
        }

        Directory.CreateDirectory(sourceDirectory);

        var ryuu =
            new RyuuApiClient(ryuuAuthKey);

        await ryuu.DownloadManifestPackAsync(
            requestedAppId,
            sourceDirectory);
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✗ Не удалось получить manifest-пакет: {ex.Message}");

        // Если это наша временная папка,
        // удаляем её даже при ошибке получения.
        try
        {
            if (Directory.Exists(sourceDirectory))
            {
                Directory.Delete(
                    sourceDirectory,
                    recursive: true);
            }
        }
        catch
        {
            // Здесь специально ничего не выводим,
            // чтобы не скрывать первоначальную ошибку.
        }

        await PauseAsync();
        return;
    }
}
else
{
    // Пользователь ввёл обычный путь.
    sourceDirectory =
        Path.GetFullPath(sourceInput);

    if (!Directory.Exists(sourceDirectory))
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✗ Папка не найдена: {sourceDirectory}");

        await PauseAsync();
        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        $"✓ Используем существующую папку:");
    Console.WriteLine($"  {sourceDirectory}");
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
    // depotId -> локальный manifest.
// ManifestID берём непосредственно из имени файла.
// Это важно, потому что Lua может содержать устаревший ManifestID.
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

    // Lua может содержать устаревший ManifestID.
    // Сам файл считаем источником истины, потому что его имя
    // содержит фактический ManifestID этого локального manifest.
    if (luaDepot.ManifestId != manifestId)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"⚠ ManifestID не совпадает для depot {depotId}:");
        Console.WriteLine(
            $"  Lua:  {luaDepot.ManifestId}");
        Console.WriteLine(
            $"  Файл: {manifestId}");
        Console.WriteLine(
            $"  → Используем ManifestID из файла.");
    }
    else
    {
        Console.WriteLine(
            $"✓ Manifest совпадает: " +
            $"{depotId} → {manifestId}");
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
    // Полный список depot + public manifest основной игры из Steam.
    // Этот список используется только для проверки полноты локальных
    // manifest'ов. Lua остаётся источником depot key для скачивания.
    // --------------------------------------------------------

    List<SteamExpectedDepotInfo> steamGameDepots = new();

    try
    {
        steamGameDepots =
            await SteamDepotCatalogResolver.GetDepotsAsync(
                steamClient,
                appId);

        Console.WriteLine(
            $"✓ Steam сообщил depot с public manifest: {steamGameDepots.Count}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"⚠ Не удалось получить полный список manifest основной игры: {ex.Message}");
        Console.WriteLine(
            "  Проверка полноты основной игры будет пропущена.");
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

    bool SteamDepotMatchesSelectedOs(
        SteamExpectedDepotInfo depot)
    {
        if (selectedOs == "all")
            return true;

        return string.IsNullOrWhiteSpace(depot.OsList) ||
               DepotOsMatches(depot.OsList, selectedOs);
    }

    List<SteamExpectedDepotInfo> GetRequiredGameDepots()
    {
        var dlcDepotIds =
            allDlcs
                .SelectMany(dlc =>
                    dlc.DepotIds
                        .Append(dlc.AppId))
                .ToHashSet();

        return steamGameDepots
            .Where(depot => !dlcDepotIds.Contains(depot.DepotId))
            .Where(SteamDepotMatchesSelectedOs)
            .ToList();
    }

    (List<SteamExpectedDepotInfo> Expected, List<SteamExpectedDepotInfo> Missing)
        CheckGameManifestCompleteness()
    {
        var expected = GetRequiredGameDepots();

        var missing = expected
    .Where(expectedDepot =>
        !localManifestPaths.ContainsKey(
            expectedDepot.DepotId))
    .ToList();

        return (expected, missing);
    }

    (List<SteamExpectedDepotInfo> Expected, List<SteamExpectedDepotInfo> Missing)
        CheckDlcManifestCompleteness(SteamDlcInfo dlc)
    {
        var expected = new List<SteamExpectedDepotInfo>();

        foreach (uint depotId in dlc.DepotIds.Distinct())
        {
            if (!dlc.DepotManifests.TryGetValue(
                    depotId,
                    out ulong manifestId) ||
                manifestId == 0)
            {
                // AppID DLC может быть специальным Lua-only depot.
                // Если Steam не сообщил ему собственный manifest,
                // не считаем его обязательным Steam depot.
                continue;
            }

            string osList =
                dlc.DepotOs.TryGetValue(
                    depotId,
                    out string? value)
                    ? value
                    : "";

            if (!SteamDepotMatchesSelectedOs(
                    new SteamExpectedDepotInfo
                    {
                        AppId = dlc.AppId,
                        DepotId = depotId,
                        PublicManifestId = manifestId,
                        OsList = osList
                    }))
            {
                continue;
            }

            expected.Add(
                new SteamExpectedDepotInfo
                {
                    AppId = dlc.AppId,
                    DepotId = depotId,
                    PublicManifestId = manifestId,
                    OsList = osList
                });
        }

        // Специальный случай: Lua может содержать manifest под AppID DLC,
        // хотя Steam App Info перечисляет реальные OS depot'ы отдельно.
        // Такой manifest считается дополнительным содержимым DLC, но не
        // заменяет отсутствующий OS-specific depot при проверке полноты.

        var missing = expected
    .Where(expectedDepot =>
        !localManifestPaths.ContainsKey(
            expectedDepot.DepotId))
    .ToList();

        return (expected, missing);
    }

    bool ConfirmIncompleteInstall(
        string title,
        int present,
        int total)
    {
        Console.WriteLine();
        Console.WriteLine("⚠ Недостаточно manifest для полной установки.");
        Console.WriteLine($"{title}");
        Console.WriteLine($"Найдено: {present}/{total}");
        Console.WriteLine();
        Console.WriteLine("Установить всё равно из доступных manifest?");
        Console.WriteLine("1. Да");
        Console.WriteLine("2. Нет");
        Console.WriteLine();

        while (true)
        {
            Console.Write("Выберите: ");
            string? input = Console.ReadLine();

            if (input == "1")
                return true;

            if (input == "2")
                return false;

            Console.WriteLine("✗ Неправильный выбор. Введите 1 или 2.");
        }
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
    // Записи Lua без ManifestID не являются готовыми к скачиванию
    // depot'ами. Они остаются в общем списке для проверки, но
    // не участвуют в обычном выборе и загрузке.
    var luaDepotsWithoutManifest =
        depots
            .Where(x => x.ManifestId == 0)
            .ToList();

    if (luaDepotsWithoutManifest.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine(
            "⚠ В Lua найдены записи depot без ManifestID:");

        foreach (var missingManifestDepot in luaDepotsWithoutManifest)
        {
            Console.WriteLine(
                $"  Depot {missingManifestDepot.DepotId}: ManifestID отсутствует");
        }

        Console.WriteLine(
            "  Такие записи не считаются готовыми manifest и будут проверены через Steam.");
    }

    var gameLuaDepots =
        depots
            .Where(x =>
                x.ManifestId != 0 &&
                !knownDlcDepotIds.Contains(x.DepotId))
            .ToList();

    // Если Steam вообще не вернул DLC — считаем весь Lua
    // принадлежащим основной игре.
    if (allDlcs.Count == 0)
    {
        gameLuaDepots =
            depots
                .Where(x => x.ManifestId != 0)
                .ToList();
    }

    // --------------------------------------------------------
    // Показываем диагностику depot'ов.
    // --------------------------------------------------------

    Console.WriteLine();
    Console.WriteLine("========================================");
    Console.WriteLine("ОПРЕДЕЛЕНИЕ ПЛАТФОРМ DEPOT'ОВ");
    Console.WriteLine("========================================");
    Console.WriteLine();

    foreach (SteamDepotInfo depot in depots)
    {
        if (!depotPlatforms.TryGetValue(
                depot.DepotId,
                out DepotPlatform platform))
        {
            Console.WriteLine(
                $"{depot.DepotId}: неизвестно " +
                "[нет подходящего manifest]");
            continue;
        }

        Console.WriteLine(
            $"{depot.DepotId}: " +
            $"{FormatDepotPlatform(platform)} " +
            $"[{depotPlatformDetails[depot.DepotId]}]");
    }

    // --------------------------------------------------------
    // DLC показываем только если:
    //
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

        // В Lua обязательно должен быть актуальный manifest.
        if (!localManifestPaths.ContainsKey(depotId))
            return false;

        // Для "Все ОС" оставляем любой depot,
        // который реально присутствует в Lua.
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
    // Фильтруем depot'ы основной игры.
    // --------------------------------------------------------

    Dictionary<uint, string> selectedGameSteamOs =
        depotOs;

    if (downloadGame)
    {
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("ВЫБРАННАЯ ОПЕРАЦИОННАЯ СИСТЕМА");
        Console.WriteLine("========================================");
        Console.WriteLine();
        Console.WriteLine(
            $"✓ ОС: {FormatDepotOs(selectedOs)}");
        Console.WriteLine();

        Console.WriteLine("Depot'ы основной игры:");

        foreach (SteamDepotInfo depot in gameLuaDepots)
        {
            if (depotPlatforms.TryGetValue(
                    depot.DepotId,
                    out DepotPlatform platform))
            {
                Console.WriteLine(
                    $"  {depot.DepotId}: " +
                    $"{FormatDepotPlatform(platform)}" +
                    $" [{depotPlatformDetails[depot.DepotId]}]");
            }
            else
            {
                Console.WriteLine(
                    $"  {depot.DepotId}: неизвестно");
            }
        }

        Console.WriteLine();

        depots =
            gameLuaDepots
                .Where(depot =>
                    IsDepotAllowedForSelectedOs(
                        depot.DepotId))
                .ToList();

        Console.WriteLine(
            $"✓ Подходящих depot основной игры для " +
            $"{FormatDepotOs(selectedOs)}: " +
            $"{depots.Count}/{gameLuaDepots.Count}");

        if (steamGameDepots.Count > 0)
        {
            var gameCompleteness =
                CheckGameManifestCompleteness();

            int present =
                gameCompleteness.Expected.Count -
                gameCompleteness.Missing.Count;

            Console.WriteLine();
            Console.WriteLine(
                $"Manifest основной игры: {present}/{gameCompleteness.Expected.Count}");

            if (gameCompleteness.Missing.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("⚠ Не хватает manifest основной игры:");

                foreach (var missing in gameCompleteness.Missing)
                {
                    Console.WriteLine(
                        $"  Depot {missing.DepotId}: " +
                        $"нужен manifest {missing.PublicManifestId}");
                }

                bool installAnyway =
                    ConfirmIncompleteInstall(
                        "Основная игра",
                        present,
                        gameCompleteness.Expected.Count);

                if (!installAnyway)
                {
                    Console.WriteLine();
                    Console.WriteLine(
                        "✗ Установка основной игры отменена.");

                    if (!downloadDlc)
                    {
                        await PauseAsync();
                        return;
                    }

                    depots = new List<SteamDepotInfo>();
                    downloadGame = false;
                }
            }
        }

        if (depots.Count == 0 && !downloadDlc)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Для выбранной ОС depot'ов основной игры не найдено.");

            await PauseAsync();
            return;
        }
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
            Console.WriteLine("all — скачать все DLC.");
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

var selectedNumbers =
    new HashSet<int>();

if (dlcInput.Trim().Equals(
        "all",
        StringComparison.OrdinalIgnoreCase))
{
    foreach (int number in
             Enumerable.Range(1, availableDlcs.Count))
    {
        selectedNumbers.Add(number);
    }
}
else
{
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
            "  Используйте номера из списка, например: 1 3");
        Console.WriteLine();
        continue;
    }
}

                var approvedDlcNumbers =
                    new HashSet<int>();

                bool approveAllIncomplete = false;

                foreach (int number in selectedNumbers)
                {
                    SteamDlcInfo dlc =
                        availableDlcs[number - 1];

                    var completeness =
                        CheckDlcManifestCompleteness(dlc);

                    int present =
                        completeness.Expected.Count -
                        completeness.Missing.Count;

                    if (completeness.Expected.Count == 0)
                    {
                        // Нет отдельных public manifest в App Info DLC.
                        // Если есть подходящий локальный AppID-manifest,
                        // оставляем существующую специальную логику DLC.
                        bool hasLocalDlcManifest =
                            localManifestPaths.ContainsKey(dlc.AppId);

                        if (hasLocalDlcManifest)
                        {
                            approvedDlcNumbers.Add(number);
                        }

                        continue;
                    }

                    if (completeness.Missing.Count == 0)
                    {
                        approvedDlcNumbers.Add(number);
                        continue;
                    }

                    if (approveAllIncomplete)
                    {
                        approvedDlcNumbers.Add(number);
                        continue;
                    }

                    Console.WriteLine();
                    Console.WriteLine(
                        $"⚠ DLC \"{dlc.Name}\" неполный: " +
                        $"manifest {present}/{completeness.Expected.Count}.");

                    Console.WriteLine("Недостающие manifest:");
                    foreach (var missing in completeness.Missing)
                    {
                        Console.WriteLine(
                            $"  Depot {missing.DepotId}: " +
                            $"нужен {missing.PublicManifestId}");
                    }

                    Console.WriteLine();
                    Console.WriteLine("1. Да, скачать доступную часть");
                    Console.WriteLine("2. Нет, пропустить этот DLC");
                    Console.WriteLine("3. Да для всех оставшихся неполных DLC");
                    Console.WriteLine();

                    while (true)
                    {
                        Console.Write("Выберите: ");
                        string? incompleteChoice = Console.ReadLine();

                        if (incompleteChoice == "1")
                        {
                            approvedDlcNumbers.Add(number);
                            break;
                        }

                        if (incompleteChoice == "2")
                        {
                            break;
                        }

                        if (incompleteChoice == "3")
                        {
                            approveAllIncomplete = true;
                            approvedDlcNumbers.Add(number);
                            break;
                        }

                        Console.WriteLine(
                            "✗ Неправильный выбор. Введите 1, 2 или 3.");
                    }
                }

                foreach (int number in approvedDlcNumbers)
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

                if (approvedDlcNumbers.Count == 0)
                {
                    Console.WriteLine("  Нет DLC для скачивания.");
                }
                else
                {
                    foreach (int number in approvedDlcNumbers)
                    {
                        Console.WriteLine(
                            $"  {number}. " +
                            $"{availableDlcs[number - 1].Name}");
                    }
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
        $"Проверено файлов: {session.VerifiedFiles}/{session.TotalFiles}");

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


    // --------------------------------------------------------
    // Обработка временной папки manifest после успешного скачивания.
    // Папку, указанную пользователем вручную, не трогаем.
    // --------------------------------------------------------

    if (temporaryManifestDirectory)
    {
        if (downloadFailed)
        {
            Console.WriteLine();
            Console.WriteLine(
                "⚠ Папка manifest оставлена, потому что загрузка завершилась с ошибкой.");
        }
        else if (config.DeleteManifestsAfterDownload)
        {
            try
            {
                if (Directory.Exists(sourceDirectory))
                {
                    Directory.Delete(
                        sourceDirectory,
                        recursive: true);

                    Console.WriteLine();
                    Console.WriteLine(
                        "✓ Временная папка manifest удалена.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"⚠ Не удалось удалить временную папку manifest: {ex.Message}");
            }
        }
        else
        {
            try
            {
                string? keepDirectory =
                    config.ManifestKeepDirectory;

                if (string.IsNullOrWhiteSpace(keepDirectory))
                    throw new InvalidOperationException(
                        "Не задана папка хранения manifest.");

                keepDirectory = Path.GetFullPath(keepDirectory);
                Directory.CreateDirectory(keepDirectory);

                string baseName = $"Manifest_{appId}";
                string targetDirectory =
                    GetUniqueManifestDirectory(
                        keepDirectory,
                        baseName);

                // Если папка хранения совпала с родительской папкой
                // временного manifest-каталога, targetDirectory будет
                // отличаться суффиксом и перенос останется безопасным.
                Directory.Move(
                    sourceDirectory,
                    targetDirectory);

                Console.WriteLine();
                Console.WriteLine(
                    "✓ Папка manifest сохранена:");
                Console.WriteLine(targetDirectory);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"⚠ Не удалось перенести папку manifest: {ex.Message}");
                Console.WriteLine(
                    $"  Исходная папка оставлена: {sourceDirectory}");
            }
        }
    }

    await PauseAsync();
}


static string GetUniqueManifestDirectory(
    string parentDirectory,
    string baseName)
{
    string candidate =
        Path.Combine(parentDirectory, baseName);

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
                    currentSteamUsername = username;

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

async Task<bool> LoginViaQrAsync()
{

    while (true)
    {
        try
        {
            Console.Clear();

            Console.WriteLine("========================================");
            Console.WriteLine("          ВХОД В STEAM ПО QR");
            Console.WriteLine("========================================");
            Console.WriteLine();
            Console.WriteLine("Создаём QR-код...");
            Console.WriteLine();

            if (!steamClient.IsConnected)
            {
                Console.WriteLine("Подключаемся к Steam...");

                steamClient.Connect();

                while (!steamClient.IsConnected)
                    await Task.Delay(100);

                Console.WriteLine("✓ Подключение к Steam установлено.");
                Console.WriteLine();
            }

            var auth = steamClient.Authentication;

            var authSession =
                await auth.BeginAuthSessionViaQRAsync(
                    new AuthSessionDetails
                    {
                        IsPersistentSession = true
                    });

            Console.Clear();

            Console.WriteLine("========================================");
            Console.WriteLine("          ВХОД В STEAM ПО QR");
            Console.WriteLine("========================================");
            Console.WriteLine();   

            using (var qrGenerator = new QRCodeGenerator())
            {
                QRCodeData qrCodeData =
                    qrGenerator.CreateQrCode(
                        authSession.ChallengeURL,
                        QRCodeGenerator.ECCLevel.Q);

                var matrix = qrCodeData.ModuleMatrix;

                for (int y = 0; y < matrix.Count; y += 2)
                {
                    for (int x = 0; x < matrix[y].Count; x++)
                    {
                        bool top = matrix[y][x];

                        bool bottom =
                            y + 1 < matrix.Count &&
                            matrix[y + 1][x];

                        if (top && bottom)
                            Console.Write("█");
                        else if (top)
                            Console.Write("▀");
                        else if (bottom)
                            Console.Write("▄");
                        else
                            Console.Write(" ");
                    }

                    Console.WriteLine();
                }
            }

            Console.WriteLine();
            Console.WriteLine("Отсканируйте QR-код через приложение Steam.");
            Console.WriteLine();
            Console.WriteLine("Ожидаем подтверждение...");

            var pollResult =
                await authSession.PollingWaitForResultAsync();

            if (pollResult == null)
            {
                Console.WriteLine();
                Console.WriteLine("✗ Авторизация не завершилась.");
                await PauseAsync();
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("✓ QR-код подтверждён!");
            Console.WriteLine("✓ Авторизация успешна!");

            if (string.IsNullOrEmpty(pollResult.AccessToken) ||
                string.IsNullOrEmpty(pollResult.RefreshToken))
            {
                Console.WriteLine();
                Console.WriteLine(
                    "✗ Steam не вернул необходимые токены.");

                await PauseAsync();
                return false;
            }

            TokenStore.Save(
                pollResult.AccountName,
                pollResult.AccessToken,
                pollResult.RefreshToken);

            Console.WriteLine("✓ Авторизация сохранена.");

            bool connected =
                await InitializeSteamAccountAsync(
                    pollResult.AccountName,
                    pollResult.RefreshToken);

            if (!connected)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "⚠ Авторизация успешна, но подключение к аккаунту не установлено.");

                await PauseAsync();
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("✓ Подключение к аккаунту установлено.");

            //await PauseAsync();

            return true;
        }
        catch (SteamKit2.AsyncJobFailedException)
        {
            Console.WriteLine();
            Console.WriteLine("⚠ QR-код истёк или авторизация была прервана.");
            Console.WriteLine();
            Console.WriteLine("Создаём новый QR-код...");

            await Task.Delay(1500);

            // Здесь ничего не возвращаем.
            // while (true) автоматически начнёт новый цикл.
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("✗ Произошла ошибка при авторизации:");
            Console.WriteLine();
            Console.WriteLine(ex.Message);

            await PauseAsync();

            return false;
        }
    }
}

async Task<bool> LoginWithCredentialsAsync()
{
    Console.WriteLine("========================================");
    Console.WriteLine("         ВХОД В STEAM ПО ЛОГИНУ");
    Console.WriteLine("========================================");
    Console.WriteLine();

    Console.Write("Steam login: ");

    string? username = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(username))
    {
        Console.WriteLine();
        Console.WriteLine("✗ Логин не указан.");
        await PauseAsync();
        return false;
    }

    Console.Write("Steam password: ");

    string password = ReadPassword();

    Console.WriteLine();
    Console.WriteLine("Начинаем авторизацию...");
    Console.WriteLine();

    if (!steamClient.IsConnected)
    {
        Console.WriteLine("Подключаемся к Steam...");

        steamClient.Connect();

        while (!steamClient.IsConnected)
            await Task.Delay(100);

        Console.WriteLine("✓ Подключение к Steam установлено.");
        Console.WriteLine();
    }

    var auth = steamClient.Authentication;

    try
    {
        var authSession =
            await auth.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = true
                });

        Console.WriteLine("✓ Сессия авторизации создана.");
        Console.WriteLine();
        Console.WriteLine("Ожидаем подтверждение Steam Guard...");

        var pollResult =
            await authSession.PollingWaitForResultAsync();

        if (pollResult == null)
        {
            Console.WriteLine();
            Console.WriteLine("✗ Авторизация не завершилась.");
            await PauseAsync();
            return false;
        }

        Console.WriteLine();
        Console.WriteLine("✓ Авторизация успешна!");

        if (!string.IsNullOrEmpty(pollResult.AccessToken) &&
            !string.IsNullOrEmpty(pollResult.RefreshToken))
        {
            TokenStore.Save(
                pollResult.AccountName,
                pollResult.AccessToken,
                pollResult.RefreshToken);

            Console.WriteLine("✓ Авторизация сохранена.");
        }

        // Сразу после авторизации устанавливаем
        // полноценную Steam-сессию.
        bool connected =
            await InitializeSteamAccountAsync(
                pollResult.AccountName,
                pollResult.RefreshToken);

        if (!connected)
        {
            Console.WriteLine();
            Console.WriteLine(
                "⚠ Авторизация прошла, но подключение к аккаунту не удалось.");

            await PauseAsync();
            return false;
        }

        Console.WriteLine();
        Console.WriteLine("✓ Подключение к Steam-аккаунту установлено.");

        //await PauseAsync();

        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine("✗ Ошибка авторизации:");
        Console.WriteLine();
        Console.WriteLine(ex.Message);

        await PauseAsync();

        return false;
    }
}

async Task<bool> LoginToSteamAsync()
{
    if (steamLoggedOn)
    {
        Console.WriteLine();
        Console.WriteLine("✓ Вы уже авторизованы в Steam.");
        return true;
    }

    while (true)
    {
        Console.Clear();

        Console.WriteLine("========================================");
        Console.WriteLine("              ВХОД В STEAM");
        Console.WriteLine("========================================");
        Console.WriteLine();

        Console.WriteLine("1. Войти по QR-коду");
        Console.WriteLine("2. Войти по логину и паролю");
        Console.WriteLine("0. Назад");
        Console.WriteLine();

        Console.Write("Выберите способ входа: ");

        string? choice = Console.ReadLine();

        switch (choice)
        {
            case "1":
                Console.Clear();

                if (await LoginViaQrAsync())
                    return true;

                // Если QR не удался — возвращаемся
                // к выбору способа входа.
                break;

            case "2":
                Console.Clear();

                if (await LoginWithCredentialsAsync())
                    return true;

                break;

            case "0":
                return false;

            default:
                Console.WriteLine();
                Console.WriteLine("✗ Неправильный выбор.");
                await PauseAsync();
                break;
        }
    }
}

async Task<bool> InitializeSteamAccountAsync(
    string username,
    string refreshToken)
{
    try
    {
        steamLoggedOn = false;

        Console.WriteLine();
        Console.WriteLine("Подключаем аккаунт к Steam...");

        if (!steamClient.IsConnected)
        {
            Console.WriteLine("Подключаемся к Steam...");

            steamClient.Connect();

            while (!steamClient.IsConnected)
                await Task.Delay(100);
        }

        steamUser.LogOn(
            new SteamUser.LogOnDetails
            {
                Username = username,
                AccessToken = refreshToken,
                ShouldRememberPassword = true
            });

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
                    currentSteamUsername = username;

                    Console.WriteLine();
                    Console.WriteLine(
                        "✓ Steam сообщил об успешном подключении.");

                    return true;
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"✗ Steam отклонил подключение: {logOn.Result}");

                return false;
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            "✗ Steam не подтвердил подключение за 20 секунд.");

        return false;
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"✗ Ошибка подключения аккаунта: {ex.Message}");

        steamLoggedOn = false;

        return false;
    }
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