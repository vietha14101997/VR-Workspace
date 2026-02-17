using UnityEngine;
using UnityEngine.Video;
using UnityEngine.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Extracts preview frames from video files using Unity's VideoPlayer.
/// Can composite frames with overlay icons for video indicators.
/// Now supports queuing multiple requests - processes one at a time but accepts all.
/// </summary>
public class VideoFrameExtractor : MonoBehaviour
{
    #region Private Fields
    private VideoPlayer _videoPlayer;
    private RenderTexture _renderTexture;
    private bool _isExtracting = false;
    private Action<Texture2D> _onComplete;
    private Action _onFailed;
    private float _extractionTimeout = 10f;  // Timeout in seconds (will be adaptive for high-res videos)

    // High-resolution video handling constants
    private const int HIGH_RES_THRESHOLD = 2160;          // 4K (2160p)
    private const int MAX_RENDER_SIZE = 2048;             // Max RenderTexture dimension (mobile-safe)
    private const int ULTRA_HIGH_RES_THRESHOLD = 3840;    // 4K width

    // Frame-ready tracking for reliable frame capture
    private bool _frameReadyReceived = false;
    private long _frameReadyIndex = -1;

    // Request queue for handling multiple concurrent requests
    private Queue<ExtractionRequest> _requestQueue = new Queue<ExtractionRequest>();

    private class ExtractionRequest
    {
        public string VideoPath;
        public int FrameWidth;
        public int FrameHeight;
        public Action<Texture2D> OnComplete;
        public Action OnFailed;
    }
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        SetupVideoPlayer();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void SetupVideoPlayer()
    {
        if (_videoPlayer == null)
        {
            _videoPlayer = gameObject.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.None;  // No audio needed
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.isLooping = false;
            // Use NoScaling since we create RenderTexture at correct aspect ratio
            _videoPlayer.aspectRatio = VideoAspectRatio.NoScaling;

            // Event handlers
            _videoPlayer.prepareCompleted += OnVideoPrepared;
            _videoPlayer.errorReceived += OnVideoError;
            _videoPlayer.frameReady += OnFrameReady;
        }
    }

    private void OnFrameReady(VideoPlayer source, long frameIdx)
    {
        _frameReadyReceived = true;
        _frameReadyIndex = frameIdx;
    }

    private void Cleanup()
    {
        // Clear pending requests
        _requestQueue.Clear();
        _onComplete = null;
        _onFailed = null;
        _frameReadyReceived = false;
        _frameReadyIndex = -1;

        if (_videoPlayer != null)
        {
            _videoPlayer.sendFrameReadyEvents = false;
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.errorReceived -= OnVideoError;
            _videoPlayer.frameReady -= OnFrameReady;
            _videoPlayer.Stop();
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }
    }

    /// <summary>
    /// Get the number of pending requests in the queue.
    /// </summary>
    public int PendingRequestCount => _requestQueue.Count;

