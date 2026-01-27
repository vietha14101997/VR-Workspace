using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Async file operation service for copy/move/delete operations with progress tracking.
/// Runs file I/O on background thread to avoid blocking Unity main thread.
/// </summary>
public static class FileOperationService
{
    private const int BUFFER_SIZE = 81920; // 80KB buffer for file copy
    private const int PROGRESS_UPDATE_INTERVAL_MS = 100; // Throttle progress updates

    #region Data Classes

    /// <summary>
    /// Progress information for file operations
    /// </summary>
    public class FileOperationProgress
    {
        public int TotalFiles;
        public int CompletedFiles;
        public string CurrentFileName;
        public long TotalBytes;
        public long CompletedBytes;
        public float OverallProgress => TotalBytes > 0 ? (float)CompletedBytes / TotalBytes : 0f;
    }

    /// <summary>
    /// Result of file operation
    /// </summary>
    public class FileOperationResult
    {
        public bool Success => FailCount == 0 && !WasCancelled;
        public int SuccessCount;
        public int FailCount;
        public bool WasCancelled;
        public List<string> Errors = new List<string>();
    }

    /// <summary>
    /// Helper class to track progress update timing (avoids ref parameter in async)
    /// </summary>
    private class ProgressThrottler
    {
        public DateTime LastUpdate = DateTime.MinValue;
    }

    /// <summary>
    /// Token to control pause/resume of file operations
    /// </summary>
    public class PauseToken
    {
        private volatile bool _isPaused = false;

        public bool IsPaused => _isPaused;

        public void Pause() => _isPaused = true;
        public void Resume() => _isPaused = false;

        public async Task WaitWhilePausedAsync(CancellationToken ct)
        {
            while (_isPaused && !ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
            }
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Calculate total file count and bytes for progress tracking
    /// </summary>
    public static async Task<(int fileCount, long totalBytes)> CalculateTotalSizeAsync(
        List<string> paths,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            int fileCount = 0;
            long totalBytes = 0;

            foreach (string path in paths)
            {
                ct.ThrowIfCancellationRequested();

                if (File.Exists(path))
                {
                    fileCount++;
                    totalBytes += new FileInfo(path).Length;
                }
                else if (Directory.Exists(path))
                {
                    CalculateDirectorySize(path, ref fileCount, ref totalBytes, ct);
                }
            }

            return (fileCount, totalBytes);
        }, ct);
    }

