using System.Collections.Generic;
using UnityEngine;


public enum DeviceType
{
    Unknown = 0,
    Movella = 1,
    BioPot = 2,
}

public enum MeasurementState
{
    Idle,
    Sampling,
    Paused
}

[System.Serializable]
public class BleEvent
{
    public string id;
    public string name;
    public string eventType;
    public string value;
    public string deviceType; // still string (comes from native JSON)
    public string state;
    public int rssi;
}

[System.Serializable]
public class BleDevice
{
    public string id;
    public string name;
    public DeviceType type;
    public bool isConnected;
    public int rssi;

    public bool isReady;
    public bool isAutoConnecting;
    public bool isTrusted;
    public string connectionNote;
    public MeasurementState measurementState = MeasurementState.Idle;
    public bool autoConnectFailed;
    private IDataParser parser;



    private readonly List<IBLEDeviceListener> listeners = new();

    public void AddListener(IBLEDeviceListener listener)
    {
        if (listener != null && !listeners.Contains(listener))
            listeners.Add(listener);
    }

    public void RemoveListener(IBLEDeviceListener listener)
    {
        listeners.Remove(listener);
    }


    public void SetParser(IDataParser dataParser)
    {
        parser = dataParser;
    }

    public string GetDeviceDisplayName()
    {
        var profile = BleDeviceProfiles.TryGetProfile(type);
        return profile.DisplayName;
    }

    public void HandleRawData(byte[] raw)
    {
        if (parser != null && parser.TryParse(raw, out IParsedData parsed))
        {
            NotifyData(parsed);
        }
        else
        {
            Debug.LogWarning($"[{name}] Failed to parse data.");
        }
    }


    // ✅ These will replace BLEManager's Notify methods:
    public void NotifyConnected()
    {
        foreach (var l in listeners)
            l.OnConnected(this);
    }

    public void NotifyDisconnected()
    {
        foreach (var l in listeners)
            l.OnDisconnected(this);
    }

    public void NotifyReady()
    {
        foreach (var l in listeners)
            l.OnReady(this);
    }

    public void NotifyData(IParsedData parsedData)
    {
        foreach (var l in listeners)
            l.OnData(this, parsedData);
    }

    public void NotifyMeasurementStateChanged(MeasurementState newState)
    {
        foreach (var l in listeners)
            l.OnMeasurementStateChanged(this, newState);
    }


    public void StartMeasurement()
    {
        if (!isConnected || measurementState == MeasurementState.Sampling)
            return;

        var profile = BleDeviceProfiles.TryGetProfile(type);
        if (profile?.StartCommand == null)
            return;

        BLEPlugin.Instance.SendControl(id, profile.StartCommand, "start");
        measurementState = MeasurementState.Sampling;
    }

    public void StopMeasurement()
    {
        if (!isConnected || measurementState == MeasurementState.Idle)
            return;

        var profile = BleDeviceProfiles.TryGetProfile(type);
        if (profile?.StopCommand == null)
            return;

        BLEPlugin.Instance.SendControl(id, profile.StopCommand, "stop");
        measurementState = MeasurementState.Idle;
    }

    public void PauseMeasurement()
    {
        if (!isConnected || measurementState != MeasurementState.Sampling)
            return;

        var profile = BleDeviceProfiles.TryGetProfile(type);
        if (profile?.PauseCommand == null)
            return;

        BLEPlugin.Instance.SendControl(id, profile.PauseCommand, "pause");
        measurementState = MeasurementState.Paused;
    }

    public void SetMeasurementState(MeasurementState newState)
    {

        if (measurementState == newState)
            return;

        if (newState == MeasurementState.Sampling || newState == MeasurementState.Paused)
            isReady = false;

        measurementState = newState;
        NotifyMeasurementStateChanged(newState);
    }


    public void Connect(bool initiatedByAuto = false)
    {
        var profile = BleDeviceProfiles.TryGetProfile(type);
        if (profile == null)
        {
            connectionNote = "Profile unavailable";
            isAutoConnecting = false;
            BLEManager.Instance.NotifyDevicesUpdated();
            return;
        }

        if (BLEPlugin.Instance == null)
        {
            connectionNote = "BLE plugin unavailable";
            isAutoConnecting = false;
            BLEManager.Instance.NotifyDevicesUpdated();
            return;
        }

        isAutoConnecting = initiatedByAuto;

        if (initiatedByAuto)
        {
            autoConnectFailed = false;
            connectionNote = "Auto-connecting...";
            BLEManager.Instance.ScheduleAutoConnectTimeout(id);
        }
        else
        {
            autoConnectFailed = true;
            BLEManager.Instance.CancelAutoConnectTimeout(id);
            connectionNote = "Connecting...";
        }

        BLEPlugin.Instance.Connect(id, profile);
        BLEManager.Instance.NotifyDevicesUpdated();
    }

    public void DisConnect()
    {
        TrustedDeviceStore.SetLastConnectionState(id, type, false);
        BLEPlugin.Instance.Disconnect(id);
    }
}
