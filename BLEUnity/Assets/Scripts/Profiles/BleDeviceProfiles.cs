using System;
using System.Collections.Generic;

/// <summary>
/// Registry of supported BLE device profiles. Contains metadata shared between Android and iOS
/// plus helper utilities for detecting device types from advertisement data.
/// </summary>
public static class BleDeviceProfiles
{
    private static readonly Dictionary<DeviceType, BleDeviceProfileDefinition> profiles = new()
    {
        {
            DeviceType.BioPot,
            new BleDeviceProfileDefinition(
                DeviceType.BioPot,
                new Guid("0000fff0-0000-1000-8000-00805f9b34fb"),
                new Guid("0000fff2-0000-1000-8000-00805f9b34fb"),
                new Guid("0000fff4-0000-1000-8000-00805f9b34fb"),
                requestMtu: 251,
                emitReadyEvent: true,
                autoStartMeasurement: true,
                autoStartDelay: TimeSpan.FromMilliseconds(200),
                startCommand: new byte[] { 0x01 },
                stopCommand: new byte[] { 0x02 },
                pauseCommand: new byte[] { 0x00 })
        },
        {
            DeviceType.Movella,
            new BleDeviceProfileDefinition(
                DeviceType.Movella,
                new Guid("15172000-4947-11e9-8646-d663bd873d93"),
                new Guid("15172001-4947-11e9-8646-d663bd873d93"),
                new Guid("15172003-4947-11e9-8646-d663bd873d93"),
                requestMtu: null,
                emitReadyEvent: true,
                autoStartMeasurement: true,
                autoStartDelay: TimeSpan.FromMilliseconds(300),
                startCommand: new byte[] { 0x01, 0x01, 0x02 },
                stopCommand: new byte[] { 0x01, 0x00 },
                pauseCommand: new byte[] { 0x01, 0x00 })
        }
    };

    /// <summary>
    /// Attempts to find a known profile.
    /// </summary>
    public static BleDeviceProfileDefinition TryGetProfile(DeviceType type)
    {
        profiles.TryGetValue(type, out var profile);
        return profile;
    }

    /// <summary>
    /// Determine the device type using either the native hint or the advertisement name.
    /// </summary>
    public static DeviceType DetectDeviceType(string nativeTypeHint, string advertisedName)
    {
        if (!string.IsNullOrEmpty(nativeTypeHint))
        {
            if (Enum.TryParse(nativeTypeHint, true, out DeviceType parsed))
                return parsed;
        }

        if (string.IsNullOrEmpty(advertisedName))
            return DeviceType.Unknown;

        string lower = advertisedName.ToLowerInvariant();
        if (lower.Contains("biopot"))
            return DeviceType.BioPot;
        if (lower.Contains("movella") || lower.Contains("dot"))
            return DeviceType.Movella;

        return DeviceType.Unknown;
    }
}
