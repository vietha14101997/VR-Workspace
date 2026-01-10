using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

/// <summary>
/// RTTTaskbar - Logic controller that attaches to RTTMiniFrame.
/// Manages control buttons and app slots.
/// RTTMiniFrame handles all RTT infrastructure, visual frame, and position tracking.
/// </summary>
[RequireComponent(typeof(RTTMiniFrame))]
public class RTTTaskbar : MonoBehaviour
{
    #region Static Instance
    private static RTTTaskbar _instance;
    public static RTTTaskbar Instance => _instance;
    #endregion

    #region Configuration
    [Header("Style Resources")]
    [SerializeField] private Sprite iconQuit;
    [SerializeField] private Sprite iconSettings;
    [SerializeField] private Sprite iconPassthrough;
    [SerializeField] private Sprite iconRecenter;
    [SerializeField] private Sprite iconHome;
    #endregion

    #region Private Fields
    private RTTMiniFrame _miniFrame;
    private GameObject _passthroughButton;
    private List<GameObject> _appButtons = new List<GameObject>();
    private int _activeAppButtonIndex = 0;
    private Dictionary<int, Action> _appSlotCallbacks = new Dictionary<int, Action>();

    // State
    private bool _isPassthroughOn = false;
    #endregion

    #region Lifecycle
    private void Awake()
    {
        _miniFrame = GetComponent<RTTMiniFrame>();
        LoadIcons();
    }

    private void Start()
    {
        _instance = this;

        // Wait for RTTMiniFrame to initialize, then add buttons
        StartCoroutine(InitializeAfterFrame());
    }

    private System.Collections.IEnumerator InitializeAfterFrame()
    {
        // Wait for RTTMiniFrame to build UI
        yield return null;
        yield return null;

        // Add buttons to sections
        AddControlButtons();
        AddAppButtons();

        // Sync passthrough state
        SyncPassthroughWithModeController();

        // Mark dirty to re-render
        _miniFrame.MarkDirty();
    }

    private void SyncPassthroughWithModeController()
    {
        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            _isPassthroughOn = modeController.mode == ViewMode.RealWorld;
            UpdatePassthroughButtonColor();
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }
    #endregion

    #region Button Creation
    private void AddControlButtons()
    {
        var section1 = _miniFrame.GetSection1Container();
        if (section1 == null) return;

        Color cyanColor = new Color(0f, 0.9f, 1f);
        float buttonSize = _miniFrame.ButtonSize;

        // Quit
        CreateIconButton(section1, iconQuit, "Quit", cyanColor, buttonSize, () =>
        {
            Debug.Log("[RTTTaskbar] Quit clicked");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });

        // Settings
        CreateIconButton(section1, iconSettings, "Settings", cyanColor, buttonSize, () =>
        {
            Debug.Log("[RTTTaskbar] Settings clicked");
        });

        // Passthrough
        _passthroughButton = CreateIconButton(section1, iconPassthrough, "Passthrough", cyanColor, buttonSize, TogglePassthrough);

        // Recenter
        CreateIconButton(section1, iconRecenter, "Recenter", cyanColor, buttonSize, RecenterObject);
    }

    private void AddAppButtons()
    {
        var section2 = _miniFrame.GetSection2Container();
        if (section2 == null) return;

        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);
        float buttonSize = _miniFrame.ButtonSize;
        int section2Capacity = _miniFrame.Section2Capacity;

        _appButtons.Clear();

