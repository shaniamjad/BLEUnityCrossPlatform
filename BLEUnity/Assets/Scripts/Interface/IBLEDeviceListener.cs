/// <summary>
/// Interface for classes that want to receive BLE device events.
/// </summary>
public interface IBLEDeviceListener
{
    /// <summary>Called when the device is connected.</summary>
    void OnConnected(BleDevice device);

    /// <summary>Called when the device is disconnected.</summary>
    void OnDisconnected(BleDevice device);

    /// <summary>Called when new data arrives from the device.</summary>
    void OnData(BleDevice device, byte[] rawData);
}
