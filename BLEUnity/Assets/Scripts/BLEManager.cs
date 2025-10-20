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



    // All discovered or connected devices
    private readonly Dictionary<string, BleDevice> devices = new();
    public IReadOnlyDictionary<string, BleDevice> Devices => devices;

    private readonly Dictionary<DeviceType, IDataParser> dataParsers = new();


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


    public event Action<IEnumerable<BleDevice>> OnDevicesUpdated;


    private void NotifyDevicesUpdated()
    {
        OnDevicesUpdated?.Invoke(devices.Values);
    }




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
        SetupParsers();

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

    private void SetupParsers()
    {
        dataParsers[DeviceType.Movella] = new MovellaSignalParser();
        dataParsers[DeviceType.BioPot] = new BiopotSignalParser(new BiopotGenericInfo { ChannelsNumber = 8, SamplesPerChannelNumber = 7 });

    }

    public void OnStartScanClicked()
    {
        if (initialAutoScanActive)
            userExtendedInitialScan = true;

        if (!TryStartScan())
        {
            Debug.LogWarning("[BLEManager] Ignoring scan request until Bluetooth is powered on.");
            return;
        }
    }

    public void OnStopScanClicked()
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

        switch (ev.eventType)
        {
            case "state":
                HandleBluetoothStateEvent(ev);
                break;
            case "scanResult":
                AddOrUpdateDevice(ev);
                if (devices.TryGetValue(ev.id, out var scannedDevice))
                {
                    HandleScanResult(scannedDevice);
                }
                break;

            case "ready":
                if (devices.TryGetValue(ev.id, out var readyDevice))
                {
                    readyDevice.isReady = true;
                    readyDevice.NotifyReady();
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
                    connectedDevice.NotifyConnected();// ✅ notify listeners
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
                    disconnectedDevice.NotifyDisconnected();// ✅ notify listeners
                }
                break;

            case "data":
                if (devices.TryGetValue(ev.id, out var device))
                {
                    HandleDeviceData(device, ev);
                }
                break;
        }

        NotifyDevicesUpdated();
    }


    private void AddOrUpdateDevice(BleEvent ev)
    {
        if (string.IsNullOrEmpty(ev.id)) return;

        if (!devices.TryGetValue(ev.id, out var device))
        {
            DeviceType detectedType = BleDeviceProfiles.DetectDeviceType(ev.deviceType, ev.name);
            if (TrustedDeviceStore.TryGet(ev.id, out var record) && detectedType == DeviceType.Unknown)
                detectedType = record.type;

            device = new BleDevice
            {
                id = ev.id,
                name = ev.name,
                type = detectedType,
                isConnected = false,
                rssi = ev.rssi,
                isTrusted = TrustedDeviceStore.IsTrusted(ev.id),
                connectionNote = string.Empty
            };

            devices[ev.id] = device;
        }
        else
        {
            if (!string.IsNullOrEmpty(ev.name)) device.name = ev.name;
            var detectedType = BleDeviceProfiles.DetectDeviceType(ev.deviceType, ev.name ?? device.name);

            if (detectedType == DeviceType.Unknown && TrustedDeviceStore.TryGet(ev.id, out var record))
                detectedType = record.type;

            if (detectedType != DeviceType.Unknown)
                device.type = detectedType;

            device.isTrusted = TrustedDeviceStore.IsTrusted(ev.id);

            if (ev.eventType == "scanResult")
                device.rssi = ev.rssi;
        }
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


        // Internal parser (optional)
        switch (device.type)
        {

            case DeviceType.Unknown:
                Debug.LogWarning($"[BLEManager] Unknown device type for {device.name}");
                break;
            default:

                if (dataParsers[device.type].TryParse(raw, out IParsedData result))
                {
                    device.NotifyData(result);

                }
                else
                {
                    Debug.LogWarning($"[BLEManager] Unable to parse data {device.name}");
                }
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
            NotifyDevicesUpdated();
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

        if (BLEManager.Instance.Devices.TryGetValue(deviceId, out var device))
        {
            device.StartMeasurement();
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
        device.NotifyMeasurementStateChanged(newState);
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
            NotifyDevicesUpdated();
            return;
        }

        if (BLEPlugin.Instance == null)
        {
            device.connectionNote = "BLE plugin unavailable";
            device.isAutoConnecting = false;
            NotifyDevicesUpdated();
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
        NotifyDevicesUpdated();
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
}
