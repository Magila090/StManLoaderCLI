using System;
using System.IO;
using System.Text;
using System.Reflection;

public static class GameRepair
{
    private const string ModulesFileName = "modules.txt";
    private const string SteamAppIdFileName = "steam_appid.txt";

    private const string SteamCompatDllName = "SteamCompat.dll";
    private const string WinmmDllName = "winmm.dll";

    private const string ModulesFileContent = "SteamCompat.dll";
    private const string SteamAppIdFileContent = "480";

    public static void RepairMenu()
    {
        while (true)
        {
            Console.Clear();

            Console.WriteLine("╔════════════════════════════════════════════╗");
            Console.WriteLine("║              Исправления игр               ║");
            Console.WriteLine("╚════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("1. Исправление_№1, рекомендуется");
            Console.WriteLine();

            Console.WriteLine("2. Исправление_№2, иногда помогает, но редко");
            Console.WriteLine();

            Console.WriteLine("3. Исправление_№3 (FreeTP)");
            Console.WriteLine();

            Console.WriteLine("4. Удалить все исправления");
            Console.WriteLine();

            Console.WriteLine("0. Назад");
            Console.WriteLine();

            Console.Write("Выберите фикс: ");
            string? choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    InstallSteamCompatFix();
                    break;

                case "2":
                    InstallSteamAppIdFix();
                    break;

                case "3":
                    InstallSteamEpicFix();
                    break;

                case "4":
                    RemoveAllFixes();
                    break;

                case "0":
                    return;

                default:
                    Console.WriteLine();
                    Console.WriteLine("✗ Неправильный выбор.");
                    Pause();
                    break;
            }
        }
    }

    private static void InstallSteamCompatFix()
    {
        Console.Clear();

        Console.WriteLine(
            "╔════════════════════════════════════════════╗");
        Console.WriteLine(
            "║          Установка исправления_№1          ║");
        Console.WriteLine(
            "╚════════════════════════════════════════════╝");

        Console.WriteLine();

        string? gameDirectory =
            AskGameDirectory();

        if (gameDirectory == null)
            return;

        string fixDirectory =
            ResolveFixDirectory(
                gameDirectory);

        Console.WriteLine();
        Console.WriteLine(
            "Папка установки фикса:");

        Console.WriteLine(
            fixDirectory);

        try
        {
            Console.WriteLine();
            Console.WriteLine("Устанавливаем фикс...");
            Console.WriteLine();

            string steamCompatPath =
                Path.Combine(
                    fixDirectory,
                    "SteamCompat.dll");

            string winmmPath =
                Path.Combine(
                    fixDirectory,
                    "winmm.dll");

            string modulesPath =
                Path.Combine(
                    fixDirectory,
                    "modules.txt");

            // ============================
            // SteamCompat.dll
            // ============================

            ExtractEmbeddedFile(
                "GameFixes.SteamCompat.dll",
                steamCompatPath);

            Console.WriteLine(
                "✓ Установлен SteamCompat.dll");

            // ============================
            // winmm.dll
            // ============================

            ExtractEmbeddedFile(
                "GameFixes.winmm.dll",
                winmmPath);

            Console.WriteLine(
                "✓ Установлен winmm.dll");

            // ============================
            // modules.txt
            // ============================

            if (File.Exists(modulesPath))
            {
                File.Delete(modulesPath);
            }

            File.WriteAllText(
                modulesPath,
                "SteamCompat.dll",
                new UTF8Encoding(false));

            Console.WriteLine(
                "✓ Создан modules.txt");

            // ============================
            // appid_config.txt
            // ============================

            GetOrCreateRealAppId(
                fixDirectory);

            Console.WriteLine();
            Console.WriteLine(
                "✓ Фикс успешно установлен.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Не удалось установить фикс:");

            Console.WriteLine(
                ex.Message);
        }

        Pause();
    }

