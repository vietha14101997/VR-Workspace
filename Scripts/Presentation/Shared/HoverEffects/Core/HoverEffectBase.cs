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
        [Tooltip("Duration of hover transition in seconds (0 = instant)")]
        [SerializeField] protected float transitionDuration = 0.15f;

        [Tooltip("Custom easing curve for the transition")]
        [SerializeField] protected AnimationCurve easingCurve;

        protected HoverEffectController _controller;
        protected float _currentProgress = 0f;
        protected float _targetProgress = 0f;
        protected float _rawProgress = 0f; // Linear progress before easing

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
            _rawProgress = 0f;

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
            // Skip if already at target
            if (Mathf.Approximately(_rawProgress, _targetProgress))
            {
                return;
            }

            // Linear interpolation using MoveTowards for predictable duration
            if (transitionDuration > 0f)
            {
                float speed = 1f / transitionDuration;
                _rawProgress = Mathf.MoveTowards(_rawProgress, _targetProgress, deltaTime * speed);
            }
            else
            {
                // Instant transition
                _rawProgress = _targetProgress;
            }

            // Apply easing curve for smooth feel
            _currentProgress = easingCurve.Evaluate(_rawProgress);

            // Apply the effect
            ApplyEffect(_currentProgress);
        }

        /// <summary>
        /// Apply the effect based on progress (0 = not hovered, 1 = fully hovered)
        /// </summary>
        protected abstract void ApplyEffect(float progress);

        public virtual void SetStateImmediate(bool hovered)
        {
            IsHovered = hovered;
            _rawProgress = hovered ? 1f : 0f;
            _currentProgress = _rawProgress;
            _targetProgress = _rawProgress;
            ApplyEffect(_currentProgress);
        }

        public virtual void Cleanup()
        {
            // Override in derived classes if cleanup is needed
        }

        /// <summary>
        /// Set transition duration in seconds
        /// </summary>
        public void SetTransitionDuration(float duration)
        {
            transitionDuration = Mathf.Max(0f, duration);
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

        #region Fluent API

        /// <summary>
        /// Set transition duration (fluent API)
        /// </summary>
        public T WithTransitionDuration<T>(float duration) where T : HoverEffectBase
        {
            transitionDuration = Mathf.Max(0f, duration);
            return (T)this;
        }

        #endregion
    }
}
