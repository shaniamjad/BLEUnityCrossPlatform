package com.gaminations.bleplugin;

import android.Manifest;
import android.app.Activity;
import android.bluetooth.*;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanResult;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.util.Base64;
import android.util.Log;

import androidx.core.app.ActivityCompat;

import com.unity3d.player.UnityPlayer;

import org.json.JSONException;

import java.util.HashMap;
import java.util.Locale;
import java.util.Map;

/**
 * Central BLE manager used by Unity.
 * Responsibilities:
 *  - Initialize Bluetooth adapter / scanner
 *  - Start / stop scanning and forward scan results to Unity
 *  - Create device instances (BioPot / Movella) and track them
 *  - Connect / disconnect devices
 */
public class BleManager {
    private static final String TAG = "BleManager";
    private static final String UNITY_GAMEOBJECT = "UnityBLE";
    private static String unityObjectName = UNITY_GAMEOBJECT;

    private static BluetoothManager btManager;
    private static BluetoothAdapter btAdapter;
    private static BluetoothLeScanner scanner;

    // Map of deviceAddress -> device instance
    private static final Map<String, BleDeviceBase> devices = new HashMap<>();

    // ----------------- Public API -----------------
    public static void RequestBlePermissions(String unityObject, String unityMethod) {
        PermissionFragment.requestPermissions(unityObject, unityMethod);
    }

    /**
     * Initialize with Unity GameObject name (where callbacks will be sent).
     */
    public static void init(String unityObject) {
        unityObjectName = unityObject;
        Activity act = UnityPlayer.currentActivity;
        btManager = (BluetoothManager) act.getSystemService(Context.BLUETOOTH_SERVICE);
        btAdapter = btManager != null ? btManager.getAdapter() : null;
        if (btAdapter != null) {
            scanner = btAdapter.getBluetoothLeScanner();
        }
        sendUnity("{\"eventType\":\"init\"}");
    }

    /**
     * Start scanning for BLE devices. Emits scanResult events to Unity.
     */
    public static void startScan() {
        if (scanner == null) {
            sendUnity("{\"eventType\":\"error\",\"message\":\"no scanner\"}");
            return;
        }

        // On Android 12+, need BLUETOOTH_SCAN permission to call startScan
        Activity act = UnityPlayer.currentActivity;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            if (ActivityCompat.checkSelfPermission(act, Manifest.permission.BLUETOOTH_SCAN) != PackageManager.PERMISSION_GRANTED) {
                sendUnity("{\"eventType\":\"error\",\"message\":\"missing BLUETOOTH_SCAN permission\"}");
                return;
            }
        }

        scanner.startScan(scanCallback);
        sendUnity("{\"eventType\":\"scanStarted\"}");
    }

    public static void stopScan() {
        if (scanner != null) {
            scanner.stopScan(scanCallback);
        }
        sendUnity("{\"eventType\":\"scanStopped\"}");
    }

    private static final ScanCallback scanCallback = new ScanCallback() {
        @Override
        public void onScanResult(int callbackType, ScanResult result) {
            BluetoothDevice d = result.getDevice();
            String id = (d != null && d.getAddress() != null) ? d.getAddress() : "";
            String name = (d != null && d.getName() != null) ? d.getName() : "";
            int rssi = result.getRssi();

            String json = String.format(Locale.US,
                    "{\"eventType\":\"scanResult\",\"id\":\"%s\",\"name\":\"%s\",\"deviceType\":\"\",\"rssi\":%d}",
                    id, name, rssi);
            sendUnity(json);
        }
    };

    /**
     * Create and connect a device using configuration supplied from Unity.
     */
    public static void connect(String deviceAddress, String profileJson) {
        if (devices.containsKey(deviceAddress)) {
            sendUnity("{\"eventType\":\"error\",\"message\":\"already connected\"}");
            return;
        }
        if (btAdapter == null) {
            sendUnity("{\"eventType\":\"error\",\"message\":\"Bluetooth not initialized\"}");
            return;
        }

        Activity act = UnityPlayer.currentActivity;
        // Android 12+ requires BLUETOOTH_CONNECT for connectGatt
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            if (ActivityCompat.checkSelfPermission(act, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                sendUnity("{\"eventType\":\"error\",\"message\":\"missing BLUETOOTH_CONNECT permission\"}");
                return;
            }
        }

        BluetoothDevice device = btAdapter.getRemoteDevice(deviceAddress);
        if (device == null) {
            sendUnity("{\"eventType\":\"error\",\"message\":\"device not found\"}");
            return;
        }

        DeviceConfig config;
        try {
            if (profileJson == null) {
                throw new JSONException("profile missing");
            }
            config = DeviceConfig.fromJson(profileJson);
        } catch (JSONException e) {
            sendUnity(String.format(Locale.US,
                    "{\"eventType\":\"error\",\"message\":\"invalid profile\",\"detail\":\"%s\"}",
                    e.getMessage()));
            return;
        }

        BleDeviceBase bleDevice = new ConfigurableBleDevice(device, unityObjectName, config);
        devices.put(deviceAddress, bleDevice);
        bleDevice.connect(act);
    }

    /**
     * Disconnect a connected device. Removes the device from tracking map.
     */
    public static void disconnect(String deviceAddress) {
        BleDeviceBase bleDevice = devices.remove(deviceAddress);
        if (bleDevice != null) {
            bleDevice.disconnect();
            sendUnity("{\"eventType\":\"disconnected\",\"id\":\"" + deviceAddress + "\"}");
        } else {
            sendUnity("{\"eventType\":\"error\",\"message\":\"device not connected\"}");
        }
    }

    /**
     * Disconnect all devices and clear tracking map.
     */
    public static void disconnectAll() {
        for (String key : devices.keySet().toArray(new String[0])) {
            BleDeviceBase d = devices.get(key);
            if (d != null) d.disconnect();
        }
        devices.clear();
        sendUnity("{\"eventType\":\"allDisconnected\"}");
    }

    public static void startMeasurement(String deviceAddress) {
        BleDeviceBase device = devices.get(deviceAddress);
        if (device instanceof ConfigurableBleDevice) {
            ((ConfigurableBleDevice) device).startMeasurement();
        }
    }

    public static void stopMeasurement(String deviceAddress) {
        BleDeviceBase device = devices.get(deviceAddress);
        if (device instanceof ConfigurableBleDevice) {
            ((ConfigurableBleDevice) device).stopMeasurement();
        }
    }

    public static void pauseMeasurement(String deviceAddress) {
        BleDeviceBase device = devices.get(deviceAddress);
        if (device instanceof ConfigurableBleDevice) {
            ((ConfigurableBleDevice) device).pauseMeasurement();
        }
    }

    public static void sendControl(String deviceAddress, String base64Payload, String action) {
        BleDeviceBase device = devices.get(deviceAddress);
        if (device instanceof ConfigurableBleDevice && base64Payload != null) {
            try {
                byte[] payload = Base64.decode(base64Payload, Base64.DEFAULT);
                ((ConfigurableBleDevice) device).sendCustomControl(payload, action);
            } catch (IllegalArgumentException ex) {
                Log.e(TAG, "Invalid control payload", ex);
            }
        }
    }

    // ----------------- Helpers -----------------
    private static void sendUnity(final String json) {
        Log.d(TAG, "BLE::UnityMsg: " + json);
        UnityPlayer.UnitySendMessage(unityObjectName, "OnNativeCallback", json);
    }
}
