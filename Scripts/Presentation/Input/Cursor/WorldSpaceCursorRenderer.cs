using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;

namespace VRWorkspace.Presentation.Input.Cursor
{
    /// <summary>
    /// Renders the virtual cursor as a 3D GameObject that *belongs* to the active surface —
    /// i.e. it is parented to that surface's display quad every frame. When the cursor moves
    /// to a different surface (via system-initiated traversal or VCS.SnapCursorTo),
    /// the cursor GameObject is re-parented so it visually "jumps" with the surface.
    ///
    /// Why 3D (not UI Overlay): in VR the user can look at the Scene hierarchy tab and
    /// verify which RTT surface currently owns the cursor — a critical spatial anchor.
    /// UI Overlay cursors live in screen space and have no scene presence.
    ///
    /// Singleton; auto-bootstraps. Hidden whenever VCS is in Gaze mode.
    /// </summary>
    [DefaultExecutionOrder(-7500)]
    public sealed class WorldSpaceCursorRenderer : MonoBehaviour
    {
        public static WorldSpaceCursorRenderer Instance { get; private set; }

        [SerializeField] private Sprite defaultCursorSprite;
        [SerializeField] private Color tint = Color.white;
        [Tooltip("Cursor quad size in meters (world). 0.1 ≈ 10cm, visible in VR without dominating the panel.")]
        [SerializeField, Range(0.02f, 0.5f)] private float cursorWorldSize = 0.08f;
        [Tooltip("Z offset (in surface local space) so the cursor floats slightly in front of the quad and doesn't z-fight.")]
        [SerializeField, Range(-0.05f, 0.05f)] private float localZOffset = -0.005f;

        private GameObject _cursor3D;
        private SpriteRenderer _cursorRenderer;
        private Transform _currentParent;
        private Sprite _currentSprite;
        private string _lastDiagnosticParent;
        private Vector2 _lastLoggedUV;
        private string _lastLoggedParentName;
        private float _lastLogTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[WorldSpaceCursorRenderer]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WorldSpaceCursorRenderer>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildCursor3D();
            Hide();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BuildCursor3D()
        {
            _cursor3D = new GameObject("[VCS-Cursor]");
            _cursor3D.transform.SetParent(transform, false);
            _cursor3D.SetActive(false);

            _cursorRenderer = _cursor3D.AddComponent<SpriteRenderer>();
            _currentSprite = defaultCursorSprite != null
                ? defaultCursorSprite
                : Resources.Load<Sprite>("icon_cursor")
                  ?? Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            _cursorRenderer.sprite = _currentSprite;
            _cursorRenderer.color = tint;
            _cursorRenderer.sortingOrder = 100; // render on top of quad material
            _cursor3D.transform.localScale = new Vector3(cursorWorldSize, cursorWorldSize, 1f);
        }

        public void SetCursorSprite(Sprite sprite)
        {
            _currentSprite = sprite;
            if (_cursorRenderer != null) _cursorRenderer.sprite = sprite;
        }

        public void SetVisible(bool visible)
        {
            if (_cursor3D != null) _cursor3D.SetActive(visible);
        }

        public void Hide() => SetVisible(false);

        /// <summary>
        /// World position of the cursor on its current surface quad. Updated every LateUpdate.
        /// Click dispatcher reads this to synthesize a Ray through Camera.main → this point,
        /// matching the gaze-reticle flow. Visual position == click ray direction → no UV-mapping drift.
        /// </summary>
        public Vector3 CursorWorldPosition { get; private set; }

        /// <summary>
        /// Reparents the cursor GameObject onto the new active surface's display quad,
        /// and updates local position + scale for the current UV.
        /// </summary>
        private void LateUpdate()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || _cursor3D == null) return;

            var cursor = vcs.Cursor;
            if (!cursor.IsVisible || cursor.SurfaceId == null || vcs.Surfaces.Count == 0)
            {
                SetVisible(false);
                return;
            }

            if (!vcs.Surfaces.TryGet(cursor.SurfaceId.Value, out var surface))
            {
                SetVisible(false);
                return;
            }

            var canvas = surface.RuntimeRef as RTTCanvasBase;
            if (canvas == null) { SetVisible(false); return; }

            var quad = canvas.GetQuadCollider();
            if (quad == null) { SetVisible(false); return; }

            // Reparent only when the active surface changes — avoids layout costs every frame.
            if (_currentParent != quad.transform)
            {
                _cursor3D.transform.SetParent(quad.transform, worldPositionStays: false);
                _currentParent = quad.transform;
            }

            // Local position derived directly from cursor.UV. Visual position MUST match
            // click position (UV → screen pixel for GraphicRaycaster); any visual-only
            // margin would cause clicks to hit the wrong element.
            Vector2 size = canvas.GetWorldSize();
            Vector3 localPos = new Vector3(
                (cursor.UV.x - 0.5f) * size.x,
                (cursor.UV.y - 0.5f) * size.y,
                localZOffset);
            _cursor3D.transform.localPosition = localPos;
            _cursor3D.transform.localScale = new Vector3(cursorWorldSize, cursorWorldSize, 1f);
            CursorWorldPosition = quad.transform.TransformPoint(localPos);

            // DIAGNOSTIC: project cursor's WORLD position back to screen pixel via Camera.main
            // and compare against what GraphicRaycaster would receive (UV * RT.size).
            // If these disagree, visual ≠ click target.
            // Only log when UV actually changed (or every ~0.2s when static) to avoid spam.
            Vector3 worldPos = quad.transform.TransformPoint(localPos);
            Camera cam = Camera.main;
            if (cam != null && _cursor3D.activeSelf)
            {
                Vector3 sp = cam.WorldToScreenPoint(worldPos);
                var rt = canvas.GetRenderTexture();
                int rtW = rt != null ? rt.width : 0;
                int rtH = rt != null ? rt.height : 0;
                float t = Time.unscaledTime;
                bool uvChanged = (cursor.UV - _lastLoggedUV).sqrMagnitude > 1e-6f;
                bool parentChanged = _lastLoggedParentName != quad.transform.name;
                bool periodicTick = (t - _lastLogTime) > 0.5f;
                if (uvChanged || parentChanged || periodicTick)
                {
                    Debug.Log(
                        $"[CursorVisual] panel='{canvas.name}' " +
                        $"cursor.UV=({cursor.UV.x:F3},{cursor.UV.y:F3}) " +
                        $"local=({localPos.x:F2},{localPos.y:F2}) " +
                        $"world=({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}) " +
                        $"cam.WorldToScreen=({sp.x:F0},{sp.y:F0},d={sp.z:F2}) " +
                        $"rt={rtW}x{rtH} expectedForGraphicRaycaster=({cursor.UV.x*rtW:F0},{cursor.UV.y*rtH:F0})"
                    );
                    _lastLoggedUV = cursor.UV;
                    _lastLoggedParentName = quad.transform.name;
                    _lastLogTime = t;
                }
            }

            if (!_cursor3D.activeSelf) SetVisible(true);
        }
    }
}
