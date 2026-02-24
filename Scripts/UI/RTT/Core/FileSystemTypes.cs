using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using VRWorkspace.Utilities;
using VRWorkspace.UI.RTT.Services;

namespace VRWorkspace.UI.RTT
{
    public struct MockFile
    {
        public string Name;
        public string Path;
        public bool IsFolder;
        public bool IsFolderEmpty;    // For folders: true if no children
        public string Type;           // File extension or "Folder"
        public DateTime Created;
        public DateTime Modified;
        public long Size;             // Bytes
        public TimeSpan Duration;     // For media files

        // Image/Video dimensions
        public int Width;
        public int Height;

        // Video metadata
        public float FrameRate;
        public long DataRate;         // bits per second
        public long TotalBitrate;     // bits per second

        // Music metadata
        public string Artist;
        public string Album;
        public string Genre;
        public string Title;
        public int BitRate;           // kbps
    }

    public static class FileSystemService
    {
        // Root path for file browsing
        private static string _rootPath;

        public static string RootPath
        {
            get
            {
                if (string.IsNullOrEmpty(_rootPath))
                {
                    _rootPath = GetPlatformRootPath();
                    Debug.Log($"[FileSystemService] Root path set to: {_rootPath}");
                }
                return _rootPath;
            }
        }

        private static string GetPlatformRootPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Android - Get internal storage path via Android API
            try
            {
                using (AndroidJavaClass environment = new AndroidJavaClass("android.os.Environment"))
                {
                    // Get external storage directory (shared storage accessible to user)
                    using (AndroidJavaObject externalDir = environment.CallStatic<AndroidJavaObject>("getExternalStorageDirectory"))
                    {
                        string path = externalDir.Call<string>("getAbsolutePath");
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        {
                            return path;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileSystemService] Failed to get Android storage path: {ex.Message}");
            }

            // Fallback for Android - try common paths
            string[] androidPaths = new string[]
            {
                "/storage/emulated/0",  // Primary internal storage
                "/sdcard",               // Legacy path (symlink)
                Application.persistentDataPath
            };

            foreach (string path in androidPaths)
            {
                if (Directory.Exists(path))
                {
                    return path;
                }
            }

            return Application.persistentDataPath;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Windows - use user's home directory
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            // macOS - use user's home directory
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#else
            // Fallback to persistent data path
            return Application.persistentDataPath;
#endif
        }

        public static string GetAbsolutePath(string relativePath)
        {
            if (relativePath == "root" || string.IsNullOrEmpty(relativePath))
            {
                return RootPath;
            }

            // If path starts with "root/", replace with actual root
            if (relativePath.StartsWith("root/"))
            {
                return Path.Combine(RootPath, relativePath.Substring(5));
            }

            // If already absolute path, return as is
            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            return Path.Combine(RootPath, relativePath);
        }

        public static List<MockFile> GetFiles(string path)
        {
            var list = new List<MockFile>();
            string absolutePath = GetAbsolutePath(path);

            Debug.Log($"[FileSystemService] Reading directory: {absolutePath}");

            try
            {
                if (!Directory.Exists(absolutePath))
                {
                    Debug.LogWarning($"[FileSystemService] Directory not found: {absolutePath}");
                    return list;
                }

                // Get directories
                string[] directories = Directory.GetDirectories(absolutePath);
                foreach (string dirPath in directories)
                {
                    try
                    {
                        DirectoryInfo dirInfo = new DirectoryInfo(dirPath);

                        // Skip hidden and system directories
                        if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                            (dirInfo.Attributes & FileAttributes.System) != 0)
                        {
                            continue;
                        }

                        // Skip expensive empty check - will be done lazily if needed
                        list.Add(new MockFile
                        {
                            Name = TextEncodingHelper.FixString(dirInfo.Name),
                            Path = dirPath,
                            IsFolder = true,
                            IsFolderEmpty = false, // Default to not empty, check lazily
                            Type = "Folder",
                            Created = dirInfo.CreationTime,
                            Modified = dirInfo.LastWriteTime,
                            Size = 0,
                            Duration = TimeSpan.Zero
                        });
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip directories we can't access
                        continue;
                    }
                }

                // Get files
                string[] files = Directory.GetFiles(absolutePath);

                foreach (string filePath in files)
                {
                    try
                    {
                        FileInfo fileInfo = new FileInfo(filePath);

                        // Skip hidden and system files
                        if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 ||
                            (fileInfo.Attributes & FileAttributes.System) != 0)
                        {
                            continue;
                        }

                        string extension = fileInfo.Extension.TrimStart('.').ToLower();
                        if (string.IsNullOrEmpty(extension)) extension = "file";

                        string displayName = TextEncodingHelper.FixString(fileInfo.Name);

                        list.Add(new MockFile
                        {
                            Name = displayName,
                            Path = filePath,
                            IsFolder = false,
                            Type = extension,
                            Created = fileInfo.CreationTime,
                            Modified = fileInfo.LastWriteTime,
                            Size = fileInfo.Length,
                            Duration = GetMediaDuration(filePath, extension)
                        });
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileSystemService] Error reading directory: {ex.Message}");
            }

            Debug.Log($"[FileSystemService] Found {list.Count} items ({list.FindAll(f => f.IsFolder).Count} folders, {list.FindAll(f => !f.IsFolder).Count} files)");
            return list;
        }

