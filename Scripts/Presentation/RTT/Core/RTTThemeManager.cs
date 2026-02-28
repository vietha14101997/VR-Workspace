using System;
using UnityEngine;
using TMPro;
using VRWorkspace.Domain.RTT;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Manages theme colors and fonts for the RTT UI system.
    /// Extracted from RTTManager for Single Responsibility.
    /// </summary>
    public class RTTThemeManager : IRTTThemeProvider
    {
        private RTTThemeConfig _themeConfig;
        private TMP_FontAsset _primaryFont;

        public event Action OnThemeChanged;
        public event Action OnFontChanged;

        public RTTThemeConfig Theme => _themeConfig;
        public TMP_FontAsset Font => _themeConfig?.font != null ? _themeConfig.font : _primaryFont;
        public Color PrimaryColor => _themeConfig?.primaryColor ?? new Color(0f, 0.9f, 1f);
        public Color AccentColor => _themeConfig?.accentColor ?? new Color(0.76f, 0.36f, 1f);

        public RTTThemeManager(RTTThemeConfig themeConfig, TMP_FontAsset primaryFont)
        {
            _themeConfig = themeConfig;
            _primaryFont = primaryFont;
        }

        public void SetTheme(RTTThemeConfig newTheme)
        {
            if (newTheme == null) return;
            _themeConfig = newTheme;
            OnThemeChanged?.Invoke();
            Debug.Log("[RTTThemeManager] Theme changed, notifying components");
        }

        public void RefreshAllThemes()
        {
            OnThemeChanged?.Invoke();
        }

        /// <summary>
        /// Handle runtime theme property changes from RTTThemeConfig.
        /// </summary>
        public void HandleThemePropertyChanged()
        {
            OnThemeChanged?.Invoke();
        }

        /// <summary>
        /// Get alternating color for menu items (primary/accent pattern).
        /// </summary>
        public Color GetAlternatingColor(int index)
        {
            return _themeConfig?.GetAlternatingColor(index) ?? (index % 2 == 0 ? PrimaryColor : AccentColor);
        }

        public void SetFont(TMP_FontAsset font)
        {
            if (font == null) return;
            _primaryFont = font;
            OnFontChanged?.Invoke();
            Debug.Log("[RTTThemeManager] Font changed, notifying components");
        }

        public void RefreshAllFonts()
        {
            OnFontChanged?.Invoke();
        }
    }
}
