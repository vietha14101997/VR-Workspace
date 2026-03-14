using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
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
    /// UDP listener + active probe for auto-discovering RemotePlayServer on LAN.
    ///
    /// Two-pronged approach:
    /// 1) PASSIVE: Listen for server broadcast beacons (server sends every ~2s on port 8289)
    /// 2) ACTIVE:  Send discovery probe packets to broadcast addresses of ALL network interfaces
    ///             (covers different subnets when WiFi router doesn't relay broadcasts)
    ///
    /// Server should reply to probe with the same beacon JSON format.
    /// </summary>
    public static class LanDiscoveryClient
    {
        public const int DefaultDiscoveryPort = 8289;

        // Probe message: server recognizes this and replies with beacon JSON
        private static readonly byte[] ProbeMessage =
            Encoding.UTF8.GetBytes("{\"service\":\"RemotePlayClient\",\"action\":\"discover\"}");

        /// <summary>
        /// Listen for server broadcast beacon. Returns first found server or null on timeout.
        /// </summary>
        public static async Task<DiscoveryResult> DiscoverAsync(int port = DefaultDiscoveryPort, int timeoutMs = 6000)
        {
            var all = await DiscoverAllAsync(port, timeoutMs);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>
        /// Listen for ALL server beacons within timeout + send active probes.
        /// Combines passive listening (server broadcasts) with active probing (client broadcasts).
        /// </summary>
        public static async Task<List<DiscoveryResult>> DiscoverAllAsync(
            int port = DefaultDiscoveryPort,
            int timeoutMs = 5000,
            CancellationToken externalToken = default)
        {
            Debug.Log($"[Discovery] Scanning for servers on UDP port {port} (timeout {timeoutMs}ms)...");

            var results = new List<DiscoveryResult>();
            var seenIPs = new HashSet<string>();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            cts.CancelAfter(timeoutMs);

            UdpClient udp = null;
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); // Bind to random port (avoid conflict)
                udp.EnableBroadcast = true;

                // Send active probes to all broadcast addresses immediately + after 1s
                _ = SendProbesAsync(udp, port, cts.Token);

                // Listen for responses (both broadcast beacons and probe replies)
                while (!cts.IsCancellationRequested)
                {
                    var receiveTask = udp.ReceiveAsync();
                    var delayTask = Task.Delay(timeoutMs, cts.Token);

                    var completed = await Task.WhenAny(receiveTask, delayTask);
                    if (completed == delayTask || cts.IsCancellationRequested)
                        break;

                    var result = await receiveTask;
                    var json = Encoding.UTF8.GetString(result.Buffer);

                    var parsed = Parse(json, result.RemoteEndPoint.Address.ToString());
                    if (parsed != null && seenIPs.Add(parsed.IP))
                    {
                        Debug.Log($"[Discovery] Found server: {parsed.ServerName} at {parsed.IP}:{parsed.Port}");
                        results.Add(parsed);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException ex)
            {
                Debug.LogWarning($"[Discovery] Socket error: {ex.Message}");
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Error: {ex.Message}");
            }
            finally
            {
                try { udp?.Close(); } catch { }
            }

            Debug.Log($"[Discovery] Scan complete — found {results.Count} server(s)");
            return results;
        }

        /// <summary>
        /// Send discovery probes to broadcast addresses of all active network interfaces.
        /// This helps when server and client are on different subnets of the same WiFi network.
        /// </summary>
        private static async Task SendProbesAsync(UdpClient udp, int port, CancellationToken ct)
        {
            try
            {
                // Send probes immediately
                SendProbeToAllInterfaces(udp, port);

                // Retry after 1 second (in case first probe was lost)
                await Task.Delay(1000, ct);
                SendProbeToAllInterfaces(udp, port);

                // One more retry at 2.5s
                await Task.Delay(1500, ct);
                SendProbeToAllInterfaces(udp, port);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Probe send error: {ex.Message}");
            }
        }

        private static void SendProbeToAllInterfaces(UdpClient udp, int port)
        {
            try
            {
                // Always send to global broadcast
                udp.Send(ProbeMessage, ProbeMessage.Length, new IPEndPoint(IPAddress.Broadcast, port));

                // Also send to subnet-specific broadcast addresses
                foreach (var broadcastAddr in GetAllBroadcastAddresses())
                {
                    try
                    {
                        udp.Send(ProbeMessage, ProbeMessage.Length, new IPEndPoint(broadcastAddr, port));
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Failed to send probe: {ex.Message}");
            }
        }

        /// <summary>
        /// Get broadcast addresses for all active IPv4 network interfaces.
        /// This ensures we can reach servers on different subnets.
        /// </summary>
        private static List<IPAddress> GetAllBroadcastAddresses()
        {
            var addresses = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var props = ni.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                        // Calculate broadcast: IP | ~SubnetMask
                        var ip = addr.Address.GetAddressBytes();
                        var mask = addr.IPv4Mask.GetAddressBytes();
                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++)
                            broadcast[i] = (byte)(ip[i] | ~mask[i]);

                        var broadcastAddr = new IPAddress(broadcast);
                        if (!broadcastAddr.Equals(IPAddress.Broadcast))
                            addresses.Add(broadcastAddr);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Discovery] Failed to enumerate interfaces: {ex.Message}");
            }
            return addresses;
        }

        /// <summary>
        /// Parse beacon JSON. Falls back to sender IP if beacon IP is missing.
        /// </summary>
        private static DiscoveryResult Parse(string json, string senderIP)
        {
            try
            {
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