        /// <summary>
        /// Async version of GetFiles that yields periodically to prevent UI freezing.
        /// Use this for directories that may contain many items.
        /// </summary>
        /// <param name="path">Directory path to read</param>
        /// <param name="onQuickCount">Optional callback with estimated total count (fires immediately, before enumeration)</param>
        /// <param name="onFirstBatch">Optional callback when first 8 items are ready (for immediate display)</param>
        /// <param name="onProgress">Optional callback for progress updates (items loaded so far)</param>
        /// <param name="onComplete">Callback with the final list of files</param>
        public static System.Collections.IEnumerator GetFilesAsync(string path, Action<int> onQuickCount, Action<List<MockFile>> onFirstBatch, Action<int> onProgress, Action<List<MockFile>> onComplete)
        {
            const int BATCH_SIZE = 50; // Yield every 50 items
            const int FIRST_BATCH_SIZE = 8; // Show first 8 items immediately
            bool firstBatchSent = false;
            var list = new List<MockFile>();
            string absolutePath = GetAbsolutePath(path);
            int processed = 0;
            bool shouldYield = false;

            Debug.Log($"[FileSystemService] Reading directory async: {absolutePath}");

            if (!Directory.Exists(absolutePath))
            {
                Debug.LogWarning($"[FileSystemService] Directory not found: {absolutePath}");
                onQuickCount?.Invoke(0);
                onComplete?.Invoke(list);
                yield break;
            }

            // Get directories
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(absolutePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileSystemService] Error getting directories: {ex.Message}");
                onQuickCount?.Invoke(0);
                onComplete?.Invoke(list);
                yield break;
            }

            // Get files array for quick count (very fast - just gets file names, no metadata)
            string[] files;
            try
            {
                files = Directory.GetFiles(absolutePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FileSystemService] Error getting files: {ex.Message}");
                onQuickCount?.Invoke(directories.Length);
                onComplete?.Invoke(list);
                yield break;
            }

            // === QUICK COUNT: Fire immediately with estimated total ===
            // Note: This is an estimate - actual count may be less due to hidden/system files being filtered
            int estimatedTotal = directories.Length + files.Length;
            Debug.Log($"[FileSystemService] Quick count: {estimatedTotal} items (dirs={directories.Length}, files={files.Length})");
            onQuickCount?.Invoke(estimatedTotal);

