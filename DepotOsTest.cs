using SteamKit2;

public static class DepotOsTest
{
    public static async Task RunAsync(
        SteamClient steamClient,
        uint appId)
    {
        Console.WriteLine();
        Console.WriteLine("========================================");
        Console.WriteLine("PICS DEPOT OS TEST");
        Console.WriteLine("========================================");
        Console.WriteLine();

        Console.WriteLine($"AppID: {appId}");
        Console.WriteLine("Запрашиваем App Info...");

        try
        {
            var depotOs =
                await SteamDepotOsResolver.GetDepotOsAsync(
                    steamClient,
                    appId);

            Console.WriteLine("✓ Ответ получен.");
            Console.WriteLine();

            if (depotOs.Count == 0)
            {
                Console.WriteLine(
                    "⚠ Steam не сообщил ни одного depot.");
            }
            else
            {
                Console.WriteLine(
                    $"Получено depot: {depotOs.Count}");
                Console.WriteLine();

                foreach (var item in depotOs.OrderBy(x => x.Key))
                {
                    string os =
                        string.IsNullOrWhiteSpace(item.Value)
                            ? "ALL / общий"
                            : item.Value;

                    Console.WriteLine(
                        $"Depot: {item.Key}");

                    Console.WriteLine(
                        $"  OS: {os}");

                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"✗ Ошибка: {ex.GetType().Name}");

            Console.WriteLine(
                ex.Message);
        }

        Console.WriteLine();
        Console.WriteLine("Нажмите Enter...");
        Console.ReadLine();
    }
}