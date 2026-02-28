using UnityEngine;
using UnityEngine.Video;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace VRWorkspace.UI.RTT.Services
{
    /// <summary>
    /// Service for reading file metadata (dimensions, duration, etc.)
    /// Uses Unity's native capabilities where possible.
    /// Queue-based processing to prevent main thread blocking.
    /// </summary>
    public class FileMetadataService : MonoBehaviour
    {
        #region Singleton
        private static FileMetadataService _instance;
        public static FileMetadataService Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("FileMetadataService");
                    _instance = go.AddComponent<FileMetadataService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        #endregion

        private VideoPlayer _videoPlayer;

        #region Queue-Based Metadata Loading
        /// <summary>
        /// Request data for queued metadata loading.
        /// </summary>
        private struct MetadataRequest
        {
            public string FilePath;
            public bool IsVideo;
            public Action<VideoMetadata> VideoCallback;
            public Action<AudioMetadata> AudioCallback;
        }

        private Queue<MetadataRequest> _metadataQueue = new Queue<MetadataRequest>();
        private bool _isProcessingQueue = false;
        private Coroutine _queueProcessorCoroutine;

        // Configuration: frame budget for processing metadata requests
        private const float FAST_PATH_BUDGET_MS = 12f;  // Generous budget for header parsing (no GPU impact)
        private const float SLOW_PATH_BUDGET_MS = 4f;   // Tight budget when using VideoPlayer
        private const int MAX_QUEUE_SIZE = 200;  // Allow larger queue since processing is faster
        #endregion

        #region Metadata Cache
        // Pre-loaded metadata cache: path -> metadata (persists across app opens)
        private Dictionary<string, VideoMetadata> _videoMetadataCache = new Dictionary<string, VideoMetadata>();
        private Dictionary<string, AudioMetadata> _audioMetadataCache = new Dictionary<string, AudioMetadata>();
        private Coroutine _preloadCoroutine;
        private bool _isPreloading = false;
        #endregion

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// Read image dimensions from file header (fast, doesn't load entire image).
        /// </summary>
        public void GetImageMetadata(string filePath, Action<int, int> onComplete)
        {
            int width = 0;
            int height = 0;

            try
            {
                if (File.Exists(filePath))
                {
                    // Read dimensions from file header - much faster than loading entire image
                    (width, height) = ReadImageDimensionsFromHeader(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileMetadataService] Failed to read image metadata: {ex.Message}");
            }

            onComplete?.Invoke(width, height);
        }

        /// <summary>
        /// Read image dimensions directly from file header without loading entire image.
        /// Supports PNG, JPEG, GIF, BMP, WebP.
        /// </summary>
        private (int width, int height) ReadImageDimensionsFromHeader(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                // Read first 32 bytes to identify format
                byte[] header = reader.ReadBytes(32);
                if (header.Length < 8) return (0, 0);

                // PNG: 89 50 4E 47 0D 0A 1A 0A
                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                {
                    // Width at offset 16, Height at offset 20 (big-endian)
                    int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    return (width, height);
                }

                // JPEG: FF D8 FF
                if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                {
                    return ReadJpegDimensions(stream, reader);
                }

                // GIF: 47 49 46 38
                if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
                {
                    // Width at offset 6, Height at offset 8 (little-endian)
                    int width = header[6] | (header[7] << 8);
                    int height = header[8] | (header[9] << 8);
                    return (width, height);
                }

                // BMP: 42 4D
                if (header[0] == 0x42 && header[1] == 0x4D)
                {
                    // Width at offset 18, Height at offset 22 (little-endian)
                    int width = header[18] | (header[19] << 8) | (header[20] << 16) | (header[21] << 24);
                    int height = Math.Abs(header[22] | (header[23] << 8) | (header[24] << 16) | (header[25] << 24));
                    return (width, height);
                }

                // WebP: 52 49 46 46 ... 57 45 42 50
                if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                    header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                {
                    return ReadWebPDimensions(stream, reader, header);
                }
            }

            return (0, 0);
        }

        private (int width, int height) ReadJpegDimensions(FileStream stream, BinaryReader reader)
        {
            stream.Seek(2, SeekOrigin.Begin);

            while (stream.Position < stream.Length - 1)
            {
                byte marker1 = reader.ReadByte();
                if (marker1 != 0xFF) continue;

                byte marker2 = reader.ReadByte();

                // SOF markers (Start Of Frame) contain dimensions
                if ((marker2 >= 0xC0 && marker2 <= 0xC3) ||
                    (marker2 >= 0xC5 && marker2 <= 0xC7) ||
                    (marker2 >= 0xC9 && marker2 <= 0xCB) ||
                    (marker2 >= 0xCD && marker2 <= 0xCF))
                {
                    stream.Seek(3, SeekOrigin.Current); // Skip length and precision
                    byte[] dims = reader.ReadBytes(4);
                    int height = (dims[0] << 8) | dims[1];
                    int width = (dims[2] << 8) | dims[3];
                    return (width, height);
                }

                // Skip other markers
                if (marker2 == 0xD8 || marker2 == 0xD9 || marker2 == 0x01 || (marker2 >= 0xD0 && marker2 <= 0xD7))
                    continue;

                // Read segment length and skip
                byte[] lenBytes = reader.ReadBytes(2);
                if (lenBytes.Length < 2) break;
                int segmentLen = (lenBytes[0] << 8) | lenBytes[1];
                stream.Seek(segmentLen - 2, SeekOrigin.Current);
            }

            return (0, 0);
        }

        private (int width, int height) ReadWebPDimensions(FileStream stream, BinaryReader reader, byte[] header)
        {
            // Check VP8 chunk type at offset 12
            if (header.Length < 16) return (0, 0);

            // VP8 (lossy): 56 50 38 20
            if (header[12] == 0x56 && header[13] == 0x50 && header[14] == 0x38 && header[15] == 0x20)
            {
                stream.Seek(26, SeekOrigin.Begin);
                byte[] dims = reader.ReadBytes(4);
                int width = (dims[0] | (dims[1] << 8)) & 0x3FFF;
                int height = (dims[2] | (dims[3] << 8)) & 0x3FFF;
                return (width, height);
            }

            // VP8L (lossless): 56 50 38 4C
            if (header[12] == 0x56 && header[13] == 0x50 && header[14] == 0x38 && header[15] == 0x4C)
            {
                stream.Seek(21, SeekOrigin.Begin);
                byte[] dims = reader.ReadBytes(4);
                int bits = dims[0] | (dims[1] << 8) | (dims[2] << 16) | (dims[3] << 24);
                int width = (bits & 0x3FFF) + 1;
                int height = ((bits >> 14) & 0x3FFF) + 1;
                return (width, height);
            }

            // VP8X (extended): 56 50 38 58
            if (header[12] == 0x56 && header[13] == 0x50 && header[14] == 0x38 && header[15] == 0x58)
            {
                stream.Seek(24, SeekOrigin.Begin);
                byte[] dims = reader.ReadBytes(6);
                int width = (dims[0] | (dims[1] << 8) | (dims[2] << 16)) + 1;
                int height = (dims[3] | (dims[4] << 8) | (dims[5] << 16)) + 1;
                return (width, height);
            }

            return (0, 0);
        }

        /// <summary>
        /// Read video metadata using VideoPlayer.
        /// Returns from cache instantly if available, otherwise queued for processing.
        /// </summary>
        public void GetVideoMetadata(string filePath, Action<VideoMetadata> onComplete)
        {
            // Check cache first - return instantly if we have pre-loaded data
            if (_videoMetadataCache.TryGetValue(filePath, out var cached) && cached.Duration.TotalSeconds > 0)
            {
                onComplete?.Invoke(cached);
                return;
            }

            // Limit queue size to prevent memory bloat
            if (_metadataQueue.Count >= MAX_QUEUE_SIZE)
            {
                // Queue full - return empty metadata immediately
                onComplete?.Invoke(new VideoMetadata());
                return;
            }

            // Add to queue
            _metadataQueue.Enqueue(new MetadataRequest
            {
                FilePath = filePath,
                IsVideo = true,
                VideoCallback = onComplete
            });

            // Start processor if not running
            if (!_isProcessingQueue)
            {
                _queueProcessorCoroutine = StartCoroutine(ProcessMetadataQueue());
            }
        }

        /// <summary>
        /// Cancel all pending metadata requests (e.g., when switching categories).
        /// </summary>
        public void CancelAllPendingRequests()
        {
            _metadataQueue.Clear();
        }

        /// <summary>
        /// Process queued metadata requests in two phases:
        /// Phase 1: Fast inline processing (header parsing + audio) with generous budget
        /// Phase 2: Slow VideoPlayer fallback for remaining items
        /// </summary>
        private IEnumerator ProcessMetadataQueue()
        {
            _isProcessingQueue = true;

            // Collect requests that need slow path (VideoPlayer)
            Queue<MetadataRequest> slowQueue = new Queue<MetadataRequest>();

            // PHASE 1: Process all fast-path requests inline
            while (_metadataQueue.Count > 0)
            {
                float frameStartTime = Time.realtimeSinceStartup * 1000f;

                while (_metadataQueue.Count > 0)
                {
                    var request = _metadataQueue.Dequeue();

                    if (!request.IsVideo)
                    {
                        // Audio metadata: synchronous (~<1ms)
                        ProcessAudioMetadataInline(request, storeInCache: true);
                    }
                    else
                    {
                        // Check cache first
                        if (_videoMetadataCache.TryGetValue(request.FilePath, out var cached) && cached.Duration.TotalSeconds > 0)
                        {
                            try { request.VideoCallback?.Invoke(cached); }
                            catch (Exception ex) { Debug.LogWarning($"[FileMetadataService] Callback error: {ex.Message}"); }
                        }
                        else
                        {
                            // Try header parsing inline first (~1-5ms)
                            VideoMetadata fastResult = TryGetVideoMetadataFromHeaders(request.FilePath);
                            if (fastResult.Duration.TotalSeconds > 0)
                            {
                                _videoMetadataCache[request.FilePath] = fastResult;
                                try { request.VideoCallback?.Invoke(fastResult); }
                                catch (Exception ex) { Debug.LogWarning($"[FileMetadataService] Callback error: {ex.Message}"); }
                            }
                            else
                            {
                                // Header parsing failed - defer to slow queue
                                slowQueue.Enqueue(request);
                            }
                        }
                    }

                    // Check generous budget for fast path
                    float elapsedMs = Time.realtimeSinceStartup * 1000f - frameStartTime;
                    if (elapsedMs >= FAST_PATH_BUDGET_MS)
                        break;
                }

                yield return null;
            }

            // PHASE 2: Process slow-path requests (VideoPlayer) one at a time
            while (slowQueue.Count > 0)
            {
                var request = slowQueue.Dequeue();
                VideoMetadata slowResult = new VideoMetadata();

                yield return StartCoroutine(LoadVideoMetadataViaVideoPlayer(request.FilePath, (metadata) =>
                {
                    slowResult = metadata;
                }));

                // Cache slow-path result too
                if (slowResult.Duration.TotalSeconds > 0)
                    _videoMetadataCache[request.FilePath] = slowResult;

                try { request.VideoCallback?.Invoke(slowResult); }
                catch (Exception ex) { Debug.LogWarning($"[FileMetadataService] Callback error: {ex.Message}"); }

                // Also process any new requests that arrived during VideoPlayer wait
                while (_metadataQueue.Count > 0)
                {
                    float batchStart = Time.realtimeSinceStartup * 1000f;
                    while (_metadataQueue.Count > 0)
                    {
                        var newReq = _metadataQueue.Dequeue();
                        if (!newReq.IsVideo)
                        {
                            ProcessAudioMetadataInline(newReq, storeInCache: true);
                        }
                        else
                        {
                            if (_videoMetadataCache.TryGetValue(newReq.FilePath, out var c) && c.Duration.TotalSeconds > 0)
                            {
                                try { newReq.VideoCallback?.Invoke(c); }
                                catch { }
                            }
                            else
                            {
                                VideoMetadata fr = TryGetVideoMetadataFromHeaders(newReq.FilePath);
                                if (fr.Duration.TotalSeconds > 0)
                                {
                                    _videoMetadataCache[newReq.FilePath] = fr;
                                    try { newReq.VideoCallback?.Invoke(fr); }
                                    catch { }
                                }
                                else
                                {
                                    slowQueue.Enqueue(newReq);
                                }
                            }
                        }

                        float el = Time.realtimeSinceStartup * 1000f - batchStart;
                        if (el >= SLOW_PATH_BUDGET_MS) break;
                    }
                    break; // Process one batch then continue with slow queue
                }
            }

            _isProcessingQueue = false;
            _queueProcessorCoroutine = null;
        }

        /// <summary>
        /// Try to get video metadata from file headers synchronously (~1-5ms).
        /// Returns metadata with Duration > 0 if successful.
        /// </summary>
        private VideoMetadata TryGetVideoMetadataFromHeaders(string filePath)
        {
            var metadata = new VideoMetadata();

            if (!File.Exists(filePath)) return metadata;

            string ext = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant();
            bool isMP4Family = ext == ".mp4" || ext == ".m4v" || ext == ".mov";

            if (!isMP4Family) return metadata;

            try
            {
                var headerInfo = VideoHeaderParser.TryGetVideoInfoFromMP4Headers(filePath);
                if (headerInfo.Duration > 0)
                {
                    metadata.Duration = TimeSpan.FromSeconds(headerInfo.Duration);
                    metadata.Width = headerInfo.Width;
                    metadata.Height = headerInfo.Height;

                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        metadata.TotalBitrate = (long)(fi.Length * 8 / headerInfo.Duration);
                        metadata.DataRate = metadata.TotalBitrate;
                    }
                    catch { }
                }
            }
            catch { }

            return metadata;
        }

        /// <summary>
        /// Process audio metadata synchronously inline (no coroutine overhead).
        /// Audio metadata reading is pure file I/O that completes in <1ms.
        /// </summary>
        private void ProcessAudioMetadataInline(MetadataRequest request, bool storeInCache = false)
        {
            // Check cache first
            if (_audioMetadataCache.TryGetValue(request.FilePath, out var cached) && cached.Duration.TotalSeconds > 0)
            {
                try { request.AudioCallback?.Invoke(cached); }
                catch (Exception ex) { Debug.LogWarning($"[FileMetadataService] Callback error: {ex.Message}"); }
                return;
            }

            var metadata = new AudioMetadata();

            try
            {
                if (File.Exists(request.FilePath))
                {
                    FileInfo fi = new FileInfo(request.FilePath);
                    string ext = fi.Extension.ToLower();

                    if (ext == ".mp3")
                    {
                        ReadMP3Metadata(request.FilePath, ref metadata, fi.Length);
                    }
                    else
                    {
                        int estimatedBitrate = 128;
                        if (ext == ".wav") estimatedBitrate = 1411;
                        else if (ext == ".ogg") estimatedBitrate = 160;

                        metadata.BitRate = estimatedBitrate;
                        double durationSeconds = fi.Length / (estimatedBitrate * 125.0);
                        metadata.Duration = TimeSpan.FromSeconds(durationSeconds);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileMetadataService] Failed to read audio metadata: {ex.Message}");
            }

            // Store in cache if requested
            if (storeInCache && metadata.Duration.TotalSeconds > 0)
                _audioMetadataCache[request.FilePath] = metadata;

            try
            {
                request.AudioCallback?.Invoke(metadata);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileMetadataService] Callback error: {ex.Message}");
            }
        }

        /// <summary>
        /// VideoPlayer fallback for video metadata (non-MP4 formats or header parse failure).
        /// This is the slow path - only called when inline header parsing fails.
        /// </summary>
        private IEnumerator LoadVideoMetadataViaVideoPlayer(string filePath, Action<VideoMetadata> onComplete)
        {
            var metadata = new VideoMetadata();

            if (!File.Exists(filePath))
            {
                onComplete?.Invoke(metadata);
                yield break;
            }

            GameObject tempGO = new GameObject("TempVideoPlayer");
            VideoPlayer vp = tempGO.AddComponent<VideoPlayer>();

            vp.source = VideoSource.Url;
            vp.url = "file://" + filePath;
            vp.playOnAwake = false;
            vp.renderMode = VideoRenderMode.APIOnly;

            bool isPrepared = false;
            bool hasFailed = false;

            vp.prepareCompleted += (source) => isPrepared = true;
            vp.errorReceived += (source, message) =>
            {
                Debug.LogWarning($"[FileMetadataService] Video error: {message}");
                hasFailed = true;
            };

            vp.Prepare();

            // Wait for prepare with timeout
            float timeout = 10f;
            float elapsed = 0f;
            while (!isPrepared && !hasFailed && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (isPrepared && !hasFailed)
            {
                metadata.Width = (int)vp.width;
                metadata.Height = (int)vp.height;
                metadata.Duration = TimeSpan.FromSeconds(vp.length);
                metadata.FrameRate = vp.frameRate;

                // Estimate bitrate from file size and duration
                if (metadata.Duration.TotalSeconds > 0)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        metadata.TotalBitrate = (long)(fi.Length * 8 / metadata.Duration.TotalSeconds);
                        metadata.DataRate = metadata.TotalBitrate;
                    }
                    catch { }
                }
            }

            Destroy(tempGO);
            onComplete?.Invoke(metadata);
        }

        /// <summary>
        /// Read audio metadata.
        /// Note: Unity doesn't support ID3 tags natively.
        /// For full metadata support, consider using TagLibSharp or similar.
        /// Queued to prevent main thread blocking.
        /// </summary>
        public void GetAudioMetadata(string filePath, Action<AudioMetadata> onComplete)
        {
            // Check cache first - return instantly if we have pre-loaded data
            if (_audioMetadataCache.TryGetValue(filePath, out var cached) && cached.Duration.TotalSeconds > 0)
            {
                onComplete?.Invoke(cached);
                return;
            }

            // Limit queue size to prevent memory bloat
            if (_metadataQueue.Count >= MAX_QUEUE_SIZE)
            {
                // Queue full - return empty metadata immediately
                onComplete?.Invoke(new AudioMetadata());
                return;
            }

            // Add to queue
            _metadataQueue.Enqueue(new MetadataRequest
            {
                FilePath = filePath,
                IsVideo = false,
                AudioCallback = onComplete
            });

            // Start processor if not running
            if (!_isProcessingQueue)
            {
                _queueProcessorCoroutine = StartCoroutine(ProcessMetadataQueue());
            }
        }

        private IEnumerator LoadAudioMetadataCoroutineInternal(string filePath, Action<AudioMetadata> onComplete)
        {
            var metadata = new AudioMetadata();

            if (!File.Exists(filePath))
            {
                onComplete?.Invoke(metadata);
                yield break;
            }

            try
            {
                FileInfo fi = new FileInfo(filePath);
                string ext = fi.Extension.ToLower();

                // Try to read ID3 tags for MP3 files
                if (ext == ".mp3")
                {
                    ReadMP3Metadata(filePath, ref metadata, fi.Length);
                }
                else
                {
                    // Rough bitrate estimates for other formats
                    int estimatedBitrate = 128; // kbps default
                    if (ext == ".wav") estimatedBitrate = 1411; // CD quality WAV
                    else if (ext == ".ogg") estimatedBitrate = 160;

                    metadata.BitRate = estimatedBitrate;

                    // Very rough duration estimate
                    double durationSeconds = fi.Length / (estimatedBitrate * 125.0);
                    metadata.Duration = TimeSpan.FromSeconds(durationSeconds);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FileMetadataService] Failed to read audio metadata: {ex.Message}");
            }

            yield return null;
            onComplete?.Invoke(metadata);
        }

        private void ReadMP3Metadata(string filePath, ref AudioMetadata metadata, long fileSize)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                // Try ID3v2 first (at beginning of file)
                bool hasID3v2 = TryReadID3v2(fs, ref metadata);

                // Try ID3v1 (at end of file) if ID3v2 didn't have all info
                if (string.IsNullOrEmpty(metadata.Title) || string.IsNullOrEmpty(metadata.Artist))
                {
                    TryReadID3v1(fs, ref metadata);
                }

                // Read MP3 frame header for bitrate and duration
                ReadMP3FrameInfo(fs, ref metadata, fileSize, hasID3v2);
            }
        }

        private bool TryReadID3v2(FileStream fs, ref AudioMetadata metadata)
        {
            fs.Seek(0, SeekOrigin.Begin);
            byte[] header = new byte[10];
            if (fs.Read(header, 0, 10) != 10) return false;

            // Check ID3v2 signature
            if (header[0] != 'I' || header[1] != 'D' || header[2] != '3') return false;

            int version = header[3];
            int tagSize = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) |
                          ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);

            // Read tag data
            byte[] tagData = new byte[tagSize];
            if (fs.Read(tagData, 0, tagSize) != tagSize) return false;

            int pos = 0;
            while (pos < tagSize - 10)
            {
                // Read frame header (4 bytes ID + 4 bytes size + 2 bytes flags)
                string frameId = System.Text.Encoding.ASCII.GetString(tagData, pos, 4);
                if (string.IsNullOrEmpty(frameId) || frameId[0] == '\0') break;

                int frameSize;
                if (version == 4)
                {
                    // ID3v2.4 uses syncsafe integers for frame size
                    frameSize = ((tagData[pos + 4] & 0x7F) << 21) | ((tagData[pos + 5] & 0x7F) << 14) |
                               ((tagData[pos + 6] & 0x7F) << 7) | (tagData[pos + 7] & 0x7F);
                }
                else
                {
                    // ID3v2.3 uses regular integers
                    frameSize = (tagData[pos + 4] << 24) | (tagData[pos + 5] << 16) |
                               (tagData[pos + 6] << 8) | tagData[pos + 7];
                }

                if (frameSize <= 0 || pos + 10 + frameSize > tagSize) break;

                // Extract text frames
                if (frameSize > 1)
                {
                    string value = ExtractID3v2Text(tagData, pos + 10, frameSize);
                    switch (frameId)
                    {
                        case "TIT2": metadata.Title = value; break;
                        case "TPE1": metadata.Artist = value; break;
                        case "TALB": metadata.Album = value; break;
                        case "TCON": metadata.Genre = CleanGenreString(value); break;
                    }
                }

                pos += 10 + frameSize;
            }

            return true;
        }

        private string ExtractID3v2Text(byte[] data, int offset, int length)
        {
            if (length <= 1) return "";

            byte encoding = data[offset];
            int textStart = offset + 1;
            int textLength = length - 1;

            // Skip BOM if present
            if (encoding == 1 || encoding == 2) // UTF-16
            {
                if (textLength >= 2)
                {
                    if ((data[textStart] == 0xFF && data[textStart + 1] == 0xFE) ||
                        (data[textStart] == 0xFE && data[textStart + 1] == 0xFF))
                    {
                        textStart += 2;
                        textLength -= 2;
                    }
                }
            }

            try
            {
                System.Text.Encoding enc;
                switch (encoding)
                {
                    case 0: enc = System.Text.Encoding.GetEncoding("ISO-8859-1"); break;
                    case 1: enc = System.Text.Encoding.Unicode; break;
                    case 2: enc = System.Text.Encoding.BigEndianUnicode; break;
                    case 3: enc = System.Text.Encoding.UTF8; break;
                    default: enc = System.Text.Encoding.GetEncoding("ISO-8859-1"); break;
                }

                string result = enc.GetString(data, textStart, textLength);
                // Remove null terminators
                int nullIndex = result.IndexOf('\0');
                if (nullIndex >= 0) result = result.Substring(0, nullIndex);
                return result.Trim();
            }
            catch
            {
                return "";
            }
        }

        private void TryReadID3v1(FileStream fs, ref AudioMetadata metadata)
        {
            if (fs.Length < 128) return;

            fs.Seek(-128, SeekOrigin.End);
            byte[] tag = new byte[128];
            if (fs.Read(tag, 0, 128) != 128) return;

            // Check TAG signature
            if (tag[0] != 'T' || tag[1] != 'A' || tag[2] != 'G') return;

            var encoding = System.Text.Encoding.GetEncoding("ISO-8859-1");

            if (string.IsNullOrEmpty(metadata.Title))
                metadata.Title = encoding.GetString(tag, 3, 30).Trim('\0', ' ');
            if (string.IsNullOrEmpty(metadata.Artist))
                metadata.Artist = encoding.GetString(tag, 33, 30).Trim('\0', ' ');
            if (string.IsNullOrEmpty(metadata.Album))
                metadata.Album = encoding.GetString(tag, 63, 30).Trim('\0', ' ');
            if (string.IsNullOrEmpty(metadata.Genre))
            {
                int genreIndex = tag[127];
                metadata.Genre = GetID3v1GenreName(genreIndex);
            }
        }

        private void ReadMP3FrameInfo(FileStream fs, ref AudioMetadata metadata, long fileSize, bool hasID3v2)
        {
            // Find first MP3 frame sync
            fs.Seek(hasID3v2 ? 10 : 0, SeekOrigin.Begin);

            // Skip ID3v2 tag if present
            if (hasID3v2)
            {
                fs.Seek(0, SeekOrigin.Begin);
                byte[] header = new byte[10];
                fs.Read(header, 0, 10);
                int tagSize = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) |
                              ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
                fs.Seek(10 + tagSize, SeekOrigin.Begin);
            }

            // Search for frame sync (0xFF followed by 0xE0-0xFF)
            byte[] buffer = new byte[4];
            int maxSearch = 8192;
            int searched = 0;

            while (searched < maxSearch && fs.Position < fs.Length - 4)
            {
                int b = fs.ReadByte();
                searched++;

                if (b == 0xFF)
                {
                    int b2 = fs.ReadByte();
                    if ((b2 & 0xE0) == 0xE0)
                    {
                        fs.Seek(-2, SeekOrigin.Current);
                        fs.Read(buffer, 0, 4);

                        // Parse frame header
                        int version = (buffer[1] >> 3) & 3;    // 0=2.5, 2=2, 3=1
                        int layer = (buffer[1] >> 1) & 3;      // 1=III, 2=II, 3=I
                        int bitrateIndex = (buffer[2] >> 4) & 0xF;
                        int sampleRateIndex = (buffer[2] >> 2) & 3;

                        if (version != 1 && layer != 0 && bitrateIndex != 0 && bitrateIndex != 15 && sampleRateIndex != 3)
                        {
                            int bitrate = GetMP3Bitrate(version, layer, bitrateIndex);
                            int sampleRate = GetMP3SampleRate(version, sampleRateIndex);

                            if (bitrate > 0 && sampleRate > 0)
                            {
                                metadata.BitRate = bitrate;

                                // Calculate duration from file size and bitrate
                                long audioDataSize = fileSize;
                                if (hasID3v2) audioDataSize -= fs.Position - 4;
                                if (fs.Length >= 128)
                                {
                                    fs.Seek(-128, SeekOrigin.End);
                                    byte[] tagCheck = new byte[3];
                                    fs.Read(tagCheck, 0, 3);
                                    if (tagCheck[0] == 'T' && tagCheck[1] == 'A' && tagCheck[2] == 'G')
                                        audioDataSize -= 128;
                                }

                                double durationSeconds = (audioDataSize * 8.0) / (bitrate * 1000.0);
                                metadata.Duration = TimeSpan.FromSeconds(durationSeconds);
                            }
                        }
                        break;
                    }
                }
            }

            // Fallback if no frame found
            if (metadata.BitRate == 0)
            {
                metadata.BitRate = 192;
                metadata.Duration = TimeSpan.FromSeconds(fileSize / (192 * 125.0));
            }
        }

        private int GetMP3Bitrate(int version, int layer, int index)
        {
            // Bitrate table for MPEG Audio
            int[,] bitratesV1 = {
                { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0 }, // Layer I
                { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0 },    // Layer II
                { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 }      // Layer III
            };
            int[,] bitratesV2 = {
                { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0 },    // Layer I
                { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 },         // Layer II & III
                { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 }
            };

            int layerIndex = 3 - layer; // Convert layer (1,2,3) to array index (2,1,0)
            if (layerIndex < 0 || layerIndex > 2) return 0;

            if (version == 3) // MPEG1
                return bitratesV1[layerIndex, index];
            else // MPEG2 or MPEG2.5
                return bitratesV2[layerIndex, index];
        }

        private int GetMP3SampleRate(int version, int index)
        {
            int[,] sampleRates = {
                { 44100, 48000, 32000, 0 }, // MPEG1
                { 22050, 24000, 16000, 0 }, // MPEG2
                { 11025, 12000, 8000, 0 }   // MPEG2.5
            };

            int versionIndex = version == 3 ? 0 : (version == 2 ? 1 : 2);
            return sampleRates[versionIndex, index];
        }

        private string CleanGenreString(string genre)
        {
            if (string.IsNullOrEmpty(genre)) return "";

            // Handle ID3v2 genre format like "(13)" or "(13)Pop"
            if (genre.StartsWith("("))
            {
                int endParen = genre.IndexOf(')');
                if (endParen > 1)
                {
                    string indexStr = genre.Substring(1, endParen - 1);
                    if (int.TryParse(indexStr, out int genreIndex))
                    {
                        string genreName = GetID3v1GenreName(genreIndex);
                        if (!string.IsNullOrEmpty(genreName))
                            return genreName;
                    }
                    // Return text after parentheses if present
                    if (endParen < genre.Length - 1)
                        return genre.Substring(endParen + 1).Trim();
                }
            }
            return genre;
        }

        private string GetID3v1GenreName(int index)
        {
            string[] genres = {
                "Blues", "Classic Rock", "Country", "Dance", "Disco", "Funk", "Grunge", "Hip-Hop",
                "Jazz", "Metal", "New Age", "Oldies", "Other", "Pop", "R&B", "Rap", "Reggae",
                "Rock", "Techno", "Industrial", "Alternative", "Ska", "Death Metal", "Pranks",
                "Soundtrack", "Euro-Techno", "Ambient", "Trip-Hop", "Vocal", "Jazz+Funk", "Fusion",
                "Trance", "Classical", "Instrumental", "Acid", "House", "Game", "Sound Clip",
                "Gospel", "Noise", "AlternRock", "Bass", "Soul", "Punk", "Space", "Meditative",
                "Instrumental Pop", "Instrumental Rock", "Ethnic", "Gothic", "Darkwave",
                "Techno-Industrial", "Electronic", "Pop-Folk", "Eurodance", "Dream", "Southern Rock",
                "Comedy", "Cult", "Gangsta", "Top 40", "Christian Rap", "Pop/Funk", "Jungle",
                "Native American", "Cabaret", "New Wave", "Psychedelic", "Rave", "Showtunes",
                "Trailer", "Lo-Fi", "Tribal", "Acid Punk", "Acid Jazz", "Polka", "Retro",
                "Musical", "Rock & Roll", "Hard Rock"
            };

            if (index >= 0 && index < genres.Length)
                return genres[index];
            return "";
        }

        #region Background Pre-loading
        /// <summary>
        /// Pre-load metadata for all given file paths in background.
        /// Call this after media library scan to have metadata ready before UI opens.
        /// Uses frame budget to avoid blocking main thread.
        /// </summary>
        public void PreloadMetadata(List<string> filePaths)
        {
            if (_isPreloading)
            {
                Debug.Log("[FileMetadataService] Already preloading, skipping");
                return;
            }

            if (filePaths == null || filePaths.Count == 0) return;

            if (_preloadCoroutine != null)
                StopCoroutine(_preloadCoroutine);

            _preloadCoroutine = StartCoroutine(PreloadMetadataCoroutine(filePaths));
        }

        /// <summary>
        /// Stop any running preload operation.
        /// </summary>
        public void StopPreload()
        {
            if (_preloadCoroutine != null)
            {
                StopCoroutine(_preloadCoroutine);
                _preloadCoroutine = null;
            }
            _isPreloading = false;
        }

        private IEnumerator PreloadMetadataCoroutine(List<string> filePaths)
        {
            _isPreloading = true;
            int preloaded = 0;
            int skipped = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Debug.Log($"[FileMetadataService] Starting background preload for {filePaths.Count} files");

            for (int i = 0; i < filePaths.Count; i++)
            {
                string filePath = filePaths[i];

                // Determine file type
                string ext = Path.GetExtension(filePath)?.ToLowerInvariant();
                bool isVideo = ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".webm" ||
                               ext == ".mov" || ext == ".wmv" || ext == ".m4v" || ext == ".flv";
                bool isAudio = ext == ".mp3" || ext == ".wav" || ext == ".flac" || ext == ".aac" ||
                               ext == ".ogg" || ext == ".m4a" || ext == ".wma";

                if (isVideo)
                {
                    // Skip if already cached
                    if (_videoMetadataCache.ContainsKey(filePath))
                    {
                        skipped++;
                        continue;
                    }

                    // Try fast header parsing
                    VideoMetadata metadata = TryGetVideoMetadataFromHeaders(filePath);
                    if (metadata.Duration.TotalSeconds > 0)
                    {
                        _videoMetadataCache[filePath] = metadata;
                        preloaded++;
                    }
                }
                else if (isAudio)
                {
                    // Skip if already cached
                    if (_audioMetadataCache.ContainsKey(filePath))
                    {
                        skipped++;
                        continue;
                    }

                    // Read audio metadata synchronously
                    var metadata = ReadAudioMetadataSync(filePath);
                    if (metadata.Duration.TotalSeconds > 0)
                    {
                        _audioMetadataCache[filePath] = metadata;
                        preloaded++;
                    }
                }

                // Yield every 20 items to keep UI responsive
                if ((i + 1) % 20 == 0)
                    yield return null;
            }

            sw.Stop();
            _isPreloading = false;
            _preloadCoroutine = null;

            Debug.Log($"[FileMetadataService] Preload complete: {preloaded} loaded, {skipped} cached, {filePaths.Count - preloaded - skipped} failed ({sw.ElapsedMilliseconds}ms)");
        }

        /// <summary>
        /// Read audio metadata synchronously (for preloading).
        /// </summary>
        private AudioMetadata ReadAudioMetadataSync(string filePath)
        {
            var metadata = new AudioMetadata();
            try
            {
                if (!File.Exists(filePath)) return metadata;

                FileInfo fi = new FileInfo(filePath);
                string ext = fi.Extension.ToLower();

                if (ext == ".mp3")
                {
                    ReadMP3Metadata(filePath, ref metadata, fi.Length);
                }
                else
                {
                    int estimatedBitrate = 128;
                    if (ext == ".wav") estimatedBitrate = 1411;
                    else if (ext == ".ogg") estimatedBitrate = 160;

                    metadata.BitRate = estimatedBitrate;
                    double durationSeconds = fi.Length / (estimatedBitrate * 125.0);
                    metadata.Duration = TimeSpan.FromSeconds(durationSeconds);
                }
            }
            catch { }
            return metadata;
        }

        /// <summary>
        /// Check if metadata is already cached for a file path.
        /// </summary>
        public bool HasCachedMetadata(string filePath)
        {
            return _videoMetadataCache.ContainsKey(filePath) || _audioMetadataCache.ContainsKey(filePath);
        }
        #endregion

        private void OnDestroy()
        {
            if (_queueProcessorCoroutine != null)
            {
                StopCoroutine(_queueProcessorCoroutine);
                _queueProcessorCoroutine = null;
            }
            if (_preloadCoroutine != null)
            {
                StopCoroutine(_preloadCoroutine);
                _preloadCoroutine = null;
            }
            _metadataQueue.Clear();
            _isProcessingQueue = false;
            _isPreloading = false;
        }

        #region Public Static Helpers
        /// <summary>
        /// Try to detect codec from MP4 file headers without VideoPlayer.
        /// Returns codec FourCC (e.g., "hev1", "hvc1", "avc1", "av01") or null.
        /// </summary>
        public static string TryGetCodecFromHeaders(string filePath)
        {
            string ext = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (ext != ".mp4" && ext != ".m4v" && ext != ".mov") return null;

            try
            {
                return VideoHeaderParser.TryGetVideoInfoFromMP4Headers(filePath).Codec;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        #region Video Header Parser
        /// <summary>
        /// Fallback metadata extraction from MP4/MOV file headers.
        /// Used when VideoPlayer fails (unsupported codec, high-res, etc.).
        /// Traverses nested MP4 atom structure to extract duration, dimensions, and codec.
        /// </summary>
        private static class VideoHeaderParser
        {
            public struct MP4HeaderInfo
            {
                public double Duration;
                public int Width;
                public int Height;
                public string Codec;
            }

            /// <summary>
            /// Extract comprehensive video info from MP4 headers.
            /// Returns duration, width, height, and codec FourCC.
            /// </summary>
            public static MP4HeaderInfo TryGetVideoInfoFromMP4Headers(string filePath)
            {
                var info = new MP4HeaderInfo();
                try
                {
                    using (var fs = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    using (var reader = new System.IO.BinaryReader(fs))
                    {
                        long fileLength = fs.Length;

                        // 1. Find moov atom first
                        long moovPos = FindAtomAt(reader, 0, fileLength, "moov");
                        if (moovPos < 0)
                        {
                            Debug.LogWarning("[VideoHeaderParser] moov atom not found");
                            return info;
                        }

                        fs.Position = moovPos;
                        uint moovSize = Mp4AtomParser.ReadUInt32BE(reader);
                        long moovDataStart = moovPos + 8;  // Skip size + type
                        long moovDataEnd = moovPos + moovSize;

                        // 2. Extract duration from moov/mvhd
                        long mvhdPos = FindAtomAt(reader, moovDataStart, moovDataEnd, "mvhd");
                        if (mvhdPos >= 0)
                        {
                            info.Duration = ParseMvhdDuration(reader, mvhdPos);
                            if (info.Duration > 0)
                                Debug.Log($"[VideoHeaderParser] Duration from mvhd: {info.Duration:F1}s");
                        }

                        // 3. Find first video track: moov/trak
                        long searchPos = moovDataStart;
                        while (searchPos < moovDataEnd)
                        {
                            long trakPos = FindAtomAt(reader, searchPos, moovDataEnd, "trak");
                            if (trakPos < 0) break;

                            fs.Position = trakPos;
                            uint trakSize = Mp4AtomParser.ReadUInt32BE(reader);
                            long trakDataStart = trakPos + 8;
                            long trakDataEnd = trakPos + trakSize;

                            // Check if this is a video track via mdia/hdlr
                            if (IsVideoTrack(reader, trakDataStart, trakDataEnd))
                            {
                                // Extract width/height from tkhd
                                long tkhdPos = FindAtomAt(reader, trakDataStart, trakDataEnd, "tkhd");
                                if (tkhdPos >= 0)
                                {
                                    ParseTkhdDimensions(reader, tkhdPos, out int w, out int h);
                                    if (w > 0 && h > 0)
                                    {
                                        info.Width = w;
                                        info.Height = h;
                                        Debug.Log($"[VideoHeaderParser] Dimensions from tkhd: {w}x{h}");
                                    }
                                }

                                // Extract codec from mdia/minf/stbl/stsd
                                info.Codec = ExtractCodecFromTrack(reader, trakDataStart, trakDataEnd);
                                if (!string.IsNullOrEmpty(info.Codec))
                                    Debug.Log($"[VideoHeaderParser] Codec: {info.Codec}");

                                break; // Found video track, done
                            }

                            // Move to next trak
                            searchPos = trakPos + trakSize;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[VideoHeaderParser] Failed to parse MP4 headers: {ex.Message}");
                }
                return info;
            }

            /// <summary>
            /// Legacy method for backward compatibility. Now uses nested traversal.
            /// </summary>
            public static double TryGetDurationFromMP4(string filePath)
            {
                return TryGetVideoInfoFromMP4Headers(filePath).Duration;
            }

            /// <summary>
            /// Find an atom at a specific level within the given range.
            /// Does NOT recurse into children - searches siblings only.
            /// </summary>
            private static long FindAtomAt(System.IO.BinaryReader reader, long startPos, long endPos, string atomType)
            {
                return Mp4AtomParser.FindAtomWithin(reader, endPos, atomType, startPos);
            }

            /// <summary>
            /// Parse duration from mvhd atom (movie header).
            /// </summary>
            private static double ParseMvhdDuration(System.IO.BinaryReader reader, long mvhdPos)
            {
                reader.BaseStream.Position = mvhdPos + 8; // Skip size + type
                byte version = reader.ReadByte();
                reader.ReadBytes(3); // Skip flags

                // Skip creation/modification times
                if (version == 1)
                    reader.ReadBytes(16); // 64-bit timestamps
                else
                    reader.ReadBytes(8);  // 32-bit timestamps

                uint timeScale = Mp4AtomParser.ReadUInt32BE(reader);
                ulong duration = (version == 1) ? Mp4AtomParser.ReadUInt64BE(reader) : Mp4AtomParser.ReadUInt32BE(reader);

                if (timeScale > 0)
                    return (double)duration / timeScale;

                return 0;
            }

            /// <summary>
            /// Parse width/height from tkhd atom (track header).
            /// Width/height are stored as fixed-point 16.16 values.
            /// </summary>
            private static void ParseTkhdDimensions(System.IO.BinaryReader reader, long tkhdPos, out int width, out int height)
            {
                width = 0;
                height = 0;

                reader.BaseStream.Position = tkhdPos + 8; // Skip size + type
                byte version = reader.ReadByte();
                reader.ReadBytes(3); // Skip flags

                if (version == 1)
                {
                    reader.ReadBytes(16); // creation_time + modification_time (64-bit)
                    reader.ReadBytes(4);  // track_ID
                    reader.ReadBytes(4);  // reserved
                    reader.ReadBytes(8);  // duration (64-bit)
                }
                else
                {
                    reader.ReadBytes(8);  // creation_time + modification_time (32-bit)
                    reader.ReadBytes(4);  // track_ID
                    reader.ReadBytes(4);  // reserved
                    reader.ReadBytes(4);  // duration (32-bit)
                }

                reader.ReadBytes(8);  // reserved (2x uint32)
                reader.ReadBytes(2);  // layer
                reader.ReadBytes(2);  // alternate_group
                reader.ReadBytes(2);  // volume
                reader.ReadBytes(2);  // reserved
                reader.ReadBytes(36); // matrix (9x int32)

                // Width and height are fixed-point 16.16
                uint rawWidth = Mp4AtomParser.ReadUInt32BE(reader);
                uint rawHeight = Mp4AtomParser.ReadUInt32BE(reader);
                width = (int)(rawWidth >> 16);
                height = (int)(rawHeight >> 16);
            }

            /// <summary>
            /// Check if a trak atom contains a video track by examining mdia/hdlr handler type.
            /// </summary>
            private static bool IsVideoTrack(System.IO.BinaryReader reader, long trakDataStart, long trakDataEnd)
            {
                long mdiaPos = FindAtomAt(reader, trakDataStart, trakDataEnd, "mdia");
                if (mdiaPos < 0) return false;

                reader.BaseStream.Position = mdiaPos;
                uint mdiaSize = Mp4AtomParser.ReadUInt32BE(reader);
                long mdiaDataStart = mdiaPos + 8;
                long mdiaDataEnd = mdiaPos + mdiaSize;

                long hdlrPos = FindAtomAt(reader, mdiaDataStart, mdiaDataEnd, "hdlr");
                if (hdlrPos < 0) return false;

                // hdlr: size(4) + type(4) + version(1) + flags(3) + pre_defined(4) + handler_type(4)
                reader.BaseStream.Position = hdlrPos + 8 + 4 + 4; // Skip to handler_type
                byte[] handlerType = reader.ReadBytes(4);

                // "vide" = video track
                return handlerType.Length == 4 &&
                       handlerType[0] == (byte)'v' && handlerType[1] == (byte)'i' &&
                       handlerType[2] == (byte)'d' && handlerType[3] == (byte)'e';
            }

            /// <summary>
            /// Extract codec FourCC from mdia/minf/stbl/stsd within a video track.
            /// </summary>
            private static string ExtractCodecFromTrack(System.IO.BinaryReader reader, long trakDataStart, long trakDataEnd)
            {
                try
                {
                    // Navigate: trak > mdia > minf > stbl > stsd
                    long mdiaPos = FindAtomAt(reader, trakDataStart, trakDataEnd, "mdia");
                    if (mdiaPos < 0) return null;

                    reader.BaseStream.Position = mdiaPos;
                    uint mdiaSize = Mp4AtomParser.ReadUInt32BE(reader);
                    long mdiaEnd = mdiaPos + mdiaSize;

                    long minfPos = FindAtomAt(reader, mdiaPos + 8, mdiaEnd, "minf");
                    if (minfPos < 0) return null;

                    reader.BaseStream.Position = minfPos;
                    uint minfSize = Mp4AtomParser.ReadUInt32BE(reader);
                    long minfEnd = minfPos + minfSize;

                    long stblPos = FindAtomAt(reader, minfPos + 8, minfEnd, "stbl");
                    if (stblPos < 0) return null;

                    reader.BaseStream.Position = stblPos;
                    uint stblSize = Mp4AtomParser.ReadUInt32BE(reader);
                    long stblEnd = stblPos + stblSize;

                    long stsdPos = FindAtomAt(reader, stblPos + 8, stblEnd, "stsd");
                    if (stsdPos < 0) return null;

                    // stsd: size(4) + type(4) + version(1) + flags(3) + entry_count(4) + first_entry...
                    // First entry starts at stsdPos + 16
                    // Entry format: size(4) + codec_fourcc(4) + ...
                    reader.BaseStream.Position = stsdPos + 16;
                    reader.ReadBytes(4); // entry size
                    byte[] codecBytes = reader.ReadBytes(4);
                    if (codecBytes.Length == 4)
                    {
                        return System.Text.Encoding.ASCII.GetString(codecBytes).Trim('\0');
                    }
                }
                catch { }
                return null;
            }

        }
        #endregion
    }

    public struct VideoMetadata
    {
        public int Width;
        public int Height;
        public TimeSpan Duration;
        public float FrameRate;
        public long DataRate;      // bits per second
        public long TotalBitrate;  // bits per second
    }

    public struct AudioMetadata
    {
        public TimeSpan Duration;
        public int BitRate;        // kbps
        public string Artist;
        public string Album;
        public string Genre;
        public string Title;
    }

}
