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

    // Только данные, реально скачанные из сети
    // во время текущего запуска.
    public long NetworkDownloaded;

    // Файлы текущего manifest, которые полностью скачаны,
    // но ещё не проверены.
    public long DownloadedFiles;

    // Все файлы, успешно прошедшие проверку.
    public long VerifiedFiles;

    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

    public string? LastStatusMessage;
}

public static class CdnTest
{
    private const int ProgressLineCount = 20;

    private sealed class PreparedFile
    {
        public required DepotManifest.FileData File;
        public required int FileIndex;

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

        public required int ChunkIndex;
    }

    private sealed class ResumeState
    {
        private const uint Magic = 0x534D4C53;
        private const int Version = 1;

        private readonly string stateDirectory;
        private readonly string statePath;

        private readonly uint depotId;
        private readonly ulong manifestId;
        private readonly int fileCount;

        private readonly ConcurrentDictionary<long, byte> completed =
            new();

        private readonly ConcurrentQueue<long> pending =
            new();

        private readonly CancellationTokenSource cancellation =
            new();

        private readonly SemaphoreSlim writeLock =
            new(1, 1);

        private Task? writerTask;

        private bool writerStarted;
        private bool writerStopped;


        private ResumeState(
            string outputDirectory,
            uint depotId,
            ulong manifestId,
            int fileCount)
        {
            this.depotId = depotId;
            this.manifestId = manifestId;
            this.fileCount = fileCount;

            stateDirectory =
                Path.Combine(
                    outputDirectory,
                    ".stmanloader");

            statePath =
                Path.Combine(
                    stateDirectory,
                    $"depot_{depotId}_{manifestId}.state");
        }


        public static ResumeState Open(
            string outputDirectory,
            uint depotId,
            ulong manifestId,
            int fileCount)
        {
            var state =
                new ResumeState(
                    outputDirectory,
                    depotId,
                    manifestId,
                    fileCount);

            state.LoadOrCreate();

            return state;
        }


        private static long MakeKey(
            int fileIndex,
            int chunkIndex)
        {
            return
                ((long)fileIndex << 32) |
                (uint)chunkIndex;
        }


        private static int GetFileIndex(
            long key)
        {
            return (int)(key >> 32);
        }


        private static int GetChunkIndex(
            long key)
        {
            return (int)(uint)key;
        }


        private void LoadOrCreate()
        {
            Directory.CreateDirectory(
                stateDirectory);

            // Удаляем state старого manifest
            // этого же depot.
            foreach (string oldState
                    in Directory.GetFiles(
                        stateDirectory,
                        $"depot_{depotId}_*.state"))
            {
                if (string.Equals(
                        oldState,
                        statePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(oldState);
                }
                catch
                {
                }
            }

            if (!File.Exists(statePath))
            {
                CreateEmptyState();
                return;
            }

            try
            {
                using var stream =
                    new FileStream(
                        statePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);

                using var reader =
                    new BinaryReader(stream);

                if (stream.Length < 24)
                    throw new InvalidDataException();

                uint magic =
                    reader.ReadUInt32();

                int version =
                    reader.ReadInt32();

                uint storedDepotId =
                    reader.ReadUInt32();

                ulong storedManifestId =
                    reader.ReadUInt64();

                int storedFileCount =
                    reader.ReadInt32();

                if (magic != Magic ||
                    version != Version ||
                    storedDepotId != depotId ||
                    storedManifestId != manifestId ||
                    storedFileCount != fileCount)
                {
                    throw new InvalidDataException();
                }

                // Каждая запись занимает ровно 8 байт:
                //
                // int fileIndex
                // int chunkIndex
                //
                // Если программа упала посреди последней записи,
                // неполные байты просто игнорируются.
                while (stream.Position + 8 <= stream.Length)
                {
                    int fileIndex =
                        reader.ReadInt32();

                    int chunkIndex =
                        reader.ReadInt32();

                    if (fileIndex < 0 ||
                        fileIndex >= fileCount ||
                        chunkIndex < 0)
                    {
                        continue;
                    }

                    completed.TryAdd(
                        MakeKey(
                            fileIndex,
                            chunkIndex),
                        0);
                }
            }
            catch
            {
                completed.Clear();

                CreateEmptyState();
            }
        }


        private void CreateEmptyState()
        {
            Directory.CreateDirectory(
                stateDirectory);

            using var stream =
                new FileStream(
                    statePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read);

            using var writer =
                new BinaryWriter(stream);

            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(depotId);
            writer.Write(manifestId);
            writer.Write(fileCount);
        }


        public bool IsCompleted(
            int fileIndex,
            int chunkIndex)
        {
            return completed.ContainsKey(
                MakeKey(
                    fileIndex,
                    chunkIndex));
        }


        public void MarkCompleted(
            int fileIndex,
            int chunkIndex)
        {
            long key =
                MakeKey(
                    fileIndex,
                    chunkIndex);

            // Если запись уже была —
            // второй раз её в очередь не добавляем.
            if (completed.TryAdd(key, 0))
            {
                pending.Enqueue(key);
            }
        }


        public void RemoveFile(
            int fileIndex)
        {
            foreach (long key
                    in completed.Keys)
            {
                if (GetFileIndex(key) ==
                    fileIndex)
                {
                    completed.TryRemove(
                        key,
                        out _);
                }
            }
        }


        public async Task RewriteSnapshotAsync()
        {
            string tempPath =
                statePath + ".tmp";

            await writeLock.WaitAsync();

            try
            {
                await using (
                    var stream =
                        new FileStream(
                            tempPath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            4096,
                            FileOptions.Asynchronous))
                {
                    using var writer =
                        new BinaryWriter(
                            stream,
                            System.Text.Encoding.UTF8,
                            leaveOpen: true);

                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(depotId);
                    writer.Write(manifestId);
                    writer.Write(fileCount);

                    foreach (long key
                            in completed.Keys)
                    {
                        writer.Write(
                            GetFileIndex(key));

                        writer.Write(
                            GetChunkIndex(key));
                    }

                    writer.Flush();

                    await stream.FlushAsync();
                }

                File.Move(
                    tempPath,
                    statePath,
                    true);

                while (pending.TryDequeue(out _))
                {
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                writeLock.Release();
            }
        }


        public void StartWriter()
        {
            if (writerStarted)
                return;

            writerStarted = true;

            writerTask =
                WriterLoopAsync();
        }


        private async Task WriterLoopAsync()
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        1000,
                        cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await FlushPendingAsync();
                }
                catch
                {
                    // State — вспомогательный механизм.
                    // Ошибка его записи не должна останавливать
                    // само скачивание игры.
                }
            }

