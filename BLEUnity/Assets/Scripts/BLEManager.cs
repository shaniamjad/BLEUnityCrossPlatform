using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Central BLE bridge that routes native BLE events to device listeners.
/// </summary>
public class BLEManager : MonoBehaviour
{
    public static BLEManager Instance { get; private set; }

    [SerializeField] private Button startScan;
    [SerializeField] private Button stopScan;


    // All discovered or connected devices
    private readonly Dictionary<string, BleDevice> devices = new();
    public IReadOnlyDictionary<string, BleDevice> Devices => devices;

    private readonly Dictionary<string, List<IBLEDeviceListener>> listeners = new();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator Start()
    {
        startScan.onClick.AddListener(() => BLEPlugin.Instance.StartScan());
        stopScan.onClick.AddListener(() => BLEPlugin.Instance.StopScan());

        // 1. Request BLE permissions
        BLEPlugin.Instance.RequestPermissions();
        yield return new WaitUntil(() => BLEPlugin.Instance.PermissionGranted);

        // 2. Initialize native BLE
        BLEPlugin.Instance.Init();
    }

    // ----------------------------------------------------------------------
    // --- EVENT HANDLING FROM NATIVE SIDE ---------------------------------
    // ----------------------------------------------------------------------
    public void HandleNativeEvent(string json)
    {
        BleEvent ev;
        try { ev = JsonConvert.DeserializeObject<BleEvent>(json); }
        catch (Exception ex)
        {
            Debug.LogError($"[BLEManager] Failed to parse JSON: {ex}");
            return;
        }

        if (!devices.ContainsKey(ev.id) && !string.IsNullOrEmpty(ev.id))
        {
            DeviceType detectedType = BleDeviceProfiles.DetectDeviceType(ev.deviceType, ev.name);
            devices[ev.id] = new BleDevice
            {
                id = ev.id,
                name = ev.name,
                type = detectedType,
                isConnected = false,
                rssi = ev.rssi
            };
        }

        if (devices.TryGetValue(ev.id, out var existingDevice))
        {
            if (!string.IsNullOrEmpty(ev.name)) existingDevice.name = ev.name;
            var detectedType = BleDeviceProfiles.DetectDeviceType(ev.deviceType, ev.name ?? existingDevice.name);
            if (detectedType != DeviceType.Unknown)
            {
                existingDevice.type = detectedType;
            }
            if (ev.eventType == "scanResult")
            {
                existingDevice.rssi = ev.rssi;
            }
        }


        switch (ev.eventType)
        {
            case "scanResult":
                break;

            case "ready":
                if (devices.TryGetValue(ev.id, out var readyDevice))
                {
                    HandleDeviceReady(readyDevice);
                }
                break;

            case "connected":
                if (devices.TryGetValue(ev.id, out var connectedDevice))
                {
                    connectedDevice.isConnected = true;
                    NotifyConnected(connectedDevice); // ✅ notify listeners
                }
                break;

            case "disconnected":
                if (devices.TryGetValue(ev.id, out var disconnectedDevice))
                {
                    disconnectedDevice.isConnected = false;
                    NotifyDisconnected(disconnectedDevice); // ✅ notify listeners
                }
                break;

            case "data":
                if (devices.TryGetValue(ev.id, out var device))
                {
                    HandleDeviceData(device, ev);
                }
                break;
        }

        UI_BLEDeviceList.Instance.Refresh(devices.Values);
    }

    // ----------------------------------------------------------------------
    // --- DATA HANDLING ----------------------------------------------------
    // ----------------------------------------------------------------------
    private void HandleDeviceData(BleDevice device, BleEvent ev)
    {
        if (string.IsNullOrEmpty(ev.value)) return;

        byte[] raw;
        try { raw = Convert.FromBase64String(ev.value); }
        catch (Exception e)
        {
            Debug.LogError($"[BLEManager] Failed to decode Base64 for {device.id}: {e}");
            return;
        }

        // Notify listeners
        NotifyData(device, raw);

        // Internal parser (optional)
        switch (device.type)
        {

            case DeviceType.Unknown:
            default:
                Debug.LogWarning($"[BLEManager] Unknown device type for {device.name}");
                break;
        }
    }





