using UnityEngine;

/// <summary>
/// Data class để parse JSON config từ QR code
/// Format: {"ip":"192.168.1.10","port":8288,"usbIP":"192.168.42.1","monitors":[...]}
/// </summary>
[System.Serializable]
public class QRScannerConfig
{
    public string host;        // WiFi IP (also "ip" in QR data)
    public string ip;          // Alternative field name for host
    public string port;
    public string usbIP;       // USB Tethering IP (for full TCP+UDP over USB cable)
    public string resolution;  // "1920x1080", "1600x900", etc.
    public string bitrate;     // "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps"
    public string fps;         // "30 FPS", "45 FPS", "60 FPS"
    public int monitors;       // 1, 2, 3

    /// <summary>
    /// Get the effective host (supports both "host" and "ip" field names).
    /// </summary>
    public string GetHost() => !string.IsNullOrEmpty(host) ? host : ip;

    /// <summary>
    /// Check if USB Tethering IP is available.
    /// </summary>
    public bool HasUsbTetheringIP => !string.IsNullOrEmpty(usbIP);

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
    /// Kiểm tra config có hợp lệ không (ít nhất phải có host/ip và port)
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(GetHost()) && !string.IsNullOrEmpty(port);
    }

    /// <summary>
    /// Tạo JSON string từ config
    /// </summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    public override string ToString()
    {
        string usbInfo = HasUsbTetheringIP ? $", usbIP={usbIP}" : "";
        return $"QRConfig[host={GetHost()}, port={port}{usbInfo}, resolution={resolution}, bitrate={bitrate}, fps={fps}, monitors={monitors}]";
    }
}
