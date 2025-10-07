#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>

@interface BLEManager : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>

@property (nonatomic, strong) NSString *unityObjectName;

+ (instancetype)shared;
- (void)start;
- (void)startScan;
- (void)stopScan;
- (void)connectToDevice:(NSString*)deviceId;
- (void)readCharacteristicWithService:(NSString*)service charId:(NSString*)charId;
- (void)writeCharacteristicWithService:(NSString*)service charId:(NSString*)charId data:(NSData*)data;

@end
