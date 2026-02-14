using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using TMPro;
using VRWorkspace.UI.HoverEffects;

/// <summary>
/// Error dialog for displaying playback errors and issues.
/// Provides action buttons for retry, go back, or dismiss.
/// </summary>
public class MediaErrorDialog : MonoBehaviour
{
    #region Constants
    private const float DIALOG_WIDTH = 500f;
    private const float DIALOG_MIN_HEIGHT = 250f;
    private const float PADDING = 30f;
    private const float BUTTON_HEIGHT = 50f;
    private const float FADE_DURATION = 0.3f;
    #endregion

    #region Events
    public event Action OnRetryClicked;
    public event Action OnBackClicked;
    public event Action OnDismissed;
    public event Action OnOpenExternalClicked;
    #endregion

    #region Error Types
    public enum ErrorType
    {
        FileNotFound,
        CodecNotSupported,
        UnsupportedContainer,
        NetworkError,
        PermissionDenied,
        UnknownError
    }
    #endregion

    #region Private Fields
    private TMP_FontAsset _font;
    private Color _primaryColor;
    private Color _accentColor;

    private GameObject _dialog;
    private CanvasGroup _canvasGroup;
    private Image _iconImage;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _messageText;
    private Button _retryButton;
    private TextMeshProUGUI _retryButtonText;
    private Button _backButton;
    private Button _openExternalButton;
    private Coroutine _fadeCoroutine;
    #endregion

    #region Initialization
    public void Initialize(TMP_FontAsset font, Color primaryColor, Color accentColor)
    {
        _font = font;
        _primaryColor = primaryColor;
        _accentColor = accentColor;

        BuildUI();
        Hide();
    }

    private void BuildUI()
    {
        // Overlay background
        GameObject overlayObj = new GameObject("ErrorOverlay");
        overlayObj.transform.SetParent(transform, false);

        var overlayRT = overlayObj.AddComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        var overlayBg = overlayObj.AddComponent<Image>();
        overlayBg.color = new Color(0, 0, 0, 0.7f);

        // Click to dismiss
        var overlayButton = overlayObj.AddComponent<Button>();
        overlayButton.onClick.AddListener(() => OnDismissed?.Invoke());

        // Dialog container
        _dialog = new GameObject("ErrorDialog");
        _dialog.transform.SetParent(overlayObj.transform, false);

        var dialogRT = _dialog.AddComponent<RectTransform>();
        dialogRT.anchorMin = new Vector2(0.5f, 0.5f);
        dialogRT.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRT.sizeDelta = new Vector2(DIALOG_WIDTH, DIALOG_MIN_HEIGHT);

        // Dialog background
        var dialogBg = _dialog.AddComponent<Image>();
        dialogBg.color = new Color(0.15f, 0.15f, 0.18f, 0.98f);

        // Canvas group for fade
        _canvasGroup = overlayObj.AddComponent<CanvasGroup>();

        // Content
        GameObject contentObj = new GameObject("Content");
        contentObj.transform.SetParent(_dialog.transform, false);

        var contentRT = contentObj.AddComponent<RectTransform>();
        contentRT.anchorMin = Vector2.zero;
        contentRT.anchorMax = Vector2.one;
        contentRT.offsetMin = new Vector2(PADDING, PADDING);
        contentRT.offsetMax = new Vector2(-PADDING, -PADDING);

        var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 20;
        contentLayout.childAlignment = TextAnchor.MiddleCenter;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = false;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        // Error icon
        CreateErrorIcon(contentObj.transform);

        // Title
        _titleText = CreateText(contentObj.transform, "Error", 32, FontStyles.Bold);

        // Message
        _messageText = CreateText(contentObj.transform, "An error occurred.", 24, FontStyles.Normal, new Color(1, 1, 1, 0.8f));

        // Buttons container
        CreateButtons(contentObj.transform);
    }

