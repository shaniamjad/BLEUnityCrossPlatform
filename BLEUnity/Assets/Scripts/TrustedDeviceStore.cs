using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

[Serializable]
public class TrustedDeviceRecord
{
    public string id { get; set; }
    public DeviceType type { get; set; }
    public bool? lastConnected { get; set; }
}

public static class TrustedDeviceStore
{
    private const string StorageKey = "BLE_TRUSTED_DEVICES";
    private static List<TrustedDeviceRecord> cache;

    private static List<TrustedDeviceRecord> Cache => cache ??= new List<TrustedDeviceRecord>();

    static TrustedDeviceStore() => Load();

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
            Debug.LogWarning($"[TrustedDeviceStore] Failed to deserialize: {ex.Message}");
            cache = new List<TrustedDeviceRecord>();
        }
    }

    private static void Save()
    {
        try
        {
            string json = JsonConvert.SerializeObject(Cache);
            PlayerPrefs.SetString(StorageKey, json);
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TrustedDeviceStore] Failed to persist trusted devices: {ex.Message}");
        }
    }

    public static IReadOnlyList<TrustedDeviceRecord> GetAll() => Cache;

    public static bool IsTrusted(string id)
        => !string.IsNullOrEmpty(id) && Cache.Any(r => r.id == id);

    public static bool TryGet(string id, out TrustedDeviceRecord record)
    {
        record = null;
        if (string.IsNullOrEmpty(id)) return false;
        record = Cache.FirstOrDefault(r => r.id == id);
        return record != null;
    }

    public static void AddOrUpdate(string id, DeviceType type)
    {
        if (string.IsNullOrEmpty(id)) return;

        var existing = Cache.FirstOrDefault(r => r.id == id);
        if (existing == null)
        {
            Cache.Add(new TrustedDeviceRecord
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
            return record.lastConnected != false;

        return false;
    }

    public static void SetLastConnectionState(string id, DeviceType type, bool connected)
    {
        if (string.IsNullOrEmpty(id)) return;

        var record = Cache.FirstOrDefault(r => r.id == id);
        if (record == null)
        {
            record = new TrustedDeviceRecord
            {
                id = id,
                type = type,
                lastConnected = connected
            };
            Cache.Add(record);
        }
        else
        {
            record.type = type;
            record.lastConnected = connected;
        }

        Save();
    }
}

