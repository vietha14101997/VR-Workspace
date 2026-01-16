using UnityEngine;
using UnityEngine.UI;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Hover effect that updates background material's _HoverAmount property.
    /// Works with glassmorphism shaders that have hover state support.
    /// Ported from VRButtonAnimation background material logic.
    /// </summary>
    [System.Serializable]
    public class BackgroundHoverEffect : HoverEffectBase
    {
        public override string EffectId => "background";

        [Header("Background Target")]
        [Tooltip("Names of child objects to check for background material (in order)")]
        [SerializeField] private string[] backgroundChildNames = { "Background", "ConnectBackground" };

        [Tooltip("If true, also check TargetVisuals directly for Image component")]
        [SerializeField] private bool checkTargetDirect = true;

        // Runtime
        private Material _backgroundMaterial;
        private Image _backgroundImage;

        public override void Initialize(HoverEffectController controller)
        {
            base.Initialize(controller);

            if (controller.TargetVisuals == null) return;

            // Try to find background material

            // First, check TargetVisuals directly
            if (checkTargetDirect)
            {
                var directImage = controller.TargetVisuals.GetComponent<Image>();
                if (directImage != null && directImage.material != null && directImage.material.HasProperty("_HoverAmount"))
                {
                    // Create material instance to avoid affecting other elements
                    _backgroundMaterial = new Material(directImage.material);
                    directImage.material = _backgroundMaterial;
                    _backgroundImage = directImage;
                    return;
                }
            }

            // Then check child objects
            foreach (var childName in backgroundChildNames)
            {
                if (string.IsNullOrEmpty(childName)) continue;

                Transform bgTransform = controller.TargetVisuals.Find(childName);
                if (bgTransform != null)
                {
                    var bgImage = bgTransform.GetComponent<Image>();
                    if (bgImage != null && bgImage.material != null && bgImage.material.HasProperty("_HoverAmount"))
                    {
                        // Create material instance to avoid affecting other elements
                        _backgroundMaterial = new Material(bgImage.material);
                        bgImage.material = _backgroundMaterial;
                        _backgroundImage = bgImage;
                        return;
                    }
                }
            }
        }

        public override void Cleanup()
        {
            if (_backgroundMaterial != null)
            {
                Object.Destroy(_backgroundMaterial);
                _backgroundMaterial = null;
            }
        }

        protected override void ApplyEffect(float progress)
        {
            if (_backgroundMaterial == null) return;

            if (_backgroundMaterial.HasProperty("_HoverAmount"))
            {
                _backgroundMaterial.SetFloat("_HoverAmount", progress);
            }
        }

        public override void SetStateImmediate(bool hovered)
        {
            base.SetStateImmediate(hovered);

            if (_backgroundMaterial != null && _backgroundMaterial.HasProperty("_HoverAmount"))
            {
                _backgroundMaterial.SetFloat("_HoverAmount", hovered ? 1f : 0f);
            }
        }

        #region Fluent API

        public BackgroundHoverEffect WithChildNames(params string[] names)
        {
            backgroundChildNames = names;
            return this;
        }

        public BackgroundHoverEffect WithCheckTargetDirect(bool check)
        {
            checkTargetDirect = check;
            return this;
        }

        public BackgroundHoverEffect WithTransitionDuration(float duration)
        {
            transitionDuration = Mathf.Max(0f, duration);
            return this;
        }

        #endregion
    }
}
