using System.Collections;

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
    /// Bind data to UI safely with frame budget to prevent lag.
    /// Called after fade-in animation completes.
    /// Shows loading spinner if data not ready yet.
    /// </summary>
    IEnumerator BindDataSafely();

    /// <summary>
    /// Show loading spinner in the content area.
    /// </summary>
    void ShowLoadingSpinner();

    /// <summary>
    /// Hide loading spinner.
    /// </summary>
    void HideLoadingSpinner();
}
