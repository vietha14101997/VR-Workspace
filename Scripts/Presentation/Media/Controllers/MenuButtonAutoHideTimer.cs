using UnityEngine;
using System.Collections;

namespace VRWorkspace.Presentation.Media.Controllers
{
    /// <summary>
    /// Auto-hides the Gaze-mode Menu Button after 10s of inactivity, fading back in
    /// when the user's gaze enters the button's collider area, and fading out again
    /// once the gaze leaves. The visual fade keeps the menu button discreet while
    /// making it discoverable through gaze interaction.
    ///
    /// Hide-on-leave policy:
    /// - FIRST hide after the UI controls go hidden: waits the full
    ///   <see cref="_hideDelaySeconds"/> so the user has a chance to notice the
    ///   wake-up button before it disappears.
    /// - SUBSEQUENT hides (after the user has already discovered the button): the
    ///   moment the reticle leaves the area the button fades out — no 10s wait,
    ///   because the user knows where it is and shouldn't be kept waiting.
    ///
    /// Detection model: a Physics.Raycast from Camera.main against the menu button's
    /// collider hits the same RTT DisplayQuad that the gaze reticle already targets
    /// (same VirtualObjects layer, same BoxCollider that VRGazeReticle uses), so
    /// "gaze-on-menu" here matches "gaze-reticle-visible-on-menu" exactly. The
    /// collider is intentionally kept enabled while the menu button is faded out
    /// so the gaze reticle can still see the area and the user can summon the
    /// button back by looking at where it should be.
    /// </summary>
    public class MenuButtonAutoHideTimer : MonoBehaviour
    {
        [SerializeField] private float _hideDelaySeconds = 5f;
        [SerializeField] private float _fadeDuration = 0.15f;

        private GameObject _menuButton;
        private Camera _camera;
        private int _layerMask;
        private CanvasGroup _canvasGroup;
        private float _idleSeconds;
        private Coroutine _fadeCoroutine;
        private bool _initialized;

        // The full _hideDelaySeconds count-down only applies the FIRST time the menu
        // button auto-hides after the UI controls go hidden. After that, the user has
        // already discovered the wake-up button, so subsequent hide-on-leave is immediate
        // (no 10s wait). Resetting happens in OnEnable when the controls-hide cycle
        // starts fresh (mode flip back to Gaze, controls hidden again, etc.).
        private bool _hasBeenHiddenOnce;

        private static MenuButtonAutoHideTimer Attach(GameObject menuButton, Camera camera)
        {
            if (menuButton == null) return null;
            var t = menuButton.GetComponent<MenuButtonAutoHideTimer>();
            if (t == null) t = menuButton.AddComponent<MenuButtonAutoHideTimer>();
            t._menuButton = menuButton;
            t._camera = camera;

            int layer = LayerMask.NameToLayer("VirtualObjects");
            if (layer < 0)
            {
                Debug.LogWarning("[MenuButtonAutoHideTimer] VirtualObjects layer not found; reticle-gaze detection will be disabled.");
                t._layerMask = 0;
            }
            else
            {
                t._layerMask = 1 << layer;
            }

            t._canvasGroup = menuButton.GetComponent<CanvasGroup>();
            if (t._canvasGroup == null) t._canvasGroup = menuButton.AddComponent<CanvasGroup>();
            t._idleSeconds = 0f;
            t._initialized = true;
            return t;
        }

        /// <summary>
        /// Attach the auto-hide timer to the menu button. Should be called once
        /// after the menu button's RTT frame is built.
        /// </summary>
        public static MenuButtonAutoHideTimer AttachTo(GameObject menuButton, Camera camera)
        {
            return Attach(menuButton, camera);
        }

        private void OnEnable()
        {
            // When the menu button is reactivated (e.g. switching back to Gaze mode
            // with controls hidden), reset the timer and ensure the button is visible.
            // Also reset the "first hide completed" flag so this new hide-UI cycle
            // gets the full _hideDelaySeconds count-down again.
            if (!_initialized) return;
            _idleSeconds = 0f;
            _hasBeenHiddenOnce = false;
            FadeTo(1f);
        }

        private void OnDisable()
        {
            // When the menu button is hidden externally (cursor mode, UI visible), stop
            // any running fade and let the auto-hide skip its work for this frame.
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }
        }

        private void Update()
        {
            if (!_initialized) return;
            if (_menuButton == null || _camera == null) return;
            if (!_menuButton.activeSelf) return;

            bool isGazeOnMenu = IsGazeOnMenuButton();

            if (isGazeOnMenu)
            {
                _idleSeconds = 0f;
                FadeTo(1f);
            }
            else
            {
                if (_hasBeenHiddenOnce)
                {
                    // User has already discovered the wake-up button at least once
                    // this cycle. Subsequent hide-on-leave is immediate — no need to
                    // keep the user waiting another 10s every time they glance at
                    // the area and look away.
                    FadeTo(0f);
                }
                else
                {
                    // First hide after UI controls went hidden. Give the user the full
                    // _hideDelaySeconds grace period so they have a chance to notice
                    // the wake-up button before it disappears.
                    _idleSeconds += Time.deltaTime;
                    if (_idleSeconds >= _hideDelaySeconds)
                    {
                        FadeTo(0f);
                        _hasBeenHiddenOnce = true;
                    }
                }
            }
        }

        private bool IsGazeOnMenuButton()
        {
            if (_layerMask == 0) return false;
            Ray ray = new Ray(_camera.transform.position, _camera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, _layerMask, QueryTriggerInteraction.Collide))
                return false;

            // The BoxCollider sits on the menu button's DisplayQuad (a child of the
            // RTTMenuFrame). Walk up the hierarchy to see if the hit belongs to the
            // menu button.
            Transform t = hit.collider.transform;
            while (t != null)
            {
                if (t.gameObject == _menuButton) return true;
                t = t.parent;
            }
            return false;
        }

        private void FadeTo(float target)
        {
            if (_canvasGroup == null) return;
            if (Mathf.Approximately(_canvasGroup.alpha, target)) return;
            if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
            _fadeCoroutine = StartCoroutine(FadeRoutine(target));
        }

        private IEnumerator FadeRoutine(float target)
        {
            float start = _canvasGroup.alpha;
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                if (_canvasGroup == null) yield break;
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / _fadeDuration));
                yield return null;
            }
            if (_canvasGroup != null) _canvasGroup.alpha = target;
            _fadeCoroutine = null;
        }
    }
}