    private void HandleDeviceReady(BleDevice device)
    {
        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile == null)
            return;

        if (profile.AutoStartMeasurement)
        {
            StartCoroutine(AutoStartAfterDelay(device.id, profile));
        }
    }

    private System.Collections.IEnumerator AutoStartAfterDelay(string deviceId, BleDeviceProfileDefinition profile)
    {
        float delaySeconds = (float)profile.AutoStartDelay.TotalSeconds;
        if (delaySeconds > 0f)
            yield return new WaitForSeconds(delaySeconds);

        if (BLEPlugin.Instance != null && profile.StartCommand != null)
        {
            BLEPlugin.Instance.SendControl(deviceId, profile.StartCommand, "start");
        }
    }


    // ----------------------------------------------------------------------
    // --- LISTENER REGISTRATION SYSTEM ------------------------------------
    // ----------------------------------------------------------------------

    /// <summary>Register a listener for a specific device ID.</summary>
    public void AddListener(string deviceId, IBLEDeviceListener listener)
    {
        if (!listeners.TryGetValue(deviceId, out var list))
        {
            list = new List<IBLEDeviceListener>();
            listeners[deviceId] = list;
        }

        if (!list.Contains(listener))
        {
            list.Add(listener);
        }
    }

    /// <summary>Unregister a listener for a specific device ID.</summary>
    public void RemoveListener(string deviceId, IBLEDeviceListener listener)
    {
        if (listeners.TryGetValue(deviceId, out var list))
        {
            list.Remove(listener);
            if (list.Count == 0)
                listeners.Remove(deviceId);
        }
    }

    /// <summary>Notify listeners that a device has connected.</summary>
    private void NotifyConnected(BleDevice device)
    {
        if (listeners.TryGetValue(device.id, out var list))
        {
            foreach (var l in list)
                l.OnConnected(device);
        }
    }

    /// <summary>Notify listeners that a device has disconnected.</summary>
    private void NotifyDisconnected(BleDevice device)
    {
        if (listeners.TryGetValue(device.id, out var list))
        {
            foreach (var l in list)
                l.OnDisconnected(device);
        }
    }

    /// <summary>Notify listeners that new data arrived.</summary>
    private void NotifyData(BleDevice device, byte[] raw)
    {
        if (listeners.TryGetValue(device.id, out var list))
        {
            foreach (var l in list)
                l.OnData(device, raw);
        }
    }

    // ----------------------------------------------------------------------
    // --- PUBLIC BLE ACTIONS ----------------------------------------------
    // ----------------------------------------------------------------------
    public void Connect(string deviceId, DeviceType type)
    {
        var profile = BleDeviceProfiles.TryGetProfile(type);
        if (profile == null)
        {
            Debug.LogWarning($"[BLEManager] No device profile registered for {type}. Unable to connect to {deviceId}.");
            return;
        }

        BLEPlugin.Instance.Connect(deviceId, profile);
    }

    public void DisConnect(string deviceId) =>
        BLEPlugin.Instance.Disconnect(deviceId);

    public void StopScan() =>
        BLEPlugin.Instance.StopScan();

    public void StartMeasurement(string deviceId)
    {
        if (!devices.TryGetValue(deviceId, out var device))
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile?.StartCommand != null)
        {
            BLEPlugin.Instance.SendControl(deviceId, profile.StartCommand, "start");
        }
    }

    public void StopMeasurement(string deviceId)
    {
        if (!devices.TryGetValue(deviceId, out var device))
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile?.StopCommand != null)
        {
            BLEPlugin.Instance.SendControl(deviceId, profile.StopCommand, "stop");
        }
    }

    public void PauseMeasurement(string deviceId)
    {
        if (!devices.TryGetValue(deviceId, out var device))
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile?.PauseCommand != null)
        {
            BLEPlugin.Instance.SendControl(deviceId, profile.PauseCommand, "pause");
        }
    }
}
