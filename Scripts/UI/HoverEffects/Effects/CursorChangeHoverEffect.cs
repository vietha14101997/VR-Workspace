using UnityEngine;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Hover effect that changes the VR gaze reticle cursor sprite.
    /// Useful for indicating different interaction types (text input, resize, etc.)
    /// Ported from VRInputFieldTrigger cursor change logic.
    /// </summary>
    [System.Serializable]
    public class CursorChangeHoverEffect : HoverEffectBase
    {
        public override string EffectId => "cursor_change";

        [Header("Cursor Settings")]
        [Tooltip("Custom cursor sprite to use when hovered (takes priority over resource path)")]
        [SerializeField] private Sprite customCursorSprite;

        [Tooltip("Resource path to load cursor sprite from (e.g., 'icon_text_cursor')")]
        [SerializeField] private string cursorResourcePath = "icon_text_cursor";

        [Tooltip("If true, load sprite from Resources folder using cursorResourcePath")]
        [SerializeField] private bool loadFromResources = true;

        // Runtime
        private Sprite _loadedSprite;
        private bool _isCursorChanged = false;

        public override void Initialize(HoverEffectController controller)
        {
            base.Initialize(controller);

            // Load sprite from resources if needed
            if (loadFromResources && customCursorSprite == null && !string.IsNullOrEmpty(cursorResourcePath))
            {
                _loadedSprite = Resources.Load<Sprite>(cursorResourcePath);

                // Try loading as Texture2D and creating sprite
                if (_loadedSprite == null)
                {
                    Texture2D tex = Resources.Load<Texture2D>(cursorResourcePath);
                    if (tex != null)
                    {
                        _loadedSprite = Sprite.Create(
                            tex,
                            new Rect(0, 0, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f)
                        );
                    }
                }
            }
        }

        public override void OnHoverEnter()
        {
            base.OnHoverEnter();
            ApplyCursorChange();
        }

        public override void OnHoverExit()
        {
            base.OnHoverExit();
            ResetCursor();
        }

        protected override void ApplyEffect(float progress)
        {
            // Cursor change is instant, no interpolation needed
            // The actual cursor change happens in OnHoverEnter/Exit
        }

        public override void SetStateImmediate(bool hovered)
        {
            base.SetStateImmediate(hovered);

            if (hovered)
            {
                ApplyCursorChange();
            }
            else
            {
                ResetCursor();
            }
        }

        private void ApplyCursorChange()
        {
            if (_isCursorChanged) return;

            Sprite spriteToUse = customCursorSprite ?? _loadedSprite;

            if (spriteToUse != null && VRGazeReticle.Instance != null)
            {
                VRGazeReticle.Instance.SetCursorSprite(spriteToUse);
                _isCursorChanged = true;
            }
        }

        private void ResetCursor()
        {
            if (!_isCursorChanged) return;

            if (VRGazeReticle.Instance != null)
            {
                VRGazeReticle.Instance.ResetCursorSprite();
            }
            _isCursorChanged = false;
        }

        public override void Cleanup()
        {
            // Reset cursor on cleanup
            ResetCursor();

            // Destroy dynamically created sprite
            if (_loadedSprite != null && customCursorSprite == null)
            {
                Object.Destroy(_loadedSprite);
                _loadedSprite = null;
            }
        }

        #region Fluent API

        public CursorChangeHoverEffect WithSprite(Sprite sprite)
        {
            customCursorSprite = sprite;
            loadFromResources = false;
            return this;
        }

        public CursorChangeHoverEffect WithResourcePath(string path)
        {
            cursorResourcePath = path;
            loadFromResources = true;
            customCursorSprite = null;
            return this;
        }

        // Note: Cursor changes are always instant, this is for API consistency
        public CursorChangeHoverEffect WithTransitionDuration(float duration)
        {
            transitionDuration = Mathf.Max(0f, duration);
            return this;
        }

        #endregion
    }
}
