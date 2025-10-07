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
import android.util.Log;

import androidx.core.app.ActivityCompat;

import com.unity3d.player.UnityPlayer;

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
            String deviceType = BleManager.getDeviceType(name);
            int rssi = result.getRssi();

            String json = String.format(Locale.US,
                    "{\"eventType\":\"scanResult\",\"id\":\"%s\",\"name\":\"%s\",\"deviceType\":\"%s\",\"rssi\":%d}",
                    id, name, deviceType, rssi);
            sendUnity(json);
        }
    };

    /**
     * Very simple heuristic to classify devices from their advertisement name.
     * You may alter this if you have different naming schemes.
     */
    private static String getDeviceType(String input) {
        if (input == null) return "";
        String lowerInput = input.toLowerCase();
        if (lowerInput.contains("biopot")) return "BioPot";
        if (lowerInput.contains("movella") || lowerInput.contains("dot")) return "Movella";
        return "";
    }

    /**
     * Create and connect a device of `deviceType` at `deviceAddress`.
     * Unity should pass deviceType determined from scanResult or UI choice.
     */
    public static void connect(String deviceAddress, String deviceType) {
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

        BleDeviceBase bleDevice;
        switch (deviceType) {
            case "biopot":
                bleDevice = new BioPotDevice(device, unityObjectName);
                break;
            case "movella":
                bleDevice = new MovellaDevice(device, unityObjectName);
                break;
            default:
                sendUnity("{\"eventType\":\"error\",\"message\":\"Unknown device type:" + deviceType +"\"}");
                return;
        }

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

    // ----------------- Helpers -----------------
    private static void sendUnity(final String json) {
        Log.d(TAG, "BLE::UnityMsg: " + json);
        UnityPlayer.UnitySendMessage(unityObjectName, "OnNativeCallback", json);
    }
}
