#import "BLEManager.h"
#import <UIKit/UIKit.h>
#import <dispatch/dispatch.h>

extern void UnitySendMessage(const char *, const char *, const char *);

static NSString *const kUnityCallbackMethod = @"OnNativeCallback";
static NSTimeInterval millisecondsToSeconds(int ms) { return (NSTimeInterval)ms / 1000.0; }

@interface BLEDeviceConfig : NSObject
@property (nonatomic, copy) NSString *deviceType;
@property (nonatomic, strong) CBUUID *serviceUuid;
@property (nonatomic, strong) CBUUID *controlCharacteristicUuid;
@property (nonatomic, strong) CBUUID *dataCharacteristicUuid;
@property (nonatomic, strong) NSNumber *requestMtu;
@property (nonatomic, strong) NSData *startCommand;
@property (nonatomic, strong) NSData *stopCommand;
@property (nonatomic, strong) NSData *pauseCommand;
@property (nonatomic, assign) BOOL emitReadyEvent;
@property (nonatomic, assign) BOOL autoStartOnNotification;
@property (nonatomic, assign) NSInteger notificationStartDelayMs;
+ (instancetype)configFromJson:(NSString *)json error:(NSError **)error;
@end

@implementation BLEDeviceConfig
+ (instancetype)configFromJson:(NSString *)json error:(NSError *__autoreleasing  _Nullable *)error {
    if (json.length == 0) {
        if (error) {
            *error = [NSError errorWithDomain:@"BLEPlugin" code:-1 userInfo:@{NSLocalizedDescriptionKey: @"Profile JSON missing"}];
        }
        return nil;
    }

    NSData *data = [json dataUsingEncoding:NSUTF8StringEncoding];
    if (!data) {
        if (error) {
            *error = [NSError errorWithDomain:@"BLEPlugin" code:-2 userInfo:@{NSLocalizedDescriptionKey: @"Invalid profile encoding"}];
        }
        return nil;
    }

    NSDictionary *dict = [NSJSONSerialization JSONObjectWithData:data options:0 error:error];
    if (!dict || ![dict isKindOfClass:[NSDictionary class]]) {
        return nil;
    }

    BLEDeviceConfig *config = [[BLEDeviceConfig alloc] init];
    config.deviceType = dict[@"deviceType"] ?: @"Unknown";

    NSString *serviceUuid = dict[@"serviceUuid"];
    if ([serviceUuid isKindOfClass:[NSString class]] && serviceUuid.length > 0) {
        config.serviceUuid = [CBUUID UUIDWithString:serviceUuid];
    }

    NSString *controlUuid = dict[@"controlCharacteristicUuid"];
    if ([controlUuid isKindOfClass:[NSString class]] && controlUuid.length > 0) {
        config.controlCharacteristicUuid = [CBUUID UUIDWithString:controlUuid];
    }

    NSString *dataUuid = dict[@"dataCharacteristicUuid"];
    if ([dataUuid isKindOfClass:[NSString class]] && dataUuid.length > 0) {
        config.dataCharacteristicUuid = [CBUUID UUIDWithString:dataUuid];
    }

    id requestMtu = dict[@"requestMtu"];
    if ([requestMtu respondsToSelector:@selector(integerValue)]) {
        NSInteger mtuValue = [requestMtu integerValue];
        config.requestMtu = @(mtuValue);
    }

    NSString *startCommand = dict[@"startCommand"];
    if ([startCommand isKindOfClass:[NSString class]] && startCommand.length > 0) {
        config.startCommand = [[NSData alloc] initWithBase64EncodedString:startCommand options:0];
    }

    NSString *stopCommand = dict[@"stopCommand"];
    if ([stopCommand isKindOfClass:[NSString class]] && stopCommand.length > 0) {
        config.stopCommand = [[NSData alloc] initWithBase64EncodedString:stopCommand options:0];
    }

    NSString *pauseCommand = dict[@"pauseCommand"];
    if ([pauseCommand isKindOfClass:[NSString class]] && pauseCommand.length > 0) {
        config.pauseCommand = [[NSData alloc] initWithBase64EncodedString:pauseCommand options:0];
    }

    config.emitReadyEvent = [dict[@"emitReadyEvent"] boolValue];
    if (dict[@"emitReadyEvent"] == nil) {
        config.emitReadyEvent = YES;
    }

    config.autoStartOnNotification = [dict[@"autoStartOnNotification"] boolValue];
    config.notificationStartDelayMs = [dict[@"notificationStartDelayMs"] respondsToSelector:@selector(integerValue)]
        ? [dict[@"notificationStartDelayMs"] integerValue] : 0;

    return config;
}
@end