            foreach (string dirPath in directories)
            {
                // Process directory in try block, set yield flag outside
                MockFile? dirFile = null;
                try
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dirPath);

                    // Skip hidden and system directories
                    if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                        (dirInfo.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    // Skip expensive empty check - default to not empty
                    dirFile = new MockFile
                    {
                        Name = TextEncodingHelper.FixString(dirInfo.Name),
                        Path = dirPath,
                        IsFolder = true,
                        IsFolderEmpty = false,
                        Type = "Folder",
                        Created = dirInfo.CreationTime,
                        Modified = dirInfo.LastWriteTime,
                        Size = 0,
                        Duration = TimeSpan.Zero
                    };
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                // Add and check yield outside try-catch
                if (dirFile.HasValue)
                {
                    list.Add(dirFile.Value);
                    processed++;

                    // Send first batch immediately for fast UI display
                    if (!firstBatchSent && list.Count >= FIRST_BATCH_SIZE)
                    {
                        firstBatchSent = true;
                        onFirstBatch?.Invoke(new List<MockFile>(list)); // Copy to avoid mutation
                    }

                    if (processed % BATCH_SIZE == 0)
                    {
                        shouldYield = true;
                    }
                }

                // Yield outside try-catch block
                if (shouldYield)
                {
                    onProgress?.Invoke(list.Count);
                    yield return null;
                    shouldYield = false;
                }
            }

            // Process files (files array already obtained for quick count above)
            foreach (string filePath in files)
            {
                // Process file in try block
                MockFile? fileItem = null;
                try
                {
                    FileInfo fileInfo = new FileInfo(filePath);

                    // Skip hidden and system files
                    if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 ||
                        (fileInfo.Attributes & FileAttributes.System) != 0)
                    {
                        continue;
                    }

                    string extension = fileInfo.Extension.TrimStart('.').ToLower();
                    if (string.IsNullOrEmpty(extension)) extension = "file";

                    string displayName = TextEncodingHelper.FixString(fileInfo.Name);

                    fileItem = new MockFile
                    {
                        Name = displayName,
                        Path = filePath,
                        IsFolder = false,
                        Type = extension,
                        Created = fileInfo.CreationTime,
                        Modified = fileInfo.LastWriteTime,
                        Size = fileInfo.Length,
                        Duration = GetMediaDuration(filePath, extension)
                    };
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (Exception)
                {
                    continue;
                }

                // Add and check yield outside try-catch
                if (fileItem.HasValue)
                {
                    list.Add(fileItem.Value);
                    processed++;

                    // Send first batch immediately for fast UI display
                    if (!firstBatchSent && list.Count >= FIRST_BATCH_SIZE)
                    {
                        firstBatchSent = true;
                        onFirstBatch?.Invoke(new List<MockFile>(list)); // Copy to avoid mutation
                    }

                    if (processed % BATCH_SIZE == 0)
                    {
                        shouldYield = true;
                    }
                }

                // Yield outside try-catch block
                if (shouldYield)
                {
                    onProgress?.Invoke(list.Count);
                    yield return null;
                    shouldYield = false;
                }
            }

            // If we finished but never sent first batch (less than 8 items), send what we have
            if (!firstBatchSent && list.Count > 0)
            {
                onFirstBatch?.Invoke(new List<MockFile>(list));
            }

            Debug.Log($"[FileSystemService] Found {list.Count} items async ({list.FindAll(f => f.IsFolder).Count} folders, {list.FindAll(f => !f.IsFolder).Count} files)");
            onComplete?.Invoke(list);
        }

        private static TimeSpan GetMediaDuration(string filePath, string extension)
        {
            // Media duration would require additional libraries
            // For now, return zero - can be extended later with NAudio, FFmpeg, etc.
            return TimeSpan.Zero;
        }

