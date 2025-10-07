package com.gaminations.bleplugin;

import android.app.Activity;
import android.bluetooth.*;
import android.os.Handler;
import android.os.Looper;
import android.util.Base64;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.util.UUID;

/**
 * Common base for device-specific classes.
 *
 * Responsibilities:
 *  - Store BluetoothDevice, BluetoothGatt, common characteristics
 *  - Provide helpers to enable notifications and write control values
 *  - sendUnity() helper to send JSON back to Unity
 *  - cleanup() to release resources
 *
 * Device subclasses must implement getCallback() and getTag().
 */
abstract class BleDeviceBase {
    protected final BluetoothDevice device;
    protected BluetoothGatt gatt;
    protected final String unityObjectName;

    // Optional characteristics that most devices use
    protected BluetoothGattCharacteristic controlChar;
    protected BluetoothGattCharacteristic dataChar;

    // Standard descriptor UUID for Client Characteristic Configuration (notifications)
    protected static final UUID CLIENT_CHAR_CONFIG =
            UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    // Small handler to post delayed actions on main thread
    protected final Handler mainHandler = new Handler(Looper.getMainLooper());

    BleDeviceBase(BluetoothDevice device, String unityObjectName) {
        this.device = device;
        this.unityObjectName = unityObjectName;
    }

    /**
     * Connect to the BLE device. `device.connectGatt` returns a BluetoothGatt instance,
     * and the provided callback (from getCallback()) will handle the rest of the lifecycle.
     *
     * Note: On Android 12+ you must ensure BLUETOOTH_CONNECT permission was granted before calling.
     */
    public void connect(Activity act) {
        // keep returned gatt reference for immediate operations (may be null on some implementations)
        gatt = device.connectGatt(act, false, getCallback());
    }

    /**
     * Disconnect & free resources. Subclasses shouldn't need to override this in most cases.
     */
    public void disconnect() {
        cleanup();
    }

    /**
     * Enable notifications for the currently assigned dataChar.
     * Will write the CCCD descriptor. The subclass's callback must handle onDescriptorWrite to continue.
     */
    protected void enableNotifications() {
        if (gatt == null || dataChar == null) {
            Log.e(getTag(), "enableNotifications: missing gatt or dataChar");
            return;
        }

        Log.d(getTag(), "BLE::UnityMsg:Enabling notifications for " + dataChar.getUuid());
        gatt.setCharacteristicNotification(dataChar, true);

        BluetoothGattDescriptor desc = dataChar.getDescriptor(CLIENT_CHAR_CONFIG);
        if (desc != null) {
            desc.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
            boolean ok = gatt.writeDescriptor(desc);
            Log.d(getTag(), "writeDescriptor called, success=" + ok);
        } else {
            Log.e(getTag(), "enableNotifications: descriptor not found");
        }
    }

    /**
     * Write bytes to control characteristic (start/stop/pause commands).
     */
    protected void writeControl(byte[] value, String action) {
        if (gatt == null) {
            Log.e(getTag(), "writeControl: gatt is null (" + action + ")");
            sendUnity("{\"eventType\":\"error\",\"message\":\"gatt null\",\"id\":\"" + device.getAddress() + "\"}");
            return;
        }
        if (controlChar == null) {
            Log.e(getTag(), "writeControl: controlChar is null (" + action + ")");
            sendUnity("{\"eventType\":\"error\",\"message\":\"controlChar null\",\"id\":\"" + device.getAddress() + "\"}");
            return;
        }

        controlChar.setValue(value);
        controlChar.setWriteType(BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT);
        boolean ok = gatt.writeCharacteristic(controlChar);
        Log.d(getTag(), String.format("BLE::UnityMsg:writeControl (%s) -> %s success=%s", action, bytesToHex(value), ok));
        if (!ok) {
            sendUnity("{\"eventType\":\"error\",\"message\":\"write failed\",\"id\":\"" + device.getAddress() + "\"}");
        }
    }

    /**
     * Called when a data notification arrives. Default implementation converts payload to Base64
     * and sends a generic 'data' event. Subclasses can override or call handleDataChanged(c).
     */
    protected void handleDataChanged(BluetoothGattCharacteristic c) {
        byte[] val = c.getValue();
        String b64 = Base64.encodeToString(val, Base64.NO_WRAP);
        String json = String.format("{\"eventType\":\"data\",\"id\":\"%s\",\"uuid\":\"%s\",\"value\":\"%s\"}",
                device.getAddress(), c.getUuid().toString(), b64);
        sendUnity(json);
    }

    /**
     * Cleanup resources safely.
     */
    protected void cleanup() {
        try {
            if (gatt != null) {
                gatt.disconnect();
                gatt.close();
            }
        } catch (Exception ignored) {}
        gatt = null;
        controlChar = null;
        dataChar = null;
    }

    /**
     * Helper for logging bytes as hex for debugging.
     */
    protected String bytesToHex(byte[] bytes) {
        if (bytes == null) return "";
        StringBuilder sb = new StringBuilder();
        for (byte b : bytes) sb.append(String.format("%02X ", b));
        return sb.toString().trim();
    }

    /**
     * Send JSON to Unity (OnNativeCallback).
     */
    protected void sendUnity(final String json) {
        Log.d(getTag(), "BLE::UnityMsg: " + json);
        UnityPlayer.UnitySendMessage(unityObjectName, "OnNativeCallback", json);
    }

    // Each device implements its BluetoothGattCallback (device-specific behavior).
    protected abstract BluetoothGattCallback getCallback();
    protected abstract String getTag();
}
