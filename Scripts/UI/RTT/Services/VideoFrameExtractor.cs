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
    private float _extractionTimeout = 10f;  // Timeout in seconds

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
        }
    }

    private void Cleanup()
    {
        // Clear pending requests
        _requestQueue.Clear();
        _onComplete = null;
        _onFailed = null;

        if (_videoPlayer != null)
        {
            _videoPlayer.prepareCompleted -= OnVideoPrepared;
            _videoPlayer.errorReceived -= OnVideoError;
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
    #endregion

    #region Frame Extraction
    private IEnumerator ExtractFrameCoroutine(string videoPath, int maxSize, int unused)
    {
        _isExtracting = true;

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

        // Get video's native dimensions and calculate target size maintaining aspect ratio
        int videoWidth = (int)_videoPlayer.width;
        int videoHeight = (int)_videoPlayer.height;

        if (videoWidth <= 0 || videoHeight <= 0)
        {
            Debug.LogWarning($"[VideoFrameExtractor] Invalid video dimensions: {videoWidth}x{videoHeight}");
            CompleteExtraction(null);
            yield break;
        }

        // Calculate dimensions maintaining aspect ratio (like ResizeTexture for images)
        int targetWidth, targetHeight;
        if (videoWidth > maxSize || videoHeight > maxSize)
        {
            float ratio = (float)videoWidth / videoHeight;
            if (videoWidth > videoHeight)
            {
                targetWidth = maxSize;
                targetHeight = Mathf.RoundToInt(maxSize / ratio);
            }
            else
            {
                targetHeight = maxSize;
                targetWidth = Mathf.RoundToInt(maxSize * ratio);
            }
        }
        else
        {
            // Video is smaller than max size, use native dimensions
            targetWidth = videoWidth;
            targetHeight = videoHeight;
        }

        // Now create RenderTexture with correct aspect ratio
        _renderTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        _renderTexture.Create();
        _videoPlayer.targetTexture = _renderTexture;

        // Seek to 10% into the video (to skip black frames at start)
        double seekTime = _videoPlayer.length * 0.1;
        _videoPlayer.time = seekTime;

        // Start playback briefly to render a frame
        _videoPlayer.Play();

        // Wait for frame to render
        yield return new WaitForSeconds(0.1f);

        // Wait a bit more for the render texture to update
        int waitFrames = 5;
        while (waitFrames > 0)
        {
            yield return null;
            waitFrames--;
        }

        // Capture the frame asynchronously
        Texture2D capturedFrame = null;
        bool captureComplete = false;

        StartCoroutine(CaptureFrameAsync((frame) =>
        {
            capturedFrame = frame;
            captureComplete = true;
        }));

        // Wait for async capture to complete
        while (!captureComplete)
        {
            yield return null;
        }

        // Stop and cleanup
        _videoPlayer.Stop();
        _videoPlayer.targetTexture = null;

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
                    frame = new Texture2D(_renderTexture.width, _renderTexture.height, TextureFormat.RGBA32, true);
                    frame.filterMode = FilterMode.Trilinear;
                    frame.anisoLevel = 16;
                    frame.wrapMode = TextureWrapMode.Clamp;
                    frame.LoadRawTextureData(request.GetData<byte>());
                    frame.Apply(true);
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

            Texture2D frame = new Texture2D(_renderTexture.width, _renderTexture.height, TextureFormat.RGBA32, true);
            frame.filterMode = FilterMode.Trilinear;
            frame.anisoLevel = 16;
            frame.wrapMode = TextureWrapMode.Clamp;
            frame.ReadPixels(new Rect(0, 0, _renderTexture.width, _renderTexture.height), 0, 0);
            frame.Apply(true);

            RenderTexture.active = previous;
            return frame;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VideoFrameExtractor] Failed to capture frame: {ex.Message}");
            return null;
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
