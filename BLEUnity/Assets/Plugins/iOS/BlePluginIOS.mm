#import "BlePluginIOS.h"
#import <CoreBluetooth/CoreBluetooth.h>

extern void UnitySendMessage(const char *, const char *, const char *);

@interface BleDeviceConfig : NSObject
@property (nonatomic, copy) NSString *deviceType;
@property (nonatomic, strong, nullable) CBUUID *serviceUuid;
@property (nonatomic, strong, nullable) CBUUID *controlCharacteristicUuid;
@property (nonatomic, strong, nullable) CBUUID *dataCharacteristicUuid;
@property (nonatomic, strong, nullable) NSNumber *requestMtu;
@property (nonatomic, strong, nullable) NSData *startCommand;
@property (nonatomic, strong, nullable) NSData *stopCommand;
@property (nonatomic, strong, nullable) NSData *pauseCommand;
@property (nonatomic, assign) BOOL emitReadyEvent;
@property (nonatomic, assign) BOOL autoStartOnNotification;
@property (nonatomic, assign) NSInteger notificationStartDelayMs;
+ (nullable instancetype)configFromJson:(NSString *)json error:(NSError **)error;
@end

@implementation BleDeviceConfig

+ (nullable instancetype)configFromJson:(NSString *)json error:(NSError **)error {
    if (json.length == 0) {
        if (error) {
            *error = [NSError errorWithDomain:@"BlePluginIOS" code:1 userInfo:@{NSLocalizedDescriptionKey: @"Profile payload missing"}];
        }
        return nil;
    }

    NSData *data = [json dataUsingEncoding:NSUTF8StringEncoding];
    if (!data) {
        if (error) {
            *error = [NSError errorWithDomain:@"BlePluginIOS" code:2 userInfo:@{NSLocalizedDescriptionKey: @"Unable to encode profile JSON"}];
        }
        return nil;
    }

    id obj = [NSJSONSerialization JSONObjectWithData:data options:0 error:error];
    if (!obj || ![obj isKindOfClass:[NSDictionary class]]) {
        if (error && !*error) {
            *error = [NSError errorWithDomain:@"BlePluginIOS" code:3 userInfo:@{NSLocalizedDescriptionKey: @"Invalid profile JSON"}];
        }
        return nil;
    }

    NSDictionary *dict = (NSDictionary *)obj;
    BleDeviceConfig *config = [[BleDeviceConfig alloc] init];
    config.deviceType = [[self class] stringFromValue:dict[@"deviceType"]] ?: @"Unknown";

    NSString *service = [[self class] stringFromValue:dict[@"serviceUuid"]];
    if (service.length > 0) {
        config.serviceUuid = [CBUUID UUIDWithString:service];
    }

    NSString *control = [[self class] stringFromValue:dict[@"controlCharacteristicUuid"]];
    if (control.length > 0) {
        config.controlCharacteristicUuid = [CBUUID UUIDWithString:control];
    }

    NSString *dataUuid = [[self class] stringFromValue:dict[@"dataCharacteristicUuid"]];
    if (dataUuid.length > 0) {
        config.dataCharacteristicUuid = [CBUUID UUIDWithString:dataUuid];
    }

    id requestMtu = dict[@"requestMtu"];
    if ([requestMtu respondsToSelector:@selector(integerValue)]) {
        config.requestMtu = @([requestMtu integerValue]);
    }

    config.startCommand = [[self class] dataFromBase64:dict[@"startCommand"]];
    config.stopCommand = [[self class] dataFromBase64:dict[@"stopCommand"]];
    config.pauseCommand = [[self class] dataFromBase64:dict[@"pauseCommand"]];

    id emitReady = dict[@"emitReadyEvent"];
    config.emitReadyEvent = emitReady ? [emitReady boolValue] : YES;

    id autoStart = dict[@"autoStartOnNotification"];
    config.autoStartOnNotification = autoStart ? [autoStart boolValue] : NO;

    id delay = dict[@"notificationStartDelayMs"];
    config.notificationStartDelayMs = [delay respondsToSelector:@selector(integerValue)] ? [delay integerValue] : 0;

    return config;
}