@interface BLEDeviceContext : NSObject
@property (nonatomic, strong) BLEDeviceConfig *config;
@property (nonatomic, strong) CBPeripheral *peripheral;
@property (nonatomic, strong) NSMutableDictionary<CBUUID*, CBCharacteristic*> *characteristics;
@property (nonatomic, strong) CBCharacteristic *controlCharacteristic;
@property (nonatomic, strong) CBCharacteristic *dataCharacteristic;
@property (nonatomic, assign) BOOL readyEmitted;
@property (nonatomic, assign) BOOL autoStartScheduled;
@end

@implementation BLEDeviceContext
- (instancetype)init {
    if (self = [super init]) {
        _characteristics = [NSMutableDictionary dictionary];
        _readyEmitted = NO;
        _autoStartScheduled = NO;
    }
    return self;
}
@end

@interface BLEManager ()
@property (nonatomic, strong) CBCentralManager *central;
@property (nonatomic, strong) NSMutableDictionary<NSString*, CBPeripheral*> *discoveredPeripherals;
@property (nonatomic, strong) NSMutableDictionary<NSString*, BLEDeviceContext*> *deviceContexts;
@property (nonatomic, strong) NSMutableArray<dispatch_block_t> *pendingPoweredOnBlocks;
@property (nonatomic, assign) BOOL initEventSent;
@end

@implementation BLEManager

static BLEManager *_shared = nil;

+ (instancetype)shared {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        _shared = [[BLEManager alloc] init];
    });
    return _shared;
}

- (instancetype)init {
    if ((self = [super init])) {
        _discoveredPeripherals = [NSMutableDictionary dictionary];
        _deviceContexts = [NSMutableDictionary dictionary];
        _pendingPoweredOnBlocks = [NSMutableArray array];
        _initEventSent = NO;
    }
    return self;
}

- (void)start {
    [self ensureCentralManagerWithCompletion:nil];
}

- (void)startScan {
    [self performWhenPoweredOn:^{
        [self.discoveredPeripherals removeAllObjects];
        [self.central scanForPeripheralsWithServices:nil options:@{ CBCentralManagerScanOptionAllowDuplicatesKey: @NO }];
        [self sendUnityEvent:@{ @"eventType": @"scanStarted" }];
    }];
}

- (void)stopScan {
    dispatch_async(dispatch_get_main_queue(), ^{
        [self.central stopScan];
        [self sendUnityEvent:@{ @"eventType": @"scanStopped" }];
    });
}

- (void)connectToDevice:(NSString*)deviceId profileJson:(NSString*)profileJson {
    if (deviceId.length == 0) {
        [self sendUnityError:@"device id missing" deviceId:nil];
        return;
    }

    NSError *error = nil;
    BLEDeviceConfig *config = [BLEDeviceConfig configFromJson:profileJson error:&error];
    if (!config) {
        NSString *message = error.localizedDescription ?: @"invalid profile";
        [self sendUnityError:message deviceId:deviceId];
        return;
    }

    [self performWhenPoweredOn:^{
        CBPeripheral *peripheral = self.discoveredPeripherals[deviceId];
        if (!peripheral) {
            NSUUID *uuid = [[NSUUID alloc] initWithUUIDString:deviceId];
            if (uuid) {
                NSArray<CBPeripheral *> *retrieved = [self.central retrievePeripheralsWithIdentifiers:@[uuid]];
                if (retrieved.count > 0) {
                    peripheral = retrieved.firstObject;
                }
            }
        }

        if (!peripheral) {
            [self sendUnityError:@"device not found" deviceId:deviceId];
            return;
        }

        BLEDeviceContext *context = [[BLEDeviceContext alloc] init];
        context.config = config;
        context.peripheral = peripheral;
        self.deviceContexts[deviceId] = context;

        peripheral.delegate = self;
        [self.central connectPeripheral:peripheral options:nil];
    }];
}

