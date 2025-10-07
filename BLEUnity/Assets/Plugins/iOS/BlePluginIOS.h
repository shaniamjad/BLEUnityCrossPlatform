#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BlePluginIOS : NSObject

@property (nonatomic, copy, nullable) NSString *unityObjectName;

+ (instancetype)shared;

- (void)initializeWithUnityObject:(NSString *)unityObjectName;
- (void)startScan;
- (void)stopScan;
- (void)connectDevice:(NSString *)deviceId profileJson:(NSString *)profileJson;
- (void)disconnectDevice:(NSString *)deviceId;
- (void)disconnectAllDevices;
- (void)startMeasurement:(NSString *)deviceId;
- (void)stopMeasurement:(NSString *)deviceId;
- (void)pauseMeasurement:(NSString *)deviceId;
- (void)sendControl:(NSString *)deviceId payload:(NSData *)payload action:(NSString *)action;
- (void)readCharacteristicWithService:(NSString *)serviceUuid characteristic:(NSString *)charUuid;
- (void)writeCharacteristicWithService:(NSString *)serviceUuid characteristic:(NSString *)charUuid data:(NSData *)data;

@end

NS_ASSUME_NONNULL_END
