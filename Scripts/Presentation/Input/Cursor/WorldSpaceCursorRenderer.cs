using UnityEngine;
using VRWorkspace.Domain.Input;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.Presentation.Input.VCS;
using VRWorkspace.Presentation.Input.Click;

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

        // Track cursor's frozen state (cursor stuck at popup hit position until mouse moves).
        // This avoids "jump" when popup closes (cursor stays visually at clicked position)
        // and avoids "doesn't move" bug from syncing VCS.Cursor.UV (which can pin it).
        private bool _cursorFrozenAtPopupHit;
        private Vector3 _frozenCursorWorldPos;
        private Quaternion _frozenCursorRotation;
        private Vector2 _prevCursorUV;

        // Explicit hard-override, independent of VirtualSurface.IsVisible. Callers that need
        // a guaranteed hide (e.g. mediaPlayer controls dismissing) should use SetForceHidden(true)
        // rather than SetVisible(false) directly — SetVisible alone can get silently re-enabled
        // later in the same LateUpdate by the normal surface-based show logic below if the
        // surface's IsVisible flag hasn't (yet) caught up. Checked first, before anything else.
        private bool _forceHidden;
        private Transform _lastLoggedTargetT; // [VCS_DBG] change-detection only

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
            // Use max sortingOrder so cursor always renders on top of WorldSpace popups
            // (popups use sortingOrder=100 — cursor must beat them consistently).
            _cursorRenderer.sortingOrder = short.MaxValue;
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
        /// Hard-override that guarantees the cursor stays hidden regardless of the normal
        /// surface-based visibility logic in LateUpdate. Use this (not SetVisible) whenever
        /// a caller needs the hide to be reliable — e.g. mediaPlayer controls dismissing —
        /// since the underlying VirtualSurface.IsVisible flag can lag by a frame or get
        /// re-registered mid-transition. Call SetForceHidden(false) to release the override
        /// and let normal per-frame positioning/visibility resume.
        /// </summary>
        public void SetForceHidden(bool hidden)
        {
            _forceHidden = hidden;
            if (hidden) SetVisible(false);
        }

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

            if (_forceHidden)
            {
                SetVisible(false);
                return;
            }

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

            // Hide cursor when its current surface is invisible (e.g., mediaPlayer controls hidden)
            if (!surface.IsVisible)
            {
                SetVisible(false);
                return;
            }

            Transform refT = GetReferenceTransform();
            if (refT == null) { SetVisible(false); return; }

            var canvas = surface.RuntimeRef as RTTCanvasBase;
            var actionBar = surface.RuntimeRef as ActionBarSurfaceController;
            var cursorAnchor = surface.RuntimeRef as IVirtualCursorAnchor;

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
            else if (cursorAnchor != null)
            {
                targetT = cursorAnchor.AnchorTransform;
            }

            if (targetT == null) { SetVisible(false); return; }

            // [VCS_DBG] Log whenever the resolved target transform changes surface/object —
            // catches exactly the moment of a reported Z jump (e.g. Hub -> Controls), showing
            // targetT's identity and depth along refT.forward so the two can be compared
            // directly instead of guessing which one is "wrong."
            if (targetT != _lastLoggedTargetT)
            {
                _lastLoggedTargetT = targetT;
                float depthAlongRefT = Vector3.Dot(targetT.position - refT.position, refT.forward);
                Debug.Log($"[VCS_DBG] targetT changed -> name={targetT.name} kind={(canvas != null ? "RTTCanvasBase:" + canvas.name : actionBar != null ? "ActionBar" : "IVirtualCursorAnchor")} worldPos={targetT.position} depthAlongRefTForward={depthAlongRefT:F4}");
            }

            // ── Popup override: when cursor ray hits an active WorldSpace popup,
            //    place cursor visual at popup hit point (NOT reparented to popup — that
            //    would deactivate cursor when popup closes).
            //    See ClickDispatcher.Popup.cs for hit detection.
            var popupDispatcher = ClickDispatcher.Instance;
            if (popupDispatcher != null && popupDispatcher.HasPopupHit && popupDispatcher.LastPopupTransform != null)
            {
                ApplyPopupCursor(popupDispatcher.LastPopupTransform, popupDispatcher.LastPopupWorldHit);
                _cursorFrozenAtPopupHit = true;
                _frozenCursorWorldPos = popupDispatcher.LastPopupWorldHit
                    + popupDispatcher.LastPopupTransform.forward * 0.015f;
                _frozenCursorRotation = popupDispatcher.LastPopupTransform.rotation;
                _prevCursorUV = cursor.UV;
                return;
            }

            // ── Frozen cursor handling: when popup was just closed, keep cursor visually
            //    at clicked position until mouse moves. Avoids both:
            //    1) "Cursor jump" bug (cursor teleports to old VCS.UV on RTTMenuFrame).
            //    2) "Cursor doesn't move" bug (syncing VCS.UV pinned cursor).
            if (_cursorFrozenAtPopupHit)
            {
                // Unfreeze as soon as mouse moves VCS.Cursor.UV (delta applied by
                // MouseDeltaDriver earlier this frame).
                bool mouseMoved = !Mathf.Approximately(cursor.UV.x, _prevCursorUV.x)
                                || !Mathf.Approximately(cursor.UV.y, _prevCursorUV.y);
                if (mouseMoved)
                {
                    _cursorFrozenAtPopupHit = false;
                    // Fall through to normal RTTMenuFrame positioning below.
                }
                else
                {
                    // Keep cursor pinned to where user clicked the button.
                    // Use saved rotation (popup's orientation) — Quaternion.identity would
                    // face away from camera and flip the sprite horizontally.
                    // Apply same worldOffset logic as popup/RTTMenuFrame for visual consistency.
                    Vector2 frozenSpriteSize = GetCurrentSpriteSize();
                    float frozenWorldW = cursorWorldSize * frozenSpriteSize.x;
                    float frozenWorldH = cursorWorldSize * frozenSpriteSize.y;
                    Vector3 frozenWorldOffset = _frozenCursorRotation * Vector3.right * (frozenWorldW * 0.5f)
                                              - _frozenCursorRotation * Vector3.up * (frozenWorldH * 0.5f);
                    _cursor3D.transform.position = _frozenCursorWorldPos + frozenWorldOffset;
                    _cursor3D.transform.rotation = _frozenCursorRotation;

                    // Compensate parent scale (cursor may still be child of RTTMenuFrame quad).
                    Vector3 frozenParentScale = _cursor3D.transform.parent != null
                        ? _cursor3D.transform.parent.lossyScale
                        : Vector3.one;
                    _cursor3D.transform.localScale = new Vector3(
                        cursorWorldSize / Mathf.Max(1e-4f, frozenParentScale.x),
                        cursorWorldSize / Mathf.Max(1e-4f, frozenParentScale.y),
                        1f);

                    if (!_cursor3D.activeSelf) SetVisible(true);
                    _prevCursorUV = cursor.UV;
                    return;
                }
            }
            _prevCursorUV = cursor.UV;

            // 1) Find the physical size of the active surface
            Vector2 physicalSize = Vector2.one;
            if (canvas != null)
            {
                physicalSize = RTTCanvasAutoRegistrar.Instance != null
                    ? RTTCanvasAutoRegistrar.Instance.GetVirtualSize(canvas, refT)
                    : canvas.GetWorldSize();
            }
            else if (actionBar != null)
            {
                physicalSize = actionBar.PhysicalSize;
            }
            else if (cursorAnchor != null)
            {
                physicalSize = cursorAnchor.PhysicalSize;
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

        /// <summary>
        /// Position the cursor visual directly on the popup BoxCollider at the world hit point.
        /// IMPORTANT: cursor is NOT reparented to popup — that would deactivate cursor when
        /// popup.gameObject.SetActive(false). Cursor stays at its current parent (might still
        /// be RTTMenuFrame quad with non-uniform scale from previous frame).
        ///
        /// Compensates for parent scale so cursor visual world size is always cursorWorldSize,
        /// regardless of parent's lossyScale.
        /// </summary>
        private void ApplyPopupCursor(Transform popupT, Vector3 worldHit)
        {
            // Z-offset same as RTTMenuFrame branch. With high sortingOrder, cursor always
            // renders on top of popup content regardless of Z.
            CursorWorldPosition = worldHit + popupT.forward * localZOffset;

            // Apply same worldOffset as RTTMenuFrame branch — positions sprite's top-left
            // at hotspot for consistent cursor visual across all surfaces.
            Vector2 spriteSize = GetCurrentSpriteSize();
            float worldW = cursorWorldSize * spriteSize.x;
            float worldH = cursorWorldSize * spriteSize.y;
            Vector3 worldOffset = popupT.right * (worldW * 0.5f) - popupT.up * (worldH * 0.5f);

            _cursor3D.transform.position = CursorWorldPosition + worldOffset;
            _cursor3D.transform.rotation = popupT.rotation;

            // CRITICAL: compensate for parent scale (RTTMenuFrame quad has scale (1.6, 0.9, 1)).
            // Without this, cursor's world scale would be localScale * parentScale = much larger
            // than intended. By dividing, world scale = cursorWorldSize consistently.
            Vector3 parentScale = _cursor3D.transform.parent != null
                ? _cursor3D.transform.parent.lossyScale
                : Vector3.one;
            _cursor3D.transform.localScale = new Vector3(
                cursorWorldSize / Mathf.Max(1e-4f, parentScale.x),
                cursorWorldSize / Mathf.Max(1e-4f, parentScale.y),
                1f);

            if (!_cursor3D.activeSelf) SetVisible(true);
        }

        private Vector2 GetCurrentSpriteSize()
        {
            if (_currentSprite == null) return Vector2.one;
            return new Vector2(
                _currentSprite.rect.width / _currentSprite.pixelsPerUnit,
                _currentSprite.rect.height / _currentSprite.pixelsPerUnit);
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