            try
            {
                await FlushPendingAsync();
            }
            catch
            {
            }
        }


        private async Task FlushPendingAsync()
        {
            var batch =
                new List<long>();

            while (pending.TryDequeue(
                    out long key))
            {
                batch.Add(key);
            }

            if (batch.Count == 0)
                return;

            await writeLock.WaitAsync();

            try
            {
                await using var stream =
                    new FileStream(
                        statePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous);

                using var writer =
                    new BinaryWriter(
                        stream,
                        System.Text.Encoding.UTF8,
                        leaveOpen: true);

                foreach (long key in batch)
                {
                    writer.Write(
                        GetFileIndex(key));

                    writer.Write(
                        GetChunkIndex(key));
                }

                writer.Flush();

                await stream.FlushAsync();
            }
            catch
            {
                // Если запись не удалась —
                // возвращаем отметки обратно.
                //
                // Даже если часть реально успела записаться,
                // дубликаты безопасны:
                // при следующем чтении используется HashSet.
                foreach (long key in batch)
                {
                    pending.Enqueue(key);
                }

                throw;
            }
            finally
            {
                writeLock.Release();
            }
        }


        public async Task StopAsync()
        {
            if (writerStopped)
                return;

            writerStopped = true;

            if (writerStarted)
            {
                cancellation.Cancel();

                if (writerTask != null)
                {
                    try
                    {
                        await writerTask;
                    }
                    catch
                    {
                    }
                }
            }

            try
            {
                await FlushPendingAsync();
            }
            catch
            {
            }
        }


        public async Task DeleteAsync()
        {
            await StopAsync();

            try
            {
                if (File.Exists(statePath))
                {
                    File.Delete(statePath);
                }
            }
            catch
            {
            }

            try
            {
                if (Directory.Exists(stateDirectory) &&
                    !Directory.EnumerateFileSystemEntries(
                        stateDirectory)
                    .Any())
                {
                    Directory.Delete(
                        stateDirectory);
                }
            }
            catch
            {
            }
        }
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

        // Счётчик загруженных файлов относится только к текущему manifest.
        Interlocked.Exchange(ref session.DownloadedFiles, 0);

        session.LastStatusMessage = null;

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

        var resumeState =
            ResumeState.Open(
                outputDirectory,
                depot.DepotId,
                depot.ManifestId,
                files.Count);

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

                    Interlocked.Increment(ref session.VerifiedFiles);

                    PrintSkipped(
                        manifestNumber,
                        manifestCount,
                        currentFileNumber,
                        session.TotalFiles,
                        file.FileName,
                        session);

