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
    [SerializeField] private UI_BLEToast toast;

    [Header("Scanning")]
    [SerializeField, Tooltip("Duration of the automatic scan that runs on launch (seconds).")]
    private float autoScanDurationSeconds = 8f;
    [SerializeField, Tooltip("Duration of a manual scan triggered from the UI (seconds).")]
    private float manualScanDurationSeconds = 12f;
    [SerializeField, Tooltip("Duration of the recovery scan kicked off after resume/disconnect (seconds).")]
    private float resumeScanDurationSeconds = 6f;
    [SerializeField, Tooltip("Automatically reconnect to devices that were previously trusted.")]
    private bool autoReconnectEnabled = true;

    public event Action<bool, float> ScanStateChanged;
    public event Action<BleDevice, MeasurementState> MeasurementStateChanged;
    public bool IsScanning => isScanning;


    private const string AutoReconnectPrefsKey = "ble.autoReconnect.enabled";
    private const float AutoReconnectTimeoutSeconds = 15f;
    // All discovered or connected devices
    private readonly Dictionary<string, BleDevice> devices = new();
    public IReadOnlyDictionary<string, BleDevice> Devices => devices;

    private readonly Dictionary<string, List<IBLEDeviceListener>> listeners = new();
    private readonly HashSet<string> pendingAutoConnections = new();
    private readonly HashSet<string> userRequestedDisconnects = new();
    private readonly Dictionary<string, Coroutine> autoReconnectTimeouts = new();
    private Coroutine trustedDiscoveryRoutine;

    private Coroutine scanRoutine;
    private bool isScanning;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            autoReconnectEnabled = PlayerPrefs.GetInt(
                AutoReconnectPrefsKey,
                autoReconnectEnabled ? 1 : 0) == 1;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private IEnumerator Start()
    {
        startScan.onClick.AddListener(OnStartScanPressed);
        stopScan.onClick.AddListener(OnStopScanPressed);

        if (toast == null)
            toast = UI_BLEToast.Instance;

        SetScanState(false, 0f);

        // 1. Request BLE permissions
        BLEPlugin.Instance.RequestPermissions();
        yield return new WaitUntil(() => BLEPlugin.Instance.PermissionGranted);

        // 2. Initialize native BLE
        BLEPlugin.Instance.Init();

        // Kick off an automatic discovery scan so the device list is pre-populated.
        if (autoScanDurationSeconds > 0f)
        {
            BeginScan(autoScanDurationSeconds);
        }
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            Debug.Log("[BLEManager] Application paused. Suspending scans and pausing measurements.");
            StopActiveScan();

            foreach (var device in devices.Values)
            {
                if (device.isConnected)
                {
                    PauseMeasurement(device.id);
                }
            }
        }
        else if (autoReconnectEnabled)
        {
            Debug.Log("[BLEManager] Application resumed. Launching quick discovery scan.");
            BeginScan(resumeScanDurationSeconds);
        }
        else
        {
            Debug.Log("[BLEManager] Application resumed. Auto-reconnect disabled by user.");
        }
    }

    private void OnStartScanPressed()
    {
        BeginScan(manualScanDurationSeconds);
    }

    private void OnStopScanPressed()
    {
        StopActiveScan();
    }

    private void BeginScan(float durationSeconds)
    {
        if (BLEPlugin.Instance == null)
            return;

        if (durationSeconds <= 0f)
            durationSeconds = manualScanDurationSeconds;

        StopActiveScan();

        durationSeconds = Mathf.Max(0.5f, durationSeconds);
        Debug.Log($"[BLEManager] Starting scan for {durationSeconds:F1}s.");
        scanRoutine = StartCoroutine(ScanRoutine(durationSeconds));

        if (autoReconnectEnabled)
        {
            if (trustedDiscoveryRoutine != null)
            {
                StopCoroutine(trustedDiscoveryRoutine);
            }

            trustedDiscoveryRoutine = StartCoroutine(TrustedDiscoveryWatch(AutoReconnectTimeoutSeconds));
        }
    }

    private IEnumerator ScanRoutine(float durationSeconds)
    {
        BLEPlugin.Instance.StartScan();
        SetScanState(true, durationSeconds);

        float remaining = durationSeconds;
        while (remaining > 0f)
        {
            yield return null;
            remaining -= Time.deltaTime;
            ScanStateChanged?.Invoke(true, Mathf.Max(0f, remaining));
        }

        scanRoutine = null;
        StopActiveScan();
    }

    private void StopActiveScan()
    {
        if (scanRoutine != null)
        {
            StopCoroutine(scanRoutine);
            scanRoutine = null;
        }

        if (!isScanning)
        {
            SetScanState(false, 0f);
            return;
        }

        BLEPlugin.Instance?.StopScan();
        Debug.Log("[BLEManager] Scan stopped.");
        SetScanState(false, 0f);
    }

    private void SetScanState(bool scanning, float remainingSeconds)
    {
        isScanning = scanning;
        if (startScan != null)
            startScan.interactable = !scanning;
        if (stopScan != null)
            stopScan.interactable = scanning;

        ScanStateChanged?.Invoke(scanning, Mathf.Max(0f, remainingSeconds));
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
                if (devices.TryGetValue(ev.id, out var scanDevice))
                {
                    BLETrustedDeviceStore.MarkSeen(scanDevice);
                    TryQueueAutoConnect(scanDevice);
                }
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
                    pendingAutoConnections.Remove(connectedDevice.id);
                    ClearAutoReconnectWatch(connectedDevice.id);
                    BLETrustedDeviceStore.Remember(connectedDevice);
                    UpdateMeasurementState(connectedDevice, MeasurementState.Idle);
                    NotifyConnected(connectedDevice); // ✅ notify listeners
                    userRequestedDisconnects.Remove(connectedDevice.id);
                }
                break;

            case "disconnected":
                if (devices.TryGetValue(ev.id, out var disconnectedDevice))
                {
                    disconnectedDevice.isConnected = false;
                    UpdateMeasurementState(disconnectedDevice, MeasurementState.Idle);
                    NotifyDisconnected(disconnectedDevice); // ✅ notify listeners

                    pendingAutoConnections.Remove(disconnectedDevice.id);
                    ClearAutoReconnectWatch(disconnectedDevice.id);

                    if (autoReconnectEnabled && !userRequestedDisconnects.Contains(disconnectedDevice.id))
                    {
                        Debug.Log($"[BLEManager] Lost {disconnectedDevice.name}. Queuing recovery scan.");
                        BeginScan(resumeScanDurationSeconds);
                    }
                    else
                    {
                        userRequestedDisconnects.Remove(disconnectedDevice.id);
                    }
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

        UpdateMeasurementState(device, MeasurementState.Sampling);

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


    private void UpdateMeasurementState(BleDevice device, MeasurementState state)
    {
        if (device == null)
            return;

        if (device.measurementState == state)
            return;

        device.measurementState = state;
        NotifyMeasurementStateChanged(device, state);
    }





    private void HandleDeviceReady(BleDevice device)
    {
        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile == null)
            return;

        if (profile.AutoStartMeasurement)
        {
            UpdateMeasurementState(device, MeasurementState.Starting);
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
            if (devices.TryGetValue(deviceId, out var device))
            {
                UpdateMeasurementState(device, MeasurementState.Sampling);
            }
        }
        else if (devices.TryGetValue(deviceId, out var fallbackDevice))
        {
            UpdateMeasurementState(fallbackDevice, MeasurementState.Idle);
        }
    }



    private void TryQueueAutoConnect(BleDevice device)
    {
        if (!autoReconnectEnabled || BLEPlugin.Instance == null)
            return;

        if (device == null || device.isConnected || device.type == DeviceType.Unknown)
            return;

        if (string.IsNullOrEmpty(device.id))
            return;

        if (!BLETrustedDeviceStore.Contains(device.id))
            return;

        if (pendingAutoConnections.Contains(device.id) || userRequestedDisconnects.Contains(device.id))
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile == null)
            return;

        BLETrustedDeviceStore.RememberProfileHint(device.id, profile, device.name);
        pendingAutoConnections.Add(device.id);
        Debug.Log($"[BLEManager] Auto-connecting to trusted device {device.name} ({device.id})");
        BLEPlugin.Instance.Connect(device.id, profile);
        StartAutoReconnectTimeout(device.id);
    }

    private void StartAutoReconnectTimeout(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;

        if (autoReconnectTimeouts.TryGetValue(deviceId, out var existing))
        {
            StopCoroutine(existing);
            autoReconnectTimeouts.Remove(deviceId);
        }

        var routine = StartCoroutine(AutoReconnectTimeout(deviceId, AutoReconnectTimeoutSeconds));
        autoReconnectTimeouts[deviceId] = routine;
    }

    private IEnumerator AutoReconnectTimeout(string deviceId, float timeoutSeconds)
    {
        float elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            if (!pendingAutoConnections.Contains(deviceId))
            {
                break;
            }

            if (devices.TryGetValue(deviceId, out var device) && device.isConnected)
            {
                autoReconnectTimeouts.Remove(deviceId);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        autoReconnectTimeouts.Remove(deviceId);

        if (devices.TryGetValue(deviceId, out var failedDevice) && pendingAutoConnections.Remove(deviceId))
        {
            Debug.LogWarning($"[BLEManager] Auto-connection to {failedDevice.name} timed out.");
            ShowToast($"Couldn't auto-connect to {failedDevice.name}. Tap connect to retry.");
        }
        else if (pendingAutoConnections.Remove(deviceId))
        {
            Debug.LogWarning($"[BLEManager] Auto-connection to {deviceId} timed out.");
            if (BLETrustedDeviceStore.TryGet(deviceId, out var record) && !string.IsNullOrEmpty(record.Name))
            {
                ShowToast($"Couldn't auto-connect to {record.Name}. Tap connect to retry.");
            }
            else
            {
                ShowToast("Couldn't auto-connect. Try connecting manually.");
            }
        }
    }

    private void ClearAutoReconnectWatch(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;

        if (autoReconnectTimeouts.TryGetValue(deviceId, out var routine))
        {
            StopCoroutine(routine);
            autoReconnectTimeouts.Remove(deviceId);
        }
    }

    private IEnumerator TrustedDiscoveryWatch(float timeoutSeconds)
    {
        yield return new WaitForSeconds(timeoutSeconds);
        trustedDiscoveryRoutine = null;

        if (!autoReconnectEnabled)
            yield break;

        var missingDevices = new List<string>();
        foreach (var record in BLETrustedDeviceStore.TrustedDevices)
        {
            if (record == null || string.IsNullOrEmpty(record.DeviceId))
                continue;

            if (userRequestedDisconnects.Contains(record.DeviceId))
                continue;

            if (devices.TryGetValue(record.DeviceId, out var device) && device.isConnected)
                continue;

            if (pendingAutoConnections.Contains(record.DeviceId))
                continue;

            var label = !string.IsNullOrEmpty(record.Name) ? record.Name : record.DeviceId;
            missingDevices.Add(label);
        }

        if (missingDevices.Count > 0)
        {
            string joined = missingDevices.Count == 1
                ? missingDevices[0]
                : string.Join(", ", missingDevices);

            ShowToast($"Couldn't find {joined}. Tap Start Scan to retry.");
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

    private void NotifyMeasurementStateChanged(BleDevice device, MeasurementState state)
    {
        MeasurementStateChanged?.Invoke(device, state);

        if (listeners.TryGetValue(device.id, out var list))
        {
            foreach (var l in list)
                l.OnMeasurementStateChanged(device, state);
        }
    }

    // ----------------------------------------------------------------------
    // --- PUBLIC BLE ACTIONS ----------------------------------------------
    // ----------------------------------------------------------------------
    public void Connect(string deviceId, DeviceType type)
    {
        userRequestedDisconnects.Remove(deviceId);
        var profile = BleDeviceProfiles.TryGetProfile(type);
        if (profile == null)
        {
            Debug.LogWarning($"[BLEManager] No device profile registered for {type}. Unable to connect to {deviceId}.");
            return;
        }

        if (devices.TryGetValue(deviceId, out var device))
        {
            device.type = type;
            BLETrustedDeviceStore.RememberProfileHint(deviceId, profile, device.name);
        }
        else
        {
            BLETrustedDeviceStore.RememberProfileHint(deviceId, profile);
        }

        BLEPlugin.Instance.Connect(deviceId, profile);
    }

    public void DisConnect(string deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            userRequestedDisconnects.Add(deviceId);
        }

        BLEPlugin.Instance.Disconnect(deviceId);
    }

    public void StopScan() =>
        StopActiveScan();

    public void StartMeasurement(string deviceId)
    {
        if (!devices.TryGetValue(deviceId, out var device))
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        UpdateMeasurementState(device, MeasurementState.Starting);

        if (profile?.StartCommand != null)
        {
            BLEPlugin.Instance.SendControl(deviceId, profile.StartCommand, "start");
        }

        UpdateMeasurementState(device, MeasurementState.Sampling);
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

        UpdateMeasurementState(device, MeasurementState.Idle);
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
        UpdateMeasurementState(device, MeasurementState.Paused);
    }

    public void SetAutoReconnectEnabled(bool enabled)
    {
        if (autoReconnectEnabled == enabled)
            return;

        autoReconnectEnabled = enabled;
        Debug.Log($"[BLEManager] Auto-reconnect {(enabled ? "enabled" : "disabled")}.");
        PlayerPrefs.SetInt(AutoReconnectPrefsKey, enabled ? 1 : 0);
        PlayerPrefs.Save();

        if (!autoReconnectEnabled)
        {
            foreach (var deviceId in new List<string>(pendingAutoConnections))
            {
                ClearAutoReconnectWatch(deviceId);
            }

            pendingAutoConnections.Clear();

            if (trustedDiscoveryRoutine != null)
            {
                StopCoroutine(trustedDiscoveryRoutine);
                trustedDiscoveryRoutine = null;
            }
        }
    }

    private void ShowToast(string message)
    {
        if (toast == null || string.IsNullOrEmpty(message))
            return;

        toast.Show(message);
    }
}
