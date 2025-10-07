using UnityEngine;


public enum DeviceType
{
    Unknown = 0,
    Movella = 1,
    BioPot = 2,
}



[System.Serializable]
public class BleEvent
{
    public string id;
    public string name;
    public string eventType;
    public string value;
    public string deviceType; // still string (comes from native JSON)
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
}


