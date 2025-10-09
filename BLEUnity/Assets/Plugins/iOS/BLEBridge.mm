#import <Foundation/Foundation.h>
#import "BLEManager.h"

#ifdef __cplusplus
extern "C" {
#endif

void _ble_init(const char* unityObjectName) {
    NSString *obj = [NSString stringWithUTF8String:unityObjectName ?: "UnityBLE"];
    BLEManager *manager = [BLEManager shared];
    manager.unityObjectName = obj;
    [manager start];
}

void _ble_startScan() {
    [[BLEManager shared] startScan];
}

void _ble_stopScan() {
    [[BLEManager shared] stopScan];
}

void _ble_connect(const char* deviceId, const char* profileJson) {
    NSString *did = deviceId ? [NSString stringWithUTF8String:deviceId] : @"";
    NSString *profile = profileJson ? [NSString stringWithUTF8String:profileJson] : @"";
    [[BLEManager shared] connectToDevice:did profileJson:profile];
}

void _ble_disconnect(const char* deviceId) {
    NSString *did = deviceId ? [NSString stringWithUTF8String:deviceId] : @"";
    [[BLEManager shared] disconnectDevice:did];
}

void _ble_startMeasurement(const char* deviceId) {
    NSString *did = deviceId ? [NSString stringWithUTF8String:deviceId] : @"";
    [[BLEManager shared] startMeasurement:did];
}

void _ble_stopMeasurement(const char* deviceId) {
    NSString *did = deviceId ? [NSString stringWithUTF8String:deviceId] : @"";
    [[BLEManager shared] stopMeasurement:did];
}

void _ble_pauseMeasurement(const char* deviceId) {
    NSString *did = deviceId ? [NSString stringWithUTF8String:deviceId] : @"";
    [[BLEManager shared] pauseMeasurement:did];
}

void _ble_sendControl(const char* deviceId, const char* base64Data, const char* action) {
    NSString *did = deviceId ? [NSString stringWithUTF8String:deviceId] : @"";
    NSString *payload = base64Data ? [NSString stringWithUTF8String:base64Data] : @"";
    NSString *act = action ? [NSString stringWithUTF8String:action] : @"";
    [[BLEManager shared] sendControl:did base64Payload:payload action:act];
}

void _ble_readCharacteristic(const char* serviceUuid, const char* charUuid) {
    NSString *service = serviceUuid ? [NSString stringWithUTF8String:serviceUuid] : @"";
    NSString *characteristic = charUuid ? [NSString stringWithUTF8String:charUuid] : @"";
    [[BLEManager shared] readCharacteristicWithService:service charId:characteristic];
}

void _ble_writeCharacteristic(const char* serviceUuid, const char* charUuid, const char* base64Data) {
    NSString *service = serviceUuid ? [NSString stringWithUTF8String:serviceUuid] : @"";
    NSString *characteristic = charUuid ? [NSString stringWithUTF8String:charUuid] : @"";
    NSString *b64 = base64Data ? [NSString stringWithUTF8String:base64Data] : @"";
    NSData *data = [[NSData alloc] initWithBase64EncodedString:b64 options:0];
    [[BLEManager shared] writeCharacteristicWithService:service charId:characteristic data:data];
}

#ifdef __cplusplus
}
#endif
