using System;
using System.Text;
using Unity.WebRTC;
using UnityEngine;
using VRWorkspace.Core;

namespace VRWorkspace.Streaming
{
    /// <summary>
    /// Sends mouse/keyboard input events to the server via "input" DataChannel.
    /// Binary protocol — compact, pre-allocated buffers to avoid GC.
    /// All Send calls are guarded with try-catch to prevent native crashes
    /// when DataChannel is disposed between ready-check and send.
    /// </summary>
    public partial class PhaseProtocolClient
    {
        private RTCDataChannel _inputChannel;

        // Pre-allocated buffers (reused per-frame to avoid GC pressure at 60Hz)
        private readonly byte[] _mouseMoveBuffer = new byte[5];
        private readonly byte[] _mouseButtonBuffer = new byte[3];
        private readonly byte[] _mouseWheelBuffer = new byte[5];
        private readonly byte[] _keyBuffer = new byte[4];
        private readonly byte[] _warpCursorBuffer = new byte[10];
        private readonly byte[] _gamepadBuffer = new byte[13];

        private bool IsInputChannelReady =>
            _inputChannel != null && _inputChannel.ReadyState == RTCDataChannelState.Open;

        private void TrySendInput(byte[] buffer)
        {
            try
            {
                var dc = _inputChannel;
                if (dc != null && dc.ReadyState == RTCDataChannelState.Open)
                    dc.Send(buffer);
            }
            catch (Exception)
            {
                // DC disposed between check and send — safe to ignore
            }
        }

        public void SendMouseMove(short dx, short dy)
        {
            if (!IsInputChannelReady) return;
            _mouseMoveBuffer[0] = 0x01;
            BitConverter.TryWriteBytes(new Span<byte>(_mouseMoveBuffer, 1, 2), dx);
            BitConverter.TryWriteBytes(new Span<byte>(_mouseMoveBuffer, 3, 2), dy);
            TrySendInput(_mouseMoveBuffer);
        }

        public void SendMouseButton(byte button, bool down)
        {
            if (!IsInputChannelReady) return;
            _mouseButtonBuffer[0] = 0x02;
            _mouseButtonBuffer[1] = button;
            _mouseButtonBuffer[2] = (byte)(down ? 1 : 0);
            TrySendInput(_mouseButtonBuffer);
        }

        public void SendMouseWheel(short deltaY, short deltaX = 0)
        {
            if (!IsInputChannelReady) return;
            _mouseWheelBuffer[0] = 0x03;
            BitConverter.TryWriteBytes(new Span<byte>(_mouseWheelBuffer, 1, 2), deltaY);
            BitConverter.TryWriteBytes(new Span<byte>(_mouseWheelBuffer, 3, 2), deltaX);
            TrySendInput(_mouseWheelBuffer);
        }

        public void SendKey(ushort vk, bool down)
        {
            if (!IsInputChannelReady) return;
            _keyBuffer[0] = 0x04;
            BitConverter.TryWriteBytes(new Span<byte>(_keyBuffer, 1, 2), vk);
            _keyBuffer[3] = (byte)(down ? 1 : 0);
            TrySendInput(_keyBuffer);
        }

        public void SendText(string text)
        {
            if (!IsInputChannelReady || string.IsNullOrEmpty(text)) return;
            byte[] utf8 = Encoding.UTF8.GetBytes(text);
            if (utf8.Length > ushort.MaxValue) return;
            byte[] buf = new byte[3 + utf8.Length];
            buf[0] = 0x05;
            BitConverter.TryWriteBytes(new Span<byte>(buf, 1, 2), (ushort)utf8.Length);
            Buffer.BlockCopy(utf8, 0, buf, 3, utf8.Length);
            TrySendInput(buf);
        }

        public void SendWarpCursor(int monitorIndex, float u, float v)
        {
            if (!IsInputChannelReady) return;
            _warpCursorBuffer[0] = 0x06;
            _warpCursorBuffer[1] = (byte)monitorIndex;
            BitConverter.TryWriteBytes(new Span<byte>(_warpCursorBuffer, 2, 4), u);
            BitConverter.TryWriteBytes(new Span<byte>(_warpCursorBuffer, 6, 4), v);
            TrySendInput(_warpCursorBuffer);
        }

        public void SendGamepadState(ushort buttons, byte leftTrigger, byte rightTrigger,
            short thumbLX, short thumbLY, short thumbRX, short thumbRY)
        {
            if (!IsInputChannelReady) return;
            _gamepadBuffer[0] = 0x07;
            BitConverter.TryWriteBytes(new Span<byte>(_gamepadBuffer, 1, 2), buttons);
            _gamepadBuffer[3] = leftTrigger;
            _gamepadBuffer[4] = rightTrigger;
            BitConverter.TryWriteBytes(new Span<byte>(_gamepadBuffer, 5, 2), thumbLX);
            BitConverter.TryWriteBytes(new Span<byte>(_gamepadBuffer, 7, 2), thumbLY);
            BitConverter.TryWriteBytes(new Span<byte>(_gamepadBuffer, 9, 2), thumbRX);
            BitConverter.TryWriteBytes(new Span<byte>(_gamepadBuffer, 11, 2), thumbRY);
            TrySendInput(_gamepadBuffer);
        }
    }
}
