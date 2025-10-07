#import "BLEManager.h"
#import <UIKit/UIKit.h>

static BLEManager *_shared = nil;

@implementation BLEManager {
    CBCentralManager *_central;
    NSMutableDictionary<NSString*, CBPeripheral*> *_foundPeripherals;
    CBPeripheral *_connectedPeripheral;
    NSMutableDictionary<CBUUID*, CBCharacteristic*> *_characteristics;
}

+ (instancetype)shared {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        _shared = [[BLEManager alloc] init];
    });
    return _shared;
}

- (instancetype)init {
    if ((self = [super init])) {
        _foundPeripherals = [NSMutableDictionary dictionary];
        _characteristics = [NSMutableDictionary dictionary];
    }
    return self;
}

- (void)setUnityObjectName:(NSString *)unityObjectName {
    _unityObjectName = unityObjectName;
}

- (void)start {
    dispatch_async(dispatch_get_main_queue(), ^{
        _central = [[CBCentralManager alloc] initWithDelegate:self queue:dispatch_get_main_queue()];
    });
}

- (void)startScan {
    if (_central.state != CBManagerStatePoweredOn) {
        [self sendUnityEvent:@{@"event":@"error", @"message":@"Bluetooth not powered on"}];
        return;
    }
    [_foundPeripherals removeAllObjects];
    [_central scanForPeripheralsWithServices:nil options:@{CBCentralManagerScanOptionAllowDuplicatesKey:@NO}];
    [self sendUnityEvent:@{@"event":@"scanStarted"}];
}

- (void)stopScan {
    [_central stopScan];
    [self sendUnityEvent:@{@"event":@"scanStopped"}];
}

- (void)connectToDevice:(NSString*)deviceId {
    CBPeripheral *p = _foundPeripherals[deviceId];
    if (!p) { [self sendUnityEvent:@{@"event":@"error",@"message":@"device not found"}]; return; }
    _connectedPeripheral = p;
    _connectedPeripheral.delegate = self;
    [_central connectPeripheral:_connectedPeripheral options:nil];
}

// CBCentralManagerDelegate
- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    NSString *state = @"unknown";
    switch (central.state) {
        case CBManagerStatePoweredOn: state = @"poweredOn"; break;
        case CBManagerStatePoweredOff: state = @"poweredOff"; break;
        case CBManagerStateUnauthorized: state = @"unauthorized"; break;
        case CBManagerStateUnsupported: state = @"unsupported"; break;
        default: break;
    }
    [self sendUnityEvent:@{@"event":@"state", @"state":state}];
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral
       advertisementData:(NSDictionary<NSString *,id> *)advertisementData RSSI:(NSNumber *)RSSI {
    if (!peripheral.identifier.UUIDString) return;
    NSString *uuid = peripheral.identifier.UUIDString;
    _foundPeripherals[uuid] = peripheral;
    NSDictionary *obj = @{
        @"event": @"scanResult",
        @"id": uuid,
        @"name": peripheral.name ?: @"",
        @"rssi": RSSI
    };
    [self sendUnityEvent:obj];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    [peripheral discoverServices:nil];
    [self sendUnityEvent:@{@"event":@"connected", @"id":peripheral.identifier.UUIDString}];
}

- (void)centralManager:(CBCentralManager *)central didFailToConnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    [self sendUnityEvent:@{@"event":@"connectFailed", @"id":peripheral.identifier.UUIDString, @"message": error.localizedDescription ?: @""}];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    for (CBService *s in peripheral.services) {
        [peripheral discoverCharacteristics:nil forService:s];
    }
    [self sendUnityEvent:@{@"event":@"servicesDiscovered", @"id":peripheral.identifier.UUIDString}];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    for (CBCharacteristic *c in service.characteristics) {
        NSString *key = c.UUID.UUIDString;
        _characteristics[c.UUID] = c;
        // notify Unity about characteristic
        [self sendUnityEvent:@{@"event":@"charFound",@"service":service.UUID.UUIDString, @"char":key}];
    }
}

- (void)readCharacteristicWithService:(NSString*)service charId:(NSString*)charId {
    CBUUID *suuid = [CBUUID UUIDWithString:service];
    CBUUID *cuuid = [CBUUID UUIDWithString:charId];
    CBCharacteristic *c = _characteristics[cuuid];
    if (c && _connectedPeripheral) [_connectedPeripheral readValueForCharacteristic:c];
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    NSData *d = characteristic.value ?: [NSData data];
    NSString *b64 = [d base64EncodedStringWithOptions:0];
    [self sendUnityEvent:@{@"event":@"charValue",@"char":characteristic.UUID.UUIDString, @"value":b64}];
}

- (void)writeCharacteristicWithService:(NSString*)service charId:(NSString*)charId data:(NSData*)data {
    CBUUID *cuuid = [CBUUID UUIDWithString:charId];
    CBCharacteristic *c = _characteristics[cuuid];
    if (c && _connectedPeripheral) {
        CBCharacteristicWriteType type = CBCharacteristicWriteWithResponse;
        [_connectedPeripheral writeValue:data forCharacteristic:c type:type];
    }
}

// convenience: send JSON via UnitySendMessage
- (void)sendUnityEvent:(NSDictionary*)obj {
    NSError *err = nil;
    NSData *d = [NSJSONSerialization dataWithJSONObject:obj options:0 error:&err];
    if (!d) return;
    NSString *jsonStr = [[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding];
    if (!_unityObjectName) return;
    UnitySendMessage([_unityObjectName UTF8String], "OnNativeCallback", [jsonStr UTF8String]);
}

@end