    private static void InstallSteamAppIdFix()
    {
        Console.Clear();

        Console.WriteLine(
            "╔════════════════════════════════════════════╗");
        Console.WriteLine(
            "║          Установка исправления_№2          ║");
        Console.WriteLine(
            "╚════════════════════════════════════════════╝");

        Console.WriteLine();

        string? gameDirectory =
            AskGameDirectory();

        if (gameDirectory == null)
            return;

        string fixDirectory =
            ResolveFixDirectory(
                gameDirectory);

        Console.WriteLine();
        Console.WriteLine(
            "Папка установки фикса:");

        Console.WriteLine(
            fixDirectory);

        try
        {
            string steamAppIdPath =
                Path.Combine(
                    fixDirectory,
                    "steam_appid.txt");

            if (File.Exists(steamAppIdPath))
            {
                File.Delete(steamAppIdPath);
            }

            File.WriteAllText(
                steamAppIdPath,
                "480",
                new UTF8Encoding(false));

            Console.WriteLine();
            Console.WriteLine(
                "✓ Создан steam_appid.txt");

            Console.WriteLine(
                "✓ AppID: 480");

            Console.WriteLine();
            Console.WriteLine(
                "✓ Фикс успешно установлен.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Не удалось установить фикс:");

            Console.WriteLine(
                ex.Message);
        }

        Pause();
    }

    private static void InstallSteamEpicFix()
    {
        Console.Clear();

        Console.WriteLine(
            "╔════════════════════════════════════════════╗");
        Console.WriteLine(
            "║          Установка исправления_№3          ║");
        Console.WriteLine(
            "╚════════════════════════════════════════════╝");

        Console.WriteLine();

        string? gameDirectory =
            AskGameDirectory();

        if (gameDirectory == null)
            return;

        string fixDirectory =
            ResolveFixDirectory(
                gameDirectory);

        Console.WriteLine();
        Console.WriteLine(
            "Папка установки фикса:");

        Console.WriteLine(
            fixDirectory);

        try
        {
            Console.WriteLine();
            Console.WriteLine(
                "Устанавливаем фикс...");
            Console.WriteLine();

            // ----------------------------------------------------
            // Получаем настоящий AppID.
            // Если appid_config.txt отсутствует —
            // функция сама спросит AppID и создаст его.
            // ----------------------------------------------------

            uint realAppId =
                GetOrCreateRealAppId(
                    fixDirectory);

            // ----------------------------------------------------
            // Пути
            // ----------------------------------------------------

            string steamFixDllPath =
                Path.Combine(
                    fixDirectory,
                    "SteamFix64.dll");

            string epicFixDllPath =
                Path.Combine(
                    fixDirectory,
                    "EpicFix64.dll");

            string winmmDllPath =
                Path.Combine(
                    fixDirectory,
                    "winmm.dll");

            string winmmTxtPath =
                Path.Combine(
                    fixDirectory,
                    "winmm.txt");

            string steamFixIniPath =
                Path.Combine(
                    fixDirectory,
                    "SteamFix.ini");

            string epicFixIniPath =
                Path.Combine(
                    fixDirectory,
                    "EpicFix.ini");

            // ----------------------------------------------------
            // DLL
            // ----------------------------------------------------

            ExtractEmbeddedFile(
                "GameFixes.Fix3.SteamFix64.dll",
                steamFixDllPath);

            Console.WriteLine(
                "✓ Установлен SteamFix64.dll");

            ExtractEmbeddedFile(
                "GameFixes.Fix3.EpicFix64.dll",
                epicFixDllPath);

            Console.WriteLine(
                "✓ Установлен EpicFix64.dll");

            ExtractEmbeddedFile(
                "GameFixes.Fix3.winmm.dll",
                winmmDllPath);

            Console.WriteLine(
                "✓ Установлен winmm.dll");

            // ----------------------------------------------------
            // winmm.txt
            // ----------------------------------------------------

            string winmmContent =
                "SteamFix64.dll\r\n" +
                "EpicFix64.dll";

            WriteTextWithoutBom(
                winmmTxtPath,
                winmmContent);

            Console.WriteLine(
                "✓ Создан winmm.txt");

            // ----------------------------------------------------
            // EpicFix.ini
            // ----------------------------------------------------

            string epicFixContent =
                "[EOS]\r\n" +
                "NoAuth=true";

            WriteTextWithoutBom(
                epicFixIniPath,
                epicFixContent);

            Console.WriteLine(
                "✓ Создан EpicFix.ini");

            // ----------------------------------------------------
            // SteamFix.ini
            // ----------------------------------------------------

            string steamFixContent =
                "[Main]\r\n" +
                $"RealAppId={realAppId}\r\n" +
                "FakeAppId=480\r\n" +
                "#Language=english\r\n" +
                "BuildId=0\r\n" +
                "\r\n" +
                "[Misc]\r\n" +
                "Overlay=true\r\n" +
                "FilterLobby=false\r\n" +
                "\r\n" +
                "[Interfaces]\r\n" +
                "Apps=true\r\n" +
                "Friends=true\r\n" +
                "User=true\r\n" +
                "Inventory=true\r\n" +
                "Stats=true\r\n" +
                "Storage=true\r\n" +
                "Utils=true\r\n" +
                "Workshop=false";

            WriteTextWithoutBom(
                steamFixIniPath,
                steamFixContent);

            Console.WriteLine(
                "✓ Создан SteamFix.ini");

            Console.WriteLine(
                $"✓ RealAppId: {realAppId}");

            Console.WriteLine();
            Console.WriteLine(
                "✓ Исправление №3 успешно установлено.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Не удалось установить исправление:");

            Console.WriteLine(
                ex.Message);
        }

        Pause();
    }