        public static string GetParentPath(string path)
        {
            string absolutePath = GetAbsolutePath(path);

            // Don't go above root
            if (absolutePath == RootPath || string.IsNullOrEmpty(absolutePath))
            {
                return "root";
            }

            string parentPath = Directory.GetParent(absolutePath)?.FullName;

            if (string.IsNullOrEmpty(parentPath) || parentPath == RootPath)
            {
                return "root";
            }

            return parentPath;
        }

        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }

        public static string GetSDCardPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Try common SD card paths on Android
            string[] sdcardPaths = new string[]
            {
                "/storage/sdcard1",
                "/storage/extSdCard",
                "/storage/external_SD"
            };

            foreach (string path in sdcardPaths)
            {
                if (Directory.Exists(path))
                {
                    return path;
                }
            }
#endif
            // Fallback to root path (no SD card)
            return RootPath;
        }

        public static string GetDownloadsPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string downloadsPath = Path.Combine(RootPath, "Download");
            if (Directory.Exists(downloadsPath))
            {
                return downloadsPath;
            }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string downloadsPath = Path.Combine(RootPath, "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                return downloadsPath;
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            string downloadsPath = Path.Combine(RootPath, "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                return downloadsPath;
            }
#endif
            return RootPath;
        }

        public static string GetVideosPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Try DCIM and Movies folders
            string dcimPath = Path.Combine(RootPath, "DCIM");
            string moviesPath = Path.Combine(RootPath, "Movies");

            if (Directory.Exists(moviesPath))
            {
                return moviesPath;
            }
            if (Directory.Exists(dcimPath))
            {
                return dcimPath;
            }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (!string.IsNullOrEmpty(videosPath) && Directory.Exists(videosPath))
            {
                return videosPath;
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            string moviesPath = Path.Combine(RootPath, "Movies");
            if (Directory.Exists(moviesPath))
            {
                return moviesPath;
            }
#endif
            return RootPath;
        }

        public static string GetMusicPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            string musicPath = Path.Combine(RootPath, "Music");
            if (Directory.Exists(musicPath))
            {
                return musicPath;
            }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            string musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!string.IsNullOrEmpty(musicPath) && Directory.Exists(musicPath))
            {
                return musicPath;
            }
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            string musicPath = Path.Combine(RootPath, "Music");
            if (Directory.Exists(musicPath))
            {
                return musicPath;
            }
