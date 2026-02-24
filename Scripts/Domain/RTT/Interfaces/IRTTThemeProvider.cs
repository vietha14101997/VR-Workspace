using System;
using UnityEngine;
using TMPro;

namespace VRWorkspace.Domain.RTT
{
    /// <summary>
    /// Theme and font provider interface. Extracted from RTTManager.
    /// </summary>
    public interface IRTTThemeProvider
    {
        Color PrimaryColor { get; }
        Color AccentColor { get; }
        TMP_FontAsset Font { get; }
        event Action OnThemeChanged;
        event Action OnFontChanged;
    }
}