        for (int i = 0; i < section2Capacity; i++)
        {
            Sprite slotIcon = (i == 0) ? iconHome : null;
            string slotName = (i == 0) ? "Home" : $"AppSlot_{i}";
            bool isActive = (i == _activeAppButtonIndex);
            Color btnColor = isActive ? purpleColor : cyanColor;
            int capturedIndex = i;

            var btn = CreateAppButton(section2, slotIcon, slotName, btnColor, buttonSize, capturedIndex);
            _appButtons.Add(btn);

            if (i == 0)
            {
                RewireHomeButton(btn);
            }
            else
            {
                var cg = btn.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 0f;
            }
        }
    }

    private GameObject CreateIconButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize, Action onClick)
    {
        var btn = VRButtonFactory.CreateBareIconButton(
            parent, buttonSize, icon, glowColor,
            () =>
            {
                onClick?.Invoke();
                _miniFrame.MarkDirty();
            },
            0.05f, 0.6f
        );

        btn.name = $"Btn_{name}";
        SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

        return btn;
    }

    private GameObject CreateAppButton(Transform parent, Sprite icon, string name, Color glowColor, float buttonSize, int index)
    {
        GameObject btn = new GameObject($"AppBtn_{name}");
        btn.transform.SetParent(parent, false);

        RectTransform rt = btn.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);

        CanvasGroup cg = btn.AddComponent<CanvasGroup>();

        if (icon != null)
        {
            var iconBtn = VRButtonFactory.CreateBareIconButton(
                btn.transform, buttonSize, icon, glowColor,
                () =>
                {
                    SelectAppButton(index);
                    _miniFrame.MarkDirty();
                },
                0.05f, 0.6f
            );
            iconBtn.transform.SetAsFirstSibling();
            SetLayerRecursively(iconBtn, LayerMask.NameToLayer("UI"));
        }

        SetLayerRecursively(btn, LayerMask.NameToLayer("UI"));

        return btn;
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer == -1) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
    #endregion

    #region Button Actions
    private void TogglePassthrough()
    {
        _isPassthroughOn = !_isPassthroughOn;
        Debug.Log($"[RTTTaskbar] Passthrough: {(_isPassthroughOn ? "ON" : "OFF")}");

        UpdatePassthroughButtonColor();

        var modeController = FindObjectOfType<ModeController>();
        if (modeController != null)
        {
            modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
        }

        _miniFrame.MarkDirty();
    }

    private void UpdatePassthroughButtonColor()
    {
        if (_passthroughButton == null) return;

        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);
        Color targetColor = _isPassthroughOn ? purpleColor : cyanColor;

        Transform iconTransform = _passthroughButton.transform.Find("HitArea/Visuals/Content/Icon");
        if (iconTransform != null)
        {
            Image iconImg = iconTransform.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.color = Color.Lerp(targetColor, Color.white, 0.9f);

                Shadow[] shadows = iconTransform.GetComponents<Shadow>();
                if (shadows.Length >= 2)
                {
                    Color glowCol = Color.Lerp(targetColor, Color.white, 0.7f);
                    glowCol.a = 0.4f;
                    shadows[0].effectColor = glowCol;
                    shadows[1].effectColor = glowCol;
                }
            }
        }
    }

    public void SetPassthrough(bool isOn)
    {
        if (_isPassthroughOn != isOn)
        {
            _isPassthroughOn = isOn;
            UpdatePassthroughButtonColor();

            var modeController = FindObjectOfType<ModeController>();
            if (modeController != null)
            {
                modeController.SetMode(_isPassthroughOn ? ViewMode.RealWorld : ViewMode.VirtualSpace);
            }

            _miniFrame.MarkDirty();
            Debug.Log($"[RTTTaskbar] Passthrough set to {(_isPassthroughOn ? "ON" : "OFF")}");
        }
    }

    private void RecenterObject()
    {
        Debug.Log("[RTTTaskbar] Recenter clicked");
        StartCoroutine(RecenterRoutine());
    }

    private System.Collections.IEnumerator RecenterRoutine()
    {
        VRGazeReticle reticle = VRGazeReticle.Instance;
        if (reticle == null) reticle = FindObjectOfType<VRGazeReticle>();

        if (reticle != null)
        {
            reticle.EnterRecenterMode(iconRecenter);
        }

        float duration = 2.0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            if (reticle != null)
            {
                reticle.UpdateRecenterProgress(progress);
            }

            yield return null;
        }

        Camera cam = Camera.main;
        if (cam != null)
        {
            RecenterAllVirtualObjects(cam);
        }

        if (reticle != null)
        {
            reticle.ExitRecenterMode();
        }

        _miniFrame.MarkDirty();
        Debug.Log("[RTTTaskbar] Recenter complete.");
    }

    private void RecenterAllVirtualObjects(Camera cam)
    {
        GameObject virtualObjectsParent = GameObject.Find("VirtualObjects");
        if (virtualObjectsParent == null)
        {
            Debug.LogWarning("[RTTTaskbar] VirtualObjects parent not found, falling back to primary only");
            RecenterPrimaryOnly(cam);
            return;
        }

        RTTMenuFrame primary = RTTMenuFrame.PrimaryInstance;
        if (primary == null)
        {
            Debug.LogWarning("[RTTTaskbar] No primary RTTMenuFrame found");
            return;
        }

        Vector3 pivotPos = primary.transform.position;
        Quaternion pivotRot = primary.transform.rotation;

        List<Transform> children = new List<Transform>();
        List<Vector3> relativePositions = new List<Vector3>();
        List<Quaternion> relativeRotations = new List<Quaternion>();

        foreach (Transform child in virtualObjectsParent.transform)
        {
            children.Add(child);
            Vector3 relPos = Quaternion.Inverse(pivotRot) * (child.position - pivotPos);
            relativePositions.Add(relPos);
            Quaternion relRot = Quaternion.Inverse(pivotRot) * child.rotation;
            relativeRotations.Add(relRot);
        }

        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(
            new Vector2(pivotPos.x, pivotPos.z),
            new Vector2(camPos.x, camPos.z)
        );

        Vector3 newPivotPos = camPos + camForward * hDist;
        newPivotPos.y = pivotPos.y;
        Quaternion newPivotRot = Quaternion.LookRotation(camForward);

        for (int i = 0; i < children.Count; i++)
        {
            Transform child = children[i];
            child.position = newPivotPos + newPivotRot * relativePositions[i];
            child.rotation = newPivotRot * relativeRotations[i];
        }
    }

    private void RecenterPrimaryOnly(Camera cam)
    {
        // Recenter the follow target if set
        var followTarget = _miniFrame.GetFollowTarget();
        if (followTarget != null)
        {
            RecenterTransform(followTarget, cam);
        }
        else
        {
            // No follow target, recenter this transform directly
            RecenterTransform(transform, cam);
        }
    }

    private void RecenterTransform(Transform target, Camera cam)
    {
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 currentPos = target.position;
        Vector3 camPos = cam.transform.position;
        float hDist = Vector2.Distance(new Vector2(currentPos.x, currentPos.z), new Vector2(camPos.x, camPos.z));

        Vector3 newPos = camPos + camForward * hDist;
        newPos.y = currentPos.y;

        target.position = newPos;
        target.rotation = Quaternion.LookRotation(camForward);
    }

    private void SelectAppButton(int index)
    {
        if (index == _activeAppButtonIndex) return;

        _activeAppButtonIndex = index;
        UpdateAllAppButtonColors();
        Debug.Log($"[RTTTaskbar] App button {index} selected");

        _miniFrame.MarkDirty();
    }

    private void UpdateAllAppButtonColors()
    {
        Color cyanColor = new Color(0f, 0.9f, 1f);
        Color purpleColor = new Color(0.9f, 0.3f, 1f);

        for (int i = 0; i < _appButtons.Count; i++)
        {
            bool isActive = (i == _activeAppButtonIndex);
            var btn = _appButtons[i];
            if (btn == null) continue;

            SetBareIconButtonColor(btn, isActive ? purpleColor : cyanColor);
        }

        _miniFrame.MarkDirty();
    }

    private void SetBareIconButtonColor(GameObject container, Color color)
    {
        Transform iconTransform = null;

        foreach (Transform child in container.transform)
        {
            var hitArea = child.Find("HitArea");
            if (hitArea != null)
            {
                var visuals = hitArea.Find("Visuals");
                if (visuals != null)
                {
                    var content = visuals.Find("Content");
                    if (content != null)
                    {
                        iconTransform = content.Find("Icon");
                        break;
                    }
                }
            }
        }

        if (iconTransform == null) return;

        Image iconImg = iconTransform.GetComponent<Image>();
        if (iconImg != null)
        {
            iconImg.color = Color.Lerp(color, Color.white, 0.9f);

            Shadow[] shadows = iconTransform.GetComponents<Shadow>();
            if (shadows.Length >= 2)
            {
                Color glowCol = Color.Lerp(color, Color.white, 0.7f);
                glowCol.a = 0.4f;
                shadows[0].effectColor = glowCol;
                shadows[1].effectColor = glowCol;
            }
        }
    }
    #endregion

    #region Public API
    public bool IsPassthroughOn => _isPassthroughOn;
    public int ActiveAppButtonIndex => _activeAppButtonIndex;
    public bool IsHomeActive => _activeAppButtonIndex == 0;

    /// <summary>
    /// Hide the taskbar by delegating to RTTMiniFrame.
    /// </summary>
    public void Hide()
    {
        if (_miniFrame != null)
            _miniFrame.Hide();
    }

    /// <summary>
    /// Show the taskbar by delegating to RTTMiniFrame.
    /// </summary>
    public void Show()
    {
        if (_miniFrame != null)
            _miniFrame.Show();
    }

    public void SelectHome()
    {
        SelectAppButton(0);
    }

    public void SelectAppButtonPublic(int index)
    {
        SelectAppButton(index);
    }
    #endregion

    #region App Slot Management
    public void RegisterApp(int slotIndex, Sprite icon, Action onClick)
    {
        Debug.Log($"[RTTTaskbar] RegisterApp called - slotIndex={slotIndex}, icon={(icon != null ? icon.name : "NULL")}, _appButtons.Count={_appButtons.Count}");

        if (slotIndex < 1 || slotIndex >= _appButtons.Count)
        {
            Debug.LogWarning($"[RTTTaskbar] Invalid slot index: {slotIndex} (must be 1 to {_appButtons.Count - 1})");
            return;
        }

        var slot = _appButtons[slotIndex];
        if (slot == null)
        {
            Debug.LogWarning($"[RTTTaskbar] _appButtons[{slotIndex}] is null!");
            return;
        }

        _appSlotCallbacks[slotIndex] = onClick;

        var cg = slot.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            Debug.Log($"[RTTTaskbar] Set slot {slotIndex} alpha to 1");
        }

        SetSlotIcon(slot, icon, slotIndex);

        _miniFrame.MarkDirty();
        Debug.Log($"[RTTTaskbar] Registered app in slot {slotIndex} complete");
    }

    public void UnregisterApp(int slotIndex)
    {
        if (slotIndex < 1 || slotIndex >= _appButtons.Count)
            return;

        var slot = _appButtons[slotIndex];
        if (slot == null) return;

        _appSlotCallbacks.Remove(slotIndex);

        var cg = slot.GetComponent<CanvasGroup>();
        if (cg != null) cg.alpha = 0f;

        if (_activeAppButtonIndex == slotIndex)
        {
            SelectAppButton(0);
            RTTManager appManager = RTTManager.Instance;
            if (appManager != null)
            {
                appManager.SwitchToHome();
            }
        }

        _miniFrame.MarkDirty();
        Debug.Log($"[RTTTaskbar] Unregistered app from slot {slotIndex}");
    }

    public void SelectSlot(int index)
    {
        Debug.Log($"[RTTTaskbar] SelectSlot({index}) called, current active: {_activeAppButtonIndex}");
        SelectAppButton(index);
    }

    private void SetSlotIcon(GameObject slot, Sprite icon, int slotIndex)
    {
        Debug.Log($"[RTTTaskbar] SetSlotIcon - slot={slot.name}, icon={(icon != null ? icon.name : "NULL")}, slotIndex={slotIndex}");

        int oldChildCount = slot.transform.childCount;
        for (int i = slot.transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(slot.transform.GetChild(i).gameObject);
            else
                DestroyImmediate(slot.transform.GetChild(i).gameObject);
        }
        Debug.Log($"[RTTTaskbar] SetSlotIcon - removed {oldChildCount} old children");

        if (icon == null)
        {
            Debug.LogWarning($"[RTTTaskbar] SetSlotIcon - icon is NULL, returning early");
            return;
        }

        Color cyanColor = new Color(0f, 0.9f, 1f);
        float buttonSize = _miniFrame.ButtonSize;

        var iconBtn = VRButtonFactory.CreateBareIconButton(
            slot.transform, buttonSize, icon, cyanColor,
            () =>
            {
                if (_appSlotCallbacks.ContainsKey(slotIndex))
                {
                    _appSlotCallbacks[slotIndex]?.Invoke();
                }
                else
                {
                    SelectAppButton(slotIndex);
                }
                _miniFrame.MarkDirty();
            },
            0.05f, 0.6f
        );
        iconBtn.transform.SetAsFirstSibling();

        SetLayerRecursively(iconBtn, LayerMask.NameToLayer("UI"));

        Debug.Log($"[RTTTaskbar] SetSlotIcon - created iconBtn={iconBtn.name}, slot.childCount now={slot.transform.childCount}");

        Color purpleColor = new Color(0.9f, 0.3f, 1f);
        if (slotIndex == _activeAppButtonIndex)
        {
            SetBareIconButtonColor(slot, purpleColor);
        }
    }

    private void HandleHomeClick()
    {
        RTTManager appManager = RTTManager.Instance;
        if (appManager != null)
        {
            appManager.SwitchToHome();
        }
        else
        {
            SelectAppButton(0);
        }
        _miniFrame.MarkDirty();
    }

    private void RewireHomeButton(GameObject homeBtn)
    {
        var button = homeBtn.GetComponentInChildren<UnityEngine.UI.Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                HandleHomeClick();
            });
        }
    }
    #endregion

    #region Icons
    private void LoadIcons()
    {
        if (iconQuit == null) iconQuit = LoadIcon("quit");
        if (iconSettings == null) iconSettings = LoadIcon("settings");
        if (iconPassthrough == null) iconPassthrough = LoadIcon("passthrough");
        if (iconRecenter == null) iconRecenter = LoadIcon("recenter");
        if (iconHome == null) iconHome = LoadIcon("home");

        Debug.Log($"[RTTTaskbar] Icons loaded - Quit:{iconQuit != null}, Settings:{iconSettings != null}, Passthrough:{iconPassthrough != null}, Recenter:{iconRecenter != null}, Home:{iconHome != null}");
    }

    public static Sprite LoadIcon(string name)
    {
        var sprite = Resources.Load<Sprite>($"icon_{name}");
        if (sprite == null)
        {
            Debug.LogWarning($"[RTTTaskbar] Failed to load icon: icon_{name} from Resources");
        }
        return sprite;
    }
    #endregion
}
