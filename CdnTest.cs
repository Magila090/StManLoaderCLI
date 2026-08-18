using SteamKit2;
using SteamKit2.CDN;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;

public sealed class DownloadSession
{
    public long TotalFiles { get; init; }
    public ulong TotalExpected { get; init; }
    public long TotalDownloaded;
    public long CompletedFiles;
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
}

public static class CdnTest
{

    private sealed class PreparedFile
    {
        public required DepotManifest.FileData File;
        public required string OutputPath;
        public required string TempPath;
        public required FileStream Stream;

        public long Downloaded;
        public int CompletedChunks;
    }

    private sealed class ChunkWork
    {
        public required PreparedFile PreparedFile;
        public required DepotManifest.ChunkData Chunk;
    }

    public static async Task RunAsync(
        SteamClient steamClient,
        SteamDepotInfo depot,
        string manifestPath,
        string outputDirectory,
        DownloadSession session,
        int manifestNumber,
        int manifestCount,
        int parallelDownloads)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"=== MANIFEST {manifestNumber}/{manifestCount} | DEPOT {depot.DepotId} ===");
        Console.WriteLine($"ManifestID: {depot.ManifestId}");
        Console.WriteLine();

        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine("Читаем manifest...");

        DepotManifest manifest;

        await using (var stream = File.OpenRead(manifestPath))
        {
            manifest = DepotManifest.Deserialize(stream);
        }

        Console.WriteLine($"✓ Файлов в manifest: {manifest.Files.Count}");

        var steamContent = steamClient.GetHandler<SteamContent>();

        var cdnServers =
            await steamContent.GetServersForSteamPipe(
                null,
                10);

        if (cdnServers.Count == 0)
        {
            throw new IOException(
                "Список CDN-серверов пуст.");
        }

        var serverList =
    cdnServers
        .Where(x => !string.IsNullOrWhiteSpace(x.Host))
        .ToList();

        if (serverList.Count == 0)
        {
            throw new IOException(
                "Список CDN-серверов пуст.");
        }

        var cdnServer =
            serverList.FirstOrDefault(
                x => !string.IsNullOrEmpty(x.Host));

        if (cdnServer == null)
        {
            throw new IOException(
                "Не найден подходящий CDN-сервер.");
        }

        Console.WriteLine(
            $"✓ CDN: {cdnServer.Host}:{cdnServer.Port}");

        byte[] depotKeyBytes = Convert.FromHexString(depot.DepotKey);

        // В некоторых manifest'ах Steam имена файлов зашифрованы.
        // До использования FileName как пути их обязательно нужно расшифровать.
        if (manifest.FilenamesEncrypted)
        {
            Console.WriteLine("Расшифровываем имена файлов manifest...");
            manifest.DecryptFilenames(depotKeyBytes);
            Console.WriteLine("✓ Имена файлов расшифрованы.");
            Console.WriteLine();
        }

        using var cdnClient = new Client(steamClient);

        var authTokens =
            new ConcurrentDictionary<string, string?>();

        int currentCdnIndex = 0;

        var cdnLock = new SemaphoreSlim(1, 1);

        async Task<string?> GetCdnTokenAsync(Server server)
        {
            if (string.IsNullOrWhiteSpace(server.Host))
                return null;

            string host = server.Host;

            if (authTokens.TryGetValue(host, out var cachedToken))
                return cachedToken;

            var auth =
                await steamContent.GetCDNAuthToken(
                    depot.AppId,
                    depot.DepotId,
                    host);

            string? token = auth?.Token;

            authTokens.TryAdd(host, token);

            return token;
        }

        async Task<(Server Server, string? Token)> GetCurrentCdnAsync()
        {
            await cdnLock.WaitAsync();

            try
            {
                for (int i = 0; i < serverList.Count; i++)
                {
                    int index =
                        (currentCdnIndex + i) % serverList.Count;

                    var server = serverList[index];

                    if (string.IsNullOrWhiteSpace(server.Host))
                        continue;

                    try
                    {
                        var token =
                            await GetCdnTokenAsync(server);

                        currentCdnIndex = index;

                        return (server, token);
                    }
                    catch
                    {
                        // Этот сервер не смог получить auth.
                        // Пробуем следующий.
                    }
                }

                throw new IOException(
                    "Не удалось получить CDN auth ни для одного сервера.");
            }
            finally
            {
                cdnLock.Release();
            }
        }

        // CDN auth token для каждого сервера.
        // Токен получаем только тогда, когда реально понадобился сервер.

        var files = manifest.Files
            .Where(x => x.Chunks != null && x.Chunks.Count > 0)
            .ToList();

        Console.WriteLine(
            $"Параллельных chunks одновременно: {parallelDownloads}");
        Console.WriteLine();

        // ------------------------------------------------------------
        // 1. Сначала проверяем существующие файлы и готовим остальные.
        // ------------------------------------------------------------

        var preparedFiles = new List<PreparedFile>();
        var allChunks = new ConcurrentQueue<ChunkWork>();

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];

            string relativePath = file.FileName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            string outputPath = Path.Combine(outputDirectory, relativePath);

            string? directory = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            long currentFileNumber = i + 1;

            if (File.Exists(outputPath))
            {
                bool valid =
                    await ValidateExistingFileAsync(outputPath, file);

                if (valid)
                {
                    Interlocked.Add(
                        ref session.TotalDownloaded,
                        checked((long)file.TotalSize));

                    Interlocked.Increment(ref session.CompletedFiles);

                    PrintSkipped(
                        manifestNumber,
                        manifestCount,
                        currentFileNumber,
                        session.TotalFiles,
                        file.FileName,
                        session);

                    continue;
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"⚠ Файл отличается от manifest, перекачиваем: {file.FileName}");
            }

            string tempPath = outputPath + ".downloading";

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            output.SetLength(checked((long)file.TotalSize));

            var prepared = new PreparedFile
            {
                File = file,
                OutputPath = outputPath,
                TempPath = tempPath,
                Stream = output
            };

            preparedFiles.Add(prepared);

            foreach (var chunk in file.Chunks)
            {
                allChunks.Enqueue(new ChunkWork
                {
                    PreparedFile = prepared,
                    Chunk = chunk
                });
            }
        }

        if (allChunks.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine("✓ Все файлы этого manifest уже проверены.");
            return;
        }

        Console.WriteLine(
            $"Chunks в общей очереди: {allChunks.Count}");
        Console.WriteLine(
            $"Файлов нужно скачать: {preparedFiles.Count}");
        Console.WriteLine();

        // ------------------------------------------------------------
        // 2. Все chunks всех файлов одного manifest попадают
        //    в ОБЩУЮ очередь.
        //
        //    Поэтому файл с одним chunk не заставляет остальные
        //    соединения простаивать.
        // ------------------------------------------------------------

        using var cancellation = new CancellationTokenSource();

        Exception? workerException = null;

        async Task WorkerAsync()
        {
            while (!cancellation.IsCancellationRequested &&
                   allChunks.TryDequeue(out var work))
            {
                try
                {
                    var prepared = work.PreparedFile;
                    var chunk = work.Chunk;

                    byte[] destination = new byte[
                        checked((int)chunk.UncompressedLength)];

                    Exception? lastException = null;

                    int maxAttempts = Math.Max(1, serverList.Count);

                    for (int attempt = 0;
                         attempt < maxAttempts;
                         attempt++)
                    {
                        if (cancellation.IsCancellationRequested)
                            return;

                        try
                        {
                            var cdn =
                                await GetCurrentCdnAsync();

                            int written =
                                await cdnClient.DownloadDepotChunkAsync(
                                    depot.DepotId,
                                    chunk,
                                    cdn.Server,
                                    destination,
                                    depotKeyBytes,
                                    null,
                                    cdn.Token);

                            await RandomAccess.WriteAsync(
                                prepared.Stream.SafeFileHandle,
                                destination.AsMemory(0, written),
                                (long)chunk.Offset);

                            Interlocked.Add(
                                ref prepared.Downloaded,
                                written);

                            Interlocked.Add(
                                ref session.TotalDownloaded,
                                written);

                            Interlocked.Increment(
                                ref prepared.CompletedChunks);

                            lastException = null;

                            break;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;

                            // Переключаемся на следующий CDN.
                            await cdnLock.WaitAsync();

                            try
                            {
                                currentCdnIndex =
                                    (currentCdnIndex + 1) %
                                    serverList.Count;
                            }
                            finally
                            {
                                cdnLock.Release();
                            }
                        }
                    }

                    if (lastException != null)
                    {
                        throw new IOException(
                            $"Не удалось скачать chunk после " +
                            $"{maxAttempts} попыток.",
                            lastException);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(
                        ref workerException,
                        ex,
                        null);

                    cancellation.Cancel();
                    return;
                }
            }
        }

        // ------------------------------------------------------------
        // 3. Запускаем 20 workers. Они берут chunks из общей очереди.
        // ------------------------------------------------------------

        var workers = Enumerable
            .Range(
                0,
                Math.Min(
                    parallelDownloads,
                    Math.Max(1, allChunks.Count)))
            .Select(_ => WorkerAsync())
            .ToArray();

        // Пока workers скачивают chunks, обновляем одну строку.
        Console.Clear();
        Console.CursorVisible = false;

        while (!allChunks.IsEmpty &&
               !cancellation.IsCancellationRequested)
        {
            PrintOverallProgress(
                manifestNumber,
                manifestCount,
                preparedFiles,
                session,
                parallelDownloads);

            await Task.Delay(200);
        }

        try
        {
            await Task.WhenAll(workers);
        }
        catch
        {
            // Настоящая ошибка находится в workerException.
        }

        if (workerException != null)
        {
            foreach (var prepared in preparedFiles)
                await prepared.Stream.DisposeAsync();

            foreach (var prepared in preparedFiles)
            {
                if (File.Exists(prepared.TempPath))
                    File.Delete(prepared.TempPath);
            }

            // Из общего счётчика убираем chunks, записанные этим manifest
            // до ошибки, чтобы состояние не оставалось завышенным.
            long downloadedByManifest =
                preparedFiles.Sum(x => x.Downloaded);

            Interlocked.Add(
                ref session.TotalDownloaded,
                -downloadedByManifest);

            throw new IOException(
                $"Ошибка загрузки chunks: {workerException.Message}",
                workerException);
        }

        // ------------------------------------------------------------
        // 4. Все chunks скачаны. Теперь проверяем каждый файл целиком.
        //    Только после SHA-1 он становится настоящим файлом.
        // ------------------------------------------------------------

        foreach (var prepared in preparedFiles)
            await prepared.Stream.FlushAsync();

        foreach (var prepared in preparedFiles)
            await prepared.Stream.DisposeAsync();

        foreach (var prepared in preparedFiles)
        {
            ulong actualSize =
                (ulong)new FileInfo(prepared.TempPath).Length;

            if (actualSize != prepared.File.TotalSize)
            {
                if (File.Exists(prepared.TempPath))
                    File.Delete(prepared.TempPath);

                long downloadedByManifest =
                    preparedFiles.Sum(x => x.Downloaded);

                Interlocked.Add(
                    ref session.TotalDownloaded,
                    -downloadedByManifest);

                throw new IOException(
                    $"Размер файла не совпадает: " +
                    $"{prepared.File.FileName} " +
                    $"({actualSize}/{prepared.File.TotalSize})");
            }

            bool hashOk =
                await ValidateExistingFileAsync(
                    prepared.TempPath,
                    prepared.File);

            if (!hashOk)
            {
                foreach (var item in preparedFiles)
                {
                    if (File.Exists(item.TempPath))
                        File.Delete(item.TempPath);
                }

                long downloadedByManifest =
                    preparedFiles.Sum(x => x.Downloaded);

                Interlocked.Add(
                    ref session.TotalDownloaded,
                    -downloadedByManifest);

                throw new IOException(
                    $"SHA-1 не совпадает с manifest: " +
                    prepared.File.FileName);
            }
        }

        // ------------------------------------------------------------
        // 5. Только после успешной проверки всех файлов заменяем
        //    временные файлы настоящими.
        // ------------------------------------------------------------

        foreach (var prepared in preparedFiles)
        {
            File.Move(
                prepared.TempPath,
                prepared.OutputPath,
                true);

            Interlocked.Increment(
                ref session.CompletedFiles);
        }

        PrintOverallProgress(
            manifestNumber,
            manifestCount,
            preparedFiles,
            session,
            parallelDownloads);

        Console.WriteLine();
        Console.WriteLine(
            $"✓ Manifest {manifestNumber}/{manifestCount} полностью скачан и проверен.");
    }

    private static async Task<bool> ValidateExistingFileAsync(
        string path,
        DepotManifest.FileData file)
    {
        try
        {
            long size = new FileInfo(path).Length;

            if ((ulong)size != file.TotalSize)
                return false;

            if (file.FileHash == null ||
                file.FileHash.Length == 0)
            {
                return false;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.SequentialScan);

            byte[] hash =
                await SHA1.HashDataAsync(stream);

            return CryptographicOperations.FixedTimeEquals(
                hash,
                file.FileHash);
        }
        catch
        {
            return false;
        }
    }

    private static void PrintSkipped(
        int manifestNumber,
        int manifestCount,
        long fileNumber,
        long totalFiles,
        string fileName,
        DownloadSession session)
    {
        double totalPercent =
            session.TotalExpected == 0
                ? 100
                : session.TotalDownloaded * 100.0 /
                  (double)session.TotalExpected;

        double speed = 0;

        if (session.Stopwatch.Elapsed.TotalSeconds > 0)
        {
            speed =
                session.TotalDownloaded /
                session.Stopwatch.Elapsed.TotalSeconds;
        }

        TimeSpan eta = TimeSpan.Zero;

        if (speed > 0)
        {
            double remaining =
                Math.Max(
                    0,
                    (double)session.TotalExpected -
                    session.TotalDownloaded);

            eta = TimeSpan.FromSeconds(
                remaining / speed);
        }

        string line =
            $"[{manifestNumber}/{manifestCount}] " +
            $"[{fileNumber}/{totalFiles}] " +
            $"✓ Проверен: {fileName} | " +
            $"Всего {totalPercent:F1}% | " +
            $"Скорость {FormatSize((ulong)Math.Max(0, speed))}/s | " +
            $"Осталось {FormatTime(eta)}";

        PrintLine(line);
    }

    private static void PrintOverallProgress(
        int manifestNumber,
        int manifestCount,
        List<PreparedFile> preparedFiles,
        DownloadSession session,
        int parallelDownloads)
    {
        PreparedFile? active =
            preparedFiles
                .Where(x =>
                    x.CompletedChunks < x.File.Chunks.Count)
                .OrderByDescending(x => x.Downloaded)
                .FirstOrDefault();

        double totalPercent =
            session.TotalExpected == 0
                ? 100
                : session.TotalDownloaded * 100.0 /
                  (double)session.TotalExpected;

        double speed = 0;

        if (session.Stopwatch.Elapsed.TotalSeconds > 0)
        {
            speed =
                session.TotalDownloaded /
                session.Stopwatch.Elapsed.TotalSeconds;
        }

        TimeSpan eta = TimeSpan.Zero;

        if (speed > 0)
        {
            double remaining =
                Math.Max(
                    0,
                    (double)session.TotalExpected -
                    session.TotalDownloaded);

            eta = TimeSpan.FromSeconds(
                remaining / speed);
        }

        string fileName =
            active?.File.FileName ?? "Подготовка...";

        if (fileName.Length > 65)
            fileName = "..." + fileName[^62..];

        double filePercent = 0;

        if (active != null &&
            active.File.TotalSize > 0)
        {
            filePercent =
                active.Downloaded * 100.0 /
                (double)active.File.TotalSize;
        }

        const int barWidth = 40;

        int filled =
            (int)Math.Round(
                barWidth * totalPercent / 100.0);

        filled = Math.Clamp(
            filled,
            0,
            barWidth);

        string progressBar =
            new string('█', filled) +
            new string('░', barWidth - filled);

        try
        {
            Console.SetCursorPosition(0, 0);
        }
        catch
        {
        }

        Console.WriteLine(
            "╔══════════════════════════════════════════════════════════════╗");

        Console.WriteLine(
            $"║                    ЗАГРУЗКА STEAM                           ║");

        Console.WriteLine(
            "╚══════════════════════════════════════════════════════════════╝");

        Console.WriteLine();

        Console.WriteLine(
            $"Manifest: {manifestNumber} / {manifestCount}");

        Console.WriteLine(
            $"Файлов:   {session.CompletedFiles} / {session.TotalFiles}");

        Console.WriteLine();

        Console.WriteLine(
            $"Текущий файл: {fileName}");

        Console.WriteLine(
            $"Файл: {filePercent:F1}%");

        Console.WriteLine();

        Console.WriteLine(
            $"[{progressBar}] {totalPercent:F1}%");

        Console.WriteLine();

        Console.WriteLine(
            $"Скачано:  {FormatSize((ulong)Math.Max(0, session.TotalDownloaded))} / {FormatSize(session.TotalExpected)}");

        Console.WriteLine(
            $"Скорость: {FormatSize((ulong)Math.Max(0, speed))}/s");

        Console.WriteLine(
            $"Осталось: {FormatTime(eta)}");

        Console.WriteLine();

        Console.WriteLine(
            $"Потоков:  {parallelDownloads}");
    }

    private static void PrintLine(string line)
    {
        int width;

        try
        {
            width = Math.Max(Console.WindowWidth - 1, 80);
        }
        catch
        {
            width = 120;
        }

        if (line.Length > width)
            line = line[..width];

        Console.Write("\r" + line.PadRight(width));
    }

    private static string FormatSize(ulong bytes)
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

    private static string FormatTime(TimeSpan time)
    {
        if (time.TotalSeconds < 0 ||
            double.IsNaN(time.TotalSeconds) ||
            double.IsInfinity(time.TotalSeconds))
        {
            return "--:--";
        }

        if (time.TotalHours >= 1)
            return time.ToString(@"hh\:mm\:ss");

        return time.ToString(@"mm\:ss");
    }
}