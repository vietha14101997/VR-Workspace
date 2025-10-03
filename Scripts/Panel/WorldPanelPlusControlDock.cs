using UnityEngine;
using System.Collections.Generic;
using System.Collections;

[ExecuteAlways]
[RequireComponent(typeof(Transform))]
public class WorldPanelPlusControlDock : MonoBehaviour
{
    [Header("Screen-space Y lock")]
    public bool lockScreenYToTray = true;
    [Range(0f, 0.5f)] public float dockViewportYOffset = 0.06f;
    public enum DockYRef { TrayBottom, TrayCenter }
    public DockYRef yReference = DockYRef.TrayBottom;

    [Header("Bind")]
    public WorldPanelPlus panel;

    [Header("Freeze")]
    public bool freezeWorldRotation = true;
    Quaternion _frozenWorldRot;

    [Header("Button layout")]
    public Vector2 buttonSize = new Vector2(0.08f, 0.045f);
    public float buttonGap = 0.02f;

    [Header("Visuals")]
    public float cornerRadius = 0.06f;
    public float border = 0.004f;

    Transform _backplate;
    readonly List<WorldPanelPlusDockButton> _buttons = new();
    float _zLift = 0.01f;
    Vector3 _offsetLocalNoZ;
    float _targetGapY = 0f;

    bool _minimized = false;
    Coroutine _dockAnim;
    readonly Dictionary<WorldPanelPlusDockButton, Vector3> _expandedPos = new();
    Transform _toggleBtnTr;
    float _animDuration = 0.25f;
    Transform _visualRoot;
    Transform _neutralRoot;
    Vector3 _lastNeutralRef;
    Vector3 _lastLossyDock;

    public void SetMinimized(bool on)
    {
        if (_minimized == on) return;
        _minimized = on;

        // Nếu đang ở Editor (chưa Play) hoặc chưa Build xong dock → áp dụng tức thời, KHÔNG chạy coroutine
        if (!Application.isPlaying || _backplate == null || _toggleBtnTr == null)
        {
            var binder = _backplate ? _backplate.GetComponent<RoundedBackplateBinder>() : null;
            if (binder) binder.SetForceCircle(_minimized);
            ApplyMinimizeVisualState();
            ResizeBackplateToActiveButtons();
            return;
        }

        var b = _backplate.GetComponent<RoundedBackplateBinder>();
        if (b) b.SetForceCircle(_minimized);
        ResizeBackplateToActiveButtons();

        if (_dockAnim != null) StopCoroutine(_dockAnim);
        _dockAnim = StartCoroutine(AnimateDock(on));
    }

    public void RecomputeFromPanel()
    {
        ComputeLocalOffset();
        EnsurePose();
        PlaceDockImmediate();
        _targetGapY = MeasureCurrentGapY();
        CenterButtonsVertically();
        ResizeBackplateToActiveButtons();
    }

    public float GetBackplateHeight()
    {
        return _backplate ? _backplate.localScale.y : (buttonSize.y + 0.04f);
    }

    public void CenterXUnderTray()
    {
        if (!panel) return;
        var cam = Camera.main;
        _offsetLocalNoZ.x = 0f;
        Vector3 baseWorld = panel.transform.TransformPoint(_offsetLocalNoZ);
        Vector3 viewDir = cam ? (panel.transform.position - cam.transform.position).normalized : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;
        Vector3 pos = baseWorld + zOffset;
        float trayBottomY = panel.GetTrayBottomYWorld(cam);
        float dockTopY = GetDockTopYWorldAt(pos);
        float deltaY = trayBottomY - dockTopY - _targetGapY;
        pos.y += deltaY;
        transform.position = pos;
    }

    void Reset() { panel = GetComponentInParent<WorldPanelPlus>(); }
    void Awake() { if (!panel) panel = GetComponentInParent<WorldPanelPlus>(); }

    void Start()
    {
        ClearChildren();
        Build();
        RecomputeFromPanel();
        PurgeUnknownBackgrounds();
    }

    void OnTransformParentChanged() { PlaceDockImmediate(); }

    void OnEnable() { if (!panel) panel = GetComponentInParent<WorldPanelPlus>(); ApplyMinimizeVisualState(); }

    void OnValidate() { ApplyMinimizeVisualState(); CenterButtonsVertically(); ResizeBackplateToActiveButtons(); }