+ (NSString *)stringFromValue:(id)value {
    if ([value isKindOfClass:[NSString class]]) {
        return (NSString *)value;
    }
    return nil;
}

+ (NSData *)dataFromBase64:(id)value {
    if (![value isKindOfClass:[NSString class]]) {
        return nil;
    }
    NSString *string = (NSString *)value;
    if (string.length == 0) {
        return nil;
    }
    NSData *data = [[NSData alloc] initWithBase64EncodedString:string options:0];
    return data;
}

@end

@interface BleDeviceContext : NSObject
@property (nonatomic, strong) BleDeviceConfig *config;
@property (nonatomic, strong) CBPeripheral *peripheral;
@property (nonatomic, strong, nullable) CBCharacteristic *controlCharacteristic;
@property (nonatomic, strong, nullable) CBCharacteristic *dataCharacteristic;
@property (nonatomic, assign) BOOL readyEventSent;
@end

@implementation BleDeviceContext
@end

@interface BlePluginIOS () <CBCentralManagerDelegate, CBPeripheralDelegate>
@property (nonatomic, strong) CBCentralManager *centralManager;
@property (nonatomic, strong) NSMutableDictionary<NSString *, CBPeripheral *> *discoveredPeripherals;
@property (nonatomic, strong) NSMutableDictionary<NSString *, BleDeviceContext *> *deviceContexts;
@end

@implementation BlePluginIOS

+ (instancetype)shared {
    static BlePluginIOS *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[BlePluginIOS alloc] init];
    });
    return instance;
}

- (instancetype)init {
    if (self = [super init]) {
        _discoveredPeripherals = [NSMutableDictionary dictionary];
        _deviceContexts = [NSMutableDictionary dictionary];
    }
    return self;
}

- (void)initializeWithUnityObject:(NSString *)unityObjectName {
    dispatch_async(dispatch_get_main_queue(), ^{
        self.unityObjectName = unityObjectName;
        if (!self.centralManager) {
            self.centralManager = [[CBCentralManager alloc] initWithDelegate:self queue:nil];
        } else {
            self.centralManager.delegate = self;
        }
        [self sendUnityEvent:@{ @"eventType": @"init" }];
    });
}

- (void)startScan {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!self.centralManager) {
            [self sendError:@"Bluetooth not initialized" deviceId:nil];
            return;
        }
        if (self.centralManager.state != CBManagerStatePoweredOn) {
            [self sendError:@"Bluetooth not powered on" deviceId:nil];
            return;
        }
        [self.discoveredPeripherals removeAllObjects];
        [self.centralManager scanForPeripheralsWithServices:nil options:@{ CBCentralManagerScanOptionAllowDuplicatesKey: @NO }];
        [self sendUnityEvent:@{ @"eventType": @"scanStarted" }];
    });
}

- (void)stopScan {
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!self.centralManager) {
            return;
        }
        [self.centralManager stopScan];
        [self sendUnityEvent:@{ @"eventType": @"scanStopped" }];
    });
}

- (void)connectDevice:(NSString *)deviceId profileJson:(NSString *)profileJson {
    if (deviceId.length == 0) {
        [self sendError:@"device id missing" deviceId:nil];
        return;
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        if (!self.centralManager) {
            [self sendError:@"Bluetooth not initialized" deviceId:deviceId];
            return;
        }

        if (self.deviceContexts[deviceId] != nil) {
            [self sendError:@"already connected" deviceId:deviceId];
            return;
        }

        NSError *configError = nil;
        BleDeviceConfig *config = [BleDeviceConfig configFromJson:profileJson error:&configError];
        if (!config) {
            NSString *detail = configError.localizedDescription ?: @"invalid profile";
            [self sendUnityEvent:@{ @"eventType": @"error",
                                    @"message": @"invalid profile",
                                    @"detail": detail,
                                    @"id": deviceId }];
            return;
        }

        CBPeripheral *peripheral = self.discoveredPeripherals[deviceId];
        if (!peripheral) {
            NSUUID *uuid = [[NSUUID alloc] initWithUUIDString:deviceId];
            if (uuid) {
                NSArray<CBPeripheral *> *retrieved = [self.centralManager retrievePeripheralsWithIdentifiers:@[uuid]];
                peripheral = retrieved.firstObject;
            }
        }

        if (!peripheral) {
            [self sendError:@"device not found" deviceId:deviceId];
            return;
        }

        BleDeviceContext *context = [[BleDeviceContext alloc] init];
        context.config = config;
        context.peripheral = peripheral;
        context.readyEventSent = NO;
        self.deviceContexts[deviceId] = context;

        peripheral.delegate = self;
        [self.centralManager connectPeripheral:peripheral options:nil];
    });
}

