using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Services;
using VRWorkspace.Utilities;

namespace VRWorkspace.UI.RTT.Controllers
{
    public partial class RTTFileManagerController
    {
        /// <summary>
        /// Check if folder creation is allowed in the current path.
        /// </summary>
        public bool CanCreateFolderHere()
        {
            Debug.Log($"[Controller] CanCreateFolderHere called, _currentPath: '{_currentPath}'");
            bool result = FileSystemService.CanCreateFolderInPath(_currentPath);
            Debug.Log($"[Controller] CanCreateFolderHere result: {result}");
            return result;
        }

        /// <summary>
        /// Create a new folder in the current directory
        /// </summary>
        public void CreateFolder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                Debug.LogWarning("[Controller] Cannot create folder: name is empty");
                return;
            }

            // Sanitize folder name
            string sanitizedName = SanitizeFolderName(folderName);
            if (string.IsNullOrEmpty(sanitizedName))
            {
                Debug.LogWarning("[Controller] Cannot create folder: invalid name after sanitization");
                return;
            }

            string newFolderPath = System.IO.Path.Combine(_currentPath, sanitizedName);

            try
            {
                if (Directory.Exists(newFolderPath))
                {
                    Debug.LogWarning($"[Controller] Folder already exists: {newFolderPath}");
                    // TODO: Show error notification to user
                    return;
                }

                Directory.CreateDirectory(newFolderPath);
                Debug.Log($"[Controller] Created folder: {newFolderPath}");

                // Refresh current directory to show new folder
                NavigateTo(_currentPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Controller] Failed to create folder: {e.Message}");
                // TODO: Show error notification to user
            }
        }

        private string SanitizeFolderName(string name)
        {
            // Remove invalid characters for file/folder names
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            string result = name;

            foreach (char c in invalidChars)
            {
                result = result.Replace(c.ToString(), "");
            }

            return result.Trim();
        }

        /// <summary>
        /// Rename a file or folder
        /// </summary>
        public void RenameItem(string sourcePath, string newName)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(newName))
            {
                Debug.LogWarning("[Controller] Cannot rename: invalid parameters");
                return;
            }

            // Sanitize new name
            string sanitizedName = SanitizeFolderName(newName);
            if (string.IsNullOrEmpty(sanitizedName))
            {
                Debug.LogWarning("[Controller] Cannot rename: invalid name after sanitization");
                return;
            }

            try
            {
                string directory = System.IO.Path.GetDirectoryName(sourcePath);
                string newPath = System.IO.Path.Combine(directory, sanitizedName);

                // Check if source and destination are the same
                if (sourcePath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log("[Controller] Rename skipped: same name");
                    return;
                }

                // Check if destination already exists
                bool isDirectory = Directory.Exists(sourcePath);
                bool isFile = File.Exists(sourcePath);

                if (!isDirectory && !isFile)
                {
                    Debug.LogWarning($"[Controller] Source does not exist: {sourcePath}");
                    return;
                }

                if (Directory.Exists(newPath) || File.Exists(newPath))
                {
                    Debug.LogWarning($"[Controller] Cannot rename: destination already exists: {newPath}");
                    // TODO: Show error notification to user
                    return;
                }

                if (isDirectory)
                {
                    Directory.Move(sourcePath, newPath);
                    Debug.Log($"[Controller] Renamed folder: {sourcePath} -> {newPath}");
                }
                else
                {
                    // For files, preserve the extension if user didn't provide one
                    string sourceExt = System.IO.Path.GetExtension(sourcePath);
                    string newExt = System.IO.Path.GetExtension(sanitizedName);
                    if (string.IsNullOrEmpty(newExt) && !string.IsNullOrEmpty(sourceExt))
                    {
                        newPath = System.IO.Path.Combine(directory, sanitizedName + sourceExt);
                    }

                    File.Move(sourcePath, newPath);
                    Debug.Log($"[Controller] Renamed file: {sourcePath} -> {newPath}");
                }

                // Refresh current directory to show renamed item
                NavigateTo(_currentPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Controller] Failed to rename: {e.Message}");
                // TODO: Show error notification to user
            }
        }

        /// <summary>
        /// Delete multiple files/folders.
        /// </summary>
        public void DeleteItems(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                Debug.LogWarning("[Controller] No items to delete");
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (string path in paths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        // Delete folder recursively
                        Directory.Delete(path, recursive: true);
                        Debug.Log($"[Controller] Deleted folder: {path}");
                        successCount++;
                    }
                    else if (File.Exists(path))
                    {
                        // Delete file
                        File.Delete(path);
                        Debug.Log($"[Controller] Deleted file: {path}");
                        successCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[Controller] Item not found: {path}");
                        failCount++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Controller] Failed to delete {path}: {e.Message}");
                    failCount++;
                }
            }

            Debug.Log($"[Controller] Delete completed: {successCount} succeeded, {failCount} failed");

            // Refresh current directory to reflect changes
            if (successCount > 0)
            {
                NavigateTo(_currentPath);
            }
        }

        /// <summary>
        /// Copy multiple files/folders to destination.
        /// </summary>
        public void CopyItems(List<string> sourcePaths, string destination, bool overwrite = false)
        {
            if (sourcePaths == null || sourcePaths.Count == 0)
            {
                Debug.LogWarning("[Controller] No items to copy");
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (string sourcePath in sourcePaths)
            {
                try
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(destination, fileName);

                    if (Directory.Exists(sourcePath))
                    {
                        // Copy folder recursively
                        CopyDirectoryRecursive(sourcePath, destPath, overwrite);
                        Debug.Log($"[Controller] Copied folder: {sourcePath} -> {destPath}");
                        successCount++;
                    }
                    else if (File.Exists(sourcePath))
                    {
                        // Copy file
                        if (overwrite || !File.Exists(destPath))
                        {
                            File.Copy(sourcePath, destPath, overwrite);
                            Debug.Log($"[Controller] Copied file: {sourcePath} -> {destPath}");
                            successCount++;
                        }
                        else
                        {
                            Debug.LogWarning($"[Controller] File already exists: {destPath}");
                            failCount++;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Controller] Source not found: {sourcePath}");
                        failCount++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Controller] Failed to copy {sourcePath}: {e.Message}");
                    failCount++;
                }
            }

            Debug.Log($"[Controller] Copy completed: {successCount} succeeded, {failCount} failed");

            // Refresh current directory to show new items
            if (successCount > 0)
            {
                NavigateTo(_currentPath);
            }
        }

        /// <summary>
        /// Move multiple files/folders to destination.
        /// </summary>
        public void MoveItems(List<string> sourcePaths, string destination, bool overwrite = false)
        {
            if (sourcePaths == null || sourcePaths.Count == 0)
            {
                Debug.LogWarning("[Controller] No items to move");
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (string sourcePath in sourcePaths)
            {
                try
                {
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(destination, fileName);

                    // Handle overwrite
                    if (overwrite)
                    {
                        if (Directory.Exists(destPath))
                        {
                            Directory.Delete(destPath, true);
                        }
                        else if (File.Exists(destPath))
                        {
                            File.Delete(destPath);
                        }
                    }

                    if (Directory.Exists(sourcePath))
                    {
                        // Move folder
                        if (!Directory.Exists(destPath))
                        {
                            Directory.Move(sourcePath, destPath);
                            Debug.Log($"[Controller] Moved folder: {sourcePath} -> {destPath}");
                            successCount++;
                        }
                        else
                        {
                            Debug.LogWarning($"[Controller] Destination folder already exists: {destPath}");
                            failCount++;
                        }
                    }
                    else if (File.Exists(sourcePath))
                    {
                        // Move file
                        if (!File.Exists(destPath))
                        {
                            File.Move(sourcePath, destPath);
                            Debug.Log($"[Controller] Moved file: {sourcePath} -> {destPath}");
                            successCount++;
                        }
                        else
                        {
                            Debug.LogWarning($"[Controller] Destination file already exists: {destPath}");
                            failCount++;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[Controller] Source not found: {sourcePath}");
                        failCount++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Controller] Failed to move {sourcePath}: {e.Message}");
                    failCount++;
                }
            }

            Debug.Log($"[Controller] Move completed: {successCount} succeeded, {failCount} failed");

            // Refresh current directory to reflect changes
            if (successCount > 0)
            {
                NavigateTo(_currentPath);
            }
        }

        /// <summary>
        /// Recursively copy a directory.
        /// </summary>
        private void CopyDirectoryRecursive(string sourceDir, string destDir, bool overwrite)
        {
            // Create destination directory
            Directory.CreateDirectory(destDir);

            // Copy files
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite);
            }

            // Copy subdirectories
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectoryRecursive(subDir, destSubDir, overwrite);
            }
        }

        #region Async File Operations

        /// <summary>
        /// Async copy operation with progress reporting.
        /// Runs on background thread to avoid blocking UI.
        /// </summary>
        public async Task<FileOperationService.FileOperationResult> CopyItemsAsync(
            List<string> sourcePaths,
            string destination,
            bool overwrite,
            IProgress<FileOperationService.FileOperationProgress> progress,
            CancellationToken ct,
            FileOperationService.PauseToken pauseToken = null)
        {
            if (sourcePaths == null || sourcePaths.Count == 0)
            {
                Debug.LogWarning("[Controller] No items to copy");
                return new FileOperationService.FileOperationResult();
            }

            Debug.Log($"[Controller] Starting async copy: {sourcePaths.Count} items to {destination}");

            var result = await FileOperationService.CopyAsync(
                sourcePaths, destination, overwrite, progress, ct, pauseToken);

            Debug.Log($"[Controller] Async copy completed: {result.SuccessCount} succeeded, {result.FailCount} failed, cancelled: {result.WasCancelled}");

            // Refresh UI on main thread after completion
            if (result.SuccessCount > 0 && !result.WasCancelled)
            {
                // Use Unity's main thread
                await Task.Yield(); // Ensure we're back on main thread
                NavigateTo(_currentPath);
            }

            return result;
        }

        /// <summary>
        /// Async move operation with progress reporting.
        /// Runs on background thread to avoid blocking UI.
        /// </summary>
        public async Task<FileOperationService.FileOperationResult> MoveItemsAsync(
            List<string> sourcePaths,
            string destination,
            bool overwrite,
            IProgress<FileOperationService.FileOperationProgress> progress,
            CancellationToken ct,
            FileOperationService.PauseToken pauseToken = null)
        {
            if (sourcePaths == null || sourcePaths.Count == 0)
            {
                Debug.LogWarning("[Controller] No items to move");
                return new FileOperationService.FileOperationResult();
            }

            Debug.Log($"[Controller] Starting async move: {sourcePaths.Count} items to {destination}");

            var result = await FileOperationService.MoveAsync(
                sourcePaths, destination, overwrite, progress, ct, pauseToken);

            Debug.Log($"[Controller] Async move completed: {result.SuccessCount} succeeded, {result.FailCount} failed, cancelled: {result.WasCancelled}");

            // Refresh UI on main thread after completion
            if (result.SuccessCount > 0 && !result.WasCancelled)
            {
                await Task.Yield(); // Ensure we're back on main thread
                NavigateTo(_currentPath);
            }

            return result;
        }

        /// <summary>
        /// Async delete operation with progress reporting.
        /// Runs on background thread to avoid blocking UI.
        /// </summary>
        public async Task<FileOperationService.FileOperationResult> DeleteItemsAsync(
            List<string> paths,
            IProgress<FileOperationService.FileOperationProgress> progress,
            CancellationToken ct,
            FileOperationService.PauseToken pauseToken = null)
        {
            if (paths == null || paths.Count == 0)
            {
                Debug.LogWarning("[Controller] No items to delete");
                return new FileOperationService.FileOperationResult();
            }

            Debug.Log($"[Controller] Starting async delete: {paths.Count} items");

            var result = await FileOperationService.DeleteAsync(paths, progress, ct, pauseToken);

            Debug.Log($"[Controller] Async delete completed: {result.SuccessCount} succeeded, {result.FailCount} failed, cancelled: {result.WasCancelled}");

            // Refresh UI on main thread after completion
            if (result.SuccessCount > 0 && !result.WasCancelled)
            {
                await Task.Yield(); // Ensure we're back on main thread
                NavigateTo(_currentPath);
            }

            return result;
        }

        #endregion

        /// <summary>
        /// Get current path for clipboard operations.
        /// </summary>
        public string GetCurrentPath()
        {
            return _currentPath;
        }

        /// <summary>
        /// Get MockFile info for the current folder.
        /// Used to display current folder info in detail panel (e.g., during edit mode).
        /// </summary>
        public MockFile GetCurrentFolderInfo()
        {
            // Convert "root" to actual file system path for Directory operations
            string absolutePath = FileSystemService.GetAbsolutePath(_currentPath);
            DateTime folderModified = DateTime.MinValue;
            try
            {
                if (Directory.Exists(absolutePath))
                {
                    folderModified = Directory.GetLastWriteTime(absolutePath);
                }
            }
            catch { }

            var folderInfo = new MockFile
            {
                Name = TextEncodingHelper.FixString(System.IO.Path.GetFileName(absolutePath)),
                Path = _currentPath,
                IsFolder = true,
                Modified = folderModified
            };
            if (string.IsNullOrEmpty(folderInfo.Name)) folderInfo.Name = "Root";

            return folderInfo;
        }

        /// <summary>
        /// Get MockFile info for a specific file/folder by path.
        /// Returns null if the file doesn't exist or can't be found.
        /// </summary>
        public MockFile? GetFileInfo(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Try to find in current files list first (faster)
            foreach (var file in _filteredFiles)
            {
                if (file.Path == path)
                {
                    return file;
                }
            }

            // If not found in current list, create from path
            try
            {
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    return new MockFile
                    {
                        Name = TextEncodingHelper.FixString(fileInfo.Name),
                        Path = path,
                        IsFolder = false,
                        Type = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
                        Created = fileInfo.CreationTime,
                        Modified = fileInfo.LastWriteTime,
                        Size = fileInfo.Length
                    };
                }
                else if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    return new MockFile
                    {
                        Name = TextEncodingHelper.FixString(dirInfo.Name),
                        Path = path,
                        IsFolder = true,
                        Type = "Folder",
                        Created = dirInfo.CreationTime,
                        Modified = dirInfo.LastWriteTime
                    };
                }
            }
            catch { }

            return null;
        }
    }
}