    /// <summary>
    /// Calculate total file count for delete operation (no bytes needed)
    /// </summary>
    public static async Task<int> CalculateTotalFileCountAsync(
        List<string> paths,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            int fileCount = 0;

            foreach (string path in paths)
            {
                ct.ThrowIfCancellationRequested();

                if (File.Exists(path))
                {
                    fileCount++;
                }
                else if (Directory.Exists(path))
                {
                    fileCount += CountFilesInDirectory(path, ct);
                }
            }

            return fileCount;
        }, ct);
    }

    /// <summary>
    /// Async copy operation with progress reporting
    /// </summary>
    public static async Task<FileOperationResult> CopyAsync(
        List<string> sourcePaths,
        string destination,
        bool overwrite,
        IProgress<FileOperationProgress> progress,
        CancellationToken ct,
        PauseToken pauseToken = null)
    {
        var result = new FileOperationResult();

        // Pre-calculate total size
        var (totalFiles, totalBytes) = await CalculateTotalSizeAsync(sourcePaths, ct);

        var progressData = new FileOperationProgress
        {
            TotalFiles = totalFiles,
            TotalBytes = totalBytes
        };

        var throttler = new ProgressThrottler();

        await Task.Run(async () =>
        {
            foreach (string sourcePath in sourcePaths)
            {
                // Check for pause
                if (pauseToken != null)
                    await pauseToken.WaitWhilePausedAsync(ct);

                if (ct.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }

                try
                {
                    if (File.Exists(sourcePath))
                    {
                        string fileName = Path.GetFileName(sourcePath);
                        string destPath = Path.Combine(destination, fileName);

                        progressData.CurrentFileName = fileName;
                        ReportProgress(progress, progressData, throttler);

                        await CopyFileWithProgressAsync(
                            sourcePath, destPath, overwrite,
                            bytesWritten =>
                            {
                                progressData.CompletedBytes += bytesWritten;
                                ReportProgress(progress, progressData, throttler);
                            },
                            ct, pauseToken);

                        progressData.CompletedFiles++;
                        result.SuccessCount++;
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        string dirName = Path.GetFileName(sourcePath);
                        string destDir = Path.Combine(destination, dirName);

                        await CopyDirectoryAsync(
                            sourcePath, destDir, overwrite,
                            progressData, progress, throttler,
                            result, ct, pauseToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    result.WasCancelled = true;
                    break;
                }
                catch (Exception e)
                {
                    result.FailCount++;
                    result.Errors.Add($"Failed to copy {sourcePath}: {e.Message}");
                    Debug.LogError($"[FileOperationService] Copy failed: {sourcePath} - {e.Message}");
                }
            }

            // Final progress update
            progress?.Report(progressData);

        }, ct);

        return result;
    }

    /// <summary>
    /// Async move operation with progress reporting
    /// </summary>
    public static async Task<FileOperationResult> MoveAsync(
        List<string> sourcePaths,
        string destination,
        bool overwrite,
        IProgress<FileOperationProgress> progress,
        CancellationToken ct,
        PauseToken pauseToken = null)
    {
        var result = new FileOperationResult();

        // Pre-calculate total size
        var (totalFiles, totalBytes) = await CalculateTotalSizeAsync(sourcePaths, ct);

        var progressData = new FileOperationProgress
        {
            TotalFiles = totalFiles,
            TotalBytes = totalBytes
        };

        var throttler = new ProgressThrottler();

        await Task.Run(async () =>
        {
            foreach (string sourcePath in sourcePaths)
            {
                // Check for pause
                if (pauseToken != null)
                    await pauseToken.WaitWhilePausedAsync(ct);

                if (ct.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }

                try
                {
                    string itemName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(destination, itemName);

                    progressData.CurrentFileName = itemName;
                    ReportProgress(progress, progressData, throttler);

                    // Handle overwrite
                    if (overwrite)
                    {
                        if (File.Exists(destPath))
                            File.Delete(destPath);
                        else if (Directory.Exists(destPath))
                            Directory.Delete(destPath, true);
                    }

                    if (File.Exists(sourcePath))
                    {
                        // Try fast move first (same volume)
                        if (TryFastMove(sourcePath, destPath))
                        {
                            long fileSize = new FileInfo(destPath).Length;
                            progressData.CompletedBytes += fileSize;
                            progressData.CompletedFiles++;
                            result.SuccessCount++;
                        }
                        else
                        {
                            // Cross-volume: copy + delete
                            await CopyFileWithProgressAsync(
                                sourcePath, destPath, overwrite,
                                bytesWritten =>
                                {
                                    progressData.CompletedBytes += bytesWritten;
                                    ReportProgress(progress, progressData, throttler);
                                },
                                ct, pauseToken);

                            File.Delete(sourcePath);
                            progressData.CompletedFiles++;
                            result.SuccessCount++;
                        }
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        // Try fast move for directory
                        if (TryFastMove(sourcePath, destPath))
                        {
                            // Fast move succeeded - update progress for all files
                            var (dirFiles, dirBytes) = CountDirectoryContents(destPath);
                            progressData.CompletedBytes += dirBytes;
                            progressData.CompletedFiles += dirFiles;
                            result.SuccessCount++;
                        }
                        else
                        {
                            // Cross-volume: copy + delete
                            await CopyDirectoryAsync(
                                sourcePath, destPath, overwrite,
                                progressData, progress, throttler,
                                result, ct, pauseToken);

                            if (!ct.IsCancellationRequested)
                            {
                                Directory.Delete(sourcePath, true);
                            }
                        }
                    }

                    ReportProgress(progress, progressData, throttler);
                }
                catch (OperationCanceledException)
                {
                    result.WasCancelled = true;
                    break;
                }
                catch (Exception e)
                {
                    result.FailCount++;
                    result.Errors.Add($"Failed to move {sourcePath}: {e.Message}");
                    Debug.LogError($"[FileOperationService] Move failed: {sourcePath} - {e.Message}");
                }
            }

            // Final progress update
            progress?.Report(progressData);

        }, ct);

        return result;
    }

    /// <summary>
    /// Async delete operation with progress reporting
    /// </summary>
    public static async Task<FileOperationResult> DeleteAsync(
        List<string> paths,
        IProgress<FileOperationProgress> progress,
        CancellationToken ct,
        PauseToken pauseToken = null)
    {
        var result = new FileOperationResult();

        // Pre-calculate total file count
        int totalFiles = await CalculateTotalFileCountAsync(paths, ct);

        var progressData = new FileOperationProgress
        {
            TotalFiles = totalFiles,
            TotalBytes = totalFiles, // Use file count as "bytes" for progress calculation
            CompletedBytes = 0
        };

        var throttler = new ProgressThrottler();

        await Task.Run(async () =>
        {
            foreach (string path in paths)
            {
                // Check for pause
                if (pauseToken != null)
                    await pauseToken.WaitWhilePausedAsync(ct);

                if (ct.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }

                try
                {
                    string itemName = Path.GetFileName(path);
                    progressData.CurrentFileName = itemName;
                    ReportProgress(progress, progressData, throttler);

                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        progressData.CompletedFiles++;
                        progressData.CompletedBytes++;
                        result.SuccessCount++;
                        ReportProgress(progress, progressData, throttler);
                    }
                    else if (Directory.Exists(path))
                    {
                        // Delete directory contents with progress
                        DeleteDirectoryWithProgress(path, progressData, progress, throttler, result, ct);

                        if (!ct.IsCancellationRequested)
                        {
                            // Finally delete the directory itself
                            Directory.Delete(path, true);
                            result.SuccessCount++;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[FileOperationService] Path not found: {path}");
                        result.FailCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    result.WasCancelled = true;
                    break;
                }
                catch (Exception e)
                {
                    result.FailCount++;
                    result.Errors.Add($"Failed to delete {path}: {e.Message}");
                    Debug.LogError($"[FileOperationService] Delete failed: {path} - {e.Message}");
                }
            }

            // Final progress update
            progress?.Report(progressData);

        }, ct);

        return result;
    }

    #endregion

    #region Private Helpers

    private static void CalculateDirectorySize(string dir, ref int fileCount, ref long totalBytes, CancellationToken ct)
    {
        try
        {
            foreach (string file in Directory.GetFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                fileCount++;
                totalBytes += new FileInfo(file).Length;
            }

            foreach (string subDir in Directory.GetDirectories(dir))
            {
                ct.ThrowIfCancellationRequested();
                CalculateDirectorySize(subDir, ref fileCount, ref totalBytes, ct);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories
        }
    }

    private static int CountFilesInDirectory(string dir, CancellationToken ct)
    {
        int count = 0;
        try
        {
            foreach (string file in Directory.GetFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                count++;
            }

            foreach (string subDir in Directory.GetDirectories(dir))
            {
                ct.ThrowIfCancellationRequested();
                count += CountFilesInDirectory(subDir, ct);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Skip inaccessible directories
        }
        return count;
    }

    private static (int fileCount, long totalBytes) CountDirectoryContents(string dir)
    {
        int fileCount = 0;
        long totalBytes = 0;

        try
        {
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                fileCount++;
                totalBytes += new FileInfo(file).Length;
            }
        }
        catch { }

        return (fileCount, totalBytes);
    }

    private static async Task CopyFileWithProgressAsync(
        string source,
        string dest,
        bool overwrite,
        Action<long> onBytesWritten,
        CancellationToken ct,
        PauseToken pauseToken = null)
    {
        // Ensure destination directory exists
        string destDir = Path.GetDirectoryName(dest);
        if (!Directory.Exists(destDir))
            Directory.CreateDirectory(destDir);

        using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_SIZE, true))
        using (var destStream = new FileStream(dest, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, BUFFER_SIZE, true))
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                // Check for pause during file copy
                if (pauseToken != null)
                    await pauseToken.WaitWhilePausedAsync(ct);

                ct.ThrowIfCancellationRequested();

                await destStream.WriteAsync(buffer, 0, bytesRead, ct);

                onBytesWritten?.Invoke(bytesRead);
            }
        }

        // Copy file attributes
        try
        {
            File.SetCreationTime(dest, File.GetCreationTime(source));
            File.SetLastWriteTime(dest, File.GetLastWriteTime(source));
        }
        catch { }
    }

    private static async Task CopyDirectoryAsync(
        string sourceDir,
        string destDir,
        bool overwrite,
        FileOperationProgress progressData,
        IProgress<FileOperationProgress> progress,
        ProgressThrottler throttler,
        FileOperationResult result,
        CancellationToken ct,
        PauseToken pauseToken = null)
    {
        // Create destination directory
        Directory.CreateDirectory(destDir);

        // Copy files
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            // Check for pause
            if (pauseToken != null)
                await pauseToken.WaitWhilePausedAsync(ct);

            ct.ThrowIfCancellationRequested();

            try
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(destDir, fileName);

                progressData.CurrentFileName = fileName;
                ReportProgress(progress, progressData, throttler);

                await CopyFileWithProgressAsync(
                    file, destFile, overwrite,
                    bytesWritten =>
                    {
                        progressData.CompletedBytes += bytesWritten;
                        ReportProgress(progress, progressData, throttler);
                    },
                    ct, pauseToken);

                progressData.CompletedFiles++;
                result.SuccessCount++;
            }
            catch (Exception e)
            {
                result.FailCount++;
                result.Errors.Add($"Failed to copy {file}: {e.Message}");
            }
        }

        // Recursively copy subdirectories
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            // Check for pause
            if (pauseToken != null)
                await pauseToken.WaitWhilePausedAsync(ct);

            ct.ThrowIfCancellationRequested();

            string dirName = Path.GetFileName(subDir);
            string destSubDir = Path.Combine(destDir, dirName);

            await CopyDirectoryAsync(
                subDir, destSubDir, overwrite,
                progressData, progress, throttler,
                result, ct, pauseToken);
        }
    }

    private static void DeleteDirectoryWithProgress(
        string dir,
        FileOperationProgress progressData,
        IProgress<FileOperationProgress> progress,
        ProgressThrottler throttler,
        FileOperationResult result,
        CancellationToken ct)
    {
        try
        {
            // Delete files first
            foreach (string file in Directory.GetFiles(dir))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    string fileName = Path.GetFileName(file);
                    progressData.CurrentFileName = fileName;

                    File.Delete(file);
                    progressData.CompletedFiles++;
                    progressData.CompletedBytes++;
                    ReportProgress(progress, progressData, throttler);
                }
                catch (Exception e)
                {
                    result.FailCount++;
                    result.Errors.Add($"Failed to delete {file}: {e.Message}");
                }
            }

            // Recursively delete subdirectories
            foreach (string subDir in Directory.GetDirectories(dir))
            {
                ct.ThrowIfCancellationRequested();
                DeleteDirectoryWithProgress(subDir, progressData, progress, throttler, result, ct);

                try
                {
                    Directory.Delete(subDir);
                }
                catch { }
            }
        }
        catch (UnauthorizedAccessException e)
        {
            result.FailCount++;
            result.Errors.Add($"Access denied: {dir} - {e.Message}");
        }
    }

    private static bool TryFastMove(string source, string dest)
    {
        try
        {
            // Check if same volume (fast move possible)
            string sourceRoot = Path.GetPathRoot(source);
            string destRoot = Path.GetPathRoot(dest);

            if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(source))
                    File.Move(source, dest);
                else if (Directory.Exists(source))
                    Directory.Move(source, dest);

                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void ReportProgress(
        IProgress<FileOperationProgress> progress,
        FileOperationProgress data,
        ProgressThrottler throttler)
    {
        if (progress == null) return;

        DateTime now = DateTime.Now;
        if ((now - throttler.LastUpdate).TotalMilliseconds >= PROGRESS_UPDATE_INTERVAL_MS)
        {
            // Create a copy to avoid race conditions
            progress.Report(new FileOperationProgress
            {
                TotalFiles = data.TotalFiles,
                CompletedFiles = data.CompletedFiles,
                CurrentFileName = data.CurrentFileName,
                TotalBytes = data.TotalBytes,
                CompletedBytes = data.CompletedBytes
            });
            throttler.LastUpdate = now;
        }
    }

    #endregion
}
