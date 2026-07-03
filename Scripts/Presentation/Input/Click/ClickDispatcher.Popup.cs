using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRWorkspace.Domain.Input;
using VRWorkspace.Presentation.Input.Cursor;
using VRWorkspace.Presentation.Input.VCS;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.Components;

namespace VRWorkspace.Presentation.Input.Click
{
    /// <summary>
    /// Partial class: popup-specific raycast + click dispatch + close-on-outside.
    ///
    /// When any WorldSpace popup is open (RTTPopupMenu, RTTPopupInputable, RTTProgressPopup),
    /// the cursor ray hits the popup's BoxCollider first (popup is closer to camera than
    /// RTTMenuFrame). This file encapsulates the popup-first raycast branch shared between
    /// UpdateHover (hover) and OnClickDispatched (click).
    ///
    /// Behaviour parity with VRGazeReticle (lines 707-735): if popup is open and click ray
    /// misses the popup, the active popup is hidden and the click is suppressed.
    /// </summary>
    public sealed partial class ClickDispatcher
    {
        // ── Reusable scratch buffers (avoid per-frame GC alloc) ────────────────
        private readonly List<RaycastResult> _popupRaycastResults = new List<RaycastResult>();

        // ── Cached state (read by WorldSpaceCursorRenderer) ─────────────────────
        private Vector3 _lastPopupWorldHit;
        private Transform _lastPopupTransform;
        private bool _hasPopupHit;

        /// <summary>World position of the cursor's last raycast hit on a popup.</summary>
        public Vector3 LastPopupWorldHit => _lastPopupWorldHit;

        /// <summary>Transform of the popup BoxCollider that the cursor is currently hitting.</summary>
        public Transform LastPopupTransform => _lastPopupTransform;

        /// <summary>True when the cursor ray is currently hitting an active popup's BoxCollider.</summary>
        public bool HasPopupHit => _hasPopupHit;

        // ── Layer mask cache (VirtualObjects | UI) ──────────────────────────────
        private static int s_popupRaycastMask = -1;
        private const float kPopupMaxRaycastDistance = 10f;

        /// <summary>
        /// Reset static state on domain reload so layer indices are re-resolved.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPopupStaticState()
        {
            s_popupRaycastMask = -1;
        }

        private static int GetPopupRaycastMask()
        {
            if (s_popupRaycastMask != -1) return s_popupRaycastMask;
            int mask = 0;
            int vo = LayerMask.NameToLayer("VirtualObjects");
            int ui = LayerMask.NameToLayer("UI");
            if (vo != -1) mask |= 1 << vo;
            if (ui != -1) mask |= 1 << ui;
            s_popupRaycastMask = mask;
            return mask;
        }

        // ── Active popup detection ─────────────────────────────────────────────
        /// <summary>
        /// Returns the currently open WorldSpace popup (any of the 3 types), or null.
        /// YAGNI: no coordinator — each popup class exposes its own static.
        /// </summary>
        private static MonoBehaviour GetActiveWorldSpacePopup()
        {
            if (RTTPopupMenu.CurrentlyOpenPopup != null) return RTTPopupMenu.CurrentlyOpenPopup;
            if (RTTPopupInputable.CurrentlyOpenPopup != null) return RTTPopupInputable.CurrentlyOpenPopup;
            if (RTTProgressPopup.CurrentlyOpenPopup != null) return RTTProgressPopup.CurrentlyOpenPopup;
            return null;
        }

        /// <summary>
        /// Returns true if the GameObject belongs to any of the 3 active popup types' panel hierarchy.
        /// </summary>
        private static bool IsPartOfAnyActivePopup(GameObject obj)
        {
            if (obj == null) return false;
            if (RTTPopupMenu.CurrentlyOpenPopup != null && RTTPopupMenu.CurrentlyOpenPopup.IsPartOfPopupPanel(obj))
                return true;
            if (RTTPopupInputable.CurrentlyOpenPopup != null && RTTPopupInputable.CurrentlyOpenPopup.IsPartOfPopupPanel(obj))
                return true;
            if (RTTProgressPopup.CurrentlyOpenPopup != null && RTTProgressPopup.CurrentlyOpenPopup.IsPartOfPopupPanel(obj))
                return true;
            return false;
        }

