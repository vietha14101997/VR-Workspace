using UnityEngine;

namespace VRWorkspace.UI.Config
{
    /// <summary>
    /// Centralized constants for UI components.
    /// Replaces hardcoded magic numbers throughout the codebase.
    /// </summary>
    public static class UIConstants
    {
        #region Default Colors

        /// <summary>
        /// Default primary color (Cyan): RGB(0, 0.9, 1)
        /// </summary>
        public static readonly Color DefaultPrimaryColor = new Color(0f, 0.9f, 1f);

        /// <summary>
        /// Default accent color (Purple): RGB(0.76, 0.36, 1)
        /// </summary>
        public static readonly Color DefaultAccentColor = new Color(0.76f, 0.36f, 1f);

        /// <summary>
        /// Default connect button colors
        /// </summary>
        public static readonly Color DefaultConnectColorA = new Color(0.2f, 0.9f, 1f);   // Cyan
        public static readonly Color DefaultConnectColorB = new Color(0.1f, 0.4f, 0.8f); // Deep Sea Blue
        public static readonly Color DefaultConnectColorC = new Color(0.7f, 0.3f, 1f);   // Purple

        /// <summary>
        /// Button active color (when toggle is on)
        /// </summary>
        public static readonly Color DefaultButtonActiveColor = new Color(0.9f, 0.3f, 1f);

        /// <summary>
        /// Button inactive color (when toggle is off)
        /// </summary>
        public static readonly Color DefaultButtonInactiveColor = new Color(0f, 0.9f, 1f);

        #endregion

        #region Button Defaults

        /// <summary>
        /// Default button corner radius (normalized, 0-1)
        /// </summary>
        public const float ButtonCornerRadius = 0.12f;

        /// <summary>
        /// Default button edge padding (normalized, 0-1)
        /// </summary>
        public const float ButtonEdgePadding = 0.06f;

        /// <summary>
        /// Default button background alpha
        /// </summary>
        public const float ButtonBackgroundAlpha = 0.08f;

        /// <summary>
        /// Default button border width (normalized)
        /// </summary>
        public const float ButtonBorderWidth = 0.025f;

        /// <summary>
        /// Default button glow width (normalized)
        /// </summary>
        public const float ButtonGlowWidth = 0.04f;

        /// <summary>
        /// Default button glow intensity
        /// </summary>
        public const float ButtonGlowIntensity = 2.5f;

        /// <summary>
        /// Default button pop amount (Z-depth change on hover)
        /// </summary>
        public const float ButtonPopAmount = 0.05f;

        /// <summary>
        /// Default button hover scale amount (5% = 0.05)
        /// </summary>
        public const float ButtonHoverScaleAmount = 0.05f;

        /// <summary>
        /// Default button font size
        /// </summary>
        public const int ButtonDefaultFontSize = 40;

        /// <summary>
        /// Default button width
        /// </summary>
        public const float ButtonDefaultWidth = 200f;

        /// <summary>
        /// Default button height
        /// </summary>
        public const float ButtonDefaultHeight = 80f;

        /// <summary>
        /// Default icon size for icon-only buttons (ratio of button size)
        /// </summary>
        public const float ButtonIconSizeRatio = 0.55f;

        /// <summary>
        /// Default icon padding for buttons
        /// </summary>
        public const float ButtonIconPadding = 28f;

        /// <summary>
        /// Default spacing between icon and text
        /// </summary>
        public const float ButtonSpacing = 18f;

        #endregion

        #region Input Field Defaults

        /// <summary>
        /// Font size to box height ratio for input fields
        /// </summary>
        public const float InputFontToBoxRatio = 2.2f;

        /// <summary>
        /// Fixed label height for input fields
        /// </summary>
        public const float InputLabelHeight = 32f;

        /// <summary>
        /// Horizontal padding for input field text
        /// </summary>
        public const float InputHorizontalPadding = 30f;

        /// <summary>
        /// Vertical padding for input field text
        /// </summary>
        public const float InputVerticalPadding = 8f;

        /// <summary>
        /// Default input field border width (thicker than buttons)
        /// </summary>
        public const float InputBorderWidth = 0.09f;

        /// <summary>
        /// Default input field pop amount
        /// </summary>
        public const float InputPopAmount = 0.005f;

        /// <summary>
        /// Default input font size
        /// </summary>
        public const int InputDefaultFontSize = 36;

