using UnityEngine;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Hover effect that scales the target transform.
    /// Ported from VRButtonAnimation scale logic.
    /// </summary>
    [System.Serializable]
    public class ScaleHoverEffect : HoverEffectBase
    {
        public override string EffectId => "scale";

        [Header("Scale Settings")]
        [Tooltip("Scale value when hovered (1.05 = 5% larger)")]
        [SerializeField] private float hoverScale = 1.05f;

        [Tooltip("If true, scale uniformly on all axes")]
        [SerializeField] private bool uniformScale = true;

        [Tooltip("Custom scale for non-uniform scaling (used when uniformScale is false)")]
        [SerializeField] private Vector3 customHoverScale = new Vector3(1.05f, 1.05f, 1f);

        // Runtime
        private Vector3 _originalScale;
        private Transform _target;

        public override void Initialize(HoverEffectController controller)
        {
            base.Initialize(controller);

            _target = controller.TargetVisuals;
            if (_target != null)
            {
                _originalScale = _target.localScale;
            }
        }

        protected override void ApplyEffect(float progress)
        {
            if (_target == null) return;

            Vector3 targetScale;
            if (uniformScale)
            {
                float scale = Mathf.Lerp(1f, hoverScale, progress);
                targetScale = _originalScale * scale;
            }
            else
            {
                targetScale = Vector3.Lerp(_originalScale, Vector3.Scale(_originalScale, customHoverScale), progress);
            }

            _target.localScale = targetScale;
        }

        public override void SetStateImmediate(bool hovered)
        {
            base.SetStateImmediate(hovered);

            // Also directly set the scale for immediate effect
            if (_target != null)
            {
                if (hovered)
                {
                    if (uniformScale)
                    {
                        _target.localScale = _originalScale * hoverScale;
                    }
                    else
                    {
                        _target.localScale = Vector3.Scale(_originalScale, customHoverScale);
                    }
                }
                else
                {
                    _target.localScale = _originalScale;
                }
            }
        }

        #region Fluent API

        public ScaleHoverEffect WithHoverScale(float scale)
        {
            hoverScale = scale;
            uniformScale = true;
            return this;
        }

        public ScaleHoverEffect WithCustomScale(Vector3 scale)
        {
            customHoverScale = scale;
            uniformScale = false;
            return this;
        }

        public ScaleHoverEffect WithUniformScale(bool uniform)
        {
            uniformScale = uniform;
            return this;
        }

        #endregion
    }
}
