using UnityEngine;
using UnityEngine.Video;
using System;
using System.Collections;
using System.IO;

/// <summary>
/// Service for reading file metadata (dimensions, duration, etc.)
/// Uses Unity's native capabilities where possible.
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
    /// Read image dimensions without keeping texture in memory.
    /// </summary>
    public void GetImageMetadata(string filePath, Action<int, int> onComplete)
    {
        StartCoroutine(LoadImageMetadataCoroutine(filePath, onComplete));
    }

    private IEnumerator LoadImageMetadataCoroutine(string filePath, Action<int, int> onComplete)
    {
        int width = 0;
        int height = 0;

        try
        {
            if (File.Exists(filePath))
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(fileData))
                {
                    width = tex.width;
                    height = tex.height;
                }
                Destroy(tex);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileMetadataService] Failed to read image metadata: {ex.Message}");
        }

        yield return null;
        onComplete?.Invoke(width, height);
    }

    /// <summary>
    /// Read video metadata using VideoPlayer.
    /// </summary>
    public void GetVideoMetadata(string filePath, Action<VideoMetadata> onComplete)
    {
        StartCoroutine(LoadVideoMetadataCoroutine(filePath, onComplete));
    }

    private IEnumerator LoadVideoMetadataCoroutine(string filePath, Action<VideoMetadata> onComplete)
    {
        var metadata = new VideoMetadata();

        if (!File.Exists(filePath))
        {
            onComplete?.Invoke(metadata);
            yield break;
        }

        // Create temporary VideoPlayer
        GameObject tempGO = new GameObject("TempVideoPlayer");
        tempGO.SetActive(false);
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
        float timeout = 5f;
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
            if (vp.length > 0)
            {
                try
                {
                    FileInfo fi = new FileInfo(filePath);
                    metadata.TotalBitrate = (long)(fi.Length * 8 / vp.length); // bits per second
                    metadata.DataRate = metadata.TotalBitrate; // Approximate
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
    /// </summary>
    public void GetAudioMetadata(string filePath, Action<AudioMetadata> onComplete)
    {
        StartCoroutine(LoadAudioMetadataCoroutine(filePath, onComplete));
    }

    private IEnumerator LoadAudioMetadataCoroutine(string filePath, Action<AudioMetadata> onComplete)
    {
        var metadata = new AudioMetadata();

        if (!File.Exists(filePath))
        {
            onComplete?.Invoke(metadata);
            yield break;
        }

        // Try to estimate duration from file size (very rough)
        // For accurate metadata, need external library like TagLibSharp
        try
        {
            FileInfo fi = new FileInfo(filePath);
            string ext = fi.Extension.ToLower();

            // Rough bitrate estimates for common formats
            int estimatedBitrate = 128; // kbps default
            if (ext == ".wav") estimatedBitrate = 1411; // CD quality WAV
            else if (ext == ".mp3") estimatedBitrate = 192;
            else if (ext == ".ogg") estimatedBitrate = 160;

            metadata.BitRate = estimatedBitrate;

            // Very rough duration estimate: fileSize / (bitrate * 125)
            // 125 = 1000/8 (kbps to bytes per second)
            double durationSeconds = fi.Length / (estimatedBitrate * 125.0);
            metadata.Duration = TimeSpan.FromSeconds(durationSeconds);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FileMetadataService] Failed to estimate audio metadata: {ex.Message}");
        }

        yield return null;
        onComplete?.Invoke(metadata);
    }
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