        // ── Popup raycast helper (shared by hover + click) ─────────────────────
        private struct PopupRaycastResult
        {
            public bool raycastHit;
            public bool isPartOfActivePopup;
            public RaycastHit hit;
            public GameObject uiElement;
            public GraphicRaycaster raycaster;
        }

        /// <summary>
        /// Raycast through the cursor world position, check if it hits any active popup BoxCollider,
        /// then resolve the popup's GraphicRaycaster hit (if any). Updates _lastPopupWorldHit /
        /// _lastPopupTransform / _hasPopupHit as a side effect.
        ///
        /// IMPORTANT: ray source is computed from VCS.Cursor.UV projected onto the underlying
        /// RTTMenuFrame (NOT from CursorWorldPosition). Otherwise cursor visual on popup would
        /// feed back into the raycast, causing the cursor to drift toward popup center
        /// ("auto-slide" bug).
        /// </summary>
        private PopupRaycastResult TryPopupRaycast(Camera cam)
        {
            var result = new PopupRaycastResult();
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || !vcs.Cursor.SurfaceId.HasValue)
            {
                ClearPopupHitCache();
                return result;
            }
            if (GetActiveWorldSpacePopup() == null)
            {
                ClearPopupHitCache();
                return result;
            }

            // Authoritative ray source: VCS cursor UV → world position on its current surface
            // (always RTTMenuFrame when popup-first branch runs, NOT the popup itself).
            Vector3 worldPos;
            if (vcs.Surfaces.TryGet(vcs.Cursor.SurfaceId.Value, out var surface))
            {
                worldPos = ComputeCursorWorldPosOnSurface(cam, surface, vcs.Cursor.UV);
            }
            else
            {
                ClearPopupHitCache();
                return result;
            }

            Vector3 dir = worldPos - cam.transform.position;
            float dist = dir.magnitude;
            if (dist < 0.01f)
            {
                ClearPopupHitCache();
                return result;
            }

            Ray ray = new Ray(cam.transform.position, dir / dist);
            if (!Physics.Raycast(ray, out RaycastHit hit, kPopupMaxRaycastDistance, GetPopupRaycastMask(), QueryTriggerInteraction.Collide))
            {
                ClearPopupHitCache();
                return result;
            }

            result.raycastHit = true;
            result.hit = hit;
            result.isPartOfActivePopup = IsPartOfAnyActivePopup(hit.collider.gameObject);

            if (result.isPartOfActivePopup)
            {
                result.raycaster = hit.collider.GetComponentInParent<GraphicRaycaster>();
                if (result.raycaster != null)
                {
                    Vector2 screenPos = cam.WorldToScreenPoint(hit.point);
                    var pData = new PointerEventData(EventSystem.current) { position = screenPos };
                    _popupRaycastResults.Clear();
                    result.raycaster.Raycast(pData, _popupRaycastResults);
                    if (_popupRaycastResults.Count > 0)
                        result.uiElement = _popupRaycastResults[0].gameObject;
                }
            }

            _hasPopupHit = result.isPartOfActivePopup;
            _lastPopupWorldHit = result.isPartOfActivePopup ? hit.point : Vector3.zero;
            _lastPopupTransform = result.isPartOfActivePopup ? hit.collider.transform : null;
            return result;
        }

        private void ClearPopupHitCache()
        {
            _hasPopupHit = false;
            _lastPopupWorldHit = Vector3.zero;
            _lastPopupTransform = null;
        }

