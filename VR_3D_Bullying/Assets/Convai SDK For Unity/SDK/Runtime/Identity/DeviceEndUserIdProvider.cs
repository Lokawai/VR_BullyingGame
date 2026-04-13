using System;
using Convai.Domain.Identity;
using UnityEngine;

namespace Convai.Runtime.Identity
{
    /// <summary>
    ///     Generates an end user identifier using Unity's device identifier with a persisted GUID fallback.
    ///     All Unity API calls (<see cref="PlayerPrefs" />, <see cref="SystemInfo" />) are marshaled to
    ///     the main thread via <see cref="UnityScheduler" /> to prevent threading violations.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This provider generates a stable, device-specific identifier that allows the Convai server
    ///         to track end users across sessions. This enables features like:
    ///     </para>
    ///     <para>
    ///         When the device identifier is unavailable, a GUID is generated once and persisted via
    ///         <see cref="PlayerPrefs" /> so the same fallback is reused across sessions.
    ///         <list type="bullet">
    ///             <item>Long-Term Memory (LTM) - Characters remember previous conversations</item>
    ///             <item>Monthly Active User (MAU) tracking for billing</item>
    ///             <item>Cross-session conversation continuity</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public sealed class DeviceEndUserIdProvider : IEndUserIdProvider
    {
        private const string FallbackIdKey = "convai.end_user_id";
        private readonly IDeviceInfoProvider _deviceInfoProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeviceEndUserIdProvider" /> class
        ///     using the default <see cref="SystemDeviceInfoProvider" />.
        /// </summary>
        public DeviceEndUserIdProvider() : this(new SystemDeviceInfoProvider())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DeviceEndUserIdProvider" /> class
        ///     with the specified device info provider.
        /// </summary>
        /// <param name="deviceInfoProvider">
        ///     The provider for device information. If <c>null</c>, falls back to <see cref="SystemDeviceInfoProvider" />.
        /// </param>
        public DeviceEndUserIdProvider(IDeviceInfoProvider deviceInfoProvider)
        {
            _deviceInfoProvider = deviceInfoProvider ?? new SystemDeviceInfoProvider();
        }

        /// <summary>
        ///     Generates a stable end user identifier for this device.
        /// </summary>
        /// <returns>
        ///     The device's unique identifier from <see cref="IDeviceInfoProvider.GetDeviceUniqueIdentifier" />,
        ///     or a persisted GUID if the device identifier is unavailable or invalid.
        /// </returns>
        public string GenerateEndUserId()
        {
            if (UnityScheduler.IsOnMainThread) return GenerateEndUserIdDirect();

            return UnityScheduler.PostAsync(GenerateEndUserIdDirect).GetAwaiter().GetResult();
        }

        /// <summary>
        ///     Core logic — must run on the main thread because both
        ///     <see cref="IDeviceInfoProvider.GetDeviceUniqueIdentifier" /> and
        ///     <see cref="PlayerPrefs" /> require main-thread access.
        /// </summary>
        private string GenerateEndUserIdDirect()
        {
            string deviceIdentifier = _deviceInfoProvider.GetDeviceUniqueIdentifier();
            if (IsValid(deviceIdentifier)) return deviceIdentifier;

            return GetOrCreateFallbackId();
        }

        private static string GetOrCreateFallbackId()
        {
            string storedId = PlayerPrefs.GetString(FallbackIdKey, string.Empty);
            if (IsValid(storedId)) return storedId;

            string newId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(FallbackIdKey, newId);
            PlayerPrefs.Save();
            return newId;
        }

        /// <summary>
        ///     Validates that the device identifier is usable.
        /// </summary>
        /// <param name="value">The device identifier to validate.</param>
        /// <returns>
        ///     True if the identifier is valid and usable; false if it's null, empty,
        ///     unsupported, or consists entirely of zeros.
        /// </returns>
        internal static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            if (value == SystemInfo.unsupportedIdentifier) return false;

            bool allZeros = true;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '0')
                {
                    allZeros = false;
                    break;
                }
            }

            return !allZeros;
        }
    }
}
