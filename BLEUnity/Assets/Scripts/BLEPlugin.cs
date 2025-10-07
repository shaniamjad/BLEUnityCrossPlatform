using System;
using UnityEngine;

public class BLEPlugin : MonoBehaviour
{
    public static BLEPlugin Instance;
    // Android helpers
    private const string ANDROID_CLASS = "com.gaminations.bleplugin.BleManager";

    public bool PermissionGranted { get; private set; }


    void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }



    public void RequestPermissions()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var jc = new AndroidJavaClass(ANDROID_CLASS))
        {
            jc.CallStatic("RequestBlePermissions", gameObject.name, "OnPermissionResult");
        }
#endif
    }

    // Called by Android Fragment
    public void OnPermissionResult(string status)
    {
        Debug.Log("BLE Permission Result: " + status);
        if (status == "granted")
        {
            // Start BLE scan/connection
            PermissionGranted = true;
        }
        else
        {
            // Show message to user
            PermissionGranted = false;
        }
    }



    // Called by native code
    public void OnNativeCallback(string json)
    {
        Debug.Log($"BLE native -> unity: {json}");
        // parse JSON and raise events as needed
        BLEManager.Instance?.HandleNativeEvent(json);

    }



    public void Init()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS)) {
            jc.CallStatic("init", GetUnityGameObjectName());
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_init(GetUnityGameObjectName());
#endif
    }

    public void StartScan()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS)) jc.CallStatic("startScan");
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_startScan();
#endif
    }

    public void StopScan()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS)) jc.CallStatic("stopScan");
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_stopScan();
#endif
    }

    public void Connect(string deviceId, BleDeviceProfileDefinition profile)
    {
        if (profile == null)
        {
            Debug.LogError($"[BLEPlugin] No profile provided for {deviceId}");
            return;
        }

        string payload = profile.ToJsonPayload();

#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS))
        {
            jc.CallStatic("connect", deviceId, payload);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_connect(deviceId, payload);
#endif
    }

    public void StartMeasurement(string deviceId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS))
        {
            jc.CallStatic("startMeasurement", deviceId);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_startMeasurement(deviceId);
#endif
    }

    public void StopMeasurement(string deviceId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS))
        {
            jc.CallStatic("stopMeasurement", deviceId);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_stopMeasurement(deviceId);
#endif
    }

    public void PauseMeasurement(string deviceId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS))
        {
            jc.CallStatic("pauseMeasurement", deviceId);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_pauseMeasurement(deviceId);
#endif
    }

    public void SendControl(string deviceId, byte[] payload, string action)
    {
        if (payload == null || payload.Length == 0)
            return;

        string base64 = Convert.ToBase64String(payload);
        action ??= string.Empty;

#if UNITY_ANDROID && !UNITY_EDITOR
        using (var jc = new AndroidJavaClass(ANDROID_CLASS))
        {
            jc.CallStatic("sendControl", deviceId, base64, action);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_sendControl(deviceId, base64, action);
#endif
    }

    public void Disconnect(string deviceId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var jc = new AndroidJavaClass(ANDROID_CLASS))
        {
            jc.CallStatic("disconnect", deviceId);
        }
#elif UNITY_IOS && !UNITY_EDITOR
        _DisconnectDevice(deviceId); // bridge to Obj-C
#endif
    }



    public void ReadCharacteristic(string serviceUuid, string charUuid)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS)) jc.CallStatic("readCharacteristic", serviceUuid, charUuid);
#elif UNITY_IOS && !UNITY_EDITOR
        _ble_readCharacteristic(serviceUuid, charUuid);
#endif
    }

    public void WriteCharacteristic(string serviceUuid, string charUuid, byte[] data)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using(var jc = new AndroidJavaClass(ANDROID_CLASS)) jc.CallStatic("writeCharacteristic", serviceUuid, charUuid, Convert.ToBase64String(data));
#elif UNITY_IOS && !UNITY_EDITOR
        string b64 = Convert.ToBase64String(data);
        _ble_writeCharacteristic(serviceUuid, charUuid, b64);
#endif
    }

    // iOS native function signatures
#if UNITY_IOS && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_init(string unityObjectName);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_startScan();
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_stopScan();
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_connect(string deviceId, string profileJson);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_startMeasurement(string deviceId);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_stopMeasurement(string deviceId);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_pauseMeasurement(string deviceId);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_sendControl(string deviceId, string base64Payload, string action);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_readCharacteristic(string serviceUuid, string charUuid);
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void _ble_writeCharacteristic(string serviceUuid, string charUuid, string base64Data);
#endif

    private string GetUnityGameObjectName() => gameObject.name;
}