                    resumeState.RemoveFile(i);

                    string staleTempPath =
                        outputPath + ".downloading";

                    if (File.Exists(staleTempPath))
                    {
                        try
                        {
                            File.Delete(staleTempPath);
                        }
                        catch
                        {
                        }
                    }

                    continue;
                }

                Console.WriteLine();
                Console.WriteLine(
                    $"⚠ Файл отличается от manifest, перекачиваем: {file.FileName}");
            }

            string tempPath =
                outputPath + ".downloading";

            bool canResume = false;

            // ------------------------------------------------------------
            // Проверяем, можно ли использовать старый .downloading.
            // ------------------------------------------------------------

            if (File.Exists(tempPath))
            {
                try
                {
                    long tempSize =
                        new FileInfo(tempPath).Length;

                    canResume =
                        (ulong)tempSize ==
                        file.TotalSize;
                }
                catch
                {
                    canResume = false;
                }
            }


            // Если временный файл неправильного размера,
            // его state использовать нельзя.
            if (!canResume)
            {
                resumeState.RemoveFile(i);

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }


            // ------------------------------------------------------------
            // Открываем существующий .downloading
            // или создаём новый.
            // ------------------------------------------------------------

            var output =
                new FileStream(
                    tempPath,
                    canResume
                        ? FileMode.Open
                        : FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan);


            if (!canResume)
            {
                output.SetLength(
                    checked(
                        (long)file.TotalSize));
            }


            var prepared =
                new PreparedFile
                {
                    File = file,
                    FileIndex = i,

                    OutputPath = outputPath,
                    TempPath = tempPath,
                    Stream = output
                };

            preparedFiles.Add(prepared);


            // ------------------------------------------------------------
            // В очередь отправляем только те chunks,
            // которых нет в resume state.
            // ------------------------------------------------------------

            for (int chunkIndex = 0;
                chunkIndex < file.Chunks.Count;
                chunkIndex++)
            {
                var chunk =
                    file.Chunks[chunkIndex];

                if (canResume &&
                    resumeState.IsCompleted(
                        i,
                        chunkIndex))
                {
                    long restoredBytes =
                        checked(
                            (long)chunk.UncompressedLength);

                    prepared.Downloaded +=
                        restoredBytes;

                    prepared.CompletedChunks++;

                    Interlocked.Add(
                        ref session.TotalDownloaded,
                        restoredBytes);

                    continue;
                }


                allChunks.Enqueue(
                    new ChunkWork
                    {
                        PreparedFile = prepared,
                        Chunk = chunk,
                        ChunkIndex = chunkIndex
                    });
            }


            // Все chunks файла уже были скачаны
            // во время прошлого запуска.
            if (prepared.CompletedChunks ==
                file.Chunks.Count)
            {
                Interlocked.Increment(
                    ref session.DownloadedFiles);
            }
        }

        // При новой попытке загрузки один раз переписываем state,
        // удаляя из него устаревшие записи.
        await resumeState.RewriteSnapshotAsync();
        resumeState.StartWriter();

        if (preparedFiles.Count == 0)
        {
            await resumeState.DeleteAsync();

            Console.WriteLine();
            Console.WriteLine(
                "✓ Все файлы этого manifest уже проверены.");

            return;
        }


        if (allChunks.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine(
                "✓ Все chunks восстановлены из предыдущей загрузки.");
        }
        else
        {
            Console.WriteLine(
                $"Chunks в общей очереди: {allChunks.Count}");

            Console.WriteLine(
                $"Файлов нужно скачать: {preparedFiles.Count}");

            Console.WriteLine();
        }

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

                    int chunkCount = prepared.File.Chunks.Count;

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

                            Interlocked.Add(
                                ref session.NetworkDownloaded,
                                written);

                            int completedChunks =
                                Interlocked.Increment(
                                    ref prepared.CompletedChunks);

                            resumeState.MarkCompleted(
                                prepared.FileIndex,
                                work.ChunkIndex);

                            // Все chunks файла скачаны — файл считается
                            // загруженным, но ещё не проверенным.
                            if (completedChunks == chunkCount)
                            {
                                Interlocked.Increment(
                                    ref session.DownloadedFiles);
                            }

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
            // Сначала стараемся физически записать данные
            // временных файлов на диск.
            foreach (var prepared in preparedFiles)
            {
                try
                {
                    await prepared.Stream.FlushAsync();
                }
                catch
                {
                }
            }

            foreach (var prepared in preparedFiles)
            {
                try
                {
                    await prepared.Stream.DisposeAsync();
                }
                catch
                {
                }
            }

            // Теперь сохраняем последние отметки state.
            await resumeState.StopAsync();

            EndProgressDisplay();

            throw new IOException(
                $"Ошибка загрузки chunks: {workerException.Message}",
                workerException);
        }

        // ------------------------------------------------------------
        // 4. Все chunks скачаны. Проверяем файлы по одному.
        //
        //    ВАЖНО:
        //    После успешной проверки каждого файла сразу заменяем
        //    .downloading на настоящий файл. Поэтому готовые файлы
        //    появляются в папке сразу, не дожидаясь завершения
        //    всего manifest.
        // ------------------------------------------------------------

        foreach (var prepared in preparedFiles)
            await prepared.Stream.FlushAsync();

        foreach (var prepared in preparedFiles)
            await prepared.Stream.DisposeAsync();

        // Workers больше ничего не добавят.
        // Сохраняем последнюю пачку state.
        await resumeState.StopAsync();

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

                resumeState.RemoveFile(
                    prepared.FileIndex);

                await resumeState.RewriteSnapshotAsync();

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
                if (File.Exists(prepared.TempPath))
                    File.Delete(prepared.TempPath);

                long downloadedByManifest =
                    preparedFiles.Sum(x => x.Downloaded);

                Interlocked.Add(
                    ref session.TotalDownloaded,
                    -downloadedByManifest);

                resumeState.RemoveFile(
                    prepared.FileIndex);

                await resumeState.RewriteSnapshotAsync();

                throw new IOException(
                    $"SHA-1 не совпадает с manifest: " +
                    prepared.File.FileName);
            }

            // Файл полностью скачан и проверен — сразу делаем его
            // обычным файлом в папке назначения.
            File.Move(
                prepared.TempPath,
                prepared.OutputPath,
                true);

            resumeState.RemoveFile(
                prepared.FileIndex);

            Interlocked.Increment(
                ref session.VerifiedFiles);

            session.LastStatusMessage =
                $"✓ Файл готов: {prepared.File.FileName}";

            PrintOverallProgress(
                manifestNumber,
                manifestCount,
                preparedFiles,
                session,
                parallelDownloads);
        }

        await resumeState.DeleteAsync();

        EndProgressDisplay();

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
                session.NetworkDownloaded /
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
                session.NetworkDownloaded /
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

        string[] lines =
        {
            "╔══════════════════════════════════════════════════════════════╗",
            "║                     ЗАГРУЗКА STEAM                           ║",
            "╚══════════════════════════════════════════════════════════════╝",
            "",
            $"Manifest: {manifestNumber} / {manifestCount}",
            $"Проверено файлов: {session.VerifiedFiles} / {session.TotalFiles}",
            $"Загружено файлов: {session.DownloadedFiles} / {preparedFiles.Count}",
            "",
            $"Текущий файл: {fileName}",
            $"Файл: {filePercent:F1}%",
            "",
            $"[{progressBar}] {totalPercent:F1}%",
            "",
            $"Скачано:  {FormatSize((ulong)Math.Max(0, session.TotalDownloaded))} / {FormatSize(session.TotalExpected)}",
            $"Скорость: {FormatSize((ulong)Math.Max(0, speed))}/s",
            $"Осталось: {FormatTime(eta)}",
            "",
            $"Потоков:  {parallelDownloads}",
            "",
            session.LastStatusMessage ?? ""
        };

        RenderProgress(
            lines);
    }

    private static void RenderProgress(
        string[] lines)
    {
        int width;

        try
        {
            width =
                Math.Max(
                    Console.WindowWidth - 1,
                    1);
        }
        catch
        {
            width = 120;
        }

        try
        {
            Console.SetCursorPosition(
                0,
                0);
        }
        catch
        {
            try
            {
                Console.Clear();
            }
            catch
            {
            }
        }

        for (int i = 0;
             i < ProgressLineCount;
             i++)
        {
            string line =
                i < lines.Length
                    ? lines[i]
                    : "";

            if (line.Length > width)
            {
                line =
                    line[..width];
            }

            // ВАЖНО:
            // PadRight очищает остаток предыдущей,
            // более длинной строки.
            string output =
                line.PadRight(width);

            Console.Write(output);

            if (i <
                ProgressLineCount - 1)
            {
                Console.WriteLine();
            }
        }
    }

    private static void EndProgressDisplay()
    {
        try
        {
            int targetRow =
                Math.Min(
                    ProgressLineCount,
                    Console.BufferHeight - 1);

            Console.SetCursorPosition(
                0,
                targetRow);

            Console.CursorVisible = true;
        }
        catch
        {
            try
            {
                Console.CursorVisible = true;
            }
            catch
            {
            }

            Console.WriteLine();
        }
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