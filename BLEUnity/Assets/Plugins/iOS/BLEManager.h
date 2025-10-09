#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>

@interface BLEManager : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>

@property (nonatomic, copy) NSString *unityObjectName;

+ (instancetype)shared;
- (void)start;
- (void)startScan;
- (void)stopScan;
- (void)connectToDevice:(NSString*)deviceId profileJson:(NSString*)profileJson;
- (void)disconnectDevice:(NSString*)deviceId;
- (void)startMeasurement:(NSString*)deviceId;
- (void)stopMeasurement:(NSString*)deviceId;
- (void)pauseMeasurement:(NSString*)deviceId;
- (void)sendControl:(NSString*)deviceId base64Payload:(NSString*)payload action:(NSString*)action;
- (void)readCharacteristicWithService:(NSString*)service charId:(NSString*)charId;
- (void)writeCharacteristicWithService:(NSString*)service charId:(NSString*)charId data:(NSData*)data;

@end