        /// <summary>
        /// Default label font size
        /// </summary>
        public const int InputDefaultLabelFontSize = 24;

        #endregion

        #region Dropdown Defaults

        /// <summary>
        /// Font size to box height ratio for dropdowns
        /// </summary>
        public const float DropdownFontToBoxRatio = 4.2f;

        /// <summary>
        /// Font size to icon size ratio for dropdowns
        /// </summary>
        public const float DropdownFontToIconRatio = 2.5f;

        /// <summary>
        /// Icon zone ratio (portion of width for icon area)
        /// </summary>
        public const float DropdownIconZoneRatio = 0.28f;

        /// <summary>
        /// Content padding for dropdowns
        /// </summary>
        public const float DropdownContentPadding = 12f;

        /// <summary>
        /// Arrow width for dropdowns
        /// </summary>
        public const float DropdownArrowWidth = 70f;

        /// <summary>
        /// Content left offset (percentage)
        /// </summary>
        public const float DropdownContentLeftOffset = 0.05f;

        /// <summary>
        /// Arrow right offset (percentage)
        /// </summary>
        public const float DropdownArrowRightOffset = 0.05f;

        /// <summary>
        /// Default dropdown border width
        /// </summary>
        public const float DropdownBorderWidth = 0.04f;

        /// <summary>
        /// Default dropdown pop amount
        /// </summary>
        public const float DropdownPopAmount = 0.005f;

        /// <summary>
        /// Maximum visible options in dropdown panel
        /// </summary>
        public const int DropdownMaxVisibleOptions = 5;

        #endregion

        #region Animation Defaults

        /// <summary>
        /// Default hover scale for RTT panels (where Z-pop doesn't work)
        /// </summary>
        public const float DefaultHoverScale = 1.05f;

        /// <summary>
        /// Bare icon button hover scale (higher for RTT panels)
        /// </summary>
        public const float BareIconHoverScale = 1.15f;

        /// <summary>
        /// Default hover animation speed
        /// </summary>
        public const float DefaultHoverAnimSpeed = 12f;

        /// <summary>
        /// Default pulse animation speed
        /// </summary>
        public const float DefaultPulseSpeed = 2f;

        /// <summary>
        /// Marquee scroll speed for text
        /// </summary>
        public const float MarqueeScrollSpeed = 80f;

        #endregion

        #region Grid/Thumbnail Defaults

        /// <summary>
        /// Default thumbnail size for grid items
        /// </summary>
        public const int ThumbnailSize = 512;

        /// <summary>
        /// Grid item hover scale
        /// </summary>
        public const float GridItemHoverScale = 1.05f;

        /// <summary>
        /// Grid item hover animation speed
        /// </summary>
        public const float GridItemHoverAnimSpeed = 12f;

        #endregion

        #region Glow Effect Defaults

        /// <summary>
        /// Default inner shadow distance for icon glow
        /// </summary>
        public const float GlowInnerDistance = 2f;

        /// <summary>
        /// Default outer shadow distance for bloom glow
        /// </summary>
        public const float GlowOuterDistance = 5f;

        /// <summary>
        /// Default inner glow alpha
        /// </summary>
        public const float GlowInnerAlpha = 0.4f;

        /// <summary>
        /// Default outer glow alpha (bloom)
        /// </summary>
        public const float GlowOuterAlpha = 0.15f;

        /// <summary>
        /// Default text glow distance
        /// </summary>
        public const float TextGlowDistance = 3f;

        /// <summary>
        /// Default text glow alpha
        /// </summary>
        public const float TextGlowAlpha = 0.55f;

        /// <summary>
        /// White mix ratio for glow color (0.7 = 70% towards white)
        /// </summary>
        public const float GlowWhiteMix = 0.7f;

        /// <summary>
        /// White mix ratio for icon tint (0.9 = 90% towards white)
        /// </summary>
        public const float IconTintWhiteMix = 0.9f;

        #endregion

        #region Layer Names

        /// <summary>
        /// Default layer name for VR UI elements
        /// </summary>
        public const string VirtualObjectsLayer = "VirtualObjects";

        #endregion

        #region BoxCollider Defaults

        /// <summary>
        /// Default BoxCollider depth (Z size)
        /// </summary>
        public const float ColliderDepth = 0.1f;

        /// <summary>
        /// Default BoxCollider Z offset (center)
        /// </summary>
        public const float ColliderZOffset = -0.1f;

        #endregion
    }
}