- (void)disconnectDevice:(NSString *)deviceId {
    if (deviceId.length == 0) {
        return;
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        BleDeviceContext *context = [self contextForDevice:deviceId];
        if (!context) {
            [self sendError:@"device not connected" deviceId:deviceId];
            return;
        }
        if (context.peripheral) {
            [self.centralManager cancelPeripheralConnection:context.peripheral];
        } else {
            NSString *deviceType = context.config ? (context.config.deviceType ?: @"") : @"";
            [self cleanupDevice:deviceId];
            [self sendUnityEvent:@{ @"eventType": @"disconnected",
                                    @"deviceType": deviceType,
                                    @"id": deviceId }];
        }
    });
}

- (void)disconnectAllDevices {
    dispatch_async(dispatch_get_main_queue(), ^{
        NSArray<NSString *> *keys = [[self.deviceContexts allKeys] copy];
        for (NSString *deviceId in keys) {
            [self disconnectDevice:deviceId];
        }
        [self sendUnityEvent:@{ @"eventType": @"allDisconnected" }];
    });
}

- (void)startMeasurement:(NSString *)deviceId {
    dispatch_async(dispatch_get_main_queue(), ^{
        BleDeviceContext *context = [self contextForDevice:deviceId];
        if (!context || !context.config.startCommand) {
            return;
        }
        [self sendControl:deviceId payload:context.config.startCommand action:@"start"];
    });
}

- (void)stopMeasurement:(NSString *)deviceId {
    dispatch_async(dispatch_get_main_queue(), ^{
        BleDeviceContext *context = [self contextForDevice:deviceId];
        if (!context || !context.config.stopCommand) {
            return;
        }
        [self sendControl:deviceId payload:context.config.stopCommand action:@"stop"];
    });
}

- (void)pauseMeasurement:(NSString *)deviceId {
    dispatch_async(dispatch_get_main_queue(), ^{
        BleDeviceContext *context = [self contextForDevice:deviceId];
        if (!context || !context.config.pauseCommand) {
            return;
        }
        [self sendControl:deviceId payload:context.config.pauseCommand action:@"pause"];
    });
}

- (void)sendControl:(NSString *)deviceId payload:(NSData *)payload action:(NSString *)action {
    if (payload.length == 0) {
        return;
    }
    NSString *actionLabel = action ?: @"";
    dispatch_async(dispatch_get_main_queue(), ^{
        BleDeviceContext *context = [self contextForDevice:deviceId];
        if (!context) {
            [self sendError:@"device not connected" deviceId:deviceId];
            return;
        }
        if (!context.peripheral) {
            [self sendError:@"peripheral unavailable" deviceId:deviceId];
            return;
        }

        CBCharacteristic *control = context.controlCharacteristic;
        if (!control && context.config.controlCharacteristicUuid) {
            control = [self findCharacteristicForPeripheral:context.peripheral
                                                   service:context.config.serviceUuid.UUIDString
                                              characteristic:context.config.controlCharacteristicUuid.UUIDString];
            context.controlCharacteristic = control;
        }

        if (!control) {
            [self sendError:@"control characteristic not found" deviceId:deviceId];
            return;
        }

        NSLog(@"[BlePluginIOS] writeControl (%@) length=%lu", actionLabel, (unsigned long)payload.length);
        [context.peripheral writeValue:payload forCharacteristic:control type:CBCharacteristicWriteWithResponse];
    });
}

