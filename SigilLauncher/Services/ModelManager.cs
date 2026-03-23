using SigilLauncher.Models;

namespace SigilLauncher.Services;

/// <summary>
/// Downloads and manages local GGUF models for on-device inference.
/// Models are stored at %APPDATA%\Sigil\models\{filename}.
/// </summary>
public class ModelManager
{
    private CancellationTokenSource? _downloadCts;

    public bool IsDownloading { get; private set; }
    public double DownloadProgress { get; private set; }
    public string? Error { get; private set; }

    public event Action? StateChanged;

    public static string ModelsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sigil", "models");

    /// <summary>
    /// Full path where the given model would be stored.
    /// </summary>
    public string ModelPath(ModelInfo model) =>
        Path.Combine(ModelsDirectory, model.Filename);

    /// <summary>
    /// Check whether the model file already exists on disk.
    /// </summary>
    public bool IsModelDownloaded(ModelInfo model) =>
        File.Exists(ModelPath(model));

    /// <summary>
    /// Download a model using HttpClient with progress reporting.
    /// Returns the path to the downloaded file.
    /// </summary>
    public async Task<string> DownloadModelAsync(
        ModelInfo model,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var destPath = ModelPath(model);

        // Already downloaded
        if (File.Exists(destPath))
            return destPath;

        Directory.CreateDirectory(ModelsDirectory);

        _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linkedToken = _downloadCts.Token;

        IsDownloading = true;
        DownloadProgress = 0.0;
        Error = null;
        StateChanged?.Invoke();

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromHours(2); // Large model files

            using var response = await httpClient.GetAsync(model.DownloadURL, HttpCompletionOption.ResponseHeadersRead, linkedToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var tempPath = destPath + ".tmp";

            await using var contentStream = await response.Content.ReadAsStreamAsync(linkedToken);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long bytesRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, linkedToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), linkedToken);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    DownloadProgress = (double)bytesRead / totalBytes;
                    progress?.Report(DownloadProgress);
                    StateChanged?.Invoke();
                }
            }

            // Rename temp file to final destination
            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(tempPath, destPath);

            IsDownloading = false;
            DownloadProgress = 1.0;
            StateChanged?.Invoke();

            return destPath;
        }
        catch (OperationCanceledException)
        {
            IsDownloading = false;
            DownloadProgress = 0.0;
            StateChanged?.Invoke();
            throw;
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            Error = ex.Message;
            StateChanged?.Invoke();

            // Clean up partial download
            var tempPath = destPath + ".tmp";
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Cancel an in-progress download.
    /// </summary>
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
        IsDownloading = false;
        DownloadProgress = 0.0;
        StateChanged?.Invoke();
    }
}
