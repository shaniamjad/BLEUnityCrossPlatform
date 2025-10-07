using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Persists trusted BLE devices so we can automatically reconnect to them on future runs.
/// Backed by PlayerPrefs so it works across platforms without additional plugins.
/// </summary>
public static class BLETrustedDeviceStore
{
    private const string PlayerPrefsKey = "ble.trustedDevices";

    private static readonly Dictionary<string, TrustedDeviceRecord> records;

    static BLETrustedDeviceStore()
    {
        records = LoadRecords();
    }

    public static IReadOnlyCollection<TrustedDeviceRecord> TrustedDevices => records.Values;

    public static bool Contains(string deviceId)
    {
        return !string.IsNullOrEmpty(deviceId) && records.ContainsKey(deviceId);
    }

    /// <summary>
    /// Saves/updates a trusted device entry. Called whenever the user successfully connects.
    /// </summary>
    public static void Remember(BleDevice device)
    {
        if (device == null || string.IsNullOrEmpty(device.id))
            return;

        if (!records.TryGetValue(device.id, out var record))
        {
            record = new TrustedDeviceRecord(device.id);
            records[device.id] = record;
        }

        record.Update(device);
        SaveRecords();
    }

    /// <summary>
    /// Persists a profile hint for a device before the first successful connection completes.
    /// </summary>
    public static void RememberProfileHint(string deviceId, BleDeviceProfileDefinition profile, string displayName = null)
    {
        if (string.IsNullOrEmpty(deviceId) || profile == null)
            return;

        if (!records.TryGetValue(deviceId, out var record))
        {
            record = new TrustedDeviceRecord(deviceId);
            records[deviceId] = record;
        }

        record.SetProfile(profile, displayName);
        SaveRecords();
    }

    /// <summary>
    /// Updates the "last seen" timestamp for a trusted device while scanning.
    /// </summary>
    public static void MarkSeen(BleDevice device)
    {
        if (device == null || string.IsNullOrEmpty(device.id))
            return;

        if (!records.TryGetValue(device.id, out var record))
            return;

        record.Update(device, updateTrustedState: false);
        SaveRecords();
    }

    public static bool TryGet(string deviceId, out TrustedDeviceRecord record)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            record = null;
            return false;
        }

        return records.TryGetValue(deviceId, out record);
    }

    public static void Forget(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;

        if (records.Remove(deviceId))
        {
            SaveRecords();
        }
    }

    private static Dictionary<string, TrustedDeviceRecord> LoadRecords()
    {
        if (!PlayerPrefs.HasKey(PlayerPrefsKey))
            return new Dictionary<string, TrustedDeviceRecord>();

        try
        {
            var json = PlayerPrefs.GetString(PlayerPrefsKey);
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, TrustedDeviceRecord>();

            var list = JsonConvert.DeserializeObject<List<TrustedDeviceRecord>>(json);
            var dict = new Dictionary<string, TrustedDeviceRecord>();
            if (list != null)
            {
                foreach (var record in list)
                {
                    if (record != null && !string.IsNullOrEmpty(record.DeviceId))
                    {
                        dict[record.DeviceId] = record;
                    }
                }
            }

            return dict;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BLETrustedDeviceStore] Failed to load trusted devices: {ex}");
            return new Dictionary<string, TrustedDeviceRecord>();
        }
    }

    private static void SaveRecords()
    {
        try
        {
            var json = JsonConvert.SerializeObject(records.Values, Formatting.None);
            PlayerPrefs.SetString(PlayerPrefsKey, json);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[BLETrustedDeviceStore] Failed to save trusted devices: {ex}");
        }
    }

    [Serializable]
    public sealed class TrustedDeviceRecord
    {
        [JsonProperty("deviceId")] private string deviceId;
        [JsonProperty("name")] private string name;
        [JsonProperty("type")] private DeviceType type;
        [JsonProperty("lastSeenUnixSeconds")] private long lastSeenUnixSeconds;
        [JsonProperty("profileKey")] private string profileKey;

        [JsonConstructor]
        public TrustedDeviceRecord(string deviceId)
        {
            this.deviceId = deviceId;
        }

        public string DeviceId => deviceId;
        public string Name => name;
        public DeviceType Type => type;
        public string ProfileKey => profileKey;
        public DateTimeOffset LastSeenUtc =>
            lastSeenUnixSeconds > 0
                ? DateTimeOffset.FromUnixTimeSeconds(lastSeenUnixSeconds)
                : DateTimeOffset.MinValue;

        internal void Update(BleDevice device, bool updateTrustedState = true)
        {
            if (device == null)
                return;

            if (!string.IsNullOrEmpty(device.name))
                name = device.name;

            if (updateTrustedState)
            {
                type = device.type;
                profileKey = device.type.ToString();
            }

            lastSeenUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        internal void SetProfile(BleDeviceProfileDefinition profile, string displayName)
        {
            if (profile == null)
                return;

            profileKey = profile.Type.ToString();

            if (!string.IsNullOrEmpty(displayName))
            {
                name = displayName;
            }
        }
    }
}
