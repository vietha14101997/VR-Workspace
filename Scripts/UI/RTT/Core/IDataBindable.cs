using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Interface for app controllers that support background data loading and safe binding.
/// Implement this to enable smooth transitions with parallel data preparation.
/// </summary>
public interface IDataBindable
{
    /// <summary>
    /// Whether the data is ready to be bound to UI.
    /// </summary>
    bool IsDataReady { get; }

    /// <summary>
    /// Whether data is currently being prepared in background.
    /// </summary>
    bool IsPreparingData { get; }

    /// <summary>
    /// Start preparing data in background without blocking UI.
    /// Called immediately when user clicks menu button.
    /// </summary>
    void PrepareDataAsync();

    /// <summary>
    /// Get all frames (main + side panels) for coordinated fade animation.
    /// Returns list of RTTMenuFrame that should fade in together.
    /// </summary>
    List<RTTMenuFrame> GetAllFrames();

    /// <summary>
    /// Bind cached data immediately (non-blocking).
    /// If cache not loaded yet, shows empty grid or loading state.
    /// Background process will update when data is ready.
    /// Called BEFORE fade-in animation starts.
    /// </summary>
    void BindCachedDataOrEmpty();

    /// <summary>
    /// Called when background data loading completes.
    /// Updates UI if app is already visible.
    /// </summary>
    void OnBackgroundDataReady();

    /// <summary>
    /// Show loading spinner in the content area.
    /// </summary>
    void ShowLoadingSpinner();

    /// <summary>
    /// Hide loading spinner.
    /// </summary>
    void HideLoadingSpinner();

    /// <summary>
    /// Called when app transition completes and app is fully visible.
    /// Use this for post-transition setup.
    /// </summary>
    void OnAppShown();

    #region State Caching Support

    /// <summary>
    /// Whether this app supports full state caching (grid position, items, thumbnails, etc.).
    /// Apps like FileManager and MediaLibrary should return true.
    /// </summary>
    bool SupportsStateCaching { get; }

    /// <summary>
    /// Try to restore UI state from cache.
    /// Returns true if valid cache exists and was restored successfully.
    /// Called during transition when background data is not ready yet.
    /// </summary>
    bool TryRestoreCachedState();

    /// <summary>
    /// Save current UI state to cache.
    /// Called after:
    /// 1. OnBackgroundDataReady() completes binding
    /// 2. Reload button finishes refreshing
    /// 3. Grid state stabilizes after data binding
    /// </summary>
    void CacheCurrentState();

    /// <summary>
    /// Get the data buffer prepared by background thread.
    /// Returns null if data is not ready yet.
    /// Used for synchronization when data ready before UI.
    /// </summary>
    object GetPreparedDataBuffer();

    /// <summary>
    /// Bind prepared data buffer directly to UI.
    /// Called when data was ready before UI transition completed.
    /// </summary>
    /// <param name="dataBuffer">The data buffer from GetPreparedDataBuffer()</param>
    void BindPreparedData(object dataBuffer);

    /// <summary>
    /// Event fired when background data preparation completes.
    /// RTTAppManager subscribes to this to trigger OnBackgroundDataReady() and CacheCurrentState().
    /// </summary>
    event Action OnDataPrepared;

    #endregion
}
