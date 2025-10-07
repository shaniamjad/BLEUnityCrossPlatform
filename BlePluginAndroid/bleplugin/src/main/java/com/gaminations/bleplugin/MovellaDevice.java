package com.gaminations.bleplugin;

import android.bluetooth.*;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.util.UUID;

/**
 * Movella "Dot" device implementation.
 * Flow is nearly identical to BioPot, but uses different UUIDs and command payloads.
 */
class MovellaDevice extends BleDeviceBase {
    private static final String TAG = "MovellaDevice";

    private static final UUID DOT_SERVICE = UUID.fromString("15172000-4947-11e9-8646-d663bd873d93");
    private static final UUID DOT_CHARACTER = UUID.fromString("15172001-4947-11e9-8646-d663bd873d93");
    private static final UUID DOT_DATA_CHARACTER = UUID.fromString("15172003-4947-11e9-8646-d663bd873d93");

    MovellaDevice(BluetoothDevice device, String unityObjectName) {
        super(device, unityObjectName);
    }

    @Override
    protected BluetoothGattCallback getCallback() {
        return new BluetoothGattCallback() {
            @Override
            public void onConnectionStateChange(BluetoothGatt g, int status, int newState) {
                if (newState == BluetoothProfile.STATE_CONNECTED) {
                    Log.d(TAG, "BLE::UnityMsg:onConnectionStateChange: CONNECTED");
                    gatt = g;
                    gatt.discoverServices();
                    sendUnity("{\"eventType\":\"connected\",\"deviceType\":\"Movella\",\"id\":\"" + device.getAddress() + "\"}");
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                    Log.d(TAG, "BLE::UnityMsg:onConnectionStateChange: DISCONNECTED");
                    cleanup();
                    sendUnity("{\"eventType\":\"disconnected\",\"deviceType\":\"Movella\",\"id\":\"" + device.getAddress() + "\"}");
                }
            }

            @Override
            public void onServicesDiscovered(BluetoothGatt g, int status) {
                Log.d(TAG, "BLE::UnityMsg:onServicesDiscovered status=" + status);
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    sendUnity("{\"eventType\":\"error\",\"message\":\"service discovery failed\",\"id\":\"" + device.getAddress() + "\"}");
                    return;
                }

                BluetoothGattService dotService = g.getService(DOT_SERVICE);
                if (dotService == null) {
                    sendUnity("{\"eventType\":\"error\",\"message\":\"Dot service not found\",\"id\":\"" + device.getAddress() + "\"}");
                    return;
                }

                controlChar = dotService.getCharacteristic(DOT_CHARACTER);
                dataChar = dotService.getCharacteristic(DOT_DATA_CHARACTER);

                if (dataChar != null) enableNotifications();
                sendUnity("{\"eventType\":\"ready\",\"deviceType\":\"Movella\",\"id\":\"" + device.getAddress() + "\"}");
            }

            @Override
            public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor descriptor, int status) {
                Log.d(TAG, "BLE::UnityMsg:onDescriptorWrite: " + descriptor.getUuid() + " status=" + status);
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    sendUnity("{\"eventType\":\"error\",\"message\":\"descriptor write failed\",\"id\":\"" + device.getAddress() + "\"}");
                    return;
                }
                if (CLIENT_CHAR_CONFIG.equals(descriptor.getUuid())) {
                    // Start measurement automatically a bit later (some devices require a delay)
                    new Handler(Looper.getMainLooper()).postDelayed(() -> startMeasurement(), 300);
                }
            }

            @Override
            public void onCharacteristicWrite(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
                Log.d(TAG, "BLE::UnityMsg:onCharacteristicWrite: " + c.getUuid() + " status=" + status);
                if (DOT_CHARACTER.equals(c.getUuid())) {
                    sendUnity("{\"eventType\":\"dotCommandWritten\",\"id\":\"" + device.getAddress() + "\"}");
                }
            }

            @Override
            public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
                if (DOT_DATA_CHARACTER.equals(c.getUuid())) {
                    handleDataChanged(c);
                } else {
                    Log.d(TAG, "BLE::UnityMsg:onCharacteristicChanged for unexpected uuid: " + c.getUuid());
                }
            }
        };
    }

    // Movella-specific control payloads
    public void startMeasurement() { writeControl(new byte[]{0x01, 0x01, 0x02}, "start"); }
    public void pauseMeasurement() { writeControl(new byte[]{0x01, 0x00}, "pause"); }
    public void stopMeasurement()  { writeControl(new byte[]{0x01, 0x00}, "stop"); }

    @Override
    protected String getTag() { return TAG; }
}
