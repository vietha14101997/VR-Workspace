using UnityEngine;
using TMPro;

namespace VRWorkspace.UI.RTT.Components
{
    /// <summary>
    /// Base configuration shared by all RTT popup components.
    /// Provides common visual and layout fields used across RTTPopupMenu,
    /// RTTPopupInputable, and RTTProgressPopup.
    /// </summary>
    [System.Serializable]
    public class PopupConfigBase
    {
        /// <summary>Width of the popup panel in pixels.</summary>
        public float width = 500f;

        /// <summary>Primary theme color (e.g., button highlights, progress fill).</summary>
        public Color primaryColor = new Color(0f, 0.9f, 1f);

        /// <summary>Accent/secondary theme color (e.g., close button, confirm button).</summary>
        public Color accentColor = new Color(0.76f, 0.36f, 1f);

        /// <summary>Color of the world-space dark overlay that blocks background UI.</summary>
        public Color overlayColor = new Color(0f, 0f, 0f, 0.5f);

        /// <summary>Font asset used for all text in the popup. Null uses TMP default.</summary>
        public TMP_FontAsset font;

        /// <summary>Unity layer name used when placing popup GameObjects.</summary>
        public string layerName = "VirtualObjects";
    }
}