- (void)readCharacteristicWithService:(NSString *)serviceUuid characteristic:(NSString *)charUuid {
    dispatch_async(dispatch_get_main_queue(), ^{
        for (NSString *deviceId in self.deviceContexts) {
            BleDeviceContext *context = self.deviceContexts[deviceId];
            CBCharacteristic *characteristic = [self findCharacteristicForPeripheral:context.peripheral
                                                                              service:serviceUuid
                                                                         characteristic:charUuid];
            if (characteristic) {
                [context.peripheral readValueForCharacteristic:characteristic];
                break;
            }
        }
    });
}

- (void)writeCharacteristicWithService:(NSString *)serviceUuid characteristic:(NSString *)charUuid data:(NSData *)data {
    if (data.length == 0) {
        return;
    }
    dispatch_async(dispatch_get_main_queue(), ^{
        for (NSString *deviceId in self.deviceContexts) {
            BleDeviceContext *context = self.deviceContexts[deviceId];
            CBCharacteristic *characteristic = [self findCharacteristicForPeripheral:context.peripheral
                                                                              service:serviceUuid
                                                                         characteristic:charUuid];
            if (characteristic) {
                [context.peripheral writeValue:data forCharacteristic:characteristic type:CBCharacteristicWriteWithResponse];
                break;
            }
        }
    });
}

#pragma mark - CBCentralManagerDelegate

- (void)centralManagerDidUpdateState:(CBCentralManager *)central {
    NSString *stateString = @"unknown";
    switch (central.state) {
        case CBManagerStatePoweredOn: stateString = @"poweredOn"; break;
        case CBManagerStatePoweredOff: stateString = @"poweredOff"; break;
        case CBManagerStateUnauthorized: stateString = @"unauthorized"; break;
        case CBManagerStateUnsupported: stateString = @"unsupported"; break;
        case CBManagerStateResetting: stateString = @"resetting"; break;
        case CBManagerStateUnknown:
        default: stateString = @"unknown"; break;
    }

    [self sendUnityEvent:@{ @"eventType": @"state", @"state": stateString }];
}

- (void)centralManager:(CBCentralManager *)central didDiscoverPeripheral:(CBPeripheral *)peripheral
     advertisementData:(NSDictionary<NSString *,id> *)advertisementData
                  RSSI:(NSNumber *)RSSI {
    NSString *identifier = peripheral.identifier.UUIDString;
    if (identifier.length == 0) {
        return;
    }
    self.discoveredPeripherals[identifier] = peripheral;

    NSDictionary *event = @{ @"eventType": @"scanResult",
                             @"id": identifier,
                             @"name": peripheral.name ?: @"",
                             @"deviceType": @"",
                             @"rssi": RSSI ?: @0 };
    [self sendUnityEvent:event];
}

- (void)centralManager:(CBCentralManager *)central didConnectPeripheral:(CBPeripheral *)peripheral {
    NSString *deviceId = peripheral.identifier.UUIDString;
    BleDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        context = [[BleDeviceContext alloc] init];
        context.peripheral = peripheral;
        context.config = [[BleDeviceConfig alloc] init];
        self.deviceContexts[deviceId] = context;
    }

    if (context.config.requestMtu != nil) {
        NSLog(@"[BlePluginIOS] requestMtu=%@ ignored on iOS", context.config.requestMtu);
    }

    NSArray<CBUUID *> *services = context.config.serviceUuid ? @[context.config.serviceUuid] : nil;
    [peripheral discoverServices:services];

    NSDictionary *event = @{ @"eventType": @"connected",
                             @"deviceType": context.config.deviceType ?: @"",
                             @"id": deviceId };
    [self sendUnityEvent:event];
}

- (void)centralManager:(CBCentralManager *)central didFailToConnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    NSString *deviceId = peripheral.identifier.UUIDString;
    NSString *message = error.localizedDescription ?: @"failed to connect";
    [self sendUnityEvent:@{ @"eventType": @"error",
                            @"message": message,
                            @"id": deviceId ?: @"" }];
    [self cleanupDevice:deviceId];
}

