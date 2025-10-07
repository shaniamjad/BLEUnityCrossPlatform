using System;
using Newtonsoft.Json;

/// <summary>
/// Cross-platform definition of a BLE device profile. Describes the service/characteristic layout
/// and control commands required to drive a device. Serialized payload is sent to native plugins
/// so both Android and iOS share the exact same configuration.
/// </summary>
public sealed class BleDeviceProfileDefinition
{
    private readonly byte[] startCommand;
    private readonly byte[] stopCommand;
    private readonly byte[] pauseCommand;

    public BleDeviceProfileDefinition(
        DeviceType type,
        Guid serviceUuid,
        Guid controlCharacteristicUuid,
        Guid dataCharacteristicUuid,
        int? requestMtu,
        bool emitReadyEvent,
        bool autoStartMeasurement,
        TimeSpan autoStartDelay,
        byte[] startCommand,
        byte[] stopCommand,
        byte[] pauseCommand)
    {
        Type = type;
        ServiceUuid = serviceUuid;
        ControlCharacteristicUuid = controlCharacteristicUuid;
        DataCharacteristicUuid = dataCharacteristicUuid;
        RequestMtu = requestMtu;
        EmitReadyEvent = emitReadyEvent;
        AutoStartMeasurement = autoStartMeasurement;
        AutoStartDelay = autoStartDelay;
        this.startCommand = startCommand;
        this.stopCommand = stopCommand;
        this.pauseCommand = pauseCommand;
    }

    public DeviceType Type { get; }
    public Guid ServiceUuid { get; }
    public Guid ControlCharacteristicUuid { get; }
    public Guid DataCharacteristicUuid { get; }
    public int? RequestMtu { get; }
    public bool EmitReadyEvent { get; }
    public bool AutoStartMeasurement { get; }
    public TimeSpan AutoStartDelay { get; }

    public byte[] StartCommand => startCommand;
    public byte[] StopCommand => stopCommand;
    public byte[] PauseCommand => pauseCommand;

    /// <summary>
    /// Creates the payload expected by the native plugins.
    /// </summary>
    public string ToJsonPayload()
    {
        var payload = new BleDeviceProfilePayload
        {
            deviceType = Type.ToString(),
            serviceUuid = ServiceUuid.ToString(),
            controlCharacteristicUuid = ControlCharacteristicUuid.ToString(),
            dataCharacteristicUuid = DataCharacteristicUuid.ToString(),
            requestMtu = RequestMtu,
            emitReadyEvent = EmitReadyEvent,
            startCommand = startCommand != null ? Convert.ToBase64String(startCommand) : null,
            stopCommand = stopCommand != null ? Convert.ToBase64String(stopCommand) : null,
            pauseCommand = pauseCommand != null ? Convert.ToBase64String(pauseCommand) : null,
            autoStartOnNotification = false, // handled in C# now
            notificationStartDelayMs = 0
        };

        return JsonConvert.SerializeObject(payload);
    }

    private class BleDeviceProfilePayload
    {
        // JSON naming must match DeviceConfig on native side
        [JsonProperty("deviceType")] public string deviceType;
        [JsonProperty("serviceUuid")] public string serviceUuid;
        [JsonProperty("controlCharacteristicUuid")] public string controlCharacteristicUuid;
        [JsonProperty("dataCharacteristicUuid")] public string dataCharacteristicUuid;
        [JsonProperty("requestMtu")] public int? requestMtu;
        [JsonProperty("emitReadyEvent")] public bool emitReadyEvent;
        [JsonProperty("autoStartOnNotification")] public bool autoStartOnNotification;
        [JsonProperty("notificationStartDelayMs")] public int notificationStartDelayMs;
        [JsonProperty("startCommand")] public string startCommand;
        [JsonProperty("stopCommand")] public string stopCommand;
        [JsonProperty("pauseCommand")] public string pauseCommand;
    }
}