    /// <summary>
    /// Clear all pending requests in the queue.
    /// </summary>
    public void ClearPendingRequests()
    {
        // Invoke failed callbacks for all pending requests
        while (_requestQueue.Count > 0)
        {
            var request = _requestQueue.Dequeue();
            request.OnFailed?.Invoke();
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// Extract a frame from a video file.
    /// Requests are queued if extraction is in progress.
    /// </summary>
    /// <param name="videoPath">Path to the video file</param>
    /// <param name="frameWidth">Target frame width</param>
    /// <param name="frameHeight">Target frame height</param>
    /// <param name="onComplete">Callback with the extracted frame texture</param>
    /// <param name="onFailed">Callback when extraction fails</param>
    public void ExtractFrame(string videoPath, int frameWidth, int frameHeight,
        Action<Texture2D> onComplete, Action onFailed)
    {
        var request = new ExtractionRequest
        {
            VideoPath = videoPath,
            FrameWidth = frameWidth,
            FrameHeight = frameHeight,
            OnComplete = onComplete,
            OnFailed = onFailed
        };

        _requestQueue.Enqueue(request);

        // If not already extracting, start processing
        if (!_isExtracting)
        {
            TryProcessNextRequest();
        }
    }

    /// <summary>
    /// Process the next request in the queue.
    /// </summary>
    private void TryProcessNextRequest()
    {
        if (_isExtracting || _requestQueue.Count == 0)
        {
            return;
        }

        var request = _requestQueue.Dequeue();
        _onComplete = request.OnComplete;
        _onFailed = request.OnFailed;

        StartCoroutine(ExtractFrameCoroutine(request.VideoPath, request.FrameWidth, request.FrameHeight));
    }

    /// <summary>
    /// Composite a video frame with an overlay icon in the center using GPU blending.
    /// </summary>
    /// <param name="frameTexture">The video frame texture</param>
    /// <param name="overlayIcon">The overlay sprite (e.g., play button)</param>
    /// <param name="finalSize">Final output size</param>
    /// <returns>Composited sprite, or null if failed</returns>
    public Sprite CompositeWithOverlay(Texture2D frameTexture, Sprite overlayIcon, int finalSize)
    {
        if (frameTexture == null) return null;

        int width = frameTexture.width;
        int height = frameTexture.height;

        // Use GPU-based compositing with RenderTexture
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);

        // First, blit the video frame
        Graphics.Blit(frameTexture, rt);

        // Draw overlay using GPU if available
        if (overlayIcon != null && overlayIcon.texture != null)
        {
            DrawOverlayGPU(rt, overlayIcon);
        }

        // Read back the result (this is still synchronous, but the compositing was GPU-accelerated)
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, true);
        result.filterMode = FilterMode.Trilinear;
        result.anisoLevel = 16;
        result.wrapMode = TextureWrapMode.Clamp;
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply(true);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        // Create sprite from composited texture
        Sprite sprite = Sprite.Create(
            result,
            new Rect(0, 0, width, height),
            new Vector2(0.5f, 0.5f),
            100f
        );

        return sprite;
    }

    /// <summary>
    /// Draw overlay using GPU (Graphics.DrawTexture).
    /// </summary>
    private void DrawOverlayGPU(RenderTexture target, Sprite overlaySprite)
    {
        Texture2D overlayTexture = overlaySprite.texture;
        if (overlayTexture == null) return;

        // Calculate overlay size (about 50% of the smaller dimension)
        int overlaySize = Mathf.Min(target.width, target.height) * 50 / 100;

        // Center position
        float startX = (target.width - overlaySize) / 2f;
        float startY = (target.height - overlaySize) / 2f;

        // Use GL to draw overlay with alpha blending
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;

        GL.PushMatrix();
        GL.LoadPixelMatrix(0, target.width, target.height, 0);

        // Draw overlay texture with alpha blending
        Graphics.DrawTexture(
            new Rect(startX, startY, overlaySize, overlaySize),
            overlayTexture,
            new Rect(
                overlaySprite.rect.x / overlayTexture.width,
                overlaySprite.rect.y / overlayTexture.height,
                overlaySprite.rect.width / overlayTexture.width,
                overlaySprite.rect.height / overlayTexture.height
            ),
            0, 0, 0, 0,
            new Color(1, 1, 1, 0.9f) // Slight transparency
        );

        GL.PopMatrix();
        RenderTexture.active = previous;
    }

