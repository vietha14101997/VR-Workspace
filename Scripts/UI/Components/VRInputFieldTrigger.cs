using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Trigger component for VR Input Fields.
/// Handles click/pointer events to show VR keyboard.
/// Blocks system keyboard input and redirects to virtual keyboard.
/// NOTE: Hover effect (cursor change) is now handled by HoverEffectController.
/// </summary>
public class VRInputFieldTrigger : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
{
    private TMP_InputField _inputField;
    private bool _isInitialized = false;

    /// <summary>
    /// Initialize with the target input field
    /// </summary>
    public void Initialize(TMP_InputField inputField)
    {
        _inputField = inputField;
        _isInitialized = true;

        if (_inputField != null)
        {
            // On Android VR (Quest, etc.), we should block system keyboard
            // On PC VR, physical keyboard can work alongside VR keyboard
#if UNITY_ANDROID && !UNITY_EDITOR
            // For Android VR, prevent system keyboard popup
            _inputField.shouldHideMobileInput = true;
            _inputField.shouldHideSoftKeyboard = true;
#endif

            // Register with keyboard manager if available
            if (VRKeyboardManager.Instance != null)
            {
                VRKeyboardManager.Instance.RegisterInputField(_inputField);
            }
        }
    }

    void OnDestroy()
    {
        if (_inputField != null && VRKeyboardManager.Instance != null)
        {
            VRKeyboardManager.Instance.UnregisterInputField(_inputField);
        }
    }