- (void)disconnectDevice:(NSString*)deviceId {
    [self performWhenPoweredOn:^{
        BLEDeviceContext *context = self.deviceContexts[deviceId];
        if (context) {
            if (context.peripheral) {
                [self.central cancelPeripheralConnection:context.peripheral];
            }
            [self.deviceContexts removeObjectForKey:deviceId];
        }
    }];
}

- (void)startMeasurement:(NSString*)deviceId {
    [self writeControlForDevice:deviceId payloadKey:@"startCommand" defaultAction:@"start"];
}

- (void)stopMeasurement:(NSString*)deviceId {
    [self writeControlForDevice:deviceId payloadKey:@"stopCommand" defaultAction:@"stop"];
}

- (void)pauseMeasurement:(NSString*)deviceId {
    [self writeControlForDevice:deviceId payloadKey:@"pauseCommand" defaultAction:@"pause"];
}

- (void)sendControl:(NSString*)deviceId base64Payload:(NSString*)payload action:(NSString*)action {
    if (deviceId.length == 0 || payload.length == 0) {
        return;
    }
    NSData *data = [[NSData alloc] initWithBase64EncodedString:payload options:0];
    if (!data) {
        [self sendUnityError:@"invalid control payload" deviceId:deviceId];
        return;
    }
    [self writeControlForDevice:deviceId payload:data action:action ?: @"custom"];
}

- (void)readCharacteristicWithService:(NSString*)service charId:(NSString*)charId {
    if (service.length == 0 || charId.length == 0) {
        return;
    }
    CBUUID *charUUID = [CBUUID UUIDWithString:charId];
    for (NSString *key in self.deviceContexts) {
        BLEDeviceContext *ctx = self.deviceContexts[key];
        CBCharacteristic *characteristic = ctx.characteristics[charUUID];
        if (characteristic && ctx.peripheral) {
            [ctx.peripheral readValueForCharacteristic:characteristic];
            break;
        }
    }
}

- (void)writeCharacteristicWithService:(NSString*)service charId:(NSString*)charId data:(NSData*)data {
    if (service.length == 0 || charId.length == 0 || data.length == 0) {
        return;
    }
    CBUUID *charUUID = [CBUUID UUIDWithString:charId];
    for (NSString *key in self.deviceContexts) {
        BLEDeviceContext *ctx = self.deviceContexts[key];
        CBCharacteristic *characteristic = ctx.characteristics[charUUID];
        if (characteristic && ctx.peripheral) {
            [ctx.peripheral writeValue:data forCharacteristic:characteristic type:CBCharacteristicWriteWithResponse];
            break;
        }
    }
}

#pragma mark - Private helpers

- (void)writeControlForDevice:(NSString *)deviceId payloadKey:(NSString *)payloadKey defaultAction:(NSString *)action {
    BLEDeviceContext *ctx = self.deviceContexts[deviceId];
    if (!ctx) {
        [self sendUnityError:@"device not connected" deviceId:deviceId];
        return;
    }
    NSData *payload = [ctx.config valueForKey:payloadKey];
    if (!payload || payload.length == 0) {
        return;
    }
    [self writeControlForDevice:deviceId payload:payload action:action];
}

- (void)writeControlForDevice:(NSString *)deviceId payload:(NSData *)payload action:(NSString *)action {
    BLEDeviceContext *ctx = self.deviceContexts[deviceId];
    if (!ctx || !ctx.peripheral) {
        [self sendUnityError:@"device not connected" deviceId:deviceId];
        return;
    }
    if (!ctx.controlCharacteristic) {
        [self sendUnityError:@"control characteristic not available" deviceId:deviceId];
        return;
    }
    [ctx.peripheral writeValue:payload forCharacteristic:ctx.controlCharacteristic type:CBCharacteristicWriteWithResponse];
    (void)action;
}

- (void)emitReadyIfNeeded:(BLEDeviceContext *)context {
    if (!context || context.readyEmitted == YES) {
        return;
    }
    if (context.config.emitReadyEvent && context.peripheral.identifier.UUIDString.length > 0) {
        NSString *deviceId = context.peripheral.identifier.UUIDString;
        [self sendUnityEvent:@{
            @"eventType": @"ready",
            @"deviceType": context.config.deviceType ?: @"Unknown",
            @"id": deviceId
        }];
    }
    context.readyEmitted = YES;
}

