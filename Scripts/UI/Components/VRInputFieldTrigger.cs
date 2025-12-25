using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Trigger component for VR Input Fields.
/// Handles click/pointer events to show VR keyboard.
/// Blocks system keyboard input and redirects to virtual keyboard.
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
        ShowKeyboard();
    }

    /// <summary>
    /// Handle pointer down events
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        // Select the input field for visual feedback
        if (_inputField != null)
        {
            EventSystem.current?.SetSelectedGameObject(_inputField.gameObject);
        }
    }

    /// <summary>
    /// Show the VR keyboard for this input field.
    /// Uses RTTMobileKeyboard if RTTFeatureToggle.UseRTT is enabled.
    /// </summary>
    public void ShowKeyboard()
    {
        if (!_isInitialized || _inputField == null) return;

        // Use VRKeyboardManager if available (it handles RTT vs legacy internally)
        if (VRKeyboardManager.Instance != null)
        {
            VRKeyboardManager.Instance.ShowKeyboardForInput(_inputField);
        }
        else
        {
            // Fallback based on RTT feature toggle
            if (RTTFeatureToggle.UseRTT)
            {
                // Use RTT keyboard
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
            else
            {
                // Fallback to legacy mobile keyboard
                Canvas canvas = GetComponentInParent<Canvas>();
                Transform keyboardParent = canvas != null ? canvas.transform : null;
                VRMobileKeyboard.ShowForInput(_inputField, keyboardParent);
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
            // Hide based on RTT feature toggle
            if (RTTFeatureToggle.UseRTT)
            {
                RTTMobileKeyboard rttKeyboard = RTTMobileKeyboard.Instance;
                if (rttKeyboard != null)
                {
                    rttKeyboard.Hide();
                }
            }
            else
            {
                VRMobileKeyboard.HideKeyboard();
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