    /// <summary>
    /// Calculate adaptive extraction timeout based on video resolution and file size.
    /// Higher resolution and larger files need more time for VideoPlayer preparation.
    /// </summary>
    private float CalculateExtractionTimeout(int videoWidth, int videoHeight, string filePath)
    {
        float baseTimeout = 10f;

        // Factor 1: Resolution multiplier
        int pixels = videoWidth * videoHeight;
        float resolutionMultiplier = 1f;

        if (pixels > 3840 * 2160)      // > 4K
            resolutionMultiplier = 3f;
        else if (pixels > 2560 * 1440) // > 1440p
            resolutionMultiplier = 2f;
        else if (pixels > 1920 * 1080) // > 1080p
            resolutionMultiplier = 1.5f;

        // Factor 2: File size (high bitrate = longer decode time)
        try
        {
            System.IO.FileInfo fi = new System.IO.FileInfo(filePath);
            long fileSizeMB = fi.Length / (1024 * 1024);

            if (fileSizeMB > 500)  // > 500MB
                resolutionMultiplier *= 1.5f;
            else if (fileSizeMB > 200)  // > 200MB
                resolutionMultiplier *= 1.25f;
        }
        catch { }

        float adaptiveTimeout = baseTimeout * resolutionMultiplier;
        return Mathf.Clamp(adaptiveTimeout, 10f, 30f);  // Cap at 30s
    }
    #endregion