- (BLEDeviceContext *)contextForPeripheral:(CBPeripheral *)peripheral {
    for (NSString *key in self.deviceContexts) {
        BLEDeviceContext *ctx = self.deviceContexts[key];
        if (ctx.peripheral == peripheral) {
            return ctx;
        }
    }
    return nil;
}

- (void)sendUnityError:(NSString *)message deviceId:(NSString *)deviceId {
    NSMutableDictionary *payload = [@{ @"eventType": @"error", @"message": message ?: @"unknown" } mutableCopy];
    if (deviceId.length > 0) {
        payload[@"id"] = deviceId;
    }
    [self sendUnityEvent:payload];
}

- (void)sendUnityEvent:(NSDictionary *)payload {
    if (!self.unityObjectName || payload.count == 0) {
        return;
    }
    NSError *error = nil;
    NSData *jsonData = [NSJSONSerialization dataWithJSONObject:payload options:0 error:&error];
    if (!jsonData) {
        return;
    }
    NSString *json = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
    if (!json) {
        return;
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        UnitySendMessage([self.unityObjectName UTF8String], [kUnityCallbackMethod UTF8String], [json UTF8String]);
    });
}

#pragma mark - Initialization helpers

- (void)setUnityObjectName:(NSString *)unityObjectName {
    if (![_unityObjectName isEqualToString:unityObjectName]) {
        self.initEventSent = NO;
    }
    _unityObjectName = [unityObjectName copy];
    [self emitInitEventIfNeeded];
}

- (void)ensureCentralManagerWithCompletion:(dispatch_block_t)completion {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!self.central) {
            self.central = [[CBCentralManager alloc] initWithDelegate:self queue:dispatch_get_main_queue()];
        }

        [self emitInitEventIfNeeded];

        if (!completion) {
            return;
        }

        if (self.central.state == CBManagerStatePoweredOn) {
            completion();
        } else {
            [self.pendingPoweredOnBlocks addObject:[completion copy]];
        }
    });
}

- (void)performWhenPoweredOn:(dispatch_block_t)block {
    if (!block) {
        [self ensureCentralManagerWithCompletion:nil];
        return;
    }
    [self ensureCentralManagerWithCompletion:block];
}

- (void)emitInitEventIfNeeded {
    if (self.initEventSent || self.unityObjectName.length == 0 || !self.central) {
        return;
    }
    self.initEventSent = YES;
    [self sendUnityEvent:@{ @"eventType": @"init" }];
}

#pragma mark - CBCentralManagerDelegate

- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    NSString *state = @"unknown";
    switch (central.state) {
        case CBManagerStatePoweredOn: state = @"poweredOn"; break;
        case CBManagerStatePoweredOff: state = @"poweredOff"; break;
        case CBManagerStateUnauthorized: state = @"unauthorized"; break;
        case CBManagerStateUnsupported: state = @"unsupported"; break;
        case CBManagerStateResetting: state = @"resetting"; break;
        case CBManagerStateUnknown:
        default: state = @"unknown"; break;
    }
    [self emitInitEventIfNeeded];
    [self sendUnityEvent:@{ @"eventType": @"state", @"state": state }];

    if (central.state == CBManagerStatePoweredOn && self.pendingPoweredOnBlocks.count > 0) {
        NSArray<dispatch_block_t> *pending = [self.pendingPoweredOnBlocks copy];
        [self.pendingPoweredOnBlocks removeAllObjects];
        for (dispatch_block_t block in pending) {
            if (block) {
                block();
            }
        }
    }
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral
     advertisementData:(NSDictionary<NSString *,id> *)advertisementData RSSI:(NSNumber *)RSSI {
    if (!peripheral.identifier.UUIDString) {
        return;
    }
    self.discoveredPeripherals[peripheral.identifier.UUIDString] = peripheral;

    NSString *name = peripheral.name ?: @"";
    NSNumber *rssi = RSSI ?: @(0);
    [self sendUnityEvent:@{
        @"eventType": @"scanResult",
        @"id": peripheral.identifier.UUIDString,
        @"name": name,
        @"deviceType": @"",
        @"rssi": rssi
    }];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    BLEDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }
    [peripheral discoverServices:nil];
    [self sendUnityEvent:@{
        @"eventType": @"connected",
        @"deviceType": context.config.deviceType ?: @"Unknown",
        @"id": peripheral.identifier.UUIDString ?: @""
    }];
}

