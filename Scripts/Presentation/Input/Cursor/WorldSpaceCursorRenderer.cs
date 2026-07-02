using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.Presentation.Input.VCS;

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

            Transform refT = GetReferenceTransform();
            if (refT == null) { SetVisible(false); return; }

            var canvas = surface.RuntimeRef as RTTCanvasBase;
            var actionBar = surface.RuntimeRef as ActionBarSurfaceController;

            Transform targetT = null;
            if (canvas != null)
            {
                var quad = canvas.GetQuadCollider();
                if (quad != null) targetT = quad.transform;
            }
            else if (actionBar != null)
            {
                targetT = actionBar.transform;
            }

            if (targetT == null) { SetVisible(false); return; }

            // 1) Find the physical size of the active surface
            Vector2 physicalSize = Vector2.one;
            if (canvas != null)
            {
                physicalSize = canvas.GetWorldSize();
            }
            else if (actionBar != null)
            {
                physicalSize = actionBar.PhysicalSize;
            }

            // 2) Position the visual cursor directly on the physical surface based on its UV
            Vector3 hotspot = targetT.position
                + targetT.right * ((cursor.UV.x - 0.5f) * physicalSize.x)
                + targetT.up * ((cursor.UV.y - 0.5f) * physicalSize.y);

            // Apply Z-offset along local forward to prevent z-fighting
            CursorWorldPosition = hotspot + targetT.forward * localZOffset;

            // Reparent only when the active surface changes
            if (_currentParent != targetT)
            {
                _cursor3D.transform.SetParent(targetT, worldPositionStays: true);
                _currentParent = targetT;
            }

            // Sprite dimensions
            Vector2 spriteSize = Vector2.one;
            if (_currentSprite != null)
            {
                spriteSize = new Vector2(
                    _currentSprite.rect.width / _currentSprite.pixelsPerUnit,
                    _currentSprite.rect.height / _currentSprite.pixelsPerUnit);
            }

            // Desired visual size in world meters
            float worldW = cursorWorldSize * spriteSize.x;
            float worldH = cursorWorldSize * spriteSize.y;
            Vector3 worldOffset = targetT.right * (worldW * 0.5f) - targetT.up * (worldH * 0.5f);
            
            _cursor3D.transform.position = CursorWorldPosition + worldOffset;
            _cursor3D.transform.rotation = targetT.rotation;

            // Scale to world size, neutralizing parent transform scale
            Vector3 parentScale = targetT.lossyScale;
            _cursor3D.transform.localScale = new Vector3(
                cursorWorldSize / Mathf.Max(1e-4f, parentScale.x),
                cursorWorldSize / Mathf.Max(1e-4f, parentScale.y),
                1f);

            if (!_cursor3D.activeSelf) SetVisible(true);
        }

        private Transform GetReferenceTransform()
        {
            var refFrame = RTTMenuFrame.PrimaryInstance;
            if (refFrame != null) return refFrame.transform;

            var activeFrame = FindAnyObjectByType<RTTMenuFrame>();
            if (activeFrame != null) return activeFrame.transform;

            return null;
        }
    }
}
