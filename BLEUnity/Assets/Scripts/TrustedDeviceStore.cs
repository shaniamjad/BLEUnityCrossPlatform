using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
public class TrustedDeviceRecord
{
    public string id;
    public DeviceType type;
    public bool? lastConnected;
}

public static class TrustedDeviceStore
{
    private const string StorageKey = "BLE_TRUSTED_DEVICES";
    private static List<TrustedDeviceRecord> cache;

    static TrustedDeviceStore()
    {
        Load();
    }

    private static void Load()
    {
        string json = PlayerPrefs.GetString(StorageKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            cache = new List<TrustedDeviceRecord>();
            return;
        }

        try
        {
            cache = JsonConvert.DeserializeObject<List<TrustedDeviceRecord>>(json) ?? new List<TrustedDeviceRecord>();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[TrustedDeviceStore] Failed to deserialize trusted devices: {ex.Message}");
            cache = new List<TrustedDeviceRecord>();
        }
    }

    private static void Save()
    {
        if (cache == null)
            cache = new List<TrustedDeviceRecord>();

        try
        {
            string json = JsonConvert.SerializeObject(cache);
            PlayerPrefs.SetString(StorageKey, json);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TrustedDeviceStore] Failed to persist trusted devices: {ex.Message}");
        }
    }

    public static IReadOnlyList<TrustedDeviceRecord> GetAll()
    {
        return cache ?? (cache = new List<TrustedDeviceRecord>());
    }

    public static bool IsTrusted(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        return cache != null && cache.Any(r => r.id == id);
    }

    public static bool TryGet(string id, out TrustedDeviceRecord record)
    {
        record = null;
        if (string.IsNullOrEmpty(id) || cache == null)
            return false;

        record = cache.FirstOrDefault(r => r.id == id);
        return record != null;
    }

    public static void AddOrUpdate(string id, DeviceType type)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (cache == null)
            cache = new List<TrustedDeviceRecord>();

        var existing = cache.FirstOrDefault(r => r.id == id);
        if (existing == null)
        {
            cache.Add(new TrustedDeviceRecord
            {
                id = id,
                type = type,
                lastConnected = null
            });
        }
        else
        {
            existing.type = type;
        }

        Save();
    }

    public static bool ShouldAutoReconnect(string id)
    {
        if (TryGet(id, out var record))
        {
            // Default to auto-reconnect unless the last known state was an
            // intentional disconnect (false). Null indicates legacy data or an
            // unknown state, so we continue to auto-reconnect for backward
            // compatibility.
            return record.lastConnected != false;
        }

        return false;
    }

    public static void SetLastConnectionState(string id, DeviceType type, bool connected)
    {
        if (string.IsNullOrEmpty(id))
            return;

        if (cache == null)
            cache = new List<TrustedDeviceRecord>();

        var record = cache.FirstOrDefault(r => r.id == id);
        if (record == null)
        {
            record = new TrustedDeviceRecord
            {
                id = id,
                type = type,
                lastConnected = connected
            };
            cache.Add(record);
        }
        else
        {
            record.type = type;
            record.lastConnected = connected;
        }

        Save();
    }
}
