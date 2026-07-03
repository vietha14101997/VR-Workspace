using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using VRWorkspace.Domain.Input;
using VRWorkspace.Presentation.Input.Cursor;
using VRWorkspace.Presentation.Input.VCS;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Components;
using VRWorkspace.UI.HoverEffects;

namespace VRWorkspace.Presentation.Input.Click
{
    /// <summary>
    /// Bridges VCS click events to actual UI dispatch.
    ///
    /// Uses the cursor's WORLD position (from WorldSpaceCursorRenderer.CursorWorldPosition)
    /// and synthesizes a Ray from Camera.main through that point. The ray then re-uses
    /// the existing gaze flow: RTTRaycastManager.Raycast → Physics.Raycast against the
    /// quad BoxCollider → CalculateUV from worldHitPoint → GraphicRaycaster.Raycast.
    ///
    /// Why world raycast instead of UV-based:
    ///   - Visual cursor is positioned via world transform of the active surface's quad.
    ///   - Quad may have rotation (RTTMenu oriented towards camera) so UV → world mapping
    ///     is not a simple identity.
    ///   - World raycast hits the same physical quad collider that the gaze reticle uses,
    ///     so visual position == click raycast hit point == UI element. No drift.
    ///
    /// Mirrors the priority cascade in VRGazeReticle.ProcessDwellClickRTT (lines 619-735):
    ///   1. Click outside keyboard area &amp; keyboard open → close keyboard (suppress click).
    ///   2. Click anywhere else → forward via world raycast.
    /// </summary>
    [DefaultExecutionOrder(-5000)]
    public sealed partial class ClickDispatcher : MonoBehaviour
    {
        public static ClickDispatcher Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[ClickDispatcher]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ClickDispatcher>();
        }

        private bool _subscribed;
        private GameObject _currentHoveredObject;

