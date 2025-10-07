#import <Foundation/Foundation.h>
#import "BlePluginIOS.h"

static NSString *BLEStringFromC(const char *cstr) {
    if (cstr == NULL) {
        return @"";
    }
    return [NSString stringWithUTF8String:cstr];
}

static NSData *BLEDataFromBase64(const char *cstr) {
    if (cstr == NULL) {
        return nil;
    }
    NSString *string = [NSString stringWithUTF8String:cstr];
    if (string.length == 0) {
        return nil;
    }
    return [[NSData alloc] initWithBase64EncodedString:string options:0];
}

#ifdef __cplusplus
extern "C" {
#endif

void _ble_init(const char* unityObjectName) {
    [[BlePluginIOS shared] initializeWithUnityObject:BLEStringFromC(unityObjectName)];
}

void _ble_startScan() {
    [[BlePluginIOS shared] startScan];
}

void _ble_stopScan() {
    [[BlePluginIOS shared] stopScan];
}

void _ble_connect(const char* deviceId, const char* profileJson) {
    [[BlePluginIOS shared] connectDevice:BLEStringFromC(deviceId) profileJson:BLEStringFromC(profileJson)];
}

void _ble_disconnect(const char* deviceId) {
    [[BlePluginIOS shared] disconnectDevice:BLEStringFromC(deviceId)];
}

void _ble_startMeasurement(const char* deviceId) {
    [[BlePluginIOS shared] startMeasurement:BLEStringFromC(deviceId)];
}

void _ble_stopMeasurement(const char* deviceId) {
    [[BlePluginIOS shared] stopMeasurement:BLEStringFromC(deviceId)];
}

void _ble_pauseMeasurement(const char* deviceId) {
    [[BlePluginIOS shared] pauseMeasurement:BLEStringFromC(deviceId)];
}

void _ble_sendControl(const char* deviceId, const char* base64Payload, const char* action) {
    NSData *payload = BLEDataFromBase64(base64Payload);
    [[BlePluginIOS shared] sendControl:BLEStringFromC(deviceId) payload:payload action:BLEStringFromC(action)];
}

void _ble_readCharacteristic(const char* serviceUuid, const char* charUuid) {
    [[BlePluginIOS shared] readCharacteristicWithService:BLEStringFromC(serviceUuid) characteristic:BLEStringFromC(charUuid)];
}

void _ble_writeCharacteristic(const char* serviceUuid, const char* charUuid, const char* base64Data) {
    NSData *data = BLEDataFromBase64(base64Data);
    [[BlePluginIOS shared] writeCharacteristicWithService:BLEStringFromC(serviceUuid) characteristic:BLEStringFromC(charUuid) data:data];
}

#ifdef __cplusplus
}
#endif
