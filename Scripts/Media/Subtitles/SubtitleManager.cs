using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using TMPro;
using VRWorkspace.Media.Subtitles;

/// <summary>
/// Manages subtitle loading, synchronization, and display for video playback.
/// </summary>
public class SubtitleManager : MonoBehaviour
{
    #region Events
    public event Action<string> OnSubtitleTextChanged;
    public event Action<SubtitleEntry> OnSubtitleEntered;
    public event Action OnSubtitleExited;
    #endregion

    #region Properties
    public bool IsEnabled { get; set; } = true;
    public bool HasSubtitles => _entries != null && _entries.Count > 0;
    public int SubtitleCount => _entries?.Count ?? 0;
    public SubtitleEntry? CurrentSubtitle { get; private set; }
    public string CurrentSubtitleFile { get; private set; }
    #endregion

    #region Settings
    [Header("Display Settings")]
    public float FontSize = 36f;
    public Color TextColor = Color.white;
    public Color OutlineColor = Color.black;
    public float OutlineWidth = 2f;
    public float BottomOffset = 100f;  // Distance from bottom of screen
    public float SyncOffset = 0f;      // Manual time offset in seconds
    #endregion

    #region Private Fields
    private List<SubtitleEntry> _entries = new List<SubtitleEntry>();
    private int _currentIndex = -1;
    private float _lastUpdateTime = -1f;

    // UI References (created dynamically for VR)
    private GameObject _subtitleCanvas;
    private TextMeshProUGUI _subtitleText;
    private bool _isUIInitialized = false;
    #endregion

    #region Initialization
    private void Awake()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (_isUIInitialized) return;

        // Create world-space canvas for VR subtitles
        _subtitleCanvas = new GameObject("SubtitleCanvas");
        _subtitleCanvas.transform.SetParent(transform);

        Canvas canvas = _subtitleCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var canvasRT = _subtitleCanvas.GetComponent<RectTransform>();
        canvasRT.sizeDelta = new Vector2(1920, 200);
        canvasRT.localScale = Vector3.one * 0.001f;  // Scale for VR (1mm per pixel)

        // Background panel (semi-transparent)
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(_subtitleCanvas.transform, false);

