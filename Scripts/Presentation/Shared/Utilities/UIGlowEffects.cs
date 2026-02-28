using UnityEngine;
using UnityEngine.UI;

namespace VRWorkspace.UI.Utilities
{
    /// <summary>
    /// Utility class for applying glow and shadow effects to UI elements.
    /// Centralizes glow effect code to avoid duplication across factories.
    /// </summary>
    public static class UIGlowEffects
    {
        #region Icon Glow Effects

        /// <summary>
        /// Adds a basic 2-shadow glow effect to an icon.
        /// Creates a subtle halo around the icon.
        /// </summary>
        /// <param name="target">GameObject to add shadows to</param>
        /// <param name="baseColor">Theme color to base glow on</param>
        /// <param name="distance">Shadow offset distance (default 2f)</param>
        /// <param name="glowAlpha">Glow opacity (default 0.4f)</param>
        public static void AddIconGlow(GameObject target, Color baseColor,
            float distance = 2f, float glowAlpha = 0.4f)
        {
            Color glowCol = Color.Lerp(baseColor, Color.white, 0.7f);
            glowCol.a = glowAlpha;

            Shadow shadow1 = target.AddComponent<Shadow>();
            shadow1.effectColor = glowCol;
            shadow1.effectDistance = new Vector2(distance, -distance);

            Shadow shadow2 = target.AddComponent<Shadow>();
            shadow2.effectColor = glowCol;
            shadow2.effectDistance = new Vector2(-distance, distance);
        }

        /// <summary>
        /// Adds a 4-shadow bloom glow effect to an icon.
        /// Creates a stronger, more visible halo.
        /// </summary>
        /// <param name="target">GameObject to add shadows to</param>
        /// <param name="baseColor">Theme color to base glow on</param>
        /// <param name="innerDistance">Inner shadow distance (default 2f)</param>
        /// <param name="outerDistance">Outer shadow distance (default 5f)</param>
        /// <param name="innerAlpha">Inner glow opacity (default 0.4f)</param>
        /// <param name="outerAlpha">Outer glow opacity (default 0.15f)</param>
        public static void AddIconBloomGlow(GameObject target, Color baseColor,
            float innerDistance = 2f, float outerDistance = 5f,
            float innerAlpha = 0.4f, float outerAlpha = 0.15f)
        {
            // Inner glow (sharp)
            Color innerGlow = Color.Lerp(baseColor, Color.white, 0.7f);
            innerGlow.a = innerAlpha;

            Shadow shadow1 = target.AddComponent<Shadow>();
            shadow1.effectColor = innerGlow;
            shadow1.effectDistance = new Vector2(innerDistance, -innerDistance);

            Shadow shadow2 = target.AddComponent<Shadow>();
            shadow2.effectColor = innerGlow;
            shadow2.effectDistance = new Vector2(-innerDistance, innerDistance);

            // Outer glow (bloom)
            Color outerGlow = Color.Lerp(baseColor, Color.white, 0.8f);
            outerGlow.a = outerAlpha;

            Shadow shadow3 = target.AddComponent<Shadow>();
            shadow3.effectColor = outerGlow;
            shadow3.effectDistance = new Vector2(outerDistance, -outerDistance);

            Shadow shadow4 = target.AddComponent<Shadow>();
            shadow4.effectColor = outerGlow;
            shadow4.effectDistance = new Vector2(-outerDistance, outerDistance);
        }

        #endregion

        #region Text Glow Effects

        /// <summary>
        /// Adds a glow effect to text.
        /// </summary>
        /// <param name="target">GameObject containing TextMeshProUGUI</param>
        /// <param name="baseColor">Theme color to base glow on</param>
        /// <param name="distance">Shadow offset distance (default 3f)</param>
        /// <param name="glowAlpha">Glow opacity (default 0.55f)</param>
        public static void AddTextGlow(GameObject target, Color baseColor,
            float distance = 3f, float glowAlpha = 0.55f)
        {
            Color glowCol = Color.Lerp(baseColor, Color.white, 0.8f);
            glowCol.a = glowAlpha;

            Shadow shadow1 = target.AddComponent<Shadow>();
            shadow1.effectColor = glowCol;
            shadow1.effectDistance = new Vector2(distance, -distance);

            Shadow shadow2 = target.AddComponent<Shadow>();
            shadow2.effectColor = glowCol;
            shadow2.effectDistance = new Vector2(-distance, distance);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Removes all Shadow components from a GameObject.
        /// </summary>
        public static void RemoveAllShadows(GameObject target)
        {
            Shadow[] shadows = target.GetComponents<Shadow>();
            foreach (var shadow in shadows)
            {
                Object.Destroy(shadow);
            }
        }

        /// <summary>
        /// Updates the color of all Shadow components on a GameObject.
        /// </summary>
        /// <param name="target">GameObject with Shadow components</param>
        /// <param name="newColor">New base color</param>
        /// <param name="whiteMix">Amount of white to mix (default 0.7f)</param>
        /// <param name="alpha">New alpha value (default 0.4f)</param>
        public static void UpdateShadowColors(GameObject target, Color newColor,
            float whiteMix = 0.7f, float alpha = 0.4f)
        {
            Color glowCol = Color.Lerp(newColor, Color.white, whiteMix);
            glowCol.a = alpha;

            Shadow[] shadows = target.GetComponents<Shadow>();
            foreach (var shadow in shadows)
            {
                shadow.effectColor = glowCol;
            }
        }

        /// <summary>
        /// Creates a glow color from a base color by mixing with white.
        /// </summary>
        /// <param name="baseColor">Base color</param>
        /// <param name="whiteMix">Amount of white to mix (0-1, default 0.7f)</param>
        /// <param name="alpha">Alpha value (default 0.4f)</param>
        /// <returns>Glow color ready for use with shadows</returns>
        public static Color CreateGlowColor(Color baseColor, float whiteMix = 0.7f, float alpha = 0.4f)
        {
            Color glowCol = Color.Lerp(baseColor, Color.white, whiteMix);
            glowCol.a = alpha;
            return glowCol;
        }

        /// <summary>
        /// Creates a tinted icon color from a base theme color.
        /// Used for icon Image.color to give icons a slight color tint.
        /// </summary>
        /// <param name="baseColor">Theme color</param>
        /// <param name="whiteMix">Amount of white to mix (default 0.9f for near-white)</param>
        /// <returns>Tinted color for icon</returns>
        public static Color CreateIconTintColor(Color baseColor, float whiteMix = 0.9f)
        {
            return Color.Lerp(baseColor, Color.white, whiteMix);
        }

        #endregion
    }
}
