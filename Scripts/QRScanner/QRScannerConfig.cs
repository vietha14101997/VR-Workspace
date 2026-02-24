using UnityEngine;

namespace VRWorkspace.QRScanner
{
    /// <summary>
    /// Data class để parse JSON config từ QR code
    /// Format: {"ip":"192.168.1.10","port":"8288","usbIP":"192.168.42.1"}
    /// </summary>
    [System.Serializable]
    public class QRScannerConfig
    {
        public string ip;       // WiFi IP
        public string port;     // Port (string for JsonUtility compatibility)
        public string usbIP;    // USB Tethering IP (optional)

        /// <summary>
        /// Check if USB Tethering IP is available.
        /// </summary>
        public bool HasUsbIP => !string.IsNullOrEmpty(usbIP);

        /// <summary>
        /// Parse JSON string thành QRScannerConfig
        /// </summary>
        public static QRScannerConfig FromJson(string json)
        {
            try
            {
                return JsonUtility.FromJson<QRScannerConfig>(json);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"QRScannerConfig.FromJson error: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Kiểm tra config có hợp lệ không (ít nhất phải có ip và port)
        /// </summary>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(port);
        }

        public override string ToString()
        {
            string usbInfo = HasUsbIP ? $", usbIP={usbIP}" : "";
            return $"QRConfig[ip={ip}, port={port}{usbInfo}]";
        }
    }

}
