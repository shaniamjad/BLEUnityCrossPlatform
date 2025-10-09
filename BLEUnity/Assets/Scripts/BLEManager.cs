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

    private const float InitialScanDurationSeconds = 15f;
    private const float AutoConnectTimeoutSeconds = 15f;

    private bool initialAutoScanActive;
    private bool userExtendedInitialScan;
    private Coroutine initialScanCoroutine;
#if UNITY_IOS && !UNITY_EDITOR
    private bool bluetoothPoweredOn;
#else
    private bool bluetoothPoweredOn = true;
#endif
    private readonly Dictionary<string, Coroutine> autoConnectTimeouts = new();

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
        startScan.onClick.AddListener(OnStartScanClicked);
        stopScan.onClick.AddListener(OnStopScanClicked);

        // 1. Request BLE permissions
        BLEPlugin.Instance.RequestPermissions();
        yield return new WaitUntil(() => BLEPlugin.Instance.PermissionGranted);

        // 2. Initialize native BLE
        BLEPlugin.Instance.Init();

#if UNITY_IOS && !UNITY_EDITOR
        bluetoothPoweredOn = false;
        yield return new WaitUntil(() => bluetoothPoweredOn);
#endif

        if (initialScanCoroutine != null)
            StopCoroutine(initialScanCoroutine);
        initialScanCoroutine = StartCoroutine(RunInitialDiscoveryScan());
    }

    private void OnStartScanClicked()
    {
        if (initialAutoScanActive)
            userExtendedInitialScan = true;

        if (!TryStartScan())
        {
            Debug.LogWarning("[BLEManager] Ignoring scan request until Bluetooth is powered on.");
            return;
        }
    }

    private void OnStopScanClicked()
    {
        BLEPlugin.Instance.StopScan();
        if (initialAutoScanActive)
            initialAutoScanActive = false;
    }

    private IEnumerator RunInitialDiscoveryScan()
    {
        initialAutoScanActive = true;
        userExtendedInitialScan = false;

        if (!TryStartScan())
        {
            initialAutoScanActive = false;
            initialScanCoroutine = null;
            Debug.LogWarning("[BLEManager] Delaying initial scan until Bluetooth is powered on.");
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < InitialScanDurationSeconds && initialAutoScanActive)
        {
            yield return null;
            elapsed += Time.deltaTime;
        }

        if (initialAutoScanActive && !userExtendedInitialScan)
        {
            BLEPlugin.Instance.StopScan();
        }

        initialAutoScanActive = false;
        userExtendedInitialScan = false;
        initialScanCoroutine = null;
    }

    private bool TryStartScan()
    {
        if (!bluetoothPoweredOn)
            return false;

        BLEPlugin.Instance.StartScan();
        return true;
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
        if (ev.eventType != "state")
        {
            if (!devices.ContainsKey(ev.id) && !string.IsNullOrEmpty(ev.id))
            {
                DeviceType detectedType = BleDeviceProfiles.DetectDeviceType(ev.deviceType, ev.name);
                if (TrustedDeviceStore.TryGet(ev.id, out var record) && detectedType == DeviceType.Unknown)
                    detectedType = record.type;

                devices[ev.id] = new BleDevice
                {
                    id = ev.id,
                    name = ev.name,
                    type = detectedType,
                    isConnected = false,
                    rssi = ev.rssi,
                    isTrusted = TrustedDeviceStore.IsTrusted(ev.id),
                    connectionNote = string.Empty
                };
            }

            if (devices.TryGetValue(ev.id, out var existingDevice))
            {
                if (!string.IsNullOrEmpty(ev.name)) existingDevice.name = ev.name;
                var detectedType = BleDeviceProfiles.DetectDeviceType(ev.deviceType, ev.name ?? existingDevice.name);
                if (detectedType == DeviceType.Unknown && TrustedDeviceStore.TryGet(ev.id, out var record))
                    detectedType = record.type;

                if (detectedType != DeviceType.Unknown)
                {
                    existingDevice.type = detectedType;
                }

                existingDevice.isTrusted = TrustedDeviceStore.IsTrusted(ev.id);

                if (ev.eventType == "scanResult")
                {
                    existingDevice.rssi = ev.rssi;
                }
            }
        }

        switch (ev.eventType)
        {
            case "state":
                HandleBluetoothStateEvent(ev);
                break;
            case "scanResult":
                if (devices.TryGetValue(ev.id, out var scannedDevice))
                {
                    HandleScanResult(scannedDevice);
                }
                break;

            case "ready":
                if (devices.TryGetValue(ev.id, out var readyDevice))
                {
                    readyDevice.isReady = true;
                    NotifyReady(readyDevice);
                    HandleDeviceReady(readyDevice);
                }
                break;

            case "connected":
                if (devices.TryGetValue(ev.id, out var connectedDevice))
                {
                    CancelAutoConnectTimeout(ev.id);
                    connectedDevice.isConnected = true;
                    connectedDevice.isAutoConnecting = false;
                    connectedDevice.autoConnectFailed = false;
                    connectedDevice.connectionNote = string.Empty;
                    connectedDevice.isReady = false;
                    SetMeasurementState(connectedDevice, MeasurementState.Idle);
                    NotifyConnected(connectedDevice); // ✅ notify listeners
                    TrustedDeviceStore.SetLastConnectionState(connectedDevice.id, connectedDevice.type, true);
                }
                break;

            case "disconnected":
                if (devices.TryGetValue(ev.id, out var disconnectedDevice))
                {
                    CancelAutoConnectTimeout(ev.id);
                    disconnectedDevice.isConnected = false;
                    disconnectedDevice.isReady = false;
                    disconnectedDevice.isAutoConnecting = false;
                    if (!string.Equals(disconnectedDevice.connectionNote, "Auto-connect timed out", StringComparison.Ordinal))
                        disconnectedDevice.connectionNote = string.Empty;
                    SetMeasurementState(disconnectedDevice, MeasurementState.Idle);
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

        if (UI_BLEDeviceList.Instance != null)
            UI_BLEDeviceList.Instance.Refresh(devices.Values);
    }

    private void HandleBluetoothStateEvent(BleEvent ev)
    {
        string state = ev.state ?? string.Empty;
        if (string.IsNullOrEmpty(state))
            return;

        bool poweredOn = string.Equals(state, "poweredOn", StringComparison.OrdinalIgnoreCase);

        if (bluetoothPoweredOn == poweredOn)
            return;

        bluetoothPoweredOn = poweredOn;

        Debug.Log($"[BLEManager] Bluetooth state changed: {state}");

        if (!bluetoothPoweredOn && initialAutoScanActive)
        {
            initialAutoScanActive = false;
        }
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
    private void HandleScanResult(BleDevice device)
    {
        if (device == null)
            return;

        if (device.isTrusted)
        {
            if (TrustedDeviceStore.ShouldAutoReconnect(device.id))
            {
                TryBeginAutoConnect(device);
            }
            else
            {
                device.isAutoConnecting = false;
                device.autoConnectFailed = false;
                device.connectionNote = string.Empty;
            }
        }
    }

    private void TryBeginAutoConnect(BleDevice device)
    {
        if (device == null)
            return;

        if (device.isConnected || device.isAutoConnecting || device.autoConnectFailed)
            return;

        Connect(device.id, device.type, initiatedByAuto: true);
    }

    private void ScheduleAutoConnectTimeout(string deviceId)
    {
        if (autoConnectTimeouts.TryGetValue(deviceId, out var routine))
            StopCoroutine(routine);

        autoConnectTimeouts[deviceId] = StartCoroutine(AutoConnectTimeout(deviceId));
    }

    private IEnumerator AutoConnectTimeout(string deviceId)
    {
        yield return new WaitForSeconds(AutoConnectTimeoutSeconds);

        if (devices.TryGetValue(deviceId, out var device) && !device.isConnected)
        {
            device.isAutoConnecting = false;
            device.autoConnectFailed = true;
            device.connectionNote = "Auto-connect timed out";
            if (UI_BLEDeviceList.Instance != null)
                UI_BLEDeviceList.Instance.Refresh(devices.Values);
        }

        autoConnectTimeouts.Remove(deviceId);
    }

    private void CancelAutoConnectTimeout(string deviceId)
    {
        if (autoConnectTimeouts.TryGetValue(deviceId, out var routine))
        {
            StopCoroutine(routine);
            autoConnectTimeouts.Remove(deviceId);
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

        StartMeasurement(deviceId);
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

    private void SetMeasurementState(BleDevice device, MeasurementState newState)
    {
        if (device == null)
            return;

        if (device.measurementState == newState)
            return;

        if (newState == MeasurementState.Sampling || newState == MeasurementState.Paused)
            device.isReady = false;

        device.measurementState = newState;
        NotifyMeasurementStateChanged(device, newState);
    }

    private void NotifyReady(BleDevice device)
    {
        if (listeners.TryGetValue(device.id, out var list))
        {
            foreach (var l in list)
                l.OnReady(device);
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
        if (listeners.TryGetValue(device.id, out var list))
        {
            foreach (var l in list)
                l.OnMeasurementStateChanged(device, state);
        }
    }

    // ----------------------------------------------------------------------
    // --- PUBLIC BLE ACTIONS ----------------------------------------------
    // ----------------------------------------------------------------------
    public void Connect(string deviceId, DeviceType type, bool initiatedByAuto = false)
    {
        if (string.IsNullOrEmpty(deviceId))
            return;

        if (!devices.TryGetValue(deviceId, out var device))
        {
            device = new BleDevice
            {
                id = deviceId,
                type = type,
                name = deviceId
            };
            devices[deviceId] = device;
        }

        if (type != DeviceType.Unknown)
            device.type = type;

        device.isTrusted = TrustedDeviceStore.IsTrusted(device.id);

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile == null)
        {
            Debug.LogWarning($"[BLEManager] No device profile registered for {type}. Unable to connect to {deviceId}.");
            device.isAutoConnecting = false;
            device.connectionNote = "Profile unavailable";
            UI_BLEDeviceList.Instance.Refresh(devices.Values);
            return;
        }

        if (BLEPlugin.Instance == null)
        {
            device.connectionNote = "BLE plugin unavailable";
            device.isAutoConnecting = false;
            UI_BLEDeviceList.Instance.Refresh(devices.Values);
            return;
        }

        device.isAutoConnecting = initiatedByAuto;

        if (initiatedByAuto)
        {
            device.autoConnectFailed = false;
            device.connectionNote = "Auto-connecting...";
            ScheduleAutoConnectTimeout(deviceId);
        }
        else
        {
            device.autoConnectFailed = true;
            CancelAutoConnectTimeout(deviceId);
            device.connectionNote = "Connecting...";
        }

        BLEPlugin.Instance.Connect(deviceId, profile);
        if (UI_BLEDeviceList.Instance != null)
            UI_BLEDeviceList.Instance.Refresh(devices.Values);
    }

    public void DisConnect(string deviceId)
    {
        if (devices.TryGetValue(deviceId, out var device))
            TrustedDeviceStore.SetLastConnectionState(deviceId, device.type, false);
        else
            TrustedDeviceStore.SetLastConnectionState(deviceId, DeviceType.Unknown, false);

        BLEPlugin.Instance.Disconnect(deviceId);
    }

    public void StopScan() =>
        BLEPlugin.Instance.StopScan();

    public void StartMeasurement(string deviceId)
    {
        if (!devices.TryGetValue(deviceId, out var device))
            return;

        if (!device.isConnected || device.measurementState == MeasurementState.Sampling)
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile?.StartCommand == null)
            return;

        BLEPlugin.Instance.SendControl(deviceId, profile.StartCommand, "start");
        SetMeasurementState(device, MeasurementState.Sampling);
    }

    public void StopMeasurement(string deviceId)
    {
        if (!devices.TryGetValue(deviceId, out var device))
            return;

        if (!device.isConnected || device.measurementState == MeasurementState.Idle)
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile?.StopCommand == null)
            return;

        BLEPlugin.Instance.SendControl(deviceId, profile.StopCommand, "stop");
        SetMeasurementState(device, MeasurementState.Idle);
    }

    public void PauseMeasurement(string deviceId)
    {
        if (!devices.TryGetValue(deviceId, out var device))
            return;

        if (!device.isConnected || device.measurementState != MeasurementState.Sampling)
            return;

        var profile = BleDeviceProfiles.TryGetProfile(device.type);
        if (profile?.PauseCommand == null)
            return;

        BLEPlugin.Instance.SendControl(deviceId, profile.PauseCommand, "pause");
        SetMeasurementState(device, MeasurementState.Paused);
    }
}
