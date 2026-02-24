using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRWorkspace.UI.RTT;
using VRWorkspace.UI.RTT.Input;

namespace VRWorkspace.UI.RTT.Debug
{
    /// <summary>
    /// Debug overlay for RTT system.
    /// Shows performance statistics, panel information, and current raycast state.
    /// </summary>
    public class RTTDebugOverlay : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private bool showOnStart = false;
        [SerializeField] private KeyCode toggleKey = KeyCode.F12;

        [Header("Style")]
        [SerializeField] private int fontSize = 14;
        [SerializeField] private Color backgroundColor = new Color(0, 0, 0, 0.7f);
        [SerializeField] private Color textColor = Color.white;

        private Canvas _canvas;
        private GameObject _overlayPanel;
        private TextMeshProUGUI _statsText;
        private bool _isVisible;

        private float _updateInterval = 0.5f;
        private float _lastUpdateTime;

        void Start()
        {
            CreateOverlay();
            SetVisible(showOnStart);
        }

        void Update()
        {
            // Toggle with key
            if (Input.GetKeyDown(toggleKey))
            {
                SetVisible(!_isVisible);
            }

            // Update stats periodically
            if (_isVisible && Time.unscaledTime - _lastUpdateTime >= _updateInterval)
            {
                _lastUpdateTime = Time.unscaledTime;
                UpdateStats();
            }
        }

        private void CreateOverlay()
        {
            // Create Canvas
            var canvasGO = new GameObject("RTTDebugCanvas");
            canvasGO.transform.SetParent(transform);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10000;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Create Panel
            _overlayPanel = new GameObject("DebugPanel");
            _overlayPanel.transform.SetParent(canvasGO.transform, false);

            var panelImage = _overlayPanel.AddComponent<Image>();
            panelImage.color = backgroundColor;
            panelImage.raycastTarget = false;

            var panelRT = _overlayPanel.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0, 1);
            panelRT.anchorMax = new Vector2(0, 1);
            panelRT.pivot = new Vector2(0, 1);
            panelRT.sizeDelta = new Vector2(350, 200);
            panelRT.anchoredPosition = new Vector2(10, -10);

            // Create Text
            var textGO = new GameObject("StatsText");
            textGO.transform.SetParent(_overlayPanel.transform, false);

            _statsText = textGO.AddComponent<TextMeshProUGUI>();
            _statsText.fontSize = fontSize;
            _statsText.color = textColor;
            _statsText.alignment = TextAlignmentOptions.TopLeft;
            _statsText.text = "RTT Debug Overlay";

            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10, 10);
            textRT.offsetMax = new Vector2(-10, -10);
        }

        private void UpdateStats()
        {
            if (_statsText == null) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>RTT Debug Overlay</b>");
            sb.AppendLine($"Press {toggleKey} to toggle");
            sb.AppendLine("─────────────────────");

            // RTTManager stats
            if (RTTManager.Instance != null)
            {
                var stats = RTTManager.Instance.GetPerformanceStats();
                sb.AppendLine($"<color=#00ffff>Panels:</color> {stats.visiblePanels}/{stats.totalPanels}");
                sb.AppendLine($"<color=#00ffff>Memory:</color> {stats.totalTextureMemoryMB:F1} MB");
                sb.AppendLine($"<color=#00ffff>Quality:</color> {stats.currentQuality}");
            }
            else
            {
                sb.AppendLine("<color=yellow>RTTManager: Not found</color>");
            }

            sb.AppendLine("─────────────────────");

            // RTTRaycastManager stats
            if (RTTRaycastManager.Instance != null)
            {
                var hit = RTTRaycastManager.Instance.CurrentHit;
                if (hit.isValid)
                {
                    sb.AppendLine($"<color=#ff00ff>Hit Panel:</color> {(hit.panel != null ? hit.panel.name : "null")}");
                    sb.AppendLine($"<color=#ff00ff>Hit UI:</color> {(hit.hitUIElement != null ? hit.hitUIElement.name : "none")}");
                    sb.AppendLine($"<color=#ff00ff>UV:</color> ({hit.uvCoordinate.x:F2}, {hit.uvCoordinate.y:F2})");
                    sb.AppendLine($"<color=#ff00ff>Distance:</color> {hit.distance:F2}m");
                }
                else
                {
                    sb.AppendLine("<color=#888888>No RTT hit</color>");
                }
            }
            else
            {
                sb.AppendLine("<color=yellow>RTTRaycastManager: Not found</color>");
            }

            sb.AppendLine("─────────────────────");
            sb.AppendLine($"<color=#888888>FPS: {(1f / Time.unscaledDeltaTime):F0}</color>");

            _statsText.text = sb.ToString();
        }

        public void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (_overlayPanel != null)
                _overlayPanel.SetActive(visible);
        }

        public void Toggle()
        {
            SetVisible(!_isVisible);
        }
    }

}
