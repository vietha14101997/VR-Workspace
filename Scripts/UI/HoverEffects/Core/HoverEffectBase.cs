using UnityEngine;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Base class for all hover effects with common transition logic
    /// </summary>
    [System.Serializable]
    public abstract class HoverEffectBase : IHoverEffect
    {
        public abstract string EffectId { get; }

        public bool IsHovered { get; protected set; }

        [Header("Transition Settings")]
        [Tooltip("Speed of hover transition (higher = faster)")]
        [SerializeField] protected float transitionSpeed = 10f;

        [Tooltip("Custom easing curve for the transition")]
        [SerializeField] protected AnimationCurve easingCurve;

        protected HoverEffectController _controller;
        protected float _currentProgress = 0f;
        protected float _targetProgress = 0f;

        /// <summary>
        /// Default constructor - sets up default easing curve
        /// </summary>
        public HoverEffectBase()
        {
            // Default ease in-out curve
            easingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        }

        public virtual void Initialize(HoverEffectController controller)
        {
            _controller = controller;
            _currentProgress = 0f;
            _targetProgress = 0f;

            // Ensure easing curve exists
            if (easingCurve == null || easingCurve.keys.Length == 0)
            {
                easingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }
        }

        public virtual void OnHoverEnter()
        {
            IsHovered = true;
            _targetProgress = 1f;
        }

        public virtual void OnHoverExit()
        {
            IsHovered = false;
            _targetProgress = 0f;
        }

        public virtual void UpdateEffect(float deltaTime)
        {
            // Smooth interpolation toward target
            _currentProgress = Mathf.Lerp(_currentProgress, _targetProgress, deltaTime * transitionSpeed);

            // Apply easing curve
            float easedProgress = easingCurve.Evaluate(_currentProgress);

            // Apply the effect
            ApplyEffect(easedProgress);
        }

        /// <summary>
        /// Apply the effect based on progress (0 = not hovered, 1 = fully hovered)
        /// </summary>
        protected abstract void ApplyEffect(float progress);

        public virtual void SetStateImmediate(bool hovered)
        {
            IsHovered = hovered;
            _currentProgress = hovered ? 1f : 0f;
            _targetProgress = _currentProgress;
            ApplyEffect(_currentProgress);
        }

        public virtual void Cleanup()
        {
            // Override in derived classes if cleanup is needed
        }

        /// <summary>
        /// Set transition speed
        /// </summary>
        public void SetTransitionSpeed(float speed)
        {
            transitionSpeed = Mathf.Max(0.1f, speed);
        }

        /// <summary>
        /// Set custom easing curve
        /// </summary>
        public void SetEasingCurve(AnimationCurve curve)
        {
            if (curve != null && curve.keys.Length >= 2)
            {
                easingCurve = curve;
            }
        }
    }
}
