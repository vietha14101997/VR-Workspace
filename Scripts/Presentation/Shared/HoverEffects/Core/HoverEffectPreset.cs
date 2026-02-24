using UnityEngine;
using System.Collections.Generic;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// ScriptableObject that stores a collection of hover effect configurations.
    /// Can be reused across multiple UI elements.
    /// </summary>
    [CreateAssetMenu(fileName = "HoverPreset", menuName = "VR-Workspace/Hover Effect Preset")]
    public class HoverEffectPreset : ScriptableObject
    {
        [Header("Preset Info")]
        [SerializeField] private string _presetName = "Default";
        [SerializeField, TextArea(2, 4)] private string _description = "";

        [Header("Effects")]
        [SerializeReference] private List<HoverEffectBase> _effects = new List<HoverEffectBase>();

        // Public properties
        public string PresetName => _presetName;
        public string Description => _description;
        public IReadOnlyList<HoverEffectBase> Effects => _effects;

        /// <summary>
        /// Create a preset with the given effects
        /// </summary>
        public static HoverEffectPreset Create(string name, params HoverEffectBase[] effects)
        {
            var preset = CreateInstance<HoverEffectPreset>();
            preset._presetName = name;
            preset._effects = new List<HoverEffectBase>(effects);
            return preset;
        }

        /// <summary>
        /// Add an effect to this preset
        /// </summary>
        public void AddEffect(HoverEffectBase effect)
        {
            if (effect != null && !_effects.Contains(effect))
            {
                _effects.Add(effect);
            }
        }

        /// <summary>
        /// Remove an effect from this preset
        /// </summary>
        public bool RemoveEffect(HoverEffectBase effect)
        {
            return _effects.Remove(effect);
        }

        /// <summary>
        /// Clear all effects
        /// </summary>
        public void ClearEffects()
        {
            _effects.Clear();
        }

        /// <summary>
        /// Check if preset has a specific effect type
        /// </summary>
        public bool HasEffect(string effectId)
        {
            foreach (var effect in _effects)
            {
                if (effect != null && effect.EffectId == effectId)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get effect by ID
        /// </summary>
        public HoverEffectBase GetEffect(string effectId)
        {
            foreach (var effect in _effects)
            {
                if (effect != null && effect.EffectId == effectId)
                    return effect;
            }
            return null;
        }
    }

    /// <summary>
    /// Extension methods for HoverEffectBase to support cloning
    /// </summary>
    public static class HoverEffectExtensions
    {
        /// <summary>
        /// Create a deep clone of the effect for runtime use
        /// </summary>
        public static HoverEffectBase Clone(this HoverEffectBase effect)
        {
            if (effect == null) return null;

            // Use JSON serialization for deep clone
            string json = JsonUtility.ToJson(effect);
            var clone = (HoverEffectBase)System.Activator.CreateInstance(effect.GetType());
            JsonUtility.FromJsonOverwrite(json, clone);
            return clone;
        }
    }
}
