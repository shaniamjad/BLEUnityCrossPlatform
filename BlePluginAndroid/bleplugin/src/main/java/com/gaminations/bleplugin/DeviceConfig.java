package com.gaminations.bleplugin;

import android.util.Base64;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.UUID;

class DeviceConfig {
    final String deviceType;
    final UUID serviceUuid;
    final UUID controlCharacteristicUuid;
    final UUID dataCharacteristicUuid;
    final Integer requestMtu;
    final byte[] startCommand;
    final byte[] stopCommand;
    final byte[] pauseCommand;
    final boolean emitReadyEvent;
    final boolean autoStartOnNotification;
    final int notificationStartDelayMs;

    private DeviceConfig(
            String deviceType,
            UUID serviceUuid,
            UUID controlCharacteristicUuid,
            UUID dataCharacteristicUuid,
            Integer requestMtu,
            byte[] startCommand,
            byte[] stopCommand,
            byte[] pauseCommand,
            boolean emitReadyEvent,
            boolean autoStartOnNotification,
            int notificationStartDelayMs) {
        this.deviceType = deviceType;
        this.serviceUuid = serviceUuid;
        this.controlCharacteristicUuid = controlCharacteristicUuid;
        this.dataCharacteristicUuid = dataCharacteristicUuid;
        this.requestMtu = requestMtu;
        this.startCommand = startCommand;
        this.stopCommand = stopCommand;
        this.pauseCommand = pauseCommand;
        this.emitReadyEvent = emitReadyEvent;
        this.autoStartOnNotification = autoStartOnNotification;
        this.notificationStartDelayMs = notificationStartDelayMs;
    }

    static DeviceConfig fromJson(String json) throws JSONException {
        JSONObject obj = new JSONObject(json);

        String type = obj.optString("deviceType", "Unknown");
        UUID serviceUuid = parseUuid(obj, "serviceUuid");
        UUID controlUuid = parseUuid(obj, "controlCharacteristicUuid");
        UUID dataUuid = parseUuid(obj, "dataCharacteristicUuid");
        Integer requestMtu = obj.has("requestMtu") ? obj.optInt("requestMtu") : null;
        byte[] startCommand = parseBase64(obj.optString("startCommand", null));
        byte[] stopCommand = parseBase64(obj.optString("stopCommand", null));
        byte[] pauseCommand = parseBase64(obj.optString("pauseCommand", null));
        boolean emitReadyEvent = obj.optBoolean("emitReadyEvent", true);
        boolean autoStartOnNotification = obj.optBoolean("autoStartOnNotification", false);
        int notificationStartDelayMs = obj.optInt("notificationStartDelayMs", 0);

        return new DeviceConfig(type, serviceUuid, controlUuid, dataUuid, requestMtu,
                startCommand, stopCommand, pauseCommand, emitReadyEvent,
                autoStartOnNotification, notificationStartDelayMs);
    }

    private static UUID parseUuid(JSONObject obj, String key) throws JSONException {
        String value = obj.optString(key, null);
        if (value == null || value.isEmpty()) {
            return null;
        }
        return UUID.fromString(value);
    }

    private static byte[] parseBase64(String input) {
        if (input == null || input.isEmpty()) {
            return null;
        }
        return Base64.decode(input, Base64.DEFAULT);
    }
}
