using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Controller for VR image viewing.
/// Manages image display, slideshow, pan/zoom, and transitions.
/// </summary>
public class VRImageViewerController : MonoBehaviour
{
    #region Events
    public event Action OnBackToLibrary;
    public event Action<MediaImageInfo> OnImageViewed;
    public event Action<int, int> OnSlideChanged; // (currentIndex, totalCount)
    #endregion

    #region Properties
    public bool IsActive { get; private set; }
    public bool IsSlideshowPlaying { get; private set; }
    public MediaImageInfo? CurrentImage => _currentImage;
    public int CurrentIndex => _currentIndex;
    public int TotalImages => _playlist?.Count ?? 0;
    public float ZoomLevel { get; private set; } = 1f;
    #endregion

    #region Settings
    [Header("Slideshow Settings")]
    [SerializeField] private float _slideshowInterval = 5f;
    [SerializeField] private float _transitionDuration = 0.5f;
    
    [Header("Zoom Settings")]
    [SerializeField] private float _minZoom = 0.5f;
    [SerializeField] private float _maxZoom = 4f;
    [SerializeField] private float _zoomSpeed = 0.5f;
    
    [Header("Pan Settings")]
    [SerializeField] private float _panSpeed = 1f;
    #endregion

    #region Private Fields
    private ImageLoadingEngine _loadingEngine;
    private VRImageProjectionSystem _projectionSystem;
    private RTTImageControlsPanel _controlsPanel;
    private MediaEnvironmentController _environmentController;

    private List<MediaImageInfo> _playlist;
    private MediaImageInfo? _currentImage;
    private int _currentIndex;
    private Coroutine _slideshowCoroutine;
    private Coroutine _transitionCoroutine;
    private bool _isInitialized;
    
    private Vector2 _panOffset;
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize the image viewer controller
    /// </summary>
    public void Initialize(
        ImageLoadingEngine loadingEngine,
        VRImageProjectionSystem projectionSystem,
        RTTImageControlsPanel controlsPanel)
    {
        if (_isInitialized)
        {
            Debug.LogWarning("[VRImageViewerController] Already initialized");
            return;
        }

        _loadingEngine = loadingEngine;
        _projectionSystem = projectionSystem;
        _controlsPanel = controlsPanel;

        // Setup environment controller (optional)
        try
        {
            _environmentController = MediaEnvironmentController.Instance;
            _environmentController.Initialize();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[VRImageViewerController] Environment control disabled: {ex.Message}");
            _environmentController = null;
        }

        WireEvents();
        _isInitialized = true;

        Debug.Log("[VRImageViewerController] Initialized");
    }

    private void WireEvents()
    {
        if (_loadingEngine != null)
        {
            _loadingEngine.OnImageLoaded += HandleImageLoaded;
            _loadingEngine.OnLoadProgress += HandleLoadProgress;
            _loadingEngine.OnError += HandleLoadError;
        }

        if (_controlsPanel != null)
        {
            _controlsPanel.OnPrevious += ShowPrevious;
            _controlsPanel.OnNext += ShowNext;
            _controlsPanel.OnSlideshowToggle += ToggleSlideshow;
            _controlsPanel.OnZoomChanged += SetZoom;
            _controlsPanel.OnBackClicked += HandleBackClicked;
        }
    }