    private void CreateErrorIcon(Transform parent)
    {
        GameObject iconContainer = new GameObject("IconContainer");
        iconContainer.transform.SetParent(parent, false);

        var containerLE = iconContainer.AddComponent<LayoutElement>();
        containerLE.minHeight = 60;
        containerLE.preferredHeight = 60;

        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(iconContainer.transform, false);

        var iconRT = iconObj.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.5f);
        iconRT.anchorMax = new Vector2(0.5f, 0.5f);
        iconRT.sizeDelta = new Vector2(50, 50);

        _iconImage = iconObj.AddComponent<Image>();
        _iconImage.color = new Color(1, 0.4f, 0.4f);  // Red tint for error

        // Error X symbol (using text)
        GameObject symbolObj = new GameObject("Symbol");
        symbolObj.transform.SetParent(iconObj.transform, false);

        var symbolRT = symbolObj.AddComponent<RectTransform>();
        symbolRT.anchorMin = Vector2.zero;
        symbolRT.anchorMax = Vector2.one;
        symbolRT.offsetMin = Vector2.zero;
        symbolRT.offsetMax = Vector2.zero;

        var symbolText = symbolObj.AddComponent<TextMeshProUGUI>();
        symbolText.text = "!";
        symbolText.font = _font;
        symbolText.fontSize = 36;
        symbolText.fontStyle = FontStyles.Bold;
        symbolText.color = Color.white;
        symbolText.alignment = TextAlignmentOptions.Center;
    }

    private TextMeshProUGUI CreateText(Transform parent, string text, int fontSize, FontStyles style, Color? color = null)
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(parent, false);

        var textLE = textObj.AddComponent<LayoutElement>();
        textLE.minHeight = fontSize + 15;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.font = _font;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color ?? Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = true;

        return tmp;
    }

    private void CreateButtons(Transform parent)
    {
        GameObject buttonContainer = new GameObject("Buttons");
        buttonContainer.transform.SetParent(parent, false);

        var containerLE = buttonContainer.AddComponent<LayoutElement>();
        containerLE.minHeight = BUTTON_HEIGHT + 10;
        containerLE.preferredHeight = BUTTON_HEIGHT + 10;

        var containerLayout = buttonContainer.AddComponent<HorizontalLayoutGroup>();
        containerLayout.spacing = 15;
        containerLayout.childAlignment = TextAnchor.MiddleCenter;
        containerLayout.childControlWidth = true;
        containerLayout.childControlHeight = true;
        containerLayout.childForceExpandWidth = true;
        containerLayout.childForceExpandHeight = true;

        // Back button
        _backButton = CreateButton(buttonContainer.transform, "Go Back", false);
        _backButton.onClick.AddListener(() => OnBackClicked?.Invoke());

        // Open in external player button (hidden by default)
        _openExternalButton = CreateButton(buttonContainer.transform, "Open in Player", true);
        _openExternalButton.onClick.AddListener(() => OnOpenExternalClicked?.Invoke());
        _openExternalButton.gameObject.SetActive(false);

        // Retry button (also used as "Convert & Play")
        _retryButton = CreateButton(buttonContainer.transform, "Retry", true);
        _retryButtonText = _retryButton.GetComponentInChildren<TextMeshProUGUI>();
        _retryButton.onClick.AddListener(() => OnRetryClicked?.Invoke());
    }

    private Button CreateButton(Transform parent, string label, bool isPrimary)
    {
        GameObject buttonObj = new GameObject($"Btn_{label}");
        buttonObj.transform.SetParent(parent, false);

        var bgImage = buttonObj.AddComponent<Image>();
        bgImage.color = isPrimary ? _primaryColor : new Color(1, 1, 1, 0.15f);

        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = bgImage;

        // Text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRT = textObj.AddComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        var text = textObj.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = _font;
        text.fontSize = 24;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;

        // Hover effect
        var hoverController = buttonObj.AddComponent<HoverEffectController>();
        hoverController.AddEffect(new ScaleHoverEffect().WithHoverScale(1.05f));

        // VR Collider
        var collider = buttonObj.AddComponent<BoxCollider>();
        collider.size = new Vector3(150, BUTTON_HEIGHT, 10);
        collider.center = new Vector3(0, 0, -5);

        return button;
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Show error dialog with custom message.
    /// </summary>
    public void Show(string title, string message, bool showRetry = true, bool showOpenExternal = false)
    {
        _titleText.text = title;
        _messageText.text = message;
        _retryButton.gameObject.SetActive(showRetry);
        _openExternalButton.gameObject.SetActive(showOpenExternal);

        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        gameObject.SetActive(true);
        _fadeCoroutine = StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Show error dialog for specific error type.
    /// </summary>
    public void ShowError(ErrorType errorType, string additionalInfo = "")
    {
        string title;
        string message;
        bool showRetry = false;
        bool showOpenExternal = false;

        switch (errorType)
        {
            case ErrorType.FileNotFound:
                title = "File Not Found";
                message = "The video file could not be found. It may have been moved or deleted.";
                break;

            case ErrorType.CodecNotSupported:
                title = "Unsupported Codec";
                message = "This video uses a codec that is not supported by the built-in player.\n\nYou can open it in your system's default video player, or convert it to H.264 (MP4) for compatibility.";
                showOpenExternal = true;
                break;

            case ErrorType.UnsupportedContainer:
                title = "Unsupported Format";
                message = "This video file uses a container format that is not supported by the built-in player.\n\nYou can try opening it in your system's default video player instead.";
                showOpenExternal = true;
                break;

            case ErrorType.NetworkError:
                title = "Network Error";
                message = "Failed to load the video. Please check your connection and try again.";
                showRetry = true;
                break;

            case ErrorType.PermissionDenied:
                title = "Permission Denied";
                message = "Cannot access the video file. Please check file permissions.";
                break;

            default:
                title = "Playback Error";
                message = "An unexpected error occurred while playing the video.";
                showOpenExternal = true;
                break;
        }

        if (!string.IsNullOrEmpty(additionalInfo))
        {
            message += $"\n\nDetails: {additionalInfo}";
        }

        Show(title, message, showRetry, showOpenExternal);
    }

    /// <summary>
    /// Change the retry button label (e.g., "Convert & Play") and make it visible.
    /// </summary>
    public void SetRetryLabel(string label)
    {
        if (_retryButtonText != null)
            _retryButtonText.text = label;
        _retryButton.gameObject.SetActive(true);
    }

    /// <summary>
    /// Update the message text while dialog is visible (for download/convert progress).
    /// </summary>
    public void UpdateProgress(string message)
    {
        if (_messageText != null)
            _messageText.text = message;
    }

    /// <summary>
    /// Show generic error with exception details.
    /// </summary>
    public void ShowException(Exception ex)
    {
        string message = "An error occurred during video playback.";

        if (ex != null)
        {
            // Determine error type from exception
            if (ex is System.IO.FileNotFoundException)
            {
                ShowError(ErrorType.FileNotFound);
                return;
            }
            else if (ex is UnauthorizedAccessException)
            {
                ShowError(ErrorType.PermissionDenied);
                return;
            }

            message += $"\n\n{ex.Message}";
        }

        Show("Playback Error", message);
    }

    /// <summary>
    /// Hide error dialog.
    /// </summary>
    public void Hide()
    {
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeOut());
    }
    #endregion

    #region Private Methods
    private IEnumerator FadeIn()
    {
        float elapsed = 0f;
        float startAlpha = _canvasGroup.alpha;

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 1f, elapsed / FADE_DURATION);
            yield return null;
        }

        _canvasGroup.alpha = 1f;
    }

    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        float startAlpha = _canvasGroup.alpha;

        while (elapsed < FADE_DURATION)
        {
            elapsed += Time.deltaTime;
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / FADE_DURATION);
            yield return null;
        }

        _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }
    #endregion
}
