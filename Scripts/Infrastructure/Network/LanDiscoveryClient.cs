using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Result of a LAN discovery scan.
    /// </summary>
    public class DiscoveryResult
    {
        public string IP;
        public int Port;
        public string ServerName;
    }

    /// <summary>
    /// UDP listener for auto-discovering RemotePlayServer on LAN.
    /// Server broadcasts beacons every 2s on UDP port 8289.
    /// Client listens and returns the first valid beacon within timeout.
    /// </summary>
    public static class LanDiscoveryClient
    {
        public const int DefaultDiscoveryPort = 8289;

        /// <summary>
        /// Listen for server broadcast beacon. Returns first found server or null on timeout.
        /// </summary>
        public static async Task<DiscoveryResult> DiscoverAsync(int port = DefaultDiscoveryPort, int timeoutMs = 6000)
        {
            Debug.Log($"[Discovery] Listening on UDP port {port} (timeout {timeoutMs}ms)...");

            using var cts = new CancellationTokenSource(timeoutMs);
            UdpClient udp = null;
            try
            {
                udp = new UdpClient(port);
                udp.EnableBroadcast = true;

                while (!cts.IsCancellationRequested)
                {
                    var receiveTask = udp.ReceiveAsync();
                    var delayTask = Task.Delay(timeoutMs, cts.Token);

                    var completed = await Task.WhenAny(receiveTask, delayTask);
                    if (completed == delayTask || cts.IsCancellationRequested)
                    {
                        Debug.Log("[Discovery] Timeout — no server found");
                        return null;
                    }

                    var result = await receiveTask;
                    var json = Encoding.UTF8.GetString(result.Buffer);
                    Debug.Log($"[Discovery] Received from {result.RemoteEndPoint}: {json}");

                    var parsed = Parse(json, result.RemoteEndPoint.Address.ToString());
                    if (parsed != null)
                    {
                        Debug.Log($"[Discovery] Found server: {parsed.ServerName} at {parsed.IP}:{parsed.Port}");
                        return parsed;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[Discovery] Timeout — no server found");
            }
            catch (SocketException ex)
            {
                Debug.LogWarning($"[Discovery] Socket error: {ex.Message}");
            }
            catch (ObjectDisposedException)
            {
                // Normal on cancellation
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Error: {ex.Message}");
            }
            finally
            {
                try { udp?.Close(); } catch { }
            }

            return null;
        }

        /// <summary>
        /// Parse beacon JSON. Falls back to sender IP if beacon IP is missing.
        /// </summary>
        private static DiscoveryResult Parse(string json, string senderIP)
        {
            try
            {
                // Use QRScannerConfig-compatible format
                // Expected: {"service":"RemotePlayServer","ip":"...","port":"8288","name":"PC-NAME"}
                if (!json.Contains("RemotePlayServer")) return null;

                var config = JsonUtility.FromJson<BeaconData>(json);
                if (config == null) return null;

                string ip = !string.IsNullOrEmpty(config.ip) ? config.ip : senderIP;
                int port = 8288; // default
                if (!string.IsNullOrEmpty(config.port))
                    int.TryParse(config.port, out port);

                return new DiscoveryResult
                {
                    IP = ip,
                    Port = port,
                    ServerName = !string.IsNullOrEmpty(config.name) ? config.name : ip
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Parse error: {ex.Message}");
                return null;
            }
        }

        [Serializable]
        private class BeaconData
        {
            public string service;
            public string ip;
            public string port;
            public string name;
        }
    }
}
