package com.gaminations.bleplugin;

import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothProfile;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.util.Locale;

class ConfigurableBleDevice extends BleDeviceBase {
    private static final String TAG = "ConfigurableBleDevice";
    private final DeviceConfig config;

    ConfigurableBleDevice(BluetoothDevice device, String unityObjectName, DeviceConfig config) {
        super(device, unityObjectName);
        this.config = config;
    }

    @Override
    protected BluetoothGattCallback getCallback() {
        return new BluetoothGattCallback() {
            @Override
            public void onConnectionStateChange(BluetoothGatt g, int status, int newState) {
                if (newState == BluetoothProfile.STATE_CONNECTED) {
                    Log.d(TAG, "BLE::UnityMsg:onConnectionStateChange: CONNECTED");
                    gatt = g;
                    if (config.requestMtu != null) {
                        g.requestMtu(config.requestMtu);
                    } else {
                        g.discoverServices();
                    }
                    sendUnity(String.format(Locale.US,
                            "{\"eventType\":\"connected\",\"deviceType\":\"%s\",\"id\":\"%s\"}",
                            config.deviceType, device.getAddress()));
                } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                    Log.d(TAG, "BLE::UnityMsg:onConnectionStateChange: DISCONNECTED");
                    cleanup();
                    sendUnity(String.format(Locale.US,
                            "{\"eventType\":\"disconnected\",\"deviceType\":\"%s\",\"id\":\"%s\"}",
                            config.deviceType, device.getAddress()));
                }
            }

            @Override
            public void onMtuChanged(BluetoothGatt g, int mtu, int status) {
                Log.d(TAG, "BLE::UnityMsg:onMtuChanged: " + mtu + " status=" + status);
                g.discoverServices();
            }

            @Override
            public void onServicesDiscovered(BluetoothGatt g, int status) {
                Log.d(TAG, "BLE::UnityMsg:onServicesDiscovered status=" + status);
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    sendUnity(String.format(Locale.US,
                            "{\"eventType\":\"error\",\"message\":\"service discovery failed\",\"id\":\"%s\"}",
                            device.getAddress()));
                    return;
                }

                BluetoothGattService service = config.serviceUuid != null ? g.getService(config.serviceUuid) : null;
                if (service == null) {
                    sendUnity(String.format(Locale.US,
                            "{\"eventType\":\"error\",\"message\":\"service not found\",\"id\":\"%s\"}",
                            device.getAddress()));
                    return;
                }

                controlChar = config.controlCharacteristicUuid != null
                        ? service.getCharacteristic(config.controlCharacteristicUuid) : null;
                dataChar = config.dataCharacteristicUuid != null
                        ? service.getCharacteristic(config.dataCharacteristicUuid) : null;

                if (dataChar != null) {
                    enableNotifications();
                }

                if (config.emitReadyEvent) {
                    sendUnity(String.format(Locale.US,
                            "{\"eventType\":\"ready\",\"deviceType\":\"%s\",\"id\":\"%s\"}",
                            config.deviceType, device.getAddress()));
                }
            }

            @Override
            public void onDescriptorWrite(BluetoothGatt g, BluetoothGattDescriptor descriptor, int status) {
                Log.d(TAG, "BLE::UnityMsg:onDescriptorWrite: " + descriptor.getUuid() + " status=" + status);
                if (status != BluetoothGatt.GATT_SUCCESS) {
                    sendUnity(String.format(Locale.US,
                            "{\"eventType\":\"error\",\"message\":\"descriptor write failed\",\"id\":\"%s\"}",
                            device.getAddress()));
                    return;
                }

                if (CLIENT_CHAR_CONFIG.equals(descriptor.getUuid()) && config.autoStartOnNotification && config.startCommand != null) {
                    Handler handler = new Handler(Looper.getMainLooper());
//                    handler.postDelayed(() -> startMeasurement(), Math.max(config.notificationStartDelayMs, 0));
                }
            }

            @Override
            public void onCharacteristicWrite(BluetoothGatt g, BluetoothGattCharacteristic c, int status) {
                Log.d(TAG, "BLE::UnityMsg:onCharacteristicWrite: " + c.getUuid() + " status=" + status);
                if (controlChar != null && controlChar.getUuid().equals(c.getUuid())) {
                    sendUnity(String.format(Locale.US,
                            "{\"eventType\":\"controlWritten\",\"deviceType\":\"%s\",\"id\":\"%s\"}",
                            config.deviceType, device.getAddress()));
                }
            }

            @Override
            public void onCharacteristicChanged(BluetoothGatt g, BluetoothGattCharacteristic c) {
                if (dataChar != null && dataChar.getUuid().equals(c.getUuid())) {
                    handleDataChanged(c);
                }
            }
        };
    }

    public void startMeasurement() {
        if (config.startCommand != null) {
            writeControl(config.startCommand, "start");
        }
    }

    public void stopMeasurement() {
        if (config.stopCommand != null) {
            writeControl(config.stopCommand, "stop");
        }
    }

    public void pauseMeasurement() {
        if (config.pauseCommand != null) {
            writeControl(config.pauseCommand, "pause");
        }
    }

    public void sendCustomControl(byte[] payload, String action) {
        if (payload != null && payload.length > 0) {
            writeControl(payload, action);
        }
    }

    @Override
    protected String getTag() {
        return TAG + "-" + config.deviceType;
    }
}
