using UnityEngine;

namespace VRWorkspace.UI.RTT
{
    /// <summary>
    /// Feature toggle for RTT (Render-to-Texture) system.
    /// Allows enabling/disabling RTT via PlayerPrefs for A/B testing and fallback.
    /// </summary>
    public static class RTTFeatureToggle
    {
        private const string PREF_KEY = "VRWorkspace_UseRTT";
        private const bool DEFAULT_VALUE = true;

        private static bool? _cachedValue = null;

        /// <summary>
        /// Get or set whether RTT is enabled.
        /// </summary>
        public static bool UseRTT
        {
            get
            {
                if (!_cachedValue.HasValue)
                {
                    _cachedValue = PlayerPrefs.GetInt(PREF_KEY, DEFAULT_VALUE ? 1 : 0) == 1;
                }
                return _cachedValue.Value;
            }
            set
            {
                _cachedValue = value;
                PlayerPrefs.SetInt(PREF_KEY, value ? 1 : 0);
                PlayerPrefs.Save();

                Debug.Log($"[RTTFeatureToggle] RTT {(value ? "enabled" : "disabled")}");

                // Notify any listeners
                OnRTTToggled?.Invoke(value);
            }
        }

        /// <summary>
        /// Event fired when RTT is toggled.
        /// </summary>
        public static event System.Action<bool> OnRTTToggled;

        /// <summary>
        /// Toggle RTT on/off.
        /// </summary>
        public static void Toggle()
        {
            UseRTT = !UseRTT;
        }

        /// <summary>
        /// Reset to default value.
        /// </summary>
        public static void ResetToDefault()
        {
            UseRTT = DEFAULT_VALUE;
        }

        /// <summary>
        /// Clear cached value (useful for testing).
        /// </summary>
        public static void ClearCache()
        {
            _cachedValue = null;
        }
    }

    /// <summary>
    /// MonoBehaviour wrapper for RTTFeatureToggle.
    /// Attach to a GameObject to expose toggle in Inspector.
    /// </summary>
    public class RTTFeatureToggleComponent : MonoBehaviour
    {
        [Header("RTT Feature Toggle")]
        [SerializeField] private bool useRTT = true;

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                RTTFeatureToggle.UseRTT = useRTT;
            }
        }

        private void Awake()
        {
            // Sync with current setting
            useRTT = RTTFeatureToggle.UseRTT;
        }

        public void SetUseRTT(bool value)
        {
            useRTT = value;
            RTTFeatureToggle.UseRTT = value;
        }

        public void ToggleRTT()
        {
            RTTFeatureToggle.Toggle();
            useRTT = RTTFeatureToggle.UseRTT;
        }
    }

}