        /// <summary>
        /// Project VCS cursor UV onto its current surface's world position. Same math as
        /// WorldSpaceCursorRenderer.LateUpdate lines 168-171, kept in sync for consistency.
        /// </summary>
        private Vector3 ComputeCursorWorldPosOnSurface(Camera cam, VirtualSurface surface, Vector2 uv)
        {
            var canvas = surface.RuntimeRef as RTTCanvasBase;
            var actionBar = surface.RuntimeRef as ActionBarSurfaceController;

            if (canvas != null)
            {
                var quad = canvas.GetQuadCollider();
                if (quad != null)
                {
                    Vector2 worldSize = canvas.GetWorldSize();
                    return quad.transform.position
                        + quad.transform.right * ((uv.x - 0.5f) * worldSize.x)
                        + quad.transform.up * ((uv.y - 0.5f) * worldSize.y);
                }
            }
            else if (actionBar != null)
            {
                Vector2 worldSize = actionBar.PhysicalSize;
                return actionBar.transform.position
                    + actionBar.transform.right * ((uv.x - 0.5f) * worldSize.x)
                    + actionBar.transform.up * ((uv.y - 0.5f) * worldSize.y);
            }
            // Fallback: aim slightly in front of camera
            return cam.transform.position + cam.transform.forward * 0.5f;
        }

        // ── Popup click dispatch helper ─────────────────────────────────────────
        /// <summary>
        /// Dispatch click to a popup UI element via ExecuteEvents. Mirrors RTTRaycastManager.SendClick
        /// order: VRInputFieldTrigger → Button → IPointerClickHandler.
        /// </summary>
        private static void DispatchClickToPopup(PopupRaycastResult popupResult, Camera cam)
        {
            Vector2 screenPos = cam.WorldToScreenPoint(popupResult.hit.point);
            var pData = new PointerEventData(EventSystem.current) { position = screenPos };
            GameObject target = popupResult.uiElement;

            // 1) VRInputFieldTrigger (RTTPopupInputable)
            var inputFieldTrigger = target.GetComponentInParent<VRInputFieldTrigger>();
            if (inputFieldTrigger != null && inputFieldTrigger.InputField != null && inputFieldTrigger.InputField.interactable)
            {
                ExecuteEvents.Execute(inputFieldTrigger.gameObject, pData, ExecuteEvents.pointerClickHandler);
                return;
            }

            // 2) Button
            var button = target.GetComponentInParent<Button>();
            if (button != null && button.interactable)
            {
                string buttonName = button.name; // cache before click handler may destroy
                ExecuteEvents.Execute(button.gameObject, pData, ExecuteEvents.pointerClickHandler);

                if (button != null)
                {
                    var ripple = button.GetComponentInChildren<VRButtonRipple>();
                    if (ripple != null)
                    {
                        // Popup canvas size is its own RectTransform.rect; for a world-space canvas,
                        // screen-space normalization is a reasonable approximation for the ripple center.
                        Vector2 normalizedPos = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
                        ripple.TriggerRipple(normalizedPos);
                    }
                }
                return;
            }

            // 3) Generic IPointerClickHandler
            var handlers = target.GetComponentsInParent<IPointerClickHandler>();
            if (handlers != null)
            {
                foreach (var h in handlers)
                {
                    var mb = h as MonoBehaviour;
                    if (mb != null && mb.enabled)
                    {
                        ExecuteEvents.Execute(mb.gameObject, pData, ExecuteEvents.pointerClickHandler);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Hide whichever popup type is currently open. Called when click ray misses the active popup.
        /// Logs a warning if more than one popup is open simultaneously (defensive — should not happen
        /// since each popup's Show() closes the previous one).
        /// </summary>
        private static void CloseActivePopup()
        {
            int openCount = 0;
            if (RTTPopupMenu.CurrentlyOpenPopup != null) openCount++;
            if (RTTPopupInputable.CurrentlyOpenPopup != null) openCount++;
            if (RTTProgressPopup.CurrentlyOpenPopup != null) openCount++;
            if (openCount > 1)
            {
                Debug.LogWarning($"[ClickDispatcher] {openCount} WorldSpace popups open simultaneously — closing all.");
            }

            if (RTTPopupMenu.CurrentlyOpenPopup != null) RTTPopupMenu.CurrentlyOpenPopup.Hide();
            if (RTTPopupInputable.CurrentlyOpenPopup != null) RTTPopupInputable.CurrentlyOpenPopup.Hide();
            if (RTTProgressPopup.CurrentlyOpenPopup != null) RTTProgressPopup.CurrentlyOpenPopup.Hide();
        }
    }
}