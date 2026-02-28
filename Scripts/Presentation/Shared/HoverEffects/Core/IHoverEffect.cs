using UnityEngine;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Interface for all hover effects
    /// </summary>
    public interface IHoverEffect
    {
        /// <summary>
        /// Unique identifier for this effect type
        /// </summary>
        string EffectId { get; }

        /// <summary>
        /// Is this effect currently in hover state
        /// </summary>
        bool IsHovered { get; }

        /// <summary>
        /// Initialize the effect with its controller
        /// </summary>
        void Initialize(HoverEffectController controller);

        /// <summary>
        /// Called when pointer enters
        /// </summary>
        void OnHoverEnter();

        /// <summary>
        /// Called when pointer exits
        /// </summary>
        void OnHoverExit();

        /// <summary>
        /// Called every frame to update effect interpolation
        /// </summary>
        /// <param name="deltaTime">Time since last frame (unscaled)</param>
        void UpdateEffect(float deltaTime);

        /// <summary>
        /// Force immediate state change without transition
        /// </summary>
        void SetStateImmediate(bool hovered);

        /// <summary>
        /// Clean up resources when effect is destroyed
        /// </summary>
        void Cleanup();
    }
}