- (void)centralManager:(CBCentralManager *)central didDisconnectPeripheral:(CBPeripheral *)peripheral error:(NSError *)error {
    NSString *deviceId = peripheral.identifier.UUIDString;
    BleDeviceContext *context = [self contextForPeripheral:peripheral];
    NSString *message = error ? (error.localizedDescription ?: @"disconnected") : nil;
    NSString *deviceType = context ? (context.config.deviceType ?: @"") : @"";
    NSMutableDictionary *event = [@{ @"eventType": @"disconnected",
                                      @"id": deviceId ?: @"",
                                      @"deviceType": deviceType } mutableCopy];
    if (message.length > 0) {
        event[@"detail"] = message;
    }
    [self sendUnityEvent:event];
    [self cleanupDevice:deviceId];
}

#pragma mark - CBPeripheralDelegate

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverServices:(NSError *)error {
    if (error) {
        [self sendUnityEvent:@{ @"eventType": @"error",
                                @"message": error.localizedDescription ?: @"service discovery failed",
                                @"id": peripheral.identifier.UUIDString ?: @"" }];
        return;
    }

    BleDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }
    CBService *targetService = nil;
    for (CBService *service in peripheral.services) {
        if (!context.config.serviceUuid || [service.UUID isEqual:context.config.serviceUuid]) {
            targetService = service;
            break;
        }
    }

    if (!targetService) {
        [self sendError:@"service not found" deviceId:peripheral.identifier.UUIDString];
        return;
    }

    [peripheral discoverCharacteristics:nil forService:targetService];
}

- (void)peripheral:(CBPeripheral *)peripheral didDiscoverCharacteristicsForService:(CBService *)service error:(NSError *)error {
    if (error) {
        [self sendUnityEvent:@{ @"eventType": @"error",
                                @"message": error.localizedDescription ?: @"characteristic discovery failed",
                                @"id": peripheral.identifier.UUIDString ?: @"" }];
        return;
    }

    BleDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }

    if (context.config.serviceUuid && ![service.UUID isEqual:context.config.serviceUuid]) {
        return;
    }

    for (CBCharacteristic *characteristic in service.characteristics) {
        if (context.config.controlCharacteristicUuid && [characteristic.UUID isEqual:context.config.controlCharacteristicUuid]) {
            context.controlCharacteristic = characteristic;
        }
        if (context.config.dataCharacteristicUuid && [characteristic.UUID isEqual:context.config.dataCharacteristicUuid]) {
            context.dataCharacteristic = characteristic;
        }
    }

    if (context.dataCharacteristic) {
        [peripheral setNotifyValue:YES forCharacteristic:context.dataCharacteristic];
    }

    if (context.config.emitReadyEvent && !context.readyEventSent) {
        context.readyEventSent = YES;
        [self sendUnityEvent:@{ @"eventType": @"ready",
                                @"deviceType": context.config.deviceType ?: @"",
                                @"id": peripheral.identifier.UUIDString ?: @"" }];
    }

    if (context.config.controlCharacteristicUuid && !context.controlCharacteristic) {
        [self sendError:@"control characteristic not found" deviceId:peripheral.identifier.UUIDString];
    }
    if (context.config.dataCharacteristicUuid && !context.dataCharacteristic) {
        [self sendError:@"data characteristic not found" deviceId:peripheral.identifier.UUIDString];
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateNotificationStateForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    if (error) {
        [self sendUnityEvent:@{ @"eventType": @"error",
                                @"message": error.localizedDescription ?: @"failed to enable notifications",
                                @"id": peripheral.identifier.UUIDString ?: @"" }];
        return;
    }

    BleDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }

    if (context.config.autoStartOnNotification && [characteristic.UUID isEqual:context.config.dataCharacteristicUuid] && characteristic.isNotifying) {
        NSInteger delayMs = MAX(context.config.notificationStartDelayMs, 0);
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(delayMs * NSEC_PER_MSEC)), dispatch_get_main_queue(), ^{
            [self startMeasurement:peripheral.identifier.UUIDString];
        });
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didWriteValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    BleDeviceContext *context = [self contextForPeripheral:peripheral];
    if (!context) {
        return;
    }

    if (error) {
        [self sendUnityEvent:@{ @"eventType": @"error",
                                @"message": error.localizedDescription ?: @"write failed",
                                @"id": peripheral.identifier.UUIDString ?: @"" }];
        return;
    }

    if (context.controlCharacteristic && [characteristic.UUID isEqual:context.controlCharacteristic.UUID]) {
        [self sendUnityEvent:@{ @"eventType": @"controlWritten",
                                @"deviceType": context.config.deviceType ?: @"",
                                @"id": peripheral.identifier.UUIDString ?: @"" }];
    }
}

