package com.gaminations.bleplugin;

import android.bluetooth.*;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.util.UUID;

/**
 * BioPot EEG device implementation.
 * Flow:
 *  - onConnectionStateChange: request MTU, announce connected
 *  - onMtuChanged: discover services
 *  - onServicesDiscovered: get control & data chars, enable notifications
 *  - onDescriptorWrite: when CCCD written -> auto start measurement after small delay
 *  - onCharacteristicChanged: handle incoming EEG packets (Base64 encoded)
 */
class BioPotDevice extends BleDeviceBase {
    private static final String TAG = "BioPotDevice";

    private static final UUID SERVICE_EEG    = UUID.fromString("0000fff0-0000-1000-8000-00805f9b34fb");
    private static final UUID CHAR_CTRL_FFF2= UUID.fromString("0000fff2-0000-1000-8000-00805f9b34fb");
    private static final UUID CHAR_DATA_FFF4= UUID.fromString("0000fff4-0000-1000-8000-00805f9b34fb");

    BioPotDevice(BluetoothDevice device, String unityObjectName) {
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
                    // Request larger MTU for EEG payloads; onMtuChanged will call discoverServices()
                    g.requestMtu(251);
                    sendUnity("{\"eventType\":\"connected\",\"deviceType\":\"BioPot\",\"id\":\"" + device.getAddress() + "\"}");
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                    Log.d(TAG, "BLE::UnityMsg:onConnectionStateChange: DISCONNECTED");
                    cleanup();
                    sendUnity("{\"eventType\":\"disconnected\",\"deviceType\":\"BioPot\",\"id\":\"" + device.getAddress() + "\"}");
                }
            }

            @Override
            public void onMtuChanged(BluetoothGatt g, int mtu, int status) {
                Log.d(TAG, "BLE::UnityMsg:onMtuChanged: " + mtu + " status=" + status);
                // Continue with service discovery now that MTU is negotiated
                g.discoverServices();
            }

            @Override
            public void onServicesDiscovered(BluetoothGatt g, int status) {
                Log.d(TAG, "BLE::UnityMsg:onServicesDiscovered status=" + status);
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    sendUnity("{\"eventType\":\"error\",\"message\":\"service discovery failed\",\"id\":\"" + device.getAddress() + "\"}");
                    return;
                }
                BluetoothGattService eegService = g.getService(SERVICE_EEG);
                if (eegService == null) {
                    sendUnity("{\"eventType\":\"error\",\"message\":\"EEG service not found\",\"id\":\"" + device.getAddress() + "\"}");
                    return;
                }

                // cache control & data characteristics
                controlChar = eegService.getCharacteristic(CHAR_CTRL_FFF2);
                dataChar = eegService.getCharacteristic(CHAR_DATA_FFF4);

                if (dataChar == null) {
                    sendUnity("{\"eventType\":\"error\",\"message\":\"data characteristic not found\",\"id\":\"" + device.getAddress() + "\"}");
                    return;
                }

                // Enable notifications for EEG streaming
                enableNotifications();
            }

            @Override
            public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor descriptor, int status) {
                Log.d(TAG, "BLE::UnityMsg:onDescriptorWrite: " + descriptor.getUuid() + " status=" + status);
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    sendUnity("{\"eventType\":\"error\",\"message\":\"descriptor write failed\",\"id\":\"" + device.getAddress() + "\"}");
                    return;
                }

                // CCCD written -> start measurement automatically (short delay)
                if (CLIENT_CHAR_CONFIG.equals(descriptor.getUuid())) {
                    new Handler(Looper.getMainLooper()).postDelayed(() -> startMeasurement(), 200);
                }
            }

            @Override
            public void onCharacteristicWrite(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
                Log.d(TAG, "BLE::UnityMsg:onCharacteristicWrite: " + c.getUuid() + " status=" + status);
                if (CHAR_CTRL_FFF2.equals(c.getUuid())) {
                    sendUnity("{\"eventType\":\"ctrlWritten\",\"id\":\"" + device.getAddress() + "\"}");
                }
            }

            @Override
            public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
                if (CHAR_DATA_FFF4.equals(c.getUuid())) {
                    // Delegate to base handler which encodes to Base64 and sends JSON
                    handleDataChanged(c);
                }
            }
        };
    }

    // Public control methods (callable from Unity via JNI)
    public void startMeasurement() { writeControl(new byte[]{0x01}, "start"); }
    public void stopMeasurement()  { writeControl(new byte[]{0x02}, "stop"); }
    public void pauseMeasurement() { writeControl(new byte[]{0x00}, "pause"); }

    @Override
    protected String getTag() { return TAG; }
}