    #region Frame Extraction
    private IEnumerator ExtractFrameCoroutine(string videoPath, int maxSize, int unused)
    {
        _isExtracting = true;

        // PRIORITY 1: Try to extract embedded thumbnail from video metadata first
        // This matches Windows Explorer behavior and is much faster than decoding video
        Texture2D embeddedThumb = null;
        bool hasEmbedded = false;

        // Run metadata extraction (this is synchronous but fast for reading metadata)
        try
        {
            hasEmbedded = VideoMetadataThumbnailExtractor.TryExtractEmbeddedThumbnail(videoPath, out embeddedThumb);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[VideoFrameExtractor] Metadata extraction failed: {ex.Message}");
            hasEmbedded = false;
        }

        if (hasEmbedded && embeddedThumb != null)
        {
            // Resize if needed to match target size
            if (embeddedThumb.width > maxSize || embeddedThumb.height > maxSize)
            {
                Texture2D resized = ResizeTexture(embeddedThumb, maxSize);
                Destroy(embeddedThumb);
                embeddedThumb = resized;
            }

            Debug.Log($"[VideoFrameExtractor] Using embedded metadata thumbnail for: {System.IO.Path.GetFileName(videoPath)}");
            CompleteExtraction(embeddedThumb);
            yield break;
        }

        // PRIORITY 2: Fall back to extracting frame from video at 10% position
        Debug.Log($"[VideoFrameExtractor] No embedded thumbnail, extracting frame from video: {System.IO.Path.GetFileName(videoPath)}");

        // Clean up previous render texture
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        // Set video URL and prepare WITHOUT targetTexture first
        // This allows us to get the video's native dimensions
        _videoPlayer.url = "file://" + videoPath;
        _videoPlayer.Prepare();

        // Wait for preparation with timeout
        float startTime = Time.time;
        while (!_videoPlayer.isPrepared)
        {
            if (Time.time - startTime > _extractionTimeout)
            {
                Debug.LogWarning($"[VideoFrameExtractor] Preparation timeout for: {videoPath}");
                CompleteExtraction(null);
                yield break;
            }
            yield return null;
        }

        // Get video's native dimensions
        int videoWidth = (int)_videoPlayer.width;
        int videoHeight = (int)_videoPlayer.height;

        if (videoWidth <= 0 || videoHeight <= 0)
        {
            Debug.LogWarning($"[VideoFrameExtractor] Invalid video dimensions: {videoWidth}x{videoHeight}");
            CompleteExtraction(null);
            yield break;
        }

        // Detect high-resolution videos
        bool isHighRes = videoWidth > HIGH_RES_THRESHOLD || videoHeight > HIGH_RES_THRESHOLD;
        bool isUltraHighRes = videoWidth > ULTRA_HIGH_RES_THRESHOLD || videoHeight > ULTRA_HIGH_RES_THRESHOLD;

        if (isUltraHighRes)
        {
            Debug.Log($"[VideoFrameExtractor] Ultra-high-res video detected ({videoWidth}x{videoHeight}), scaling to max {MAX_RENDER_SIZE}");
        }
        else if (isHighRes)
        {
            Debug.Log($"[VideoFrameExtractor] High-res video detected ({videoWidth}x{videoHeight}), scaling for mobile compatibility");
        }

        // Calculate target dimensions with resolution cap for mobile-safety
        int targetWidth, targetHeight;
        int effectiveMaxSize = Math.Min(maxSize, MAX_RENDER_SIZE);  // Never exceed 2048 for mobile

        if (videoWidth > effectiveMaxSize || videoHeight > effectiveMaxSize)
        {
            float ratio = (float)videoWidth / videoHeight;
            if (videoWidth > videoHeight)
            {
                targetWidth = effectiveMaxSize;
                targetHeight = Mathf.RoundToInt(effectiveMaxSize / ratio);
            }
            else
            {
                targetHeight = effectiveMaxSize;
                targetWidth = Mathf.RoundToInt(effectiveMaxSize * ratio);
            }
        }
        else
        {
            // Video is smaller than max size, use native dimensions
            targetWidth = videoWidth;
            targetHeight = videoHeight;
        }

        // Update extraction timeout for high-res videos
        _extractionTimeout = CalculateExtractionTimeout(videoWidth, videoHeight, videoPath);

        // Now create RenderTexture with correct aspect ratio
        _renderTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        _renderTexture.Create();
        _videoPlayer.targetTexture = _renderTexture;

        // Seek to a position that typically shows interesting content
        // Windows-style: Use fixed time for longer videos, percentage for shorter ones
        // - For videos > 30 seconds: Seek to ~10 seconds (skip intros/loading)
        // - For videos 10-30 seconds: Seek to 30%
        // - For videos < 10 seconds: Seek to 20%
        double seekTime;
        if (_videoPlayer.length > 30)
        {
            // Longer videos: fixed 10 second mark (similar to Windows behavior)
            seekTime = 10.0;
        }
        else if (_videoPlayer.length > 10)
        {
            // Medium videos: 30% in
            seekTime = _videoPlayer.length * 0.3;
        }
        else
        {
            // Short videos: 20% in
            seekTime = _videoPlayer.length * 0.2;
        }
        
        // Calculate adaptive capture timeout based on video resolution
        float baseCaptureTimeout = 5f;
        float captureTimeout = baseCaptureTimeout;

        // For high-res videos, increase capture timeout
        if (videoWidth * videoHeight > 3840 * 2160)  // > 4K
            captureTimeout = 10f;
        else if (videoWidth * videoHeight > 1920 * 1080)  // > 1080p
            captureTimeout = 7f;

        // Try to extract frame with retry logic for problematic videos
        Texture2D capturedFrame = null;
        int maxRetries = 3;
        double[] seekPositions = new double[] { seekTime, 0.5, _videoPlayer.length * 0.5 }; // Try original, start, middle

        // Enable frame-ready events for reliable frame detection
        _videoPlayer.sendFrameReadyEvents = true;

        for (int attempt = 0; attempt < maxRetries && capturedFrame == null; attempt++)
        {
            double currentSeekTime = (attempt < seekPositions.Length) ? seekPositions[attempt] : seekTime;
            _videoPlayer.time = currentSeekTime;

            if (attempt == 0)
            {
                Debug.Log($"[VideoFrameExtractor] Seeking to {currentSeekTime:F1}s (video length: {_videoPlayer.length:F1}s)");
            }
            else
            {
                Debug.Log($"[VideoFrameExtractor] Retry {attempt}: Seeking to {currentSeekTime:F1}s");
            }

            // Reset frame-ready flag and start playback
            _frameReadyReceived = false;
            _videoPlayer.Play();

            // Wait for frameReady event (guarantees RenderTexture has actual content)
            float frameWaitStart = Time.time;
            float frameWaitTimeout = isUltraHighRes ? 8f : 5f;
            while (!_frameReadyReceived)
            {
                if (Time.time - frameWaitStart > frameWaitTimeout)
                {
                    Debug.LogWarning($"[VideoFrameExtractor] frameReady timeout ({frameWaitTimeout}s) at attempt {attempt}");
                    break;
                }
                yield return null;
            }

            if (_frameReadyReceived)
            {
                Debug.Log($"[VideoFrameExtractor] Frame ready (idx={_frameReadyIndex}) after {Time.time - frameWaitStart:F2}s");
                // Give one extra frame for GPU to finalize rendering
                yield return null;
            }
            else if (!_videoPlayer.isPlaying)
            {
                // Video stopped before producing a frame
                Debug.LogWarning($"[VideoFrameExtractor] Video stopped before frame ready at attempt {attempt}");
                continue;
            }

            // Capture the frame asynchronously
            bool captureComplete = false;

            StartCoroutine(CaptureFrameAsync((frame) =>
            {
                capturedFrame = frame;
                captureComplete = true;
            }));

            // Wait for async capture to complete
            float captureStartTime = Time.time;
            while (!captureComplete)
            {
                if (Time.time - captureStartTime > captureTimeout)
                {
                    Debug.LogWarning($"[VideoFrameExtractor] Capture timeout at attempt {attempt}");
                    captureComplete = true;
                }
                yield return null;
            }

            // Validate captured frame has actual content (not all-black)
            if (capturedFrame != null && IsFrameBlank(capturedFrame))
            {
                Debug.LogWarning($"[VideoFrameExtractor] Frame captured but blank at attempt {attempt}, retrying...");
                Destroy(capturedFrame);
                capturedFrame = null;
            }

            // If capture failed, pause and try next position
            if (capturedFrame == null && attempt < maxRetries - 1)
            {
                _videoPlayer.Pause();
                yield return new WaitForSeconds(0.2f);
            }
        }

        // Disable frame-ready events (performance impact)
        _videoPlayer.sendFrameReadyEvents = false;

        // Stop and cleanup
        _videoPlayer.Stop();
        _videoPlayer.targetTexture = null;

        if (capturedFrame != null)
        {
            Debug.Log($"[VideoFrameExtractor] Successfully extracted frame for: {System.IO.Path.GetFileName(_videoPlayer.url)}");
        }
        else
        {
            Debug.LogWarning($"[VideoFrameExtractor] Failed to extract frame after {maxRetries} attempts for: {System.IO.Path.GetFileName(_videoPlayer.url)}");
        }

        CompleteExtraction(capturedFrame);
    }

