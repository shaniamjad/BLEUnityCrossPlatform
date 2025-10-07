#import <Foundation/Foundation.h>
#import "BLEManager.h"

#ifdef __cplusplus
extern "C" {
#endif

void _ble_init(const char* unityObjectName) {
    NSString *obj = [NSString stringWithUTF8String:unityObjectName];
    [[BLEManager shared] setUnityObjectName:obj];
    [[BLEManager shared] start]; // initialize central manager
}

void _ble_startScan() {
    [[BLEManager shared] startScan];
}

void _ble_stopScan() {
    [[BLEManager shared] stopScan];
}

void _ble_connect(const char* deviceId) {
    NSString *did = [NSString stringWithUTF8String:deviceId];
    [[BLEManager shared] connectToDevice:did];
}

void _ble_readCharacteristic(const char* serviceUuid, const char* charUuid) {
    [[BLEManager shared] readCharacteristicWithService:[NSString stringWithUTF8String:serviceUuid] charId:[NSString stringWithUTF8String:charUuid]];
}

void _ble_writeCharacteristic(const char* serviceUuid, const char* charUuid, const char* base64Data) {
    NSData *data = [[NSData alloc] initWithBase64EncodedString:[NSString stringWithUTF8String:base64Data] options:0];
    [[BLEManager shared] writeCharacteristicWithService:[NSString stringWithUTF8String:serviceUuid] charId:[NSString stringWithUTF8String:charUuid] data:data];
}

#ifdef __cplusplus
}
#endif
