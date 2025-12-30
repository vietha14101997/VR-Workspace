using UnityEngine;
using System;

namespace VRWorkspace.Utils
{
    /// <summary>
    /// Helper class to open WebView on Android.
    /// Opens URLs in system browser (Chrome) which supports full WebRTC.
    /// For Android VR with Google Cardboard, this provides access to
    /// webrtc_protocolv2.html with full WebRTC support (340 Mbps vs Unity's 65 Mbps).
    /// </summary>
    public static class AndroidWebViewHelper
    {
        /// <summary>
        /// Event fired when WebView is opened.
        /// </summary>
        public static event Action<string> OnWebViewOpened;

        /// <summary>
        /// Event fired when WebView fails to open.
        /// </summary>
        public static event Action<string> OnWebViewError;

        /// <summary>
        /// Open a URL in the system browser (Chrome on Android).
        /// This provides full WebRTC support with native performance.
        /// </summary>
        /// <param name="url">The URL to open</param>
        public static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("[AndroidWebView] URL is null or empty");
                OnWebViewError?.Invoke("URL is null or empty");
                return;
            }

            Debug.Log($"[AndroidWebView] Opening URL: {url}");

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Open in Chrome/default browser - this has full WebRTC support
                Application.OpenURL(url);
                Debug.Log($"[AndroidWebView] Opened in system browser: {url}");
                OnWebViewOpened?.Invoke(url);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidWebView] Failed to open URL: {ex.Message}");
                OnWebViewError?.Invoke(ex.Message);
            }
#else
            // On Editor/PC, open default browser
            Application.OpenURL(url);
            Debug.Log($"[AndroidWebView] Opened in default browser (Editor mode): {url}");
            OnWebViewOpened?.Invoke(url);
#endif
        }

        /// <summary>
        /// Open a URL in Chrome specifically (Android only).
        /// Falls back to default browser if Chrome is not installed.
        /// </summary>
        /// <param name="url">The URL to open</param>
        public static void OpenInChrome(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Debug.LogError("[AndroidWebView] URL is null or empty");
                OnWebViewError?.Invoke("URL is null or empty");
                return;
            }

            Debug.Log($"[AndroidWebView] Opening in Chrome: {url}");

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW"))
                using (var uri = new AndroidJavaClass("android.net.Uri").CallStatic<AndroidJavaObject>("parse", url))
                {
                    intent.Call<AndroidJavaObject>("setData", uri);

                    // Try to open with Chrome specifically
                    intent.Call<AndroidJavaObject>("setPackage", "com.android.chrome");

                    try
                    {
                        activity.Call("startActivity", intent);
                        Debug.Log($"[AndroidWebView] Opened in Chrome: {url}");
                        OnWebViewOpened?.Invoke(url);
                    }
                    catch
                    {
                        // Chrome not installed, fallback to default browser
                        intent.Call<AndroidJavaObject>("setPackage", null);
                        activity.Call("startActivity", intent);
                        Debug.Log($"[AndroidWebView] Chrome not found, opened in default browser: {url}");
                        OnWebViewOpened?.Invoke(url);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AndroidWebView] Failed to open Chrome: {ex.Message}");
                // Fallback to simple OpenURL
                Application.OpenURL(url);
                OnWebViewOpened?.Invoke(url);
            }
#else
            Application.OpenURL(url);
            Debug.Log($"[AndroidWebView] Opened in default browser (Editor mode): {url}");
            OnWebViewOpened?.Invoke(url);
#endif
        }

        /// <summary>
        /// Open webrtc_protocolv2.html from the server.
        /// </summary>
        /// <param name="serverIp">Server IP address</param>
        /// <param name="httpPort">HTTP port (usually same as WS port or +1)</param>
        public static void OpenWebRTCProtocol(string serverIp, int httpPort = 5100)
        {
            // The server serves webrtc_protocolv2.html from Web folder
            string url = $"http://{serverIp}:{httpPort}/webrtc_protocolv2.html";
            OpenInChrome(url);
        }

        /// <summary>
        /// Open webrtc_protocolv2.html with connection parameters.
        /// </summary>
        /// <param name="serverIp">Server IP address</param>
        /// <param name="wsPort">WebSocket port</param>
        /// <param name="httpPort">HTTP port for serving the HTML file</param>
        /// <param name="autoConnect">Whether to auto-connect on page load</param>
        public static void OpenWebRTCProtocolWithParams(string serverIp, int wsPort = 5100, int httpPort = 5100, bool autoConnect = false)
        {
            // Pass connection parameters as URL query string
            string url = $"http://{serverIp}:{httpPort}/webrtc_protocolv2.html?wsHost={serverIp}&wsPort={wsPort}";
            if (autoConnect)
            {
                url += "&autoConnect=true";
            }
            OpenInChrome(url);
        }
    }
}
