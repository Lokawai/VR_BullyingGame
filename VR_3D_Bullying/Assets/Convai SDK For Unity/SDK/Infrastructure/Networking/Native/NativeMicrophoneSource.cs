using System;
using LiveKit;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Convai.Infrastructure.Networking.Native
{
    internal sealed class NativeMicrophoneSource : IMicrophoneSource
    {
        private readonly bool _ownsSourceObject;
        private readonly GameObject _sourceObject;
        private bool _disposed;

        public NativeMicrophoneSource(string deviceName, int deviceIndex = 0, GameObject hostObject = null)
        {
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Default" : deviceName;
            DeviceIndex = deviceIndex;

            if (hostObject == null)
            {
                _sourceObject = new GameObject($"[Convai] MicrophoneSource ({DeviceName})");
                _sourceObject.hideFlags = HideFlags.HideAndDontSave;
                Object.DontDestroyOnLoad(_sourceObject);
                _ownsSourceObject = true;
            }
            else
            {
                _sourceObject = hostObject;
                _ownsSourceObject = false;
            }

            UnderlyingSource = new MicrophoneSource(DeviceName, _sourceObject);
        }

        /// <summary>
        ///     Gets the underlying LiveKit microphone source.
        /// </summary>
        public MicrophoneSource UnderlyingSource { get; }

        public string Name => DeviceName;

        public bool IsCapturing { get; private set; }

        public string DeviceName { get; }

        public int DeviceIndex { get; }

        public bool IsMuted
        {
            get => UnderlyingSource.Muted;
            set => UnderlyingSource.SetMute(value);
        }

        public void StartCapture()
        {
            ThrowIfDisposed();

            if (IsCapturing) return;

            UnderlyingSource.Start();
            IsCapturing = true;
        }

        public void StopCapture()
        {
            if (!IsCapturing) return;

            UnderlyingSource.Stop();
            IsCapturing = false;
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopCapture();
            UnderlyingSource.Dispose();

            if (_ownsSourceObject) Object.Destroy(_sourceObject);

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeMicrophoneSource));
        }
    }
}