        private void OnEnable()
        {
            _subscribed = false;
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_subscribed && VirtualCursorSpace.Instance != null)
            {
                VirtualCursorSpace.Instance.OnClickDispatched -= OnClickDispatched;
            }
            _subscribed = false;
            ClearHover();
        }

        private void Update()
        {
            // Lazy-subscribe: VirtualCursorSpace is created lazily when the first surface
            // registers, which may happen AFTER this MonoBehaviour's OnEnable. Without
            // this retry, click events are silently dropped on the very first session.
            TrySubscribe();
            UpdateHover();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            if (VirtualCursorSpace.Instance == null) return;
            VirtualCursorSpace.Instance.OnClickDispatched += OnClickDispatched;
            _subscribed = true;
        }

        private void UpdateHover()
        {
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || vcs.Mode == InputMode.Gaze)
            {
                ClearHover();
                return;
            }

            var cursor = vcs.Cursor;
            if (!cursor.IsVisible || !cursor.SurfaceId.HasValue)
            {
                ClearHover();
                return;
            }

            if (!vcs.Surfaces.TryGet(cursor.SurfaceId.Value, out var surface))
            {
                ClearHover();
                return;
            }

            var renderer = WorldSpaceCursorRenderer.Instance;
            Camera cam = Camera.main;
            if (renderer == null || cam == null)
            {
                ClearHover();
                return;
            }

            GameObject hitObj = null;
            PointerEventData pointerData = new PointerEventData(EventSystem.current);

            var canvas = surface.RuntimeRef as RTTCanvasBase;
            var actionBar = surface.RuntimeRef as ActionBarSurfaceController;

            // ── Popup-first branch: when any WorldSpace popup is open, raycast against
            //    the popup's BoxCollider FIRST (popup is closer to camera than RTTMenuFrame).
            //    See ClickDispatcher.Popup.cs for helper implementations.
            if (GetActiveWorldSpacePopup() != null)
            {
                var popupResult = TryPopupRaycast(cam);
                if (popupResult.raycastHit && popupResult.isPartOfActivePopup)
                {
                    hitObj = popupResult.uiElement;
                }
                // If raycast missed popup or hit something outside popup, hitObj stays null
                // → no UI element hovered. Existing hover dispatch below handles ClearHover.
            }
            else
            {
                // No popup open — make sure popup hit cache is cleared so WorldSpaceCursorRenderer
                // doesn't keep applying the (now-stale) popup hit position every frame.
                ClearPopupHitCache();
            }

            if (canvas != null)
            {
                var raycastMgr = VRWorkspace.UI.RTT.Input.RTTRaycastManager.Instance;
                if (raycastMgr != null)
                {
                    Vector3 worldPos = renderer.CursorWorldPosition;
                    Vector3 dir = (worldPos - cam.transform.position);
                    float dist = dir.magnitude;
                    if (dist > 0.01f)
                    {
                        Ray ray = new Ray(cam.transform.position, dir / dist);
                        var hit = raycastMgr.Raycast(ray);
                        if (hit.isValid)
                        {
                            hitObj = hit.hitUIElement;
                            
                            // Re-use RTTRaycastManager's pointer event data if available
                            // to keep pointer state consistent
                            var pDataField = raycastMgr.GetType().GetField("_pointerEventData", 
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (pDataField != null)
                            {
                                var pData = pDataField.GetValue(raycastMgr) as PointerEventData;
                                if (pData != null) pointerData = pData;
                            }
                        }
                    }
                }
            }
            else if (actionBar != null)
            {
                var graphicRaycaster = actionBar.GetComponentInChildren<GraphicRaycaster>();
                if (graphicRaycaster != null)
                {
                    Vector3 worldPos = renderer.CursorWorldPosition;
                    Vector2 screenPos = cam.WorldToScreenPoint(worldPos);
                    pointerData.position = screenPos;

                    var results = new List<RaycastResult>();
                    graphicRaycaster.Raycast(pointerData, results);
                    if (results.Count > 0)
                    {
                        hitObj = results[0].gameObject;
                    }
                }
            }

            if (_currentHoveredObject != hitObj)
            {
                if (_currentHoveredObject != null)
                {
                    ExecuteEvents.Execute(_currentHoveredObject, pointerData, ExecuteEvents.pointerExitHandler);
                    var selectable = _currentHoveredObject.GetComponentInParent<Selectable>();
                    if (selectable != null) selectable.OnPointerExit(pointerData);

                    var hoverCtrl = _currentHoveredObject.GetComponent<HoverEffectController>();
                    if (hoverCtrl == null) hoverCtrl = _currentHoveredObject.GetComponentInParent<HoverEffectController>();
                    if (hoverCtrl != null) hoverCtrl.OnPointerExit(null);
                }

                _currentHoveredObject = hitObj;

                if (hitObj != null)
                {
                    ExecuteEvents.Execute(hitObj, pointerData, ExecuteEvents.pointerEnterHandler);
                    var selectable = hitObj.GetComponentInParent<Selectable>();
                    if (selectable != null) selectable.OnPointerEnter(pointerData);

                    var hoverCtrl = hitObj.GetComponent<HoverEffectController>();
                    if (hoverCtrl == null) hoverCtrl = hitObj.GetComponentInParent<HoverEffectController>();
                    if (hoverCtrl != null) hoverCtrl.OnPointerEnter(null);
                }
                
                // If it is an RTT canvas, mark dirty to redraw hover highlights
                if (canvas != null)
                {
                    canvas.MarkDirty();
                }
            }
        }

        private void ClearHover()
        {
            if (_currentHoveredObject != null)
            {
                PointerEventData pointerData = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(_currentHoveredObject, pointerData, ExecuteEvents.pointerExitHandler);
                var selectable = _currentHoveredObject.GetComponentInParent<Selectable>();
                if (selectable != null) selectable.OnPointerExit(pointerData);

                var hoverCtrl = _currentHoveredObject.GetComponent<HoverEffectController>();
                if (hoverCtrl == null) hoverCtrl = _currentHoveredObject.GetComponentInParent<HoverEffectController>();
                if (hoverCtrl != null) hoverCtrl.OnPointerExit(null);

                _currentHoveredObject = null;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnClickDispatched(CursorState cursor)
        {
            Debug.Log($"[CD_DBG-B0] OnClickDispatched ENTRY, cursor.SurfaceId={cursor.SurfaceId.HasValue}, value={cursor.SurfaceId}, IsVisible={cursor.IsVisible}");
            if (!cursor.SurfaceId.HasValue)
            {
                Debug.Log($"[CD_DBG-B1] EARLY RETURN: cursor.SurfaceId is null");
                return;
            }

            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || !vcs.Surfaces.TryGet(cursor.SurfaceId.Value, out var surface))
            {
                Debug.Log($"[CD_DBG-B2] EARLY RETURN: vcs={vcs != null}, surface found={vcs != null && vcs.Surfaces.TryGet(cursor.SurfaceId.Value, out _)}");
                return;
            }

            var renderer = WorldSpaceCursorRenderer.Instance;
            Camera cam = Camera.main;
            if (renderer == null || cam == null)
            {
                Debug.Log($"[CD_DBG-B] OnClickDispatched EARLY RETURN: renderer={renderer != null}, cam={cam != null}");
                return;
            }
            Debug.Log($"[CD_DBG-B] OnClickDispatched entry, cursor.SurfaceId={cursor.SurfaceId}, surface found");

            // ── MediaPlayer branch: when cursor is on mediaPlayer surface, dispatch click as
            //    mediaPlayer UI (empty-area hide / wake-from-hidden). See
            //    ClickDispatcher.MediaPlayer.cs for full implementation.
            Vector3 _mpWorldPos = renderer.CursorWorldPosition;
            Vector3 _mpDir = (_mpWorldPos - cam.transform.position);
            float _mpDist = _mpDir.magnitude;
            if (_mpDist > 0.01f)
            {
                Ray _mpRay = new Ray(cam.transform.position, _mpDir / _mpDist);
                if (TryDispatchMediaPlayerClick(cam, _mpRay))
                {
                    return;
                }
            }

            // ── Popup-first branch: when any WorldSpace popup is open, raycast against
            //    the popup FIRST (popup is 2-5cm closer to camera than RTTMenuFrame).
            //    If click hits popup UI → dispatch click. If click misses popup (or hits popup
            //    BoxCollider but no interactive UI element under the cursor, e.g., popup
            //    background) → hide popup and suppress the click. This defensive close matches
            //    VRGazeReticle line 720-734 behaviour.
            if (GetActiveWorldSpacePopup() != null)
            {
                var popupResult = TryPopupRaycast(cam);
                if (popupResult.raycastHit && popupResult.isPartOfActivePopup && popupResult.uiElement != null)
                {
                    DispatchClickToPopup(popupResult, cam);
                }
                else
                {
                    // Click ray missed active popup (or hit popup background without interactive UI).
                    // Close it (matches VRGazeReticle).
                    CloseActivePopup();
                }
                return;
            }

            var canvas = surface.RuntimeRef as RTTCanvasBase;

            // ── ActionBar fallback: WorldSpace canvas click dispatch ──────────
            var actionBar = surface.RuntimeRef as ActionBarSurfaceController;
            if (actionBar != null && canvas == null)
            {
                var graphicRaycaster = actionBar.GetComponentInChildren<GraphicRaycaster>();
                if (graphicRaycaster != null)
                {
                    Vector3 worldPos = renderer.CursorWorldPosition;
                    Vector2 screenPos = cam.WorldToScreenPoint(worldPos);

                    var pointerData = new PointerEventData(EventSystem.current)
                    {
                        position = screenPos
                    };

                    var results = new List<RaycastResult>();
                    graphicRaycaster.Raycast(pointerData, results);

                    if (results.Count > 0)
                    {
                        var target = results[0].gameObject;
                        var button = target.GetComponentInParent<Button>();
                        if (button != null && button.interactable)
                        {
                            ExecuteEvents.Execute(button.gameObject, pointerData, ExecuteEvents.pointerClickHandler);
                            return;
                        }
                        
                        var clickHandlers = target.GetComponentsInParent<IPointerClickHandler>();
                        if (clickHandlers != null && clickHandlers.Length > 0)
                        {
                            foreach (var handler in clickHandlers)
                            {
                                var mb = handler as MonoBehaviour;
                                if (mb != null && mb.enabled)
                                {
                                    ExecuteEvents.Execute(mb.gameObject, pointerData, ExecuteEvents.pointerClickHandler);
                                    return;
                                }
                            }
                        }
                    }
                }
                return;
            }
            // ──────────────────────────────────────────────────────────────────

            // Click-outside-keyboard suppression
            var keyboard = RTTMobileKeyboard.CurrentlyOpenKeyboard;
            if (keyboard != null
                && canvas != null                                  // skip suppression for WorldSpace bars
                && surface.RuntimeRef as RTTMobileKeyboard != keyboard
                && !IsInsideKeyboard(surface))
            {
                keyboard.Hide();
                return;
            }

            // World-based raycast — same flow as gaze reticle, ensures visual == click target.
            var raycastMgr = VRWorkspace.UI.RTT.Input.RTTRaycastManager.Instance;
            if (raycastMgr == null)
            {
                return;
            }

            Vector3 clickWorldPos = renderer.CursorWorldPosition;
            Vector3 dir = (clickWorldPos - cam.transform.position);
            float dist = dir.magnitude;
            if (dist < 0.01f)
            {
                return;
            }
            dir /= dist;

            Ray ray = new Ray(cam.transform.position, dir);
            var hit = raycastMgr.Raycast(ray);

            if (!hit.isValid || hit.hitUIElement == null) return;
            // The Raycast above already populated _currentHit; SendClick dispatches using it.
            raycastMgr.SendClick();
        }

        private static bool IsInsideKeyboard(VirtualSurface surface)
        {
            var vcs = VirtualCursorSpace.Instance;
            var keyboard = RTTMobileKeyboard.CurrentlyOpenKeyboard;
            if (vcs == null || keyboard == null) return false;

            object kbObj = keyboard;
            foreach (var s in vcs.Surfaces.All)
            {
                if (object.ReferenceEquals(s.RuntimeRef, kbObj)) return true;
            }
            return false;
        }
    }
}