    private static void RemoveAllFixes()
    {
        Console.Clear();

        Console.WriteLine(
            "╔════════════════════════════════════════════╗");
        Console.WriteLine(
            "║         Удаление всех исправлений          ║");
        Console.WriteLine(
            "╚════════════════════════════════════════════╝");

        Console.WriteLine();

        string? gameDirectory =
            AskGameDirectory();

        if (gameDirectory == null)
            return;

        string fixDirectory =
            ResolveFixDirectory(
                gameDirectory);

        Console.WriteLine();
        Console.WriteLine(
            "Папка исправлений:");

        Console.WriteLine(
            fixDirectory);

        Console.WriteLine();

        string[] filesToDelete =
        {
            // Исправление №1
            "SteamCompat.dll",
            "modules.txt",

            // Исправление №2
            "steam_appid.txt",

            // Исправление №3
            "SteamFix64.dll",
            "EpicFix64.dll",
            "winmm.txt",
            "SteamFix.ini",
            "EpicFix.ini",

            // Общий файл исправлений №1 / №3
            "winmm.dll"
        };

        int deletedCount = 0;

        try
        {
            foreach (string fileName in filesToDelete)
            {
                string filePath =
                    Path.Combine(
                        fixDirectory,
                        fileName);

                if (!File.Exists(filePath))
                {
                    Console.WriteLine(
                        $"— {fileName} не найден");

                    continue;
                }

                File.Delete(filePath);

                Console.WriteLine(
                    $"✓ Удалён {fileName}");

                deletedCount++;
            }

            Console.WriteLine();

            if (deletedCount == 0)
            {
                Console.WriteLine(
                    "✓ Исправления не найдены.");
            }
            else
            {
                Console.WriteLine(
                    $"✓ Удалено файлов: {deletedCount}");

                Console.WriteLine(
                    "✓ Все исправления удалены.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✗ Не удалось удалить исправления:");

            Console.WriteLine(
                ex.Message);
        }

        Pause();
    }

    private static uint GetOrCreateRealAppId(
        string fixDirectory)
    {
        string appIdConfigPath =
            Path.Combine(
                fixDirectory,
                "appid_config.txt");

        // --------------------------------------------------------
        // Сначала пытаемся прочитать существующий appid_config.txt
        // --------------------------------------------------------

        if (File.Exists(appIdConfigPath))
        {
            try
            {
                foreach (string line
                        in File.ReadAllLines(appIdConfigPath))
                {
                    string trimmed =
                        line.Trim();

                    if (!trimmed.StartsWith(
                            "realid=",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string value =
                        trimmed[
                            "realid=".Length..]
                        .Trim();

                    if (uint.TryParse(
                            value,
                            out uint existingAppId) &&
                        existingAppId != 0)
                    {
                        Console.WriteLine(
                            $"✓ AppID получен из appid_config.txt: {existingAppId}");

                        return existingAppId;
                    }
                }

                Console.WriteLine();
                Console.WriteLine(
                    "⚠ В appid_config.txt не найден корректный realid.");
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"⚠ Не удалось прочитать appid_config.txt: {ex.Message}");
            }
        }

        // --------------------------------------------------------
        // Файла нет или realid неправильный — спрашиваем AppID.
        // --------------------------------------------------------

        while (true)
        {
            Console.WriteLine();
            Console.Write(
                "Введите AppID игры: ");

            string? input =
                Console.ReadLine();

            if (!uint.TryParse(
                    input,
                    out uint appId) ||
                appId == 0)
            {
                Console.WriteLine(
                    "✗ Неправильный AppID.");

                continue;
            }

            string content =
                $"realid={appId}\r\n" +
                "fakeid=480";

            WriteTextWithoutBom(
                appIdConfigPath,
                content);

            Console.WriteLine();
            Console.WriteLine(
                "✓ Создан appid_config.txt");

            Console.WriteLine(
                $"  Real AppID: {appId}");

            Console.WriteLine(
                "  Fake AppID: 480");

            return appId;
        }
    }

    private static string? AskGameDirectory()
    {
        Console.Write("Введите путь к папке игры: ");

        string? input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Путь не указан.");
            Pause();
            return null;
        }

        string gameDirectory =
            input.Trim().Trim('"');

        try
        {
            gameDirectory =
                Path.GetFullPath(gameDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("✗ Некорректный путь:");
            Console.WriteLine(ex.Message);
            Pause();
            return null;
        }

        if (!Directory.Exists(gameDirectory))
        {
            Console.WriteLine();
            Console.WriteLine("✗ Папка игры не найдена:");
            Console.WriteLine(gameDirectory);
            Pause();
            return null;
        }

        Console.WriteLine();
        Console.WriteLine("✓ Папка игры найдена:");
        Console.WriteLine(gameDirectory);

        return gameDirectory;
    }

    public static string ResolveFixDirectory(
        string gameDirectory)
    {
        // Ищем только папки первого уровня.
        foreach (string subDirectory
                in Directory.GetDirectories(gameDirectory))
        {
            string binariesPath =
                Path.Combine(
                    subDirectory,
                    "Binaries");

            string contentPath =
                Path.Combine(
                    subDirectory,
                    "Content");

            string win64Path =
                Path.Combine(
                    binariesPath,
                    "Win64");

            // Unreal-подобная структура:
            //
            // <Game>\
            //   Binaries\
            //     Win64\
            //   Content\
            //
            if (!Directory.Exists(binariesPath) ||
                !Directory.Exists(contentPath) ||
                !Directory.Exists(win64Path))
            {
                continue;
            }

            // Проверяем, что в Win64 действительно есть exe.
            bool hasExe =
                Directory
                    .EnumerateFiles(
                        win64Path,
                        "*.exe",
                        SearchOption.TopDirectoryOnly)
                    .Any();

            if (!hasExe)
                continue;

            return win64Path;
        }

        // Специальная структура не найдена —
        // используем папку, которую указал пользователь.
        return gameDirectory;
    }

    private static void ExtractEmbeddedFile(
        string resourceName,
        string destinationPath)
    {
        Assembly assembly =
            Assembly.GetExecutingAssembly();

        using Stream? resourceStream =
            assembly.GetManifestResourceStream(
                resourceName);

        if (resourceStream == null)
        {
            throw new Exception(
                $"Встроенный ресурс не найден: {resourceName}");
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        using FileStream output =
            new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write);

        resourceStream.CopyTo(output);
    }

    private static void WriteTextWithoutBom(
        string filePath,
        string content)
    {
        File.WriteAllText(
            filePath,
            content,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine(
            "Нажмите Enter для продолжения...");

        Console.ReadLine();
    }
}
