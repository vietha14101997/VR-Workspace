using UnityEngine;
using TMPro;

namespace VRWorkspace.UI.Config
{
    /// <summary>
    /// Base configuration class for all UI components.
    /// Contains common visual settings shared across buttons, input fields, and dropdowns.
    /// </summary>
    [System.Serializable]
    public class UIComponentConfig
    {
        #region Colors

        /// <summary>
        /// Theme color for the component
        /// </summary>
        public Color themeColor = UIConstants.DefaultPrimaryColor;

        /// <summary>
        /// If true, use colors from RTTThemeConfig instead of themeColor
        /// </summary>
        public bool useThemeColors = false;

        #endregion

        #region Dimensions

        /// <summary>
        /// Component width in pixels
        /// </summary>
        public float width = UIConstants.ButtonDefaultWidth;

        /// <summary>
        /// Component height in pixels (may be auto-calculated for some components)
        /// </summary>
        public float height = UIConstants.ButtonDefaultHeight;

        /// <summary>
        /// Font size for main text
        /// </summary>
        public int fontSize = UIConstants.ButtonDefaultFontSize;

        /// <summary>
        /// Font asset (null uses default)
        /// </summary>
        public TMP_FontAsset font;

        #endregion

        #region Visual Style

        /// <summary>
        /// Corner radius for rounded corners (normalized, 0-1)
        /// </summary>
        public float cornerRadius = UIConstants.ButtonCornerRadius;

        /// <summary>
        /// Edge padding for shader effects (normalized, 0-1)
        /// </summary>
        public float edgePadding = UIConstants.ButtonEdgePadding;

        /// <summary>
        /// Background opacity (0-1)
        /// </summary>
        public float backgroundAlpha = UIConstants.ButtonBackgroundAlpha;

        /// <summary>
        /// Border width for glow border (normalized)
        /// </summary>
        public float borderWidth = UIConstants.ButtonBorderWidth;

        /// <summary>
        /// Glow width around border (normalized)
        /// </summary>
        public float glowWidth = UIConstants.ButtonGlowWidth;

        /// <summary>
        /// Glow intensity multiplier
        /// </summary>
        public float glowIntensity = UIConstants.ButtonGlowIntensity;

        #endregion

        #region Animation

        /// <summary>
        /// Z-depth pop amount on hover
        /// </summary>
        public float popAmount = UIConstants.ButtonPopAmount;

        /// <summary>
        /// Scale increase on hover (0.05 = 5%)
        /// </summary>
        public float hoverScaleAmount = UIConstants.ButtonHoverScaleAmount;

        /// <summary>
        /// Enable pulse animation
        /// </summary>
        public bool enablePulse = false;

        /// <summary>
        /// Pulse animation speed
        /// </summary>
        public float pulseSpeed = UIConstants.DefaultPulseSpeed;

        #endregion

        #region Layer

        /// <summary>
        /// Layer name for VR raycast
        /// </summary>
        public string layerName = UIConstants.VirtualObjectsLayer;

        #endregion

        #region Computed Properties

        /// <summary>
        /// Calculates aspect ratio (width/height)
        /// </summary>
        public float Aspect => width / height;

        #endregion
    }

    /// <summary>
    /// Button-specific configuration extending base config.
    /// </summary>
    [System.Serializable]
    public class ButtonComponentConfig : UIComponentConfig
    {
        #region Button-Specific

        /// <summary>
        /// Button label text
        /// </summary>
        public string label = "Button";

        /// <summary>
        /// Button icon sprite
        /// </summary>
        public Sprite icon;

        /// <summary>
        /// Show only icon (no text)
        /// </summary>
        public bool iconOnly = false;

        /// <summary>
        /// Show only text (no icon)
        /// </summary>
        public bool textOnly = false;

        /// <summary>
        /// Horizontal layout (icon left, text right)
        /// </summary>
        public bool horizontalLayout = false;

        /// <summary>
        /// Icon size in pixels
        /// </summary>
        public float iconSize = 44f;

        /// <summary>
        /// Padding around icon
        /// </summary>
        public float iconPadding = UIConstants.ButtonIconPadding;

        /// <summary>
        /// Spacing between icon and text
        /// </summary>
        public float spacing = UIConstants.ButtonSpacing;

        /// <summary>
        /// Frameless mode (no background/border, just icon)
        /// </summary>
        public bool frameless = false;

        /// <summary>
        /// Use special connect button shader
        /// </summary>
        public bool useConnectButtonShader = false;

        /// <summary>
        /// Connect button gradient color A
        /// </summary>
        public Color connectColorA = UIConstants.DefaultConnectColorA;