#endif
            return RootPath;
        }

        #region Recursive Category Scan

        /// <summary>
        /// Scan all files of a specific category recursively from root storage.
        /// This is an async operation that yields periodically to prevent freezing.
        /// </summary>
        /// <param name="category">File category to filter (Video, Music, Image)</param>
        /// <param name="onProgress">Progress callback (filesFound count)</param>
        /// <param name="onFilesFound">Incremental callback when new files are found (for real-time UI update)</param>
        /// <param name="onComplete">Completion callback with all results</param>
        public static System.Collections.IEnumerator ScanAllFilesByCategory(
            FileCategory category,
            Action<int> onProgress,
            Action<List<MockFile>> onFilesFound,
            Action<List<MockFile>> onComplete)
        {
            var results = new List<MockFile>();
            var extensions = FileCategoryHelper.GetExtensionsForCategory(category);

            if (extensions.Count == 0)
            {
                onComplete?.Invoke(results);
                yield break;
            }

            // Folders to scan (start from root and common media locations)
            var foldersToScan = new Queue<string>();
            var scannedFolders = new HashSet<string>();

            // Add root path
            foldersToScan.Enqueue(RootPath);

            // Add SD card if available
            string sdCardPath = GetSDCardPath();
            if (sdCardPath != RootPath && Directory.Exists(sdCardPath))
            {
                foldersToScan.Enqueue(sdCardPath);
            }

            int totalFoldersScanned = 0;
            int filesFound = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var batchFiles = new List<MockFile>(); // Batch for incremental updates

            // Folders to skip (system folders, hidden folders, etc.)
            var skipFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Android", ".android", ".thumbnails", ".cache",
                "lost+found", "System Volume Information", "$RECYCLE.BIN"
            };

            while (foldersToScan.Count > 0)
            {
                string currentFolder = foldersToScan.Dequeue();

                // Skip if already scanned (avoid loops from symlinks)
                if (scannedFolders.Contains(currentFolder))
                    continue;

                scannedFolders.Add(currentFolder);
                totalFoldersScanned++;

                // Yield and report progress every 50 folders or 100ms
                if (totalFoldersScanned % 50 == 0 || sw.ElapsedMilliseconds > 100)
                {
                    sw.Restart();
                    onProgress?.Invoke(filesFound);

                    // Send batch of found files for incremental UI update
                    if (batchFiles.Count > 0)
                    {
                        onFilesFound?.Invoke(new List<MockFile>(batchFiles));
                        batchFiles.Clear();
                    }

                    yield return null;
                }

                try
                {
                    // Get files in current folder
                    string[] files;
                    try
                    {
                        files = Directory.GetFiles(currentFolder);
                    }
                    catch { files = new string[0]; }

                    foreach (string filePath in files)
                    {
                        try
                        {
                            FileInfo fileInfo = new FileInfo(filePath);

                            // Skip hidden/system files
                            if ((fileInfo.Attributes & FileAttributes.Hidden) != 0 ||
                                (fileInfo.Attributes & FileAttributes.System) != 0)
                                continue;

                            string ext = fileInfo.Extension.TrimStart('.').ToLower();

                            // Check if extension matches category
                            if (extensions.Contains(ext))
                            {
                                var file = new MockFile
                                {
                                    Name = TextEncodingHelper.FixString(fileInfo.Name),
                                    Path = filePath,
                                    IsFolder = false,
                                    Type = ext,
                                    Created = fileInfo.CreationTime,
                                    Modified = fileInfo.LastWriteTime,
                                    Size = fileInfo.Length,
                                    Duration = TimeSpan.Zero
                                };
                                results.Add(file);
                                batchFiles.Add(file);
                                filesFound++;
                            }
                        }
                        catch { /* Skip inaccessible files */ }
                    }

                    // Add subdirectories to scan queue
                    string[] subdirs;
                    try
                    {
                        subdirs = Directory.GetDirectories(currentFolder);
                    }
                    catch { subdirs = new string[0]; }

                    foreach (string subdir in subdirs)
                    {
                        try
                        {
                            DirectoryInfo dirInfo = new DirectoryInfo(subdir);

                            // Skip hidden/system directories
                            if ((dirInfo.Attributes & FileAttributes.Hidden) != 0 ||
                                (dirInfo.Attributes & FileAttributes.System) != 0)
                                continue;

                            // Skip known system folders
                            if (skipFolders.Contains(dirInfo.Name))
                                continue;

                            // Skip folders starting with '.'
                            if (dirInfo.Name.StartsWith("."))
                                continue;

                            foldersToScan.Enqueue(subdir);
                        }
                        catch { /* Skip inaccessible directories */ }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip folders we can't access
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FileSystemService] Error scanning {currentFolder}: {ex.Message}");
                }
            }

            // Send any remaining batched files
            if (batchFiles.Count > 0)
            {
                onFilesFound?.Invoke(new List<MockFile>(batchFiles));
                batchFiles.Clear();
            }

            Debug.Log($"[FileSystemService] Category scan complete: {filesFound} {category} files found in {totalFoldersScanned} folders");
            onProgress?.Invoke(filesFound);
            onComplete?.Invoke(results);
        }

        #endregion

        /// <summary>
        /// Check if folder creation is allowed in the given path.
        /// Returns false for:
        /// - Virtual paths like "root" (can't create in device root listing)
        /// - Device root path (Internal Storage root - for cleanliness)
        /// - Paths that don't exist
        /// - Paths without write permission
        /// </summary>
        public static bool CanCreateFolderInPath(string path)
        {
            Debug.Log($"[FileSystemService] CanCreateFolderInPath called with path: '{path}'");

            // Can't create folders in virtual "root" path (device listing)
            if (path == "root" || string.IsNullOrEmpty(path))
            {
                Debug.Log($"[FileSystemService] Path is 'root' or empty, returning false");
                return false;
            }

            string absolutePath = GetAbsolutePath(path);
            Debug.Log($"[FileSystemService] absolutePath: '{absolutePath}'");

            // Can't create folders directly in device root (Internal Storage)
            // Normalize paths for comparison (remove trailing slashes)
            string normalizedAbsolute = absolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRoot = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            Debug.Log($"[FileSystemService] Comparing normalizedAbsolute: '{normalizedAbsolute}' with normalizedRoot: '{normalizedRoot}'");

            bool isDeviceRoot = string.Equals(normalizedAbsolute, normalizedRoot, StringComparison.OrdinalIgnoreCase);
            Debug.Log($"[FileSystemService] isDeviceRoot: {isDeviceRoot}");

            if (isDeviceRoot)
            {
                Debug.Log($"[FileSystemService] Folder creation not allowed in device root: {absolutePath}");
                return false;
            }

            // Path must exist
            if (!Directory.Exists(absolutePath))
            {
                Debug.Log($"[FileSystemService] Path does not exist: {absolutePath}");
                return false;
            }

            // Check write permission by attempting to create a temp directory
            try
            {
                string testPath = Path.Combine(absolutePath, ".vrworkspace_write_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(testPath);
                Directory.Delete(testPath);
                Debug.Log($"[FileSystemService] Write permission OK for: {absolutePath}");
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Debug.Log($"[FileSystemService] No write permission for: {absolutePath}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.Log($"[FileSystemService] Cannot write to {absolutePath}: {ex.Message}");
                return false;
            }
        }

        #region Text File Reading with Encoding Detection

        /// <summary>
        /// Read text file content with automatic encoding detection and mojibake fixing.
        /// </summary>
        /// <param name="path">File path (can be relative or absolute)</param>
        /// <returns>Properly decoded text content</returns>
        public static string ReadTextFileContent(string path)
        {
            string absolutePath = GetAbsolutePath(path);
            return TextEncodingHelper.ReadTextFile(absolutePath);
        }

        /// <summary>
        /// Read text file content with size limit and automatic encoding detection.
        /// Useful for previewing large files.
        /// </summary>
        /// <param name="path">File path (can be relative or absolute)</param>
        /// <param name="maxBytes">Maximum bytes to read (0 = no limit)</param>
        /// <returns>Properly decoded text content</returns>
        public static string ReadTextFileContentWithLimit(string path, int maxBytes = 10240)
        {
            string absolutePath = GetAbsolutePath(path);
            return TextEncodingHelper.ReadTextFileWithLimit(absolutePath, maxBytes);
        }

        /// <summary>
        /// Get the detected encoding of a text file.
        /// </summary>
        public static string GetFileEncoding(string path)
        {
            string absolutePath = GetAbsolutePath(path);
            return TextEncodingHelper.GetEncodingName(absolutePath);
        }

        /// <summary>
        /// Check if a file is likely a text file based on extension.
        /// </summary>
        public static bool IsTextFile(string extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;

            string ext = extension.TrimStart('.').ToLower();
            string[] textExtensions = {
                "txt", "md", "markdown", "json", "xml", "html", "htm", "css", "js",
                "ts", "cs", "java", "py", "rb", "php", "c", "cpp", "h", "hpp",
                "yaml", "yml", "ini", "cfg", "conf", "log", "sh", "bat", "ps1",
                "sql", "csv", "tsv", "rtf", "tex", "rst", "org", "wiki",
                "gradle", "properties", "gitignore", "dockerignore", "editorconfig"
            };

            return Array.Exists(textExtensions, e => e == ext);
        }

        #endregion
    }

}