    private IEnumerator CaptureFrameAsync(Action<Texture2D> onComplete)
    {
        if (_renderTexture == null)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        bool readbackComplete = false;
        Texture2D frame = null;
        Exception readbackError = null;

        // Use AsyncGPUReadback to avoid blocking main thread
        AsyncGPUReadback.Request(_renderTexture, 0, TextureFormat.RGBA32, (request) =>
        {
            if (request.hasError)
            {
                readbackError = new Exception("AsyncGPUReadback failed");
            }
            else
            {
                try
                {
                    frame = new Texture2D(_renderTexture.width, _renderTexture.height, TextureFormat.RGBA32, false);
                    frame.filterMode = FilterMode.Trilinear;
                    frame.anisoLevel = 16;
                    frame.wrapMode = TextureWrapMode.Clamp;
                    frame.LoadRawTextureData(request.GetData<byte>());
                    frame.Apply(true);  // Generate mipmaps from base level
                }
                catch (Exception ex)
                {
                    readbackError = ex;
                    if (frame != null)
                    {
                        Destroy(frame);
                        frame = null;
                    }
                }
            }
            readbackComplete = true;
        });

        // Wait for async readback
        while (!readbackComplete)
        {
            yield return null;
        }

        if (readbackError != null)
        {
            Debug.LogWarning($"[VideoFrameExtractor] Async capture failed: {readbackError.Message}, using fallback");
            frame = CaptureFrameSync();
        }

        onComplete?.Invoke(frame);
    }