        /// <summary>
        /// Connect button gradient color B
        /// </summary>
        public Color connectColorB = UIConstants.DefaultConnectColorB;

        /// <summary>
        /// Connect button gradient color C
        /// </summary>
        public Color connectColorC = UIConstants.DefaultConnectColorC;

        #endregion
    }

    /// <summary>
    /// Input field-specific configuration extending base config.
    /// </summary>
    [System.Serializable]
    public class InputFieldComponentConfig : UIComponentConfig
    {
        #region Input-Specific

        /// <summary>
        /// Label text above input box
        /// </summary>
        public string label = "Label";

        /// <summary>
        /// Placeholder text when empty
        /// </summary>
        public string placeholder = "Enter text...";

        /// <summary>
        /// Default/initial value
        /// </summary>
        public string defaultValue = "";

        /// <summary>
        /// Label font size
        /// </summary>
        public int labelFontSize = UIConstants.InputDefaultLabelFontSize;

        /// <summary>
        /// Content type (standard, password, email, etc.)
        /// </summary>
        public TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard;

        /// <summary>
        /// Character limit (0 = unlimited)
        /// </summary>
        public int characterLimit = 0;

        #endregion

        #region Computed Properties

        /// <summary>
        /// Box height calculated from font size
        /// </summary>
        public float BoxHeight => fontSize * UIConstants.InputFontToBoxRatio;

        /// <summary>
        /// Total height including label
        /// </summary>
        public float TotalHeight => BoxHeight + (string.IsNullOrEmpty(label) ? 0f : UIConstants.InputLabelHeight) + 24f;

        #endregion

        /// <summary>
        /// Creates config with input field defaults
        /// </summary>
        public InputFieldComponentConfig()
        {
            borderWidth = UIConstants.InputBorderWidth;
            popAmount = UIConstants.InputPopAmount;
            fontSize = UIConstants.InputDefaultFontSize;
        }
    }

    /// <summary>
    /// Dropdown-specific configuration extending base config.
    /// </summary>
    [System.Serializable]
    public class DropdownComponentConfig : UIComponentConfig
    {
        #region Dropdown-Specific

        /// <summary>
        /// Label text
        /// </summary>
        public string label = "Label";

        /// <summary>
        /// Dropdown icon
        /// </summary>
        public Sprite icon;

        /// <summary>
        /// Label font size
        /// </summary>
        public int labelFontSize = 32;

        /// <summary>
        /// Value font size
        /// </summary>
        public int valueFontSize = 36;

        /// <summary>
        /// Option list
        /// </summary>
        public System.Collections.Generic.List<string> options = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Icons for each option (optional)
        /// </summary>
        public System.Collections.Generic.List<Sprite> optionIcons = new System.Collections.Generic.List<Sprite>();

        /// <summary>
        /// Size multipliers for option icons (optional)
        /// </summary>
        public System.Collections.Generic.List<float> optionIconSizeMultipliers = new System.Collections.Generic.List<float>();

        /// <summary>
        /// Default selected index
        /// </summary>
        public int defaultIndex = 0;

        /// <summary>
        /// Maximum visible options in dropdown panel
        /// </summary>
        public int maxVisibleOptions = UIConstants.DropdownMaxVisibleOptions;

        /// <summary>
        /// Enable glassmorphism effect
        /// </summary>
        public bool enableGlassmorphism = true;

        /// <summary>
        /// Blur intensity for glassmorphism
        /// </summary>
        public float blurIntensity = 6f;

        /// <summary>
        /// Blur quality for glassmorphism
        /// </summary>
        public int blurQuality = 8;

        #endregion

        #region Computed Properties

        /// <summary>
        /// Box height calculated from font size
        /// </summary>
        public float BoxHeight => valueFontSize * (string.IsNullOrEmpty(label) ? 1.8f : UIConstants.DropdownFontToBoxRatio);

        /// <summary>
        /// Icon size calculated from font size
        /// </summary>
        public float IconSize => valueFontSize * UIConstants.DropdownFontToIconRatio * 0.95f;

        /// <summary>
        /// Option height (75% of dropdown height)
        /// </summary>
        public float OptionHeight => BoxHeight * 0.75f;

        /// <summary>
        /// Option icon size
        /// </summary>
        public float OptionIconSize => OptionHeight * 0.5f;

        #endregion

        /// <summary>
        /// Creates config with dropdown defaults
        /// </summary>
        public DropdownComponentConfig()
        {
            borderWidth = UIConstants.DropdownBorderWidth;
            popAmount = UIConstants.DropdownPopAmount;
        }
    }
}
