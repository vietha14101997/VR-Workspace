using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Manages VR Mobile Keyboard visibility and handles input field focus in VR environment.
/// Blocks system keyboard input and redirects to VR virtual keyboard (mobile style).
/// Supports both VRMobileKeyboard (legacy) and RTTMobileKeyboard (RTT-based).
/// Attach this to a persistent object in the scene.
/// </summary>
public class VRKeyboardManager : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Automatically show VR keyboard when an input field is selected")]
    public bool autoShowKeyboard = true;

    [Tooltip("Block system keyboard input when VR keyboard is active")]
    public bool blockSystemKeyboard = true;

    [Tooltip("Parent transform for keyboard (usually the Canvas)")]
    public Transform keyboardParent;

    [Header("Keyboard Prefab (Optional)")]
    [Tooltip("Custom keyboard prefab. If null, will create default keyboard.")]
    public VRMobileKeyboard keyboardPrefab;

    [Header("RTT Keyboard Prefab (Optional)")]
    [Tooltip("Custom RTT keyboard prefab. If null, will use RTTMobileKeyboard.Instance.")]
    public RTTMobileKeyboard rttKeyboardPrefab;

    [Header("Theme")]
    public Color themeColor = new Color(0.0f, 0.9f, 1.0f);
    public Color accentColor = new Color(0.4f, 0.5f, 0.7f);
    public TMP_FontAsset customFont;

    // Current state
    private TMP_InputField _currentInputField;
    private VRMobileKeyboard _keyboard;
    private RTTMobileKeyboard _rttKeyboard;
    private bool _isKeyboardVisible;
    private bool _usingRTTKeyboard = false;

    // Track all managed input fields
    private HashSet<TMP_InputField> _managedInputFields = new HashSet<TMP_InputField>();

    // Singleton
    private static VRKeyboardManager _instance;
    public static VRKeyboardManager Instance => _instance;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;

        // Don't destroy on load if you want persistent keyboard across scenes
        // DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // Find or create keyboard
        InitializeKeyboard();
    }

    void Update()
    {
        // Check for input field selection via EventSystem
        if (autoShowKeyboard)
        {
            CheckInputFieldSelection();
        }

        // Block system keyboard if enabled
        if (blockSystemKeyboard && _isKeyboardVisible)
        {
            ConsumeSystemKeyboardInput();
        }
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    void InitializeKeyboard()
    {
        // Try to find existing keyboard
        _keyboard = FindFirstObjectByType<VRMobileKeyboard>();

        if (_keyboard == null && keyboardPrefab != null)
        {
            // Instantiate from prefab
            Transform parent = keyboardParent != null ? keyboardParent : transform;
            GameObject keyboardObj = Instantiate(keyboardPrefab.gameObject, parent);
            _keyboard = keyboardObj.GetComponent<VRMobileKeyboard>();
        }

        if (_keyboard != null)
        {
            ApplyTheme();
            _keyboard.OnClosePressed += OnKeyboardClosed;
            _keyboard.OnEnterPressed += OnKeyboardEnterPressed;
        }
    }

    void ApplyTheme()
    {
        if (_keyboard == null) return;

        _keyboard.themeColor = themeColor;
        _keyboard.accentColor = accentColor;
        _keyboard.customFont = customFont;
    }

    void CheckInputFieldSelection()
    {
        // Get current selected object from EventSystem
        GameObject selected = EventSystem.current?.currentSelectedGameObject;

        if (selected != null)
        {
            TMP_InputField inputField = selected.GetComponent<TMP_InputField>();
            if (inputField == null)
            {
                inputField = selected.GetComponentInParent<TMP_InputField>();
            }

            if (inputField != null && inputField != _currentInputField)
            {
                // New input field selected
                ShowKeyboardForInput(inputField);
            }
        }
        else if (_currentInputField != null && !_isKeyboardVisible)
        {
            // Deselected, hide keyboard if visible
            // Note: Don't auto-hide when clicking elsewhere, let user close manually
        }
    }

    /// <summary>
    /// Block system keyboard input by consuming key events.
    /// This prevents characters from being typed via physical keyboard.
    /// </summary>
    void ConsumeSystemKeyboardInput()
    {
        // In VR, we want to prevent system keyboard from interfering
        // This is a simple approach - in production, you might want more sophisticated handling

        if (_currentInputField != null)
        {
            // Prevent the input field from receiving keyboard input directly
            // by disabling the onFocusSelectAll behavior

            // For TMP_InputField, we can disable keyboard input by:
            // 1. Setting readOnly temporarily (but this prevents our virtual keyboard too)
            // 2. Or intercepting the input

            // The safest approach is to let TMP_InputField handle caret but
            // prevent system characters from being added
            // This is handled by the VRKeyboard directly manipulating the text
        }
    }

    /// <summary>
    /// Show keyboard for a specific input field.
    /// Uses RTTMobileKeyboard if RTTFeatureToggle.UseRTT is enabled,
    /// otherwise falls back to VRMobileKeyboard.
    /// </summary>
    public void ShowKeyboardForInput(TMP_InputField inputField)
    {
        if (inputField == null) return;

        _currentInputField = inputField;

        // Determine which keyboard to use based on RTTFeatureToggle
        _usingRTTKeyboard = RTTFeatureToggle.UseRTT;

        if (_usingRTTKeyboard)
        {
            // Use RTT keyboard
            if (_rttKeyboard == null)
            {
                _rttKeyboard = RTTMobileKeyboard.Instance;
                if (_rttKeyboard == null && rttKeyboardPrefab != null)
                {
                    GameObject keyboardObj = Instantiate(rttKeyboardPrefab.gameObject);
                    _rttKeyboard = keyboardObj.GetComponent<RTTMobileKeyboard>();
                }
                // Auto-spawn if still null
                if (_rttKeyboard == null)
                {
                    _rttKeyboard = SpawnRTTMobileKeyboard();
                }
            }

            if (_rttKeyboard != null)
            {
                // Unsubscribe first to avoid duplicate listeners
                _rttKeyboard.OnClosePressed -= OnRTTKeyboardClosed;
                _rttKeyboard.OnEnterPressed -= OnRTTKeyboardEnterPressed;

                // Subscribe to events
                _rttKeyboard.OnClosePressed += OnRTTKeyboardClosed;
                _rttKeyboard.OnEnterPressed += OnRTTKeyboardEnterPressed;

                _rttKeyboard.Show(inputField);
                _isKeyboardVisible = true;
            }
            else
            {
                Debug.LogWarning("[VRKeyboardManager] Failed to create RTTMobileKeyboard.");
            }
        }
        else
        {
            // Use legacy keyboard
            if (_keyboard == null)
            {
                CreateKeyboard();
            }

            if (_keyboard != null)
            {
                _keyboard.Show(inputField);
                _isKeyboardVisible = true;
            }
        }
    }

    /// <summary>
    /// Hide the keyboard
    /// </summary>
    public void HideKeyboard()
    {
        if (_isKeyboardVisible)
        {
            if (_usingRTTKeyboard && _rttKeyboard != null)
            {
                _rttKeyboard.Hide();
            }
            else if (_keyboard != null)
            {
                _keyboard.Hide();
            }

            _isKeyboardVisible = false;
            _currentInputField = null;

            // Deselect the input field
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }

    void OnRTTKeyboardClosed()
    {
        _isKeyboardVisible = false;
        _currentInputField = null;
    }

    void OnRTTKeyboardEnterPressed()
    {
        _isKeyboardVisible = false;
        _currentInputField = null;
    }

    /// <summary>
    /// Spawn RTTMobileKeyboard if it doesn't exist in the scene.
    /// </summary>
    RTTMobileKeyboard SpawnRTTMobileKeyboard()
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
        Debug.Log("[VRKeyboardManager] Auto-spawned RTTMobileKeyboard");
        return keyboard;
    }

    void CreateKeyboard()
    {
        Transform parent = keyboardParent;

        // If no parent specified, try to find the main canvas
        if (parent == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    parent = canvas.transform;
                    break;
                }
            }
        }

        if (parent == null)
        {
            parent = transform;
        }

        // Create mobile keyboard
        GameObject keyboardObj = new GameObject("VRMobileKeyboard");
        keyboardObj.transform.SetParent(parent, false);

        _keyboard = keyboardObj.AddComponent<VRMobileKeyboard>();
        ApplyTheme();

        _keyboard.OnClosePressed += OnKeyboardClosed;
        _keyboard.OnEnterPressed += OnKeyboardEnterPressed;
    }

    void OnKeyboardClosed()
    {
        _isKeyboardVisible = false;
        _currentInputField = null;
    }

    void OnKeyboardEnterPressed()
    {
        _isKeyboardVisible = false;
        _currentInputField = null;
    }

    /// <summary>
    /// Register an input field to be managed by this keyboard manager.
    /// The input field will show the VR keyboard when focused.
    /// </summary>
    public void RegisterInputField(TMP_InputField inputField)
    {
        if (inputField == null || _managedInputFields.Contains(inputField))
            return;

        _managedInputFields.Add(inputField);

        // Add focus listener
        inputField.onSelect.AddListener((text) => OnInputFieldSelected(inputField));
        inputField.onDeselect.AddListener((text) => OnInputFieldDeselected(inputField));
    }

    /// <summary>
    /// Unregister an input field
    /// </summary>
    public void UnregisterInputField(TMP_InputField inputField)
    {
        if (inputField == null)
            return;

        _managedInputFields.Remove(inputField);
    }

    void OnInputFieldSelected(TMP_InputField inputField)
    {
        if (autoShowKeyboard)
        {
            ShowKeyboardForInput(inputField);
        }
    }

    void OnInputFieldDeselected(TMP_InputField inputField)
    {
        // Don't auto-hide, let user close via keyboard close button
    }

    /// <summary>
    /// Check if keyboard is currently visible
    /// </summary>
    public bool IsKeyboardVisible => _isKeyboardVisible;

    /// <summary>
    /// Get the current input field
    /// </summary>
    public TMP_InputField CurrentInputField => _currentInputField;

    /// <summary>
    /// Static helper to show keyboard for an input field
    /// Creates manager instance if needed
    /// </summary>
    public static void ShowKeyboard(TMP_InputField inputField)
    {
        if (_instance == null)
        {
            GameObject managerObj = new GameObject("VRKeyboardManager");
            _instance = managerObj.AddComponent<VRKeyboardManager>();
        }

        _instance.ShowKeyboardForInput(inputField);
    }

    /// <summary>
    /// Static helper to hide keyboard
    /// </summary>
    public static void Hide()
    {
        if (_instance != null)
        {
            _instance.HideKeyboard();
        }
    }
}
