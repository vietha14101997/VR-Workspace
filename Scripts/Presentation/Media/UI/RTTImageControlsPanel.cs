using UnityEngine;
using System;
using VRWorkspace.Media.Data;

namespace VRWorkspace.Media.UI
{
    /// <summary>
    /// UI controls panel for VR Image Viewer.
    /// Provides navigation, zoom, and slideshow controls.
    /// </summary>
    public class RTTImageControlsPanel : MonoBehaviour
    {
        #region Events
        public event Action OnPrevious;
        public event Action OnNext;
        public event Action OnSlideshowToggle;
        public event Action<float> OnZoomChanged;
        public event Action OnBackClicked;
        public event Action OnInfoToggle;
        #endregion

        #region Properties
        public bool IsVisible { get; private set; }
        #endregion

        #region Private Fields
        private bool _isSlideshowPlaying;
        private float _zoomLevel = 1f;
        private float _loadingProgress;
        private MediaImageInfo? _currentImage;
        #endregion

        #region Public API
        /// <summary>
        /// Initialize the controls panel
        /// </summary>
        public void Initialize(float width, float height, TMPro.TMP_FontAsset font, Color primaryColor, Color accentColor)
        {
            // TODO: Build UI elements
            Debug.Log("[RTTImageControlsPanel] Initialized (placeholder)");
        }

        /// <summary>
        /// Show the controls panel
        /// </summary>
        public void Show()
        {
            IsVisible = true;
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Hide the controls panel
        /// </summary>
        public void Hide()
        {
            IsVisible = false;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Set current image info for display
        /// </summary>
        public void SetImageInfo(MediaImageInfo imageInfo)
        {
            _currentImage = imageInfo;
            // TODO: Update UI with image info
        }

        /// <summary>
        /// Set slideshow playing state
        /// </summary>
        public void SetSlideshowState(bool isPlaying)
        {
            _isSlideshowPlaying = isPlaying;
            // TODO: Update slideshow button icon
        }

        /// <summary>
        /// Set zoom level display
        /// </summary>
        public void SetZoomLevel(float zoom)
        {
            _zoomLevel = zoom;
            // TODO: Update zoom slider/display
        }

        /// <summary>
        /// Set loading progress (0-1)
        /// </summary>
        public void SetLoadingProgress(float progress)
        {
            _loadingProgress = progress;
            // TODO: Update loading indicator
        }

        /// <summary>
        /// Show error message
        /// </summary>
        public void ShowError(string message)
        {
            Debug.LogError($"[RTTImageControlsPanel] {message}");
            // TODO: Show error UI
        }

        /// <summary>
        /// Show image info overlay
        /// </summary>
        public void ShowInfo(MediaImageInfo? imageInfo)
        {
            // TODO: Show info panel with EXIF data
        }
        #endregion

        #region Internal - Button Handlers
        // These will be wired to UI buttons when built

        internal void HandlePreviousClick()
        {
            OnPrevious?.Invoke();
        }

        internal void HandleNextClick()
        {
            OnNext?.Invoke();
        }

        internal void HandleSlideshowClick()
        {
            OnSlideshowToggle?.Invoke();
        }

        internal void HandleZoomChange(float value)
        {
            OnZoomChanged?.Invoke(value);
        }

        internal void HandleBackClick()
        {
            OnBackClicked?.Invoke();
        }

        internal void HandleInfoClick()
        {
            OnInfoToggle?.Invoke();
        }
        #endregion
    }

}
