using System;
using System.Threading.Tasks;

namespace VRWorkspace.Domain.Streaming
{
    /// <summary>
    /// Streaming client contract. Extracted from PhaseProtocolClient.
    /// </summary>
    public interface IStreamingClient : IDisposable
    {
        bool IsConnected { get; }
        bool IsStreaming { get; }

        Task ConnectAsync(string serverUrl);
        void Disconnect();
        void SendInputEvent(string eventType, string eventData);

        event Action OnConnected;
        event Action OnDisconnected;
        event Action<string> OnError;
    }
}
