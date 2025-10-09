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
}
