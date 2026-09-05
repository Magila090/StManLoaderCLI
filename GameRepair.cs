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
            Console.WriteLine("║                  ФИКСЫ ИГР                 ║");
            Console.WriteLine("╚════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("1. Починка_№1, рекомендуется");
            Console.WriteLine();

            Console.WriteLine("2. Починка_№2, иногда помогает, но редко");
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

        Console.WriteLine("========================================");
        Console.WriteLine("          УСТАНОВКА ПОЧИНКА_№1");
        Console.WriteLine("========================================");
        Console.WriteLine();

        string? gameDirectory =
            AskGameDirectory();

        if (gameDirectory == null)
            return;

        try
        {
            Console.WriteLine();
            Console.WriteLine("Устанавливаем фикс...");
            Console.WriteLine();

            string steamCompatPath =
                Path.Combine(
                    gameDirectory,
                    "SteamCompat.dll");

            string winmmPath =
                Path.Combine(
                    gameDirectory,
                    "winmm.dll");

            string modulesPath =
                Path.Combine(
                    gameDirectory,
                    "modules.txt");

            string appIdConfigPath =
            Path.Combine(
                gameDirectory,
                "appid_config.txt");

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

            if (File.Exists(appIdConfigPath))
            {
                Console.WriteLine(
                    "✓ appid_config.txt уже существует, не изменяем.");
            }
            else
            {
                Console.WriteLine();
                Console.Write(
                    "Введите AppID игры: ");

                string? appIdInput =
                    Console.ReadLine();

                if (!uint.TryParse(
                        appIdInput,
                        out uint realAppId) ||
                    realAppId == 0)
                {
                    throw new Exception(
                        "Указан неправильный AppID.");
                }

                string appIdConfigContent =
                    $"realid={realAppId}\n" +
                    "fakeid=480";

                File.WriteAllText(
                    appIdConfigPath,
                    appIdConfigContent,
                    new UTF8Encoding(false));

                Console.WriteLine();
                Console.WriteLine(
                    "✓ Создан appid_config.txt");

                Console.WriteLine(
                    $"  Real AppID: {realAppId}");

                Console.WriteLine(
                    "  Fake AppID: 480");
            }

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

        Console.WriteLine("========================================");
        Console.WriteLine("          УСТАНОВКА ПОЧИНКА_№2");
        Console.WriteLine("========================================");
        Console.WriteLine();

        string? gameDirectory =
            AskGameDirectory();

        if (gameDirectory == null)
            return;

        try
        {
            string steamAppIdPath =
                Path.Combine(
                    gameDirectory,
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