    void LateUpdate()
    {
        if (!panel) return;
        var cam = Camera.main;

        bool visible = panel.GetTrayAlpha01() > 0.02f || _minimized;
        SetDockRenderersAndColliders(visible);
        if (!visible) return;

        // face camera yaw-only
        if (cam)
        {
            Vector3 toCam = cam.transform.position - transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        // position
        Vector3 baseWorld = panel.transform.TransformPoint(_offsetLocalNoZ);
        Vector3 viewDir = cam ? (panel.transform.position - cam.transform.position).normalized : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;
        Vector3 pos = baseWorld + zOffset;
        float trayBottomY = panel.GetTrayBottomYWorld(cam);
        float dockTopY = GetDockTopYWorldAt(pos);
        float deltaY = trayBottomY - dockTopY - _targetGapY;
        pos.y += deltaY;
        transform.position = pos;

        // neutral scale refresh
        if (_neutralRoot && (_neutralRoot.parent && (_neutralRoot.parent.lossyScale - _lastNeutralRef).sqrMagnitude > 1e-8f))
            RefreshNeutralScale();

        // backplate alpha
        var backMr = _backplate ? _backplate.GetComponent<MeshRenderer>() : null;
        if (backMr && backMr.sharedMaterial)
        {
            float a = panel.dockMinimized ? 1f : panel.GetTrayAlpha01();
            var m = backMr.sharedMaterial;
            if (m.HasProperty("_MaskEnable")) m.SetFloat("_MaskEnable", 0f);
            if (m.HasProperty("_FillColor")) { m.SetColor("_BorderColor", new Color(1f, 1f, 1f, 0.28f * a)); }
            else { if (m.shader != Shader.Find("Unlit/Transparent")) m.shader = Shader.Find("Unlit/Transparent"); var c = m.color; c.a = 0.18f * a; m.color = c; }
        }

        if ((transform.lossyScale - _lastLossyDock).sqrMagnitude > 1e-6f)
        {
            foreach (var b in _buttons)
            {
                var bc = b.GetComponent<BoxCollider>();
                if (bc) SetColliderWorldSize(bc, new Vector3(buttonSize.x, buttonSize.y, 0.02f), 0.01f);
            }
            _lastLossyDock = transform.lossyScale;
        }
    }

    // ===================== Build =====================

    public void Build()
    {
        ClearChildren();

        var vr = new GameObject("VisualRoot"); vr.transform.SetParent(transform, false); _visualRoot = vr.transform;
        var neutral = new GameObject("NeutralScale").transform; neutral.SetParent(_visualRoot, false); _neutralRoot = neutral; RefreshNeutralScale();

        // Chỉ còn 1 nút: MinimizeToggle
        var types = new[] { WPDockButtonType.MinimizeToggle };

        float bleed = 0.015f;
        float innerW = buttonSize.x; float innerH = buttonSize.y;
        float expandedW = innerW + 2f * (border + bleed);
        float expandedH = innerH + 2f * (border + bleed);

        // backplate
        _backplate = new GameObject("DockBackplate").transform; _backplate.SetParent(_neutralRoot, false);
        _backplate.localPosition = Vector3.zero;
        var mf = _backplate.gameObject.AddComponent<MeshFilter>(); mf.sharedMesh = BuildUnitQuad();
        var mr = _backplate.gameObject.AddComponent<MeshRenderer>();
        var dockShader = Shader.Find("Unlit/WorldPanelDock");
        dockShader ??= Shader.Find("Unlit/Transparent");
        var mat = new Material(dockShader); mr.sharedMaterial = mat;
        _backplate.localScale = new Vector3(expandedW, expandedH, 1f);
        if (mat.HasProperty("_FillColor")) mat.SetColor("_FillColor", new Color(0, 0, 0, 0));
        if (mat.HasProperty("_BorderColor")) mat.SetColor("_BorderColor", new Color(1, 1, 1, 0.35f));
        if (mat.HasProperty("_Feather")) mat.SetFloat("_Feather", 0.006f);
        if (mat.HasProperty("_Border")) mat.SetFloat("_Border", border);
        if (mat.HasProperty("_RectWH")) mat.SetVector("_RectWH", new Vector4(expandedW, expandedH, 0, 0));
        var binder = _backplate.gameObject.AddComponent<RoundedBackplateBinder>();
        binder.border = border; binder.fallbackRadiusWhenExpanded = cornerRadius; binder.SetForceCircle(_minimized);

        // buttons
        _buttons.Clear();
        for (int i = 0; i < types.Length; i++)
        {
            var go = new GameObject("Btn_" + types[i]);
            go.transform.SetParent(_neutralRoot, false);
            go.transform.localPosition = new Vector3(0, 0f, 0.01f);
            var btn = go.AddComponent<WorldPanelPlusDockButton>();
            btn.panel = panel;
            btn.type = types[i];
            _toggleBtnTr = go.transform; // chính là nút minimize
            var bc = go.AddComponent<BoxCollider>();
            SetColliderWorldSize(bc, new Vector3(buttonSize.x, buttonSize.y, 0.02f), 0.01f);

            var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
            icon.name = "Icon";
            icon.transform.SetParent(go.transform, false);
            float iconSize = Mathf.Min(buttonSize.x, buttonSize.y);
            icon.transform.localScale = new Vector3(iconSize, iconSize, 1);
            icon.transform.localPosition = new Vector3(0, 0, 0.0015f);
            DestroyImmediate(icon.GetComponent<Collider>());
            var iconMr = icon.GetComponent<MeshRenderer>();
            // dùng chung icon minimize trong Resources nếu có, hoặc để màu trắng nếu thiếu
            var shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            var iconMat = new Material(shader);
            iconMat.color = Color.white;
            iconMr.sharedMaterial = iconMat;
            _buttons.Add(btn);
        }

        ComputeLocalOffset();
        EnsurePose();
        PlaceDockImmediate();
        _targetGapY = MeasureCurrentGapY();
        CaptureExpandedLayout();
        ApplyMinimizeVisualState();
        ResizeBackplateToActiveButtons(bleed);
        CenterButtonsVertically();
        PurgeUnknownBackgrounds();
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
        _buttons.Clear(); _backplate = null; _visualRoot = null; _neutralRoot = null;
    }

    void ApplyMinimizeVisualState()
    {
        if (_buttons == null) return;
        foreach (var b in _buttons)
        {
            if (!b) continue;
            bool isToggle = _toggleBtnTr && b.transform == _toggleBtnTr;
            bool show = !_minimized || isToggle;
            b.gameObject.SetActive(show);
            foreach (var r in b.GetComponentsInChildren<Renderer>(true)) if (r) r.enabled = show;
            foreach (var c in b.GetComponentsInChildren<Collider>(true)) if (c) c.enabled = show;
        }
        ResizeBackplateToActiveButtons(); CenterButtonsVertically();
        var binder = _backplate ? _backplate.GetComponent<RoundedBackplateBinder>() : null; if (binder) binder.SetForceCircle(_minimized);
    }

    void CenterButtonsVertically()
    {
        if (_buttons == null) return;
        foreach (var b in _buttons)
        {
            if (!b) continue; var t = b.transform; t.localPosition = new Vector3(0f, 0f, t.localPosition.z);
            var icon = t.Find("Icon"); if (icon) icon.localPosition = new Vector3(icon.localPosition.x, 0f, icon.localPosition.z);
        }
    }

    void ResizeBackplateToActiveButtons(float bleed = 0.015f)
    {
        if (!_backplate) return;
        float W = buttonSize.x + 2f * (border + bleed);
        float H = buttonSize.y + 2f * (border + bleed);
        if (_minimized) W = H; _backplate.localScale = new Vector3(W, H, 1f);
        var mr = _backplate.GetComponent<MeshRenderer>(); if (mr && mr.sharedMaterial && mr.sharedMaterial.HasProperty("_RectWH")) mr.sharedMaterial.SetVector("_RectWH", new Vector4(W, H, 0, 0));
        var binder = _backplate.GetComponent<RoundedBackplateBinder>(); if (binder) binder.SetForceCircle(_minimized);
    }

    void EnsurePose()
    {
        if (!panel) return; var cam = Camera.main; if (cam)
        {
            Vector3 toCam = cam.transform.position - transform.position; toCam.y = 0f;
            if (toCam.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }
    }

    void ComputeLocalOffset()
    {
        if (!panel) return;
        float below = panel.height * 0.5f + panel.trayPadding + panel.trayHoverExtra;
        float halfDock = _backplate ? _backplate.localScale.y * 0.5f : 0f;
        float margin = 0.01f;
        _offsetLocalNoZ = new Vector3(0, -(below + halfDock + margin), 0);
    }

    float GetDockTopYWorldAt(Vector3 worldPos)
    { if (_backplate == null) return worldPos.y; float half = _backplate.localScale.y * 0.5f; return (worldPos + transform.rotation * new Vector3(0, half, 0)).y; }

    float MeasureCurrentGapY()
    { var cam = Camera.main; float trayBottom = panel ? panel.GetTrayBottomYWorld(cam) : transform.position.y; float dockTop = GetDockTopYWorldAt(transform.position); return trayBottom - dockTop; }

    void PlaceDockImmediate()
    {
        if (!panel) return;
        var cam = Camera.main;
        Vector3 baseWorld = panel.transform.TransformPoint(_offsetLocalNoZ);
        Vector3 viewDir = cam ? (panel.transform.position - cam.transform.position).normalized : -panel.transform.forward;
        Vector3 zOffset = -viewDir * _zLift;
        transform.position = baseWorld + zOffset;
    }

    void RefreshNeutralScale()
    {
        if (!_neutralRoot) return; Vector3 refLossy = _neutralRoot.parent ? _neutralRoot.parent.lossyScale : Vector3.one;
        float ix = 1f / Mathf.Max(1e-5f, refLossy.x); float iy = 1f / Mathf.Max(1e-5f, refLossy.y); float iz = 1f / Mathf.Max(1e-5f, refLossy.z);
        _neutralRoot.localScale = new Vector3(ix, iy, iz); _lastNeutralRef = refLossy;
    }

    static void SetColliderWorldSize(BoxCollider bc, Vector3 worldSize, float worldZCenter = 0f)
    { var t = bc.transform; Vector3 ls = t.lossyScale; float sx = Mathf.Max(1e-5f, ls.x), sy = Mathf.Max(1e-5f, ls.y), sz = Mathf.Max(1e-5f, ls.z); bc.size = new Vector3(worldSize.x / sx, worldSize.y / sy, worldSize.z / sz); bc.center = new Vector3(0f, 0f, worldZCenter / sz); }

    void CaptureExpandedLayout() { _expandedPos.Clear(); foreach (var b in _buttons) _expandedPos[b] = b.transform.localPosition; }

    void SetDockRenderersAndColliders(bool on)
    {
        if (_visualRoot) _visualRoot.gameObject.SetActive(on || _minimized);
        var cols = GetComponentsInChildren<Collider>(true);
        foreach (var c in cols)
        {
            if (!c) continue; bool isToggle = _toggleBtnTr && c.transform.IsChildOf(_toggleBtnTr); bool allow = (!_minimized) || isToggle; c.enabled = on && allow && c.gameObject.activeInHierarchy;
        }
    }

    IEnumerator AnimateDock(bool minimize)
    {
        if (_backplate == null) yield break;
        float startTime = Time.time;
        float endTime = startTime + _animDuration;

        float height = buttonSize.y + border * 2;
        float minimizedWidth = height;
        float expandedWidth = buttonSize.x + border * 2;

        float startWidth = _backplate ? _backplate.localScale.x : expandedWidth;
        float targetWidth = minimize ? minimizedWidth : expandedWidth;

        Vector3 startPos = _backplate ? _backplate.localPosition : Vector3.zero;
        Vector3 centerPos = (_toggleBtnTr != null) ? _toggleBtnTr.localPosition : Vector3.zero;

        while (Time.time < endTime)
        {
            if (_backplate == null) yield break;
            float t = (Time.time - startTime) / _animDuration;
            float easedT = minimize ? t * t : t * (2f - t);
            _backplate.localScale = new Vector3(Mathf.Lerp(startWidth, targetWidth, easedT), height, 1);
            _backplate.localPosition = Vector3.Lerp(startPos, new Vector3(centerPos.x, 0f, 0f), easedT);
            yield return null;
        }

        if (_backplate != null)
        {
            _backplate.localScale = new Vector3(targetWidth, height, 1);
            _backplate.localPosition = new Vector3(centerPos.x, 0f, 0f);
            var binder = _backplate.GetComponent<RoundedBackplateBinder>();
            if (binder) binder.SetForceCircle(minimize);
        }
        _minimized = minimize;
        ResizeBackplateToActiveButtons();
        CenterButtonsVertically();
    }

    static Mesh BuildUnitQuad()
    {
        var m = new Mesh(); m.name = "UnitQuad";
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0), };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 }; m.RecalculateNormals(); return m;
    }

    void PurgeUnknownBackgrounds()
    {
        if (_backplate == null) return;
        bool IsAllowed(Renderer r)
        {
            if (r == null) return false; var t = r.transform; if (t == _backplate) return true;
            if (t.name == "Icon") foreach (var b in _buttons) if (b != null && t.IsChildOf(b.transform)) return true; return false;
        }
        var rends = GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) { if (r == null) continue; if (IsAllowed(r)) continue; r.enabled = false; }
    }
}