    /// <summary>
    /// Handle pointer click events (VR controller or mouse)
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_inputField == null) return;

        // Không xử lý nếu input field bị khóa
        if (!_inputField.interactable) return;

        // Calculate caret position from click position
        int caretPos = GetCharacterIndexFromPosition(eventData);
        _inputField.caretPosition = caretPos;

        // If RTT keyboard is already open for this input, update its caret position
        if (RTTMobileKeyboard.CurrentlyOpenKeyboard != null)
        {
            RTTMobileKeyboard.CurrentlyOpenKeyboard.SetCaretPosition(caretPos);
        }
        else
        {
            // Show keyboard if not open
            ShowKeyboard();
        }
    }

    /// <summary>
    /// Calculate character index from click position
    /// </summary>
    private int GetCharacterIndexFromPosition(PointerEventData eventData)
    {
        if (_inputField == null) return 0;

        string text = _inputField.text;
        if (string.IsNullOrEmpty(text)) return 0;

        // Get screen position - use RTTRaycastManager for RTT context
        Vector2 screenPos = eventData.position;
        if (RTTRaycastManager.Instance != null && RTTRaycastManager.Instance.CurrentHit.isValid)
        {
            screenPos = RTTRaycastManager.Instance.CurrentHit.screenPosition;
        }

        // Get the text component from input field
        TMP_Text textComponent = _inputField.textComponent;
        if (textComponent == null) return text.Length;

        // Force mesh update
        textComponent.ForceMeshUpdate();

        var textInfo = textComponent.textInfo;
        if (textInfo.characterCount == 0) return 0;

        // Get canvas and camera
        Canvas canvas = _inputField.GetComponentInParent<Canvas>();
        Camera cam = canvas?.worldCamera;

        // Convert screen position to local position
        RectTransform rectTransform = textComponent.rectTransform;
        Vector2 localPoint;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPos, cam, out localPoint))
        {
            // Fallback: estimate based on text length and x position
            return EstimateCharacterIndex(screenPos, text);
        }

        // Find character by comparing x positions
        for (int i = 0; i < textInfo.characterCount && i < text.Length; i++)
        {
            var charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible) continue;

            float charCenterX = (charInfo.bottomLeft.x + charInfo.bottomRight.x) / 2f;

            if (localPoint.x < charCenterX)
            {
                return i;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Estimate character index when coordinate conversion fails
    /// </summary>
    private int EstimateCharacterIndex(Vector2 screenPos, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        RectTransform rt = _inputField.GetComponent<RectTransform>();
        if (rt == null) return text.Length;

        // Get rect bounds in screen space
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        Canvas canvas = _inputField.GetComponentInParent<Canvas>();
        Camera cam = canvas?.worldCamera;

        if (cam != null)
        {
            for (int i = 0; i < 4; i++)
            {
                corners[i] = cam.WorldToScreenPoint(corners[i]);
            }
        }

        float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
        float maxX = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x);

        float normalizedX = Mathf.Clamp01((screenPos.x - minX) / (maxX - minX));
        return Mathf.RoundToInt(normalizedX * text.Length);
    }

    /// <summary>
    /// Handle pointer down events
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        // Không xử lý nếu input field bị khóa
        if (_inputField == null || !_inputField.interactable) return;

        // Select the input field for visual feedback
        EventSystem.current?.SetSelectedGameObject(_inputField.gameObject);
    }

    /// <summary>
    /// Show the VR keyboard for this input field.
    /// Uses RTTMobileKeyboard exclusively.
    /// </summary>
    public void ShowKeyboard()
    {
        if (!_isInitialized || _inputField == null) return;

        // Không mở keyboard nếu input field bị khóa
        if (!_inputField.interactable) return;

        // Use VRKeyboardManager if available (it handles RTT vs legacy internally)
        if (VRKeyboardManager.Instance != null)
        {
            VRKeyboardManager.Instance.ShowKeyboardForInput(_inputField);
        }
        else
        {
            // Fallback: Use RTT keyboard directly
            RTTMobileKeyboard rttKeyboard = RTTMobileKeyboard.Instance;
            if (rttKeyboard == null)
            {
                // Auto-spawn RTTMobileKeyboard if not found
                rttKeyboard = SpawnRTTMobileKeyboard();
            }

            if (rttKeyboard != null)
            {
                rttKeyboard.Show(_inputField);
            }
            else
            {
                Debug.LogWarning("[VRInputFieldTrigger] Failed to create RTTMobileKeyboard.");
            }
        }
    }

    /// <summary>
    /// Spawn RTTMobileKeyboard if it doesn't exist in the scene.
    /// </summary>
    private RTTMobileKeyboard SpawnRTTMobileKeyboard()
    {
        // Find VirtualObjects parent in scene
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");

        // Create a new GameObject with RTTMobileKeyboard
        GameObject keyboardObj = new GameObject("RTTMobileKeyboard");

        if (virtualObjectsParent != null)
        {
            keyboardObj.transform.SetParent(virtualObjectsParent.transform, false);
        }

        // Set VirtualObjects layer
        int virtualObjectsLayer = LayerMask.NameToLayer("VirtualObjects");
        if (virtualObjectsLayer != -1)
        {
            keyboardObj.layer = virtualObjectsLayer;
        }

        RTTMobileKeyboard keyboard = keyboardObj.AddComponent<RTTMobileKeyboard>();

        // Position will be set by RTTMobileKeyboard.Show() via UpdatePositionRelativeToTaskbar()
        Debug.Log("[VRInputFieldTrigger] Auto-spawned RTTMobileKeyboard");
        return keyboard;
    }

    /// <summary>
    /// Hide the VR keyboard
    /// </summary>
    public void HideKeyboard()
    {
        if (VRKeyboardManager.Instance != null)
        {
            VRKeyboardManager.Instance.HideKeyboard();
        }
        else
        {
            // Fallback: Hide RTT keyboard directly
            RTTMobileKeyboard rttKeyboard = RTTMobileKeyboard.Instance;
            if (rttKeyboard != null)
            {
                rttKeyboard.Hide();
            }
        }
    }

    /// <summary>
    /// Get the associated input field
    /// </summary>
    public TMP_InputField InputField => _inputField;

    /// <summary>
    /// Set input field value programmatically
    /// </summary>
    public void SetValue(string value)
    {
        if (_inputField != null)
        {
            _inputField.text = value;
        }
    }

    /// <summary>
    /// Get input field value
    /// </summary>
    public string GetValue()
    {
        return _inputField != null ? _inputField.text : "";
    }
}
