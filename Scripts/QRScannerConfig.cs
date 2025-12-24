using UnityEngine;

/// <summary>
/// Data class để parse JSON config từ QR code
/// Format: {"host":"192.168.1.10","port":"9000","resolution":"1920x1080","bitrate":"20 Mbps","fps":"60 FPS","monitors":1}
/// </summary>
[System.Serializable]
public class QRScannerConfig
{
    public string host;
    public string port;
    public string resolution;  // "1920x1080", "1600x900", etc.
    public string bitrate;     // "5 Mbps", "10 Mbps", "20 Mbps", "30 Mbps", "50 Mbps"
    public string fps;         // "30 FPS", "45 FPS", "60 FPS"
    public int monitors;       // 1, 2, 3

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
    /// Kiểm tra config có hợp lệ không (ít nhất phải có host và port)
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port);
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
        return $"QRConfig[host={host}, port={port}, resolution={resolution}, bitrate={bitrate}, fps={fps}, monitors={monitors}]";
    }
}
