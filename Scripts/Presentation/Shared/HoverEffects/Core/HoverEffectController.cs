using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Main controller component that manages hover effects on a UI element.
    /// Implements IPointerEnterHandler and IPointerExitHandler for Unity event system.
    /// </summary>
    public class HoverEffectController : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Target")]
        [Tooltip("Transform to apply visual effects to. If null, uses this transform.")]
        [SerializeField] private Transform _targetVisuals;

        [Header("Preset Configuration")]
        [Tooltip("ScriptableObject preset containing effect configurations")]
        [SerializeField] private HoverEffectPreset _preset;

        [Tooltip("If true, use inspector overrides instead of preset")]
        [SerializeField] private bool _useOverrides = false;

        [Header("Override Effects")]
        [Tooltip("List of effects to use when useOverrides is true")]
        [SerializeReference] private List<HoverEffectBase> _overrideEffects = new List<HoverEffectBase>();

        [Header("State")]
        [Tooltip("Force hover state (e.g., when dropdown is open)")]
        [SerializeField] private bool _forceHover = false;

        // Runtime state
        private bool _isHovered = false;
        private List<IHoverEffect> _activeEffects = new List<IHoverEffect>();
        private bool _isInitialized = false;
#if UNITY_EDITOR
        private bool _warnedNoEffects = false; // Only warn once per instance
#endif

        // Events
        /// <summary>
        /// Called when hover state changes (enter or exit). Parameter is true if hovering.
        /// Useful for RTT panels that need to call MarkDirty().
        /// </summary>
        public event System.Action<bool> OnHoverStateChanged;

        // Public properties
        public Transform TargetVisuals
        {
            get => _targetVisuals != null ? _targetVisuals : transform;
            set => _targetVisuals = value;
        }
        public bool IsHovered => _isHovered;
        public bool IsForceHover => _forceHover;
        public bool EffectiveHover => _isHovered || _forceHover;
        public IReadOnlyList<IHoverEffect> ActiveEffects => _activeEffects;

        void Awake()
        {
            if (_targetVisuals == null)
            {
                _targetVisuals = transform;
            }
        }

        void Start()
        {
            InitializeEffects();
        }

        void OnEnable()
        {
            // Re-initialize if effects were cleared
            if (_isInitialized && _activeEffects.Count == 0)
            {
                InitializeEffects();
            }
        }

        /// <summary>
        /// Initialize all effects from preset or overrides
        /// </summary>
        private void InitializeEffects()
        {
            // Check if effects were already added programmatically (via AddEffect)
            bool hasRuntimeEffects = _activeEffects.Count > 0;

            if (!hasRuntimeEffects)
            {
                // Only load from preset/overrides if no effects were added programmatically
                if (_preset != null && !_useOverrides)
                {
                    // Create effect instances from preset
                    foreach (var effectData in _preset.Effects)
                    {
                        var effect = effectData.Clone();
                        effect.Initialize(this);
                        _activeEffects.Add(effect);
                    }
                }
                else if (_overrideEffects != null)
                {
                    // Use override effects directly
                    foreach (var effect in _overrideEffects)
                    {
                        if (effect != null)
                        {
                            effect.Initialize(this);
                            _activeEffects.Add(effect);
                        }
                    }
                }
            }

            _isInitialized = true;

            // Apply force hover if it was set before initialization
            if (_forceHover)
            {
                foreach (var effect in _activeEffects)
                {
                    effect.SetStateImmediate(true);
                }
            }
        }

        void Update()
        {
            if (!_isInitialized) return;

            float deltaTime = Time.unscaledDeltaTime;

            foreach (var effect in _activeEffects)
            {
                effect.UpdateEffect(deltaTime);
            }
        }

        #region Pointer Events

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;

            #if UNITY_EDITOR
            if (_activeEffects.Count == 0 && !_warnedNoEffects)
            {
                Debug.LogWarning($"[HoverEffectController] OnPointerEnter on {gameObject.name} but NO EFFECTS! TargetVisuals={_targetVisuals?.name}");
                _warnedNoEffects = true; // Only warn once
            }
            #endif

            foreach (var effect in _activeEffects)
            {
                effect.OnHoverEnter();
            }

            OnHoverStateChanged?.Invoke(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;

            // Only trigger exit if not force hovering
            if (!_forceHover)
            {
                foreach (var effect in _activeEffects)
                {
                    effect.OnHoverExit();
                }

                OnHoverStateChanged?.Invoke(false);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Set force hover state (e.g., when dropdown panel is open)
        /// </summary>
        public void SetForceHover(bool force)
        {
            if (_forceHover == force) return;

            _forceHover = force;

            if (force)
            {
                // Enter hover state
                foreach (var effect in _activeEffects)
                {
                    effect.OnHoverEnter();
                }
            }
            else if (!_isHovered)
            {
                // Exit hover state only if not naturally hovered
                foreach (var effect in _activeEffects)
                {
                    effect.OnHoverExit();
                }
            }
        }

        /// <summary>
        /// Reset hover state completely
        /// </summary>
        /// <param name="immediate">If true, snap to default state without animation</param>
        public void ResetHoverState(bool immediate = false)
        {
            _isHovered = false;
            _forceHover = false;

            if (immediate)
            {
                foreach (var effect in _activeEffects)
                {
                    effect.SetStateImmediate(false);
                }
            }
            else
            {
                foreach (var effect in _activeEffects)
                {
                    effect.OnHoverExit();
                }
            }
        }

        /// <summary>
        /// Set preset at runtime
        /// </summary>
        public void SetPreset(HoverEffectPreset preset, bool reinitialize = true)
        {
            _preset = preset;
            _useOverrides = false;

            if (reinitialize && _isInitialized)
            {
                CleanupEffects();
                InitializeEffects();
            }
        }

        /// <summary>
        /// Add an effect at runtime
        /// </summary>
        public void AddEffect(HoverEffectBase effect)
        {
            if (effect == null) return;

            #if UNITY_EDITOR
            Debug.Log($"[HoverEffectController] AddEffect '{effect.EffectId}' to {gameObject.name}, TargetVisuals={_targetVisuals?.name}");
            #endif

            effect.Initialize(this);
            _activeEffects.Add(effect);

            // Apply current state
            if (EffectiveHover)
            {
                effect.SetStateImmediate(true);
            }
        }

        /// <summary>
        /// Remove an effect by ID
        /// </summary>
        public bool RemoveEffect(string effectId)
        {
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                if (_activeEffects[i].EffectId == effectId)
                {
                    _activeEffects[i].Cleanup();
                    _activeEffects.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Get effect by ID
        /// </summary>
        public IHoverEffect GetEffect(string effectId)
        {
            foreach (var effect in _activeEffects)
            {
                if (effect.EffectId == effectId)
                    return effect;
            }
            return null;
        }

        /// <summary>
        /// Check if controller has a specific effect type
        /// </summary>
        public bool HasEffect(string effectId)
        {
            return GetEffect(effectId) != null;
        }

        #endregion

        private void CleanupEffects()
        {
            foreach (var effect in _activeEffects)
            {
                effect.Cleanup();
            }
            _activeEffects.Clear();
        }

        void OnDestroy()
        {
            CleanupEffects();
        }
    }
}