- (void)centralManager:(CBCentralManager *)central didFailToConnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    NSString *deviceId = peripheral.identifier.UUIDString ?: @"";
    [self sendUnityError:error.localizedDescription ?: @"failed to connect" deviceId:deviceId];
    [self.deviceContexts removeObjectForKey:deviceId];
}

- (void)centralManager:(CBCentralManager *)central didDisconnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    NSString *deviceId = peripheral.identifier.UUIDString ?: @"";
    BLEDeviceContext *context = [self contextForPeripheral:peripheral];
    NSString *deviceType = @"Unknown";
    if (context && context.config.deviceType.length > 0) {
        deviceType = context.config.deviceType;
    }
    [self sendUnityEvent:@{
        @"eventType": @"disconnected",
        @"id": deviceId,
        @"deviceType": deviceType
    }];
    [self.deviceContexts removeObjectForKey:deviceId];
}

#pragma mark - CBPeripheralDelegate

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    BLEDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }
    if (error) {
        [self sendUnityError:error.localizedDescription ?: @"service discovery failed" deviceId:peripheral.identifier.UUIDString];
        return;
    }

    if (context.config.serviceUuid) {
        BOOL found = NO;
        for (CBService *service in peripheral.services) {
            if ([service.UUID isEqual:context.config.serviceUuid]) {
                found = YES;
                [peripheral discoverCharacteristics:nil forService:service];
            }
        }
        if (!found) {
            [self sendUnityError:@"service not found" deviceId:peripheral.identifier.UUIDString];
        }
    } else {
        for (CBService *service in peripheral.services) {
            [peripheral discoverCharacteristics:nil forService:service];
        }
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    BLEDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }
    if (error) {
        [self sendUnityError:error.localizedDescription ?: @"characteristic discovery failed" deviceId:peripheral.identifier.UUIDString];
        return;
    }

    for (CBCharacteristic *characteristic in service.characteristics) {
        context.characteristics[characteristic.UUID] = characteristic;
        if (context.config.controlCharacteristicUuid && [characteristic.UUID isEqual:context.config.controlCharacteristicUuid]) {
            context.controlCharacteristic = characteristic;
        }
        if (context.config.dataCharacteristicUuid && [characteristic.UUID isEqual:context.config.dataCharacteristicUuid]) {
            context.dataCharacteristic = characteristic;
            [peripheral setNotifyValue:YES forCharacteristic:characteristic];
        }
    }

    if (!context.config.dataCharacteristicUuid) {
        for (CBCharacteristic *c in service.characteristics) {
            if ((c.properties & CBCharacteristicPropertyNotify) == CBCharacteristicPropertyNotify) {
                context.dataCharacteristic = c;
                [peripheral setNotifyValue:YES forCharacteristic:c];
                break;
            }
        }
    }

    [self emitReadyIfNeeded:context];
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateNotificationStateForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    BLEDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }
    if (error) {
        [self sendUnityError:error.localizedDescription ?: @"notification update failed" deviceId:peripheral.identifier.UUIDString];
        return;
    }
    if (characteristic.isNotifying && context.config.autoStartOnNotification && !context.autoStartScheduled) {
        context.autoStartScheduled = YES;
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(millisecondsToSeconds((int)context.config.notificationStartDelayMs) * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
            [self startMeasurement:peripheral.identifier.UUIDString ?: @""];
        });
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didWriteValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    BLEDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }
    if (error) {
        [self sendUnityError:error.localizedDescription ?: @"write failed" deviceId:peripheral.identifier.UUIDString];
        return;
    }
    if (context.controlCharacteristic && [characteristic.UUID isEqual:context.controlCharacteristic.UUID]) {
        [self sendUnityEvent:@{
            @"eventType": @"controlWritten",
            @"deviceType": context.config.deviceType ?: @"Unknown",
            @"id": peripheral.identifier.UUIDString ?: @""
        }];
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    if (error) {
        [self sendUnityError:error.localizedDescription ?: @"characteristic update failed" deviceId:peripheral.identifier.UUIDString];
        return;
    }
    NSData *value = characteristic.value ?: [NSData data];
    NSString *base64 = [value base64EncodedStringWithOptions:0];
    [self sendUnityEvent:@{
        @"eventType": @"data",
        @"id": peripheral.identifier.UUIDString ?: @"",
        @"uuid": characteristic.UUID.UUIDString ?: @"",
        @"value": base64 ?: @""
    }];
}

@end
