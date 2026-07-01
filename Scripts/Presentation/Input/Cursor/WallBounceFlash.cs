using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using VRWorkspace.Domain.Input;

namespace VRWorkspace.Presentation.Input.Cursor
{
    /// <summary>
    /// Visual cue (Decision 8) shown when the cursor can't traverse an edge —
    /// helps the user understand that the virtual coordinate system has no neighbor
    /// in that direction (mirrors Windows multi-monitor wall-bounce).
    ///
    /// Flash recipe: 100ms total, ease-out cubic:
    ///   alpha 0 → 0.35 → 0
    ///   scale 1.0 → 1.15 → 1.0
    /// </summary>
    [DefaultExecutionOrder(-3000)]
    public sealed class WallBounceFlash : MonoBehaviour
    {
        public static WallBounceFlash Instance { get; private set; }

        [SerializeField] private float duration = 0.1f;
        [SerializeField] private Color flashColor = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField, Range(8f, 200f)] private float baseEdgeThickness = 24f;

        private RectTransform _rect;
        private Image _image;
        private Coroutine _running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("[WallBounceFlash]");
            go.hideFlags = HideFlags.DontSave;
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WallBounceFlash>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildVisual();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private bool _subscribed;

        private void OnEnable()
        {
            _subscribed = false;
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_subscribed && VirtualCursorSpace.Instance != null)
            {
                VirtualCursorSpace.Instance.OnCursorEdgeBounced -= OnBounced;
            }
            _subscribed = false;
        }

        private void Update()
        {
            TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_subscribed) return;
            if (VirtualCursorSpace.Instance == null) return;
            VirtualCursorSpace.Instance.OnCursorEdgeBounced += OnBounced;
            _subscribed = true;
        }

        private void BuildVisual()
        {
            _rect = new GameObject("BounceFlash", typeof(RectTransform)).transform as RectTransform;
            _rect.SetParent(transform, false);
            _rect.sizeDelta = new Vector2(8f, 8f);
            _rect.gameObject.SetActive(false);

            _image = _rect.gameObject.AddComponent<Image>();
            _image.color = flashColor;
            _image.raycastTarget = false;
        }

        private void OnBounced(Guid sourceSurfaceId, EdgeDirection dir, Vector2 worldPoint)
        {
            // Resolve source surface so we can place the flash exactly on its edge.
            var vcs = VirtualCursorSpace.Instance;
            if (vcs == null || !vcs.Surfaces.TryGet(sourceSurfaceId, out var surface)) return;

            PositionOnEdge(surface, dir);
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(PlayFlash());
        }

        private void PositionOnEdge(VirtualSurface surface, EdgeDirection dir)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // Compute midpoint of the edge in world space.
            Vector2 c = surface.Center;
            Vector2 s = surface.Size * 0.5f;
            Vector2 edgeLocal = dir switch
            {
                EdgeDirection.Down  => new Vector2(c.x, c.y - s.y),
                EdgeDirection.Up    => new Vector2(c.x, c.y + s.y),
                EdgeDirection.Left  => new Vector2(c.x - s.x, c.y),
                EdgeDirection.Right => new Vector2(c.x + s.x, c.y),
                _ => c
            };

            var canvasBase = surface.RuntimeRef as UnityEngine.UI.Graphic;
            Vector3 worldPos;
            if (canvasBase != null)
            {
                worldPos = canvasBase.transform.TransformPoint(new Vector3(
                    edgeLocal.x - c.x, edgeLocal.y - c.y, 0f));
            }
            else
            {
                worldPos = new Vector3(edgeLocal.x, edgeLocal.y, 0f);
            }

            Vector3 screen = cam.WorldToScreenPoint(worldPos);
            _rect.position = new Vector3(screen.x, screen.y, 0f);

            // Edge-relative thickness (taller along the perpendicular axis).
            float thickness = baseEdgeThickness;
            float length = baseEdgeThickness * 4f;
            _rect.sizeDelta = (dir == EdgeDirection.Left || dir == EdgeDirection.Right)
                ? new Vector2(thickness, length)
                : new Vector2(length, thickness);

            _rect.localScale = Vector3.one;
        }

        private IEnumerator PlayFlash()
        {
            _rect.gameObject.SetActive(true);
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float u = t / duration;
                float eased = 1f - (1f - u) * (1f - u);

                Color c = flashColor;
                c.a = Mathf.Lerp(flashColor.a, 0f, eased);
                _image.color = c;

                float scale = 1f + 0.15f * Mathf.Sin(eased * Mathf.PI);
                _rect.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }
            _rect.gameObject.SetActive(false);
            _running = null;
        }
    }
}
