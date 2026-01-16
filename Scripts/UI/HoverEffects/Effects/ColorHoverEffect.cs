using UnityEngine;
using UnityEngine.UI;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Hover effect that changes color or alpha of a UI element.
    /// Useful for background highlights, border fades, etc.
    /// </summary>
    [System.Serializable]
    public class ColorHoverEffect : HoverEffectBase
    {
        public override string EffectId => "color";

        [Header("Target Settings")]
        [Tooltip("Name of child object to apply color change to. If empty, uses TargetVisuals directly.")]
        [SerializeField] private string targetChildName = "Background";

        [Tooltip("Type of target component to modify")]
        [SerializeField] private TargetType targetType = TargetType.Image;

        public enum TargetType
        {
            Image,
            Text,
            Graphic
        }

        [Header("Color Settings")]
        [Tooltip("Color when hovered")]
        [SerializeField] private Color hoverColor = new Color(0.3f, 1f, 1f, 0.3f);

        [Tooltip("If true, only affects alpha channel")]
        [SerializeField] private bool affectAlphaOnly = false;

        [Tooltip("If true, preserves original RGB and only changes alpha")]
        [SerializeField] private bool preserveOriginalRGB = false;

        // Runtime
        private Graphic _targetGraphic;
        private Color _originalColor;

        public override void Initialize(HoverEffectController controller)
        {
            base.Initialize(controller);

            if (controller.TargetVisuals == null) return;

            // Find target graphic
            Transform target = controller.TargetVisuals;

            if (!string.IsNullOrEmpty(targetChildName))
            {
                Transform child = controller.TargetVisuals.Find(targetChildName);
                if (child != null)
                {
                    target = child;
                }
            }

            switch (targetType)
            {
                case TargetType.Image:
                    _targetGraphic = target.GetComponent<Image>();
                    break;
                case TargetType.Text:
                    _targetGraphic = target.GetComponent<Text>();
                    if (_targetGraphic == null)
                    {
                        // Try TextMeshPro
                        var tmpText = target.GetComponent<TMPro.TMP_Text>();
                        if (tmpText != null)
                        {
                            _targetGraphic = tmpText;
                        }
                    }
                    break;
                case TargetType.Graphic:
                    _targetGraphic = target.GetComponent<Graphic>();
                    break;
            }

            if (_targetGraphic != null)
            {
                _originalColor = _targetGraphic.color;
            }
        }

        protected override void ApplyEffect(float progress)
        {
            if (_targetGraphic == null) return;

            Color targetColor;

            if (affectAlphaOnly || preserveOriginalRGB)
            {
                // Only change alpha
                targetColor = _originalColor;
                targetColor.a = Mathf.Lerp(_originalColor.a, hoverColor.a, progress);
            }
            else
            {
                // Full color interpolation
                targetColor = Color.Lerp(_originalColor, hoverColor, progress);
            }

            _targetGraphic.color = targetColor;
        }

        public override void SetStateImmediate(bool hovered)
        {
            base.SetStateImmediate(hovered);

            if (_targetGraphic != null)
            {
                if (hovered)
                {
                    if (affectAlphaOnly || preserveOriginalRGB)
                    {
                        Color c = _originalColor;
                        c.a = hoverColor.a;
                        _targetGraphic.color = c;
                    }
                    else
                    {
                        _targetGraphic.color = hoverColor;
                    }
                }
                else
                {
                    _targetGraphic.color = _originalColor;
                }
            }
        }

        #region Fluent API

        public ColorHoverEffect WithTargetChild(string childName)
        {
            targetChildName = childName;
            return this;
        }

        public ColorHoverEffect WithTargetType(TargetType type)
        {
            targetType = type;
            return this;
        }

        public ColorHoverEffect WithHoverColor(Color color)
        {
            hoverColor = color;
            return this;
        }

        public ColorHoverEffect WithAlphaOnly(bool alphaOnly)
        {
            affectAlphaOnly = alphaOnly;
            return this;
        }

        public ColorHoverEffect WithPreserveRGB(bool preserve)
        {
            preserveOriginalRGB = preserve;
            return this;
        }

        public ColorHoverEffect WithTransitionDuration(float duration)
        {
            transitionDuration = Mathf.Max(0f, duration);
            return this;
        }

        #endregion
    }
}