    private Texture2D CaptureFrameSync()
    {
        if (_renderTexture == null) return null;

        try
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _renderTexture;

            Texture2D frame = new Texture2D(_renderTexture.width, _renderTexture.height, TextureFormat.RGBA32, false);
            frame.filterMode = FilterMode.Trilinear;
            frame.anisoLevel = 16;
            frame.wrapMode = TextureWrapMode.Clamp;
            frame.ReadPixels(new Rect(0, 0, _renderTexture.width, _renderTexture.height), 0, 0);
            frame.Apply(true);  // Generate mipmaps from base level

            RenderTexture.active = previous;
            return frame;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VideoFrameExtractor] Failed to capture frame: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if a captured frame is effectively blank (all-black or transparent).
    /// Samples 9 points in a 3x3 grid to detect empty frames quickly.
    /// </summary>
    private bool IsFrameBlank(Texture2D frame)
    {
        if (frame == null) return true;

        try
        {
            int w = frame.width;
            int h = frame.height;
            int validSamples = 0;
            const float THRESHOLD = 0.05f;

            // Sample 9 points in a 3x3 grid (at 25%, 50%, 75% positions)
            for (int row = 1; row <= 3; row++)
            {
                for (int col = 1; col <= 3; col++)
                {
                    int x = Mathf.Clamp(w * col / 4, 0, w - 1);
                    int y = Mathf.Clamp(h * row / 4, 0, h - 1);
                    Color pixel = frame.GetPixel(x, y);

                    // Pixel has visible content if it's not pure black/transparent
                    if (pixel.a > THRESHOLD && (pixel.r + pixel.g + pixel.b) > THRESHOLD)
                    {
                        validSamples++;
                    }
                }
            }

            // Need at least 3 out of 9 samples to have visible content
            return validSamples < 3;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VideoFrameExtractor] Cannot validate frame: {ex.Message}");
            return false; // Assume not blank if we can't read pixels
        }
    }

    private void CompleteExtraction(Texture2D frame)
    {
        _isExtracting = false;

        if (frame != null)
        {
            _onComplete?.Invoke(frame);
        }
        else
        {
            _onFailed?.Invoke();
        }

        _onComplete = null;
        _onFailed = null;

        // Process next queued request
        TryProcessNextRequest();
    }
    #endregion

    #region Overlay Compositing
    private void DrawOverlay(Texture2D target, Sprite overlaySprite)
    {
        Texture2D overlayTexture = overlaySprite.texture;

        // Calculate overlay size (about 50% of the smaller dimension)
        int overlaySize = Mathf.Min(target.width, target.height) * 50 / 100;
        int overlayWidth = overlaySize;
        int overlayHeight = overlaySize;

        // Center position
        int startX = (target.width - overlayWidth) / 2;
        int startY = (target.height - overlayHeight) / 2;

        // Get overlay pixels (from sprite rect)
        int srcX = Mathf.RoundToInt(overlaySprite.rect.x);
        int srcY = Mathf.RoundToInt(overlaySprite.rect.y);
        int srcWidth = Mathf.RoundToInt(overlaySprite.rect.width);
        int srcHeight = Mathf.RoundToInt(overlaySprite.rect.height);

        // Ensure we can read the texture
        Color[] overlayPixels;
        try
        {
            overlayPixels = overlayTexture.GetPixels(srcX, srcY, srcWidth, srcHeight);
        }
        catch
        {
            // Texture is not readable, use a fallback
            DrawFallbackOverlay(target, startX, startY, overlayWidth, overlayHeight);
            return;
        }

        // Scale and blend overlay onto target
        for (int y = 0; y < overlayHeight; y++)
        {
            for (int x = 0; x < overlayWidth; x++)
            {
                // Sample from overlay with bilinear-like approximation
                float u = (float)x / overlayWidth * srcWidth;
                float v = (float)y / overlayHeight * srcHeight;
                int srcIndex = Mathf.Clamp(Mathf.RoundToInt(v), 0, srcHeight - 1) * srcWidth +
                              Mathf.Clamp(Mathf.RoundToInt(u), 0, srcWidth - 1);

                if (srcIndex < overlayPixels.Length)
                {
                    Color overlayColor = overlayPixels[srcIndex];

                    if (overlayColor.a > 0.01f)
                    {
                        int targetX = startX + x;
                        int targetY = startY + y;

                        if (targetX >= 0 && targetX < target.width && targetY >= 0 && targetY < target.height)
                        {
                            Color targetColor = target.GetPixel(targetX, targetY);
                            Color blended = Color.Lerp(targetColor, overlayColor, overlayColor.a);
                            target.SetPixel(targetX, targetY, blended);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draw a simple play icon when the overlay texture is not readable.
    /// </summary>
    private void DrawFallbackOverlay(Texture2D target, int startX, int startY, int width, int height)
    {
        // Draw a semi-transparent circle with a play triangle
        int centerX = startX + width / 2;
        int centerY = startY + height / 2;
        int radius = width / 2;

        Color circleColor = new Color(0, 0, 0, 0.6f);
        Color triangleColor = Color.white;

        // Draw circle background
        for (int y = startY; y < startY + height; y++)
        {
            for (int x = startX; x < startX + width; x++)
            {
                if (x >= 0 && x < target.width && y >= 0 && y < target.height)
                {
                    float dx = x - centerX;
                    float dy = y - centerY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist <= radius)
                    {
                        Color targetColor = target.GetPixel(x, y);
                        Color blended = Color.Lerp(targetColor, circleColor, circleColor.a);
                        target.SetPixel(x, y, blended);
                    }
                }
            }
        }

        // Draw play triangle
        int triWidth = width / 3;
        int triHeight = height / 2;
        int triStartX = centerX - triWidth / 3;  // Slightly right of center for visual balance
        int triStartY = centerY - triHeight / 2;

        for (int y = 0; y < triHeight; y++)
        {
            float progress = (float)y / triHeight;
            int lineWidth = Mathf.RoundToInt(triWidth * Mathf.Min(progress * 2, (1 - progress) * 2));

            for (int x = 0; x < lineWidth; x++)
            {
                int px = triStartX + x;
                int py = triStartY + y;

                if (px >= 0 && px < target.width && py >= 0 && py < target.height)
                {
                    target.SetPixel(px, py, triangleColor);
                }
            }
        }
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Resize a texture to fit within maxSize while maintaining aspect ratio.
    /// Used for resizing embedded metadata thumbnails.
    /// </summary>
    private Texture2D ResizeTexture(Texture2D source, int maxSize)
    {
        int targetWidth, targetHeight;

        if (source.width > source.height)
        {
            targetWidth = maxSize;
            targetHeight = Mathf.RoundToInt((float)source.height / source.width * maxSize);
        }
        else
        {
            targetHeight = maxSize;
            targetWidth = Mathf.RoundToInt((float)source.width / source.height * maxSize);
        }

        // Use RenderTexture for GPU-accelerated resize
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Graphics.Blit(source, rt);

        Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, true);
        result.filterMode = FilterMode.Trilinear;
        result.anisoLevel = 16;
        result.wrapMode = TextureWrapMode.Clamp;
        result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        result.Apply(true);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }
    #endregion

    #region Video Player Callbacks
    private void OnVideoPrepared(VideoPlayer source)
    {
        // Video is ready to play
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogWarning($"[VideoFrameExtractor] Video error: {message}");
        if (_isExtracting)
        {
            CompleteExtraction(null);
        }
    }
    #endregion
}