- (void)peripheral:(CBPeripheral *)peripheral didUpdateValueForCharacteristic:(CBCharacteristic *)characteristic error:(NSError *)error {
    if (error) {
        [self sendUnityEvent:@{ @"eventType": @"error",
                                @"message": error.localizedDescription ?: @"read failed",
                                @"id": peripheral.identifier.UUIDString ?: @"" }];
        return;
    }

    NSData *value = characteristic.value ?: [NSData data];
    NSString *base64 = [value base64EncodedStringWithOptions:0] ?: @"";
    NSDictionary *event = @{ @"eventType": @"data",
                             @"id": peripheral.identifier.UUIDString ?: @"",
                             @"uuid": characteristic.UUID.UUIDString ?: @"",
                             @"value": base64 };
    [self sendUnityEvent:event];
}

#pragma mark - Helpers

- (BleDeviceContext *)contextForDevice:(NSString *)deviceId {
    if (deviceId.length == 0) {
        return nil;
    }
    return self.deviceContexts[deviceId];
}

- (BleDeviceContext *)contextForPeripheral:(CBPeripheral *)peripheral {
    if (!peripheral) {
        return nil;
    }
    return [self contextForDevice:peripheral.identifier.UUIDString];
}

- (void)cleanupDevice:(NSString *)deviceId {
    BleDeviceContext *context = self.deviceContexts[deviceId];
    if (context) {
        context.controlCharacteristic = nil;
        context.dataCharacteristic = nil;
        context.peripheral.delegate = nil;
        [self.deviceContexts removeObjectForKey:deviceId];
    }
}

- (void)sendUnityEvent:(NSDictionary *)event {
    if (self.unityObjectName.length == 0) {
        return;
    }
    NSError *error = nil;
    NSData *jsonData = [NSJSONSerialization dataWithJSONObject:event options:0 error:&error];
    if (!jsonData) {
        NSLog(@"[BlePluginIOS] Failed to serialize event: %@", error);
        return;
    }
    NSString *jsonString = [[NSString alloc] initWithData:jsonData encoding:NSUTF8StringEncoding];
    if (!jsonString) {
        return;
    }
    UnitySendMessage(self.unityObjectName.UTF8String, "OnNativeCallback", jsonString.UTF8String);
}

- (void)sendError:(NSString *)message deviceId:(NSString * _Nullable)deviceId {
    NSMutableDictionary *event = [@{ @"eventType": @"error",
                                      @"message": message ?: @"unknown" } mutableCopy];
    if (deviceId.length > 0) {
        event[@"id"] = deviceId;
    }
    [self sendUnityEvent:event];
}

- (CBCharacteristic *)findCharacteristicForPeripheral:(CBPeripheral *)peripheral service:(NSString *)serviceUuid characteristic:(NSString *)charUuid {
    if (!peripheral) {
        return nil;
    }
    CBUUID *serviceId = serviceUuid.length > 0 ? [CBUUID UUIDWithString:serviceUuid] : nil;
    CBUUID *charId = charUuid.length > 0 ? [CBUUID UUIDWithString:charUuid] : nil;

    for (CBService *service in peripheral.services) {
        if (serviceId && ![service.UUID isEqual:serviceId]) {
            continue;
        }
        for (CBCharacteristic *characteristic in service.characteristics) {
            if (!charId || [characteristic.UUID isEqual:charId]) {
                return characteristic;
            }
        }
    }
    return nil;
}

@end
