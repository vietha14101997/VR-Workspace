using UnityEngine;

namespace VRWorkspace.UI.HoverEffects
{
    /// <summary>
    /// Factory class for creating common hover effect configurations.
    /// Use this to programmatically add hover effects to UI elements.
    /// </summary>
    public static class HoverEffectFactory
    {
        #region Effect Presets (Configurations)

        /// <summary>
        /// Standard button hover: glow border + scale + z-pop + background
        /// </summary>
        public static void AddButtonEffects(HoverEffectController controller, float scale = 1.05f, float popAmount = 0.1f)
        {
            controller.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true)
                .WithBorderMultiplier(1f));

            controller.AddEffect(new ScaleHoverEffect()
                .WithHoverScale(scale));

            controller.AddEffect(new ZPopHoverEffect()
                .WithPopAmount(popAmount));

            controller.AddEffect(new BackgroundHoverEffect());
        }

        /// <summary>
        /// Input field hover: glow border + text cursor
        /// </summary>
        public static void AddInputFieldEffects(HoverEffectController controller)
        {
            controller.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true));

            controller.AddEffect(new CursorChangeHoverEffect()
                .WithResourcePath("icon_text_cursor"));

            controller.AddEffect(new BackgroundHoverEffect());
        }

        /// <summary>
        /// Dropdown option hover: glow border with alpha fade
        /// </summary>
        public static void AddDropdownOptionEffects(HoverEffectController controller)
        {
            controller.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(false)
                .WithAlphaFade(true));
        }

        /// <summary>
        /// Keyboard key hover: enhanced glow with 2x intensity
        /// </summary>
        public static void AddKeyboardKeyEffects(HoverEffectController controller)
        {
            controller.AddEffect(new GlowBorderHoverEffect()
                .WithShaderSwap(true)
                .WithGlowIntensity(5f)
                .WithBorderMultiplier(1.5f));
        }

        /// <summary>
        /// File grid item hover: background color overlay
        /// </summary>
        public static void AddFileGridItemEffects(HoverEffectController controller)
        {
            controller.AddEffect(new ColorHoverEffect()
                .WithTargetChild("Background")
                .WithHoverColor(new Color(0f, 0f, 0f, 0.27f)));
        }

        /// <summary>
        /// Pagination button hover: glassmorphism color change
        /// </summary>
        public static void AddPaginationButtonEffects(HoverEffectController controller, Color hoverColorA, Color hoverColorB)
        {
            // This would require a custom effect for glassmorphism materials
            // For now, use the color effect
            controller.AddEffect(new ColorHoverEffect()
                .WithTargetChild("")
                .WithHoverColor(hoverColorA)
                .WithAlphaOnly(true));
        }

        #endregion

        #region Quick Setup Methods

        /// <summary>
        /// Add HoverEffectController to a GameObject with button preset
        /// </summary>
        public static HoverEffectController SetupButton(GameObject target, Transform visualsTarget = null)
        {
            var controller = target.GetComponent<HoverEffectController>();
            if (controller == null)
            {
                controller = target.AddComponent<HoverEffectController>();
            }

            // Set target visuals if provided
            if (visualsTarget != null)
            {
                SetTargetVisuals(controller, visualsTarget);
            }

            AddButtonEffects(controller);
            return controller;
        }

        /// <summary>
        /// Add HoverEffectController to a GameObject with input field preset
        /// </summary>
        public static HoverEffectController SetupInputField(GameObject target, Transform visualsTarget = null)
        {
            var controller = target.GetComponent<HoverEffectController>();
            if (controller == null)
            {
                controller = target.AddComponent<HoverEffectController>();
            }

            if (visualsTarget != null)
            {
                SetTargetVisuals(controller, visualsTarget);
            }

            AddInputFieldEffects(controller);
            return controller;
        }

        /// <summary>
        /// Add HoverEffectController to a GameObject with dropdown option preset
        /// </summary>
        public static HoverEffectController SetupDropdownOption(GameObject target, Transform visualsTarget = null)
        {
            var controller = target.GetComponent<HoverEffectController>();
            if (controller == null)
            {
                controller = target.AddComponent<HoverEffectController>();
            }

            if (visualsTarget != null)
            {
                SetTargetVisuals(controller, visualsTarget);
            }

            AddDropdownOptionEffects(controller);
            return controller;
        }

        /// <summary>
        /// Add HoverEffectController to a GameObject with keyboard key preset
        /// </summary>
        public static HoverEffectController SetupKeyboardKey(GameObject target, Transform visualsTarget = null)
        {
            var controller = target.GetComponent<HoverEffectController>();
            if (controller == null)
            {
                controller = target.AddComponent<HoverEffectController>();
            }

            if (visualsTarget != null)
            {
                SetTargetVisuals(controller, visualsTarget);
            }

            AddKeyboardKeyEffects(controller);
            return controller;
        }

        /// <summary>
        /// Add HoverEffectController with custom effects
        /// </summary>
        public static HoverEffectController SetupCustom(GameObject target, Transform visualsTarget, params HoverEffectBase[] effects)
        {
            var controller = target.GetComponent<HoverEffectController>();
            if (controller == null)
            {
                controller = target.AddComponent<HoverEffectController>();
            }

            if (visualsTarget != null)
            {
                SetTargetVisuals(controller, visualsTarget);
            }

            foreach (var effect in effects)
            {
                controller.AddEffect(effect);
            }

            return controller;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Set the target visuals for a controller using reflection (since _targetVisuals is private)
        /// </summary>
        private static void SetTargetVisuals(HoverEffectController controller, Transform target)
        {
            var field = typeof(HoverEffectController).GetField("_targetVisuals",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(controller, target);
            }
        }

        /// <summary>
        /// Create a preset ScriptableObject at runtime (for Editor use)
        /// </summary>
        public static HoverEffectPreset CreatePreset(string name, params HoverEffectBase[] effects)
        {
            return HoverEffectPreset.Create(name, effects);
        }

        #endregion

        #region Preset Creation Helpers

        /// <summary>
        /// Create a button hover preset
        /// </summary>
        public static HoverEffectPreset CreateButtonPreset()
        {
            return HoverEffectPreset.Create("Button",
                new GlowBorderHoverEffect().WithShaderSwap(true).WithBorderMultiplier(1f),
                new ScaleHoverEffect().WithHoverScale(1.05f),
                new ZPopHoverEffect().WithPopAmount(0.1f),
                new BackgroundHoverEffect()
            );
        }

        /// <summary>
        /// Create an input field hover preset
        /// </summary>
        public static HoverEffectPreset CreateInputFieldPreset()
        {
            return HoverEffectPreset.Create("InputField",
                new GlowBorderHoverEffect().WithShaderSwap(true),
                new CursorChangeHoverEffect().WithResourcePath("icon_text_cursor"),
                new BackgroundHoverEffect()
            );
        }

        /// <summary>
        /// Create a dropdown option hover preset
        /// </summary>
        public static HoverEffectPreset CreateDropdownOptionPreset()
        {
            return HoverEffectPreset.Create("DropdownOption",
                new GlowBorderHoverEffect().WithShaderSwap(false).WithAlphaFade(true)
            );
        }

        /// <summary>
        /// Create a keyboard key hover preset
        /// </summary>
        public static HoverEffectPreset CreateKeyboardKeyPreset()
        {
            return HoverEffectPreset.Create("KeyboardKey",
                new GlowBorderHoverEffect()
                    .WithShaderSwap(true)
                    .WithGlowIntensity(5f)
                    .WithBorderMultiplier(1.5f)
            );
        }

        #endregion
    }
}
