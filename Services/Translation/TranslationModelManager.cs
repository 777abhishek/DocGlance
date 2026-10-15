using DocGlance.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Services.Translation;

public sealed record TranslationModelStatus(
    TranslationModelPair Pair,
    bool IsInstalled,
    long InstalledBytes);

public sealed record ModelDownloadProgress(
    string Pair,
    long BytesDownloaded,
    long TotalBytes,
    string CurrentFile);

public sealed class TranslationModelManager
{
    private const string CatalogFileName = "catalog.json";
    private const int DownloadAttempts = 3;

    private static readonly HttpClient Http = CreateHttpClient();
    public static TranslationModelManager Instance { get; } = new();

    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _catalogGate = new();
    private TranslationModelCatalog? _catalog;
    private string? _catalogError;

    private TranslationModelManager()
    {
    }

    public static string ModelRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DocGlance",
        "TranslationModels");

    public DateTimeOffset? CatalogFetchedAt => GetCatalog()?.FetchedAt;
    public string? CatalogError => _catalogError;

    public void EnsureCatalogLoaded() => _ = GetCatalog();

    public IReadOnlyList<TranslationModelStatus> GetStatuses()
    {
        TranslationModelCatalog? catalog = GetCatalog();
        if (catalog == null)
            return Array.Empty<TranslationModelStatus>();

        return catalog.Pairs
            .OrderBy(pair => pair.SourceLanguage, StringComparer.Ordinal)
            .ThenBy(pair => pair.TargetLanguage, StringComparer.Ordinal)
            .Select(pair =>
            {
                long installedBytes = GetInstalledPairBytes(pair);
                return new TranslationModelStatus(
                    pair,
                    IsPairInstalled(pair),
                    installedBytes);
            })
            .ToArray();
    }

    public bool IsRouteInstalled(string source, string target)
    {
        IReadOnlyList<string> required = LanguageCatalog.RequiredPairs(source, target);
        if (required.Count == 0)
            return false;
        return required.All(IsPairDirectoryComplete);
    }

    public long GetInstalledBytes()
    {
        if (!Directory.Exists(ModelRoot))
            return 0;
        return Directory.EnumerateDirectories(ModelRoot)
            .Where(path => IsKnownPairDirectory(Path.GetFileName(path)))
            .Sum(DirectorySize);
    }

    public async Task<TranslationModelCatalog> RefreshCatalogAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            status?.Report("Checking the Mozilla model catalog…");
            using HttpResponseMessage response = await Http.GetAsync(
                TranslationModelCatalog.RecordsEndpoint,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            TranslationModelCatalog catalog = TranslationModelCatalog.Parse(json);

            Directory.CreateDirectory(ModelRoot);
            string temporaryPath = Path.Combine(ModelRoot, $"{CatalogFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
                File.Move(temporaryPath, Path.Combine(ModelRoot, CatalogFileName), overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            lock (_catalogGate)
            {
                _catalog = catalog;
                _catalogError = null;
            }
            status?.Report($"Catalog updated. {catalog.Pairs.Count} model directions are available.");
            return catalog;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _catalogError = ex.Message;
            Logger.Error("[TranslationModels] Model catalog update failed.", ex);
            throw new InvalidOperationException(
                $"Could not update the model catalog. Check the connection and retry. Details: {ex.Message}",
                ex);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task DownloadPairAsync(
        string source,
        string target,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!LanguageCatalog.IsSupportedPair(source, target))
            throw new ArgumentException("That language pair is not in the initial supported set.");

        await _operationGate.WaitAsync(cancellationToken);
        string? stagingDirectory = null;
        try
        {
            TranslationModelCatalog catalog = GetCatalog() ??
                throw new InvalidOperationException(
                    "Update the model catalog before downloading language models.");
            if (!catalog.TryGetPair(source, target, out TranslationModelPair pair))
                throw new InvalidOperationException(
                    $"No current model is listed for {source} → {target}. Update the catalog and try again.");
            if (IsPairInstalled(pair))
                return;

            Directory.CreateDirectory(ModelRoot);
            stagingDirectory = Path.Combine(ModelRoot, $".{pair.Key}.{Guid.NewGuid():N}.installing");
            Directory.CreateDirectory(stagingDirectory);

            long totalBytes = pair.DownloadBytes;
            long completedBytes = 0;
            foreach (TranslationModelFile file in pair.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string downloadPath = Path.Combine(stagingDirectory, $".{file.Name}.download");
                await DownloadFileWithRetryAsync(
                    file,
                    downloadPath,
                    pair.Key,
                    completedBytes,
                    totalBytes,
                    progress,
                    cancellationToken);

                byte[] compressed = await File.ReadAllBytesAsync(downloadPath, cancellationToken);
                byte[] verified = await Task.Run(
                    () => TranslationEngineNative.VerifyAndDecompress(compressed, file.DecompressedSha256),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                string verifiedPath = Path.Combine(stagingDirectory, $".{file.Name}.verified");
                await File.WriteAllBytesAsync(verifiedPath, verified, cancellationToken);
                File.Delete(downloadPath);
                File.Move(verifiedPath, Path.Combine(stagingDirectory, file.Name));
                completedBytes += file.DownloadBytes;
                progress?.Report(new ModelDownloadProgress(pair.Key, completedBytes, totalBytes, file.Name));
            }

            cancellationToken.ThrowIfCancellationRequested();
            string manifestPath = Path.Combine(stagingDirectory, "model-manifest.json");
            var manifest = new InstalledModelManifest(
                pair.Key,
                pair.Version,
                DateTimeOffset.UtcNow,
                pair.Files.Select(file => file.Name).ToArray());
            await File.WriteAllTextAsync(
                manifestPath,
                System.Text.Json.JsonSerializer.Serialize(manifest),
                cancellationToken);

            await TranslationService.Instance.ReleaseCachedPairAsync(
                pair.Key,
                cancellationToken);
            CommitStagedPair(stagingDirectory, pair.Key);
            stagingDirectory = null;
            Logger.Log($"[TranslationModels] Installed model pair {pair.Key}; {DirectorySize(GetPairDirectory(pair.Key))} bytes on disk.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"[TranslationModels] Failed to install {source}-{target}.", ex);
            throw;
        }
        finally
        {
            if (stagingDirectory != null && Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
            _operationGate.Release();
        }
    }

    public async Task RemovePairAsync(string pairKey, CancellationToken cancellationToken = default)
    {
        if (!TryParsePairKey(pairKey, out string source, out string target))
            throw new ArgumentException("Invalid language model pair.", nameof(pairKey));

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            string directory = GetPairDirectory(pairKey);
            if (!Directory.Exists(directory))
                return;

            await TranslationService.Instance.ReleaseCachedPairAsync(pairKey, cancellationToken);
            Directory.Delete(directory, recursive: true);
            Logger.Log($"[TranslationModels] Removed model pair {pairKey}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error($"[TranslationModels] Failed to remove {pairKey}.", ex);
            throw new InvalidOperationException(
                $"Could not remove the {source} → {target} model. Close documents using translation and retry. Details: {ex.Message}",
                ex);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private TranslationModelCatalog? GetCatalog()
    {
        lock (_catalogGate)
        {
            if (_catalog != null)
                return _catalog;

            string path = Path.Combine(ModelRoot, CatalogFileName);
            if (!File.Exists(path))
                return null;
            try
            {
                _catalog = TranslationModelCatalog.Parse(
                    File.ReadAllText(path),
                    new DateTimeOffset(File.GetLastWriteTimeUtc(path)));
                _catalogError = null;
                return _catalog;
            }
            catch (Exception ex)
            {
                _catalogError = ex.Message;
                Logger.Error("[TranslationModels] Saved catalog could not be read.", ex);
                return null;
            }
        }
    }

    private async Task DownloadFileWithRetryAsync(
        TranslationModelFile file,
        string downloadPath,
        string pairKey,
        long completedBytes,
        long totalBytes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= DownloadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);
                using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUri);
                using HttpResponseMessage response = await Http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(
                    downloadPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                byte[] buffer = new byte[64 * 1024];
                long downloaded = 0;
                while (true)
                {
                    int read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    downloaded += read;
                    if (downloaded > file.DownloadBytes)
                        throw new InvalidDataException(
                            $"The download for {file.Name} exceeded the catalog size of {file.DownloadBytes} bytes.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new ModelDownloadProgress(
                        pairKey,
                        completedBytes + downloaded,
                        totalBytes,
                        file.Name));
                }
                await output.FlushAsync(cancellationToken);
                if (downloaded != file.DownloadBytes)
                    throw new InvalidDataException(
                        $"Downloaded size for {file.Name} was {downloaded} bytes; the catalog expected {file.DownloadBytes} bytes.");
                return;
            }
            catch (OperationCanceledException)
            {
                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                lastError = ex;
                if (File.Exists(downloadPath))
                    File.Delete(downloadPath);
                if (attempt == DownloadAttempts)
                    break;
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
            }
        }

        throw new IOException(
            $"Could not download {file.Name} after {DownloadAttempts} attempts. Retry the model download.",
            lastError);
    }

    private void CommitStagedPair(string stagingDirectory, string pairKey)
    {
        string destination = GetPairDirectory(pairKey);
        string backup = destination + $".{Guid.NewGuid():N}.previous";
        bool hadPrevious = Directory.Exists(destination);
        if (hadPrevious)
        {
            Directory.Move(destination, backup);
        }

        try
        {
            Directory.Move(stagingDirectory, destination);
        }
        catch
        {
            if (hadPrevious && !Directory.Exists(destination) && Directory.Exists(backup))
                Directory.Move(backup, destination);
            throw;
        }

        if (hadPrevious && Directory.Exists(backup))
            Directory.Delete(backup, recursive: true);
    }

    private static bool IsPairInstalled(TranslationModelPair pair)
    {
        string directory = GetPairDirectory(pair.Key);
        if (!Directory.Exists(directory))
            return false;
        return pair.Files.All(file => File.Exists(Path.Combine(directory, file.Name))) &&
            File.Exists(Path.Combine(directory, "model-manifest.json"));
    }

    private static bool IsPairDirectoryComplete(string pairKey)
    {
        if (!TryParsePairKey(pairKey, out _, out _))
            return false;
        string directory = GetPairDirectory(pairKey);
        if (!Directory.Exists(directory))
            return false;
        string[] files = Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name) && !name.StartsWith(".", StringComparison.Ordinal))
            .ToArray()!;
        bool hasModel = files.Any(name => name!.StartsWith("model.", StringComparison.Ordinal));
        bool hasSharedVocab = files.Any(name => name!.StartsWith("vocab.", StringComparison.Ordinal));
        bool hasSrcVocab = files.Any(name => name!.StartsWith("srcvocab.", StringComparison.Ordinal));
        bool hasTrgVocab = files.Any(name => name!.StartsWith("trgvocab.", StringComparison.Ordinal));
        return hasModel && (hasSharedVocab || (hasSrcVocab && hasTrgVocab));
    }

    private static long GetInstalledPairBytes(TranslationModelPair pair) =>
        IsPairInstalled(pair) ? DirectorySize(GetPairDirectory(pair.Key)) : 0;

    private static bool IsKnownPairDirectory(string? pairKey) =>
        TryParsePairKey(pairKey, out _, out _);

    private static bool TryParsePairKey(string? pairKey, out string source, out string target)
    {
        source = string.Empty;
        target = string.Empty;
        if (pairKey == null)
            return false;
        string[] parts = pairKey.Split('-');
        if (parts.Length != 2 || !LanguageCatalog.IsSupportedPair(parts[0], parts[1]))
            return false;
        source = parts[0];
        target = parts[1];
        return true;
    }

    private static string GetPairDirectory(string pairKey) =>
        Path.Combine(ModelRoot, pairKey);

    private static long DirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("QuickView", "1.0"));
        return client;
    }

    private sealed record InstalledModelManifest(
        string Pair,
        string Version,
        DateTimeOffset InstalledAt,
        string[] Files);
}