        var bgRT = bgObj.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        var bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.5f);

        // Subtitle text
        GameObject textObj = new GameObject("SubtitleText");
        textObj.transform.SetParent(_subtitleCanvas.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(20, 10);
        textRT.offsetMax = new Vector2(-20, -10);

        _subtitleText = textObj.AddComponent<TextMeshProUGUI>();
        _subtitleText.fontSize = FontSize;
        _subtitleText.color = TextColor;
        _subtitleText.alignment = TextAlignmentOptions.Center;
        _subtitleText.enableWordWrapping = true;
        _subtitleText.overflowMode = TextOverflowModes.Truncate;
        _subtitleText.outlineWidth = OutlineWidth;
        _subtitleText.outlineColor = OutlineColor;
        _subtitleText.text = "";

        // Start hidden
        _subtitleCanvas.SetActive(false);
        _isUIInitialized = true;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Load subtitles from file.
    /// </summary>
    public bool LoadSubtitles(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Debug.LogWarning("[SubtitleManager] No subtitle file specified");
            return false;
        }

        try
        {
            _entries = SrtParser.ParseFile(filePath);
            CurrentSubtitleFile = filePath;
            _currentIndex = -1;
            _lastUpdateTime = -1f;
            CurrentSubtitle = null;

            Debug.Log($"[SubtitleManager] Loaded {_entries.Count} subtitle entries from: {filePath}");
            return _entries.Count > 0;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SubtitleManager] Error loading subtitles: {ex.Message}");
            _entries.Clear();
            return false;
        }
    }

    /// <summary>
    /// Try to auto-load subtitles for a video file.
    /// </summary>
    public bool AutoLoadSubtitles(string videoPath)
    {
        string subtitlePath = SrtParser.FindSubtitleFile(videoPath);
        if (!string.IsNullOrEmpty(subtitlePath))
        {
            return LoadSubtitles(subtitlePath);
        }
        return false;
    }

    /// <summary>
    /// Get list of available subtitle files for a video.
    /// </summary>
    public List<SubtitleFileInfo> GetAvailableSubtitles(string videoPath)
    {
        return SrtParser.GetAvailableSubtitles(videoPath);
    }

    /// <summary>
    /// Clear loaded subtitles.
    /// </summary>
    public void ClearSubtitles()
    {
        _entries.Clear();
        _currentIndex = -1;
        _lastUpdateTime = -1f;
        CurrentSubtitle = null;
        CurrentSubtitleFile = null;
        HideSubtitle();
    }

    /// <summary>
    /// Update subtitles for current playback time.
    /// Call this every frame during video playback.
    /// </summary>
    public void UpdateTime(float currentTime)
    {
        if (!IsEnabled || _entries.Count == 0)
        {
            if (_subtitleCanvas.activeSelf)
                HideSubtitle();
            return;
        }

        // Apply sync offset
        float adjustedTime = currentTime + SyncOffset;

        // Skip if time hasn't changed significantly
        if (Mathf.Abs(adjustedTime - _lastUpdateTime) < 0.01f)
            return;

        _lastUpdateTime = adjustedTime;

        // Find current subtitle
        SubtitleEntry? newSubtitle = FindSubtitleAt(adjustedTime);

        // Check if subtitle changed
        if (newSubtitle.HasValue)
        {
            if (!CurrentSubtitle.HasValue || CurrentSubtitle.Value.Index != newSubtitle.Value.Index)
            {
                // New subtitle
                ShowSubtitle(newSubtitle.Value);
                OnSubtitleEntered?.Invoke(newSubtitle.Value);
            }
        }
        else
        {
            if (CurrentSubtitle.HasValue)
            {
                // No subtitle at this time
                HideSubtitle();
                OnSubtitleExited?.Invoke();
            }
        }

        CurrentSubtitle = newSubtitle;
    }

    /// <summary>
    /// Seek to time (reset subtitle search).
    /// </summary>
    public void Seek(float time)
    {
        _currentIndex = -1;
        _lastUpdateTime = -1f;
        UpdateTime(time);
    }

    /// <summary>
    /// Set subtitle position in world space.
    /// </summary>
    public void SetWorldPosition(Vector3 position, Quaternion rotation)
    {
        if (_subtitleCanvas != null)
        {
            _subtitleCanvas.transform.position = position;
            _subtitleCanvas.transform.rotation = rotation;
        }
    }

    /// <summary>
    /// Position subtitles relative to video screen.
    /// </summary>
    public void PositionBelowScreen(Transform screenTransform, float screenHeight)
    {
        if (_subtitleCanvas == null || screenTransform == null) return;

        // Position below the screen
        Vector3 offset = screenTransform.up * (-(screenHeight / 2f) - BottomOffset * 0.001f);
        _subtitleCanvas.transform.position = screenTransform.position + offset;
        _subtitleCanvas.transform.rotation = screenTransform.rotation;
    }

    /// <summary>
    /// Adjust sync offset.
    /// </summary>
    public void AdjustSyncOffset(float deltaSeconds)
    {
        SyncOffset = Mathf.Clamp(SyncOffset + deltaSeconds, -10f, 10f);
        Debug.Log($"[SubtitleManager] Sync offset: {SyncOffset:F2}s");
    }
    #endregion

    #region Private Methods
    private SubtitleEntry? FindSubtitleAt(float time)
    {
        if (_entries.Count == 0)
            return null;

        // Optimization: Start from current index if sequential
        int startIndex = 0;
        if (_currentIndex >= 0 && _currentIndex < _entries.Count)
        {
            // Check if current subtitle is still valid
            if (_entries[_currentIndex].IsVisibleAt(time))
            {
                return _entries[_currentIndex];
            }

            // Check next subtitle
            if (_currentIndex + 1 < _entries.Count && _entries[_currentIndex + 1].IsVisibleAt(time))
            {
                _currentIndex++;
                return _entries[_currentIndex];
            }

            startIndex = _currentIndex;
        }

        // Binary search for large jumps
        if (Mathf.Abs(time - _lastUpdateTime) > 5f)
        {
            int lo = 0, hi = _entries.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_entries[mid].EndTime < time)
                    lo = mid + 1;
                else if (_entries[mid].StartTime > time)
                    hi = mid - 1;
                else
                {
                    _currentIndex = mid;
                    return _entries[mid];
                }
            }
            // No subtitle found
            _currentIndex = lo;
            return null;
        }

        // Linear search nearby
        for (int i = Mathf.Max(0, startIndex - 2); i < Mathf.Min(_entries.Count, startIndex + 10); i++)
        {
            if (_entries[i].IsVisibleAt(time))
            {
                _currentIndex = i;
                return _entries[i];
            }
        }

        return null;
    }

    private void ShowSubtitle(SubtitleEntry entry)
    {
        if (!_isUIInitialized)
            InitializeUI();

        _subtitleText.text = entry.Text;
        _subtitleCanvas.SetActive(true);
        OnSubtitleTextChanged?.Invoke(entry.Text);
    }

    private void HideSubtitle()
    {
        if (_subtitleCanvas != null)
        {
            _subtitleCanvas.SetActive(false);
        }
        OnSubtitleTextChanged?.Invoke("");
    }
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        if (_subtitleCanvas != null)
        {
            Destroy(_subtitleCanvas);
        }
    }
    #endregion
}