    private void UnwireEvents()
    {
        if (_loadingEngine != null)
        {
            _loadingEngine.OnImageLoaded -= HandleImageLoaded;
            _loadingEngine.OnLoadProgress -= HandleLoadProgress;
            _loadingEngine.OnError -= HandleLoadError;
        }

        if (_controlsPanel != null)
        {
            _controlsPanel.OnPrevious -= ShowPrevious;
            _controlsPanel.OnNext -= ShowNext;
            _controlsPanel.OnSlideshowToggle -= ToggleSlideshow;
            _controlsPanel.OnZoomChanged -= SetZoom;
            _controlsPanel.OnBackClicked -= HandleBackClicked;
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// View a single image
    /// </summary>
    public void ViewImage(MediaImageInfo image)
    {
        _playlist = new List<MediaImageInfo> { image };
        _currentIndex = 0;
        LoadCurrentImage();
        Show();
    }

    /// <summary>
    /// View images from a list (for slideshow)
    /// </summary>
    public void ViewImages(List<MediaImageInfo> images, int startIndex = 0)
    {
        if (images == null || images.Count == 0)
        {
            Debug.LogWarning("[VRImageViewerController] No images to view");
            return;
        }

        _playlist = new List<MediaImageInfo>(images);
        _currentIndex = Mathf.Clamp(startIndex, 0, images.Count - 1);
        LoadCurrentImage();
        Show();
    }

    /// <summary>
    /// Show next image
    /// </summary>
    public void ShowNext()
    {
        if (_playlist == null || _playlist.Count <= 1) return;

        _currentIndex = (_currentIndex + 1) % _playlist.Count;
        TransitionToCurrentImage();
    }

    /// <summary>
    /// Show previous image
    /// </summary>
    public void ShowPrevious()
    {
        if (_playlist == null || _playlist.Count <= 1) return;

        _currentIndex = (_currentIndex - 1 + _playlist.Count) % _playlist.Count;
        TransitionToCurrentImage();
    }

    /// <summary>
    /// Go to specific image index
    /// </summary>
    public void GoToIndex(int index)
    {
        if (_playlist == null || index < 0 || index >= _playlist.Count) return;

        _currentIndex = index;
        TransitionToCurrentImage();
    }

    /// <summary>
    /// Start slideshow
    /// </summary>
    public void StartSlideshow()
    {
        if (IsSlideshowPlaying) return;
        if (_playlist == null || _playlist.Count <= 1) return;

        IsSlideshowPlaying = true;
        _slideshowCoroutine = StartCoroutine(SlideshowCoroutine());
        _controlsPanel?.SetSlideshowState(true);

        Debug.Log("[VRImageViewerController] Slideshow started");
    }

    /// <summary>
    /// Stop slideshow
    /// </summary>
    public void StopSlideshow()
    {
        if (!IsSlideshowPlaying) return;

        IsSlideshowPlaying = false;
        if (_slideshowCoroutine != null)
        {
            StopCoroutine(_slideshowCoroutine);
            _slideshowCoroutine = null;
        }
        _controlsPanel?.SetSlideshowState(false);

        Debug.Log("[VRImageViewerController] Slideshow stopped");
    }

    /// <summary>
    /// Toggle slideshow
    /// </summary>
    public void ToggleSlideshow()
    {
        if (IsSlideshowPlaying)
            StopSlideshow();
        else
            StartSlideshow();
    }

    /// <summary>
    /// Set slideshow interval
    /// </summary>
    public void SetSlideshowInterval(float seconds)
    {
        _slideshowInterval = Mathf.Clamp(seconds, 1f, 30f);
    }

    /// <summary>
    /// Set zoom level
    /// </summary>
    public void SetZoom(float zoom)
    {
        ZoomLevel = Mathf.Clamp(zoom, _minZoom, _maxZoom);
        _projectionSystem?.SetScale(ZoomLevel);
        _controlsPanel?.SetZoomLevel(ZoomLevel);
    }

    /// <summary>
    /// Zoom in
    /// </summary>
    public void ZoomIn()
    {
        SetZoom(ZoomLevel + _zoomSpeed);
    }

    /// <summary>
    /// Zoom out
    /// </summary>
    public void ZoomOut()
    {
        SetZoom(ZoomLevel - _zoomSpeed);
    }

    /// <summary>
    /// Reset zoom to 1x
    /// </summary>
    public void ResetZoom()
    {
        SetZoom(1f);
        _panOffset = Vector2.zero;
        _projectionSystem?.SetPanOffset(_panOffset);
    }

    /// <summary>
    /// Pan the image
    /// </summary>
    public void Pan(Vector2 delta)
    {
        if (ZoomLevel <= 1f) return; // No pan when not zoomed

        _panOffset += delta * _panSpeed;
        
        // Clamp pan based on zoom level
        float maxPan = (ZoomLevel - 1f) * 0.5f;
        _panOffset.x = Mathf.Clamp(_panOffset.x, -maxPan, maxPan);
        _panOffset.y = Mathf.Clamp(_panOffset.y, -maxPan, maxPan);

        _projectionSystem?.SetPanOffset(_panOffset);
    }

    /// <summary>
    /// Show the viewer
    /// </summary>
    public void Show()
    {
        IsActive = true;
        _projectionSystem?.Show();
        _controlsPanel?.Show();

        // Dim environment for better viewing
        _environmentController?.SetLightsEnabled(false);
    }

    /// <summary>
    /// Hide the viewer
    /// </summary>
    public void Hide()
    {
        IsActive = false;
        StopSlideshow();
        
        _projectionSystem?.Hide();
        _controlsPanel?.Hide();
        _loadingEngine?.StopLoading();

        // Restore environment
        _environmentController?.Reset();
    }

    /// <summary>
    /// Stop and cleanup
    /// </summary>
    public void Stop()
    {
        Hide();
        _loadingEngine?.UnloadCurrent();
        _playlist = null;
        _currentImage = null;
        _currentIndex = 0;
        ResetZoom();
    }

    /// <summary>
    /// Show/hide controls
    /// </summary>
    public void ToggleControls()
    {
        if (_controlsPanel != null)
        {
            if (_controlsPanel.gameObject.activeSelf)
                _controlsPanel.Hide();
            else
                _controlsPanel.Show();
        }
    }

    /// <summary>
    /// Show image info overlay
    /// </summary>
    public void ShowInfo()
    {
        _controlsPanel?.ShowInfo(_currentImage);
    }
    #endregion

    #region Private Methods
    private void LoadCurrentImage()
    {
        if (_playlist == null || _currentIndex >= _playlist.Count) return;

        _currentImage = _playlist[_currentIndex];
        _loadingEngine?.LoadImage(_currentImage.Value);

        // Update projection type
        var projectionType = _currentImage.Value.ProjectionType;
        _projectionSystem?.SetProjectionType(projectionType);

        // Update controls
        _controlsPanel?.SetImageInfo(_currentImage.Value);
        OnSlideChanged?.Invoke(_currentIndex, _playlist.Count);
    }

    private void TransitionToCurrentImage()
    {
        if (_transitionCoroutine != null)
        {
            StopCoroutine(_transitionCoroutine);
        }
        _transitionCoroutine = StartCoroutine(TransitionCoroutine());
    }

    private IEnumerator TransitionCoroutine()
    {
        // Fade out
        float elapsed = 0f;
        while (elapsed < _transitionDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / (_transitionDuration * 0.5f));
            _projectionSystem?.SetAlpha(alpha);
            yield return null;
        }

        // Load new image
        ResetZoom();
        LoadCurrentImage();

        // Wait a bit for loading
        yield return new WaitForSeconds(0.1f);

        // Fade in
        elapsed = 0f;
        while (elapsed < _transitionDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float alpha = elapsed / (_transitionDuration * 0.5f);
            _projectionSystem?.SetAlpha(alpha);
            yield return null;
        }

        _projectionSystem?.SetAlpha(1f);
        _transitionCoroutine = null;
    }

    private IEnumerator SlideshowCoroutine()
    {
        while (IsSlideshowPlaying)
        {
            yield return new WaitForSeconds(_slideshowInterval);
            
            if (IsSlideshowPlaying) // Check again in case it was stopped during wait
            {
                ShowNext();
            }
        }
    }
    #endregion

    #region Event Handlers
    private void HandleImageLoaded(Texture2D texture)
    {
        _projectionSystem?.SetTexture(texture);

        if (_currentImage.HasValue)
        {
            OnImageViewed?.Invoke(_currentImage.Value);
        }

        Debug.Log($"[VRImageViewerController] Image loaded: {_currentImage?.Title}");
    }

    private void HandleLoadProgress(float progress)
    {
        _controlsPanel?.SetLoadingProgress(progress);
    }

    private void HandleLoadError(string error)
    {
        Debug.LogError($"[VRImageViewerController] Load error: {error}");
        _controlsPanel?.ShowError(error);
    }

    private void HandleBackClicked()
    {
        Stop();
        OnBackToLibrary?.Invoke();
    }
    #endregion

    #region Unity Lifecycle
    private void OnDestroy()
    {
        Stop();
        UnwireEvents();
    }
    #endregion
}
