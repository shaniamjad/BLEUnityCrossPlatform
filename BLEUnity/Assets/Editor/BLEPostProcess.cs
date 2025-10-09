#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace BLEUnity.Editor
{
    public static class BLEPostProcess
    {
        private const string BluetoothUsageMessage = "This app requires Bluetooth access to connect to nearby BLE devices.";
        private const string AlwaysUsageKey = "NSBluetoothAlwaysUsageDescription";
        private const string PeripheralUsageKey = "NSBluetoothPeripheralUsageDescription";
        private const string CoreBluetoothFramework = "CoreBluetooth.framework";

        [PostProcessBuild(45)]
        public static void AddBluetoothPermissions(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            AddBluetoothPermissions(path);
            EnsureCoreBluetoothFramework(path);
        }

        private static void AddBluetoothPermissions(string buildPath)
        {
            var plistPath = Path.Combine(buildPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"Info.plist not found at {plistPath}; skipping BLE permission injection.");
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            var rootDict = plist.root;

            EnsureKeyWithMessage(rootDict, AlwaysUsageKey);
            EnsureKeyWithMessage(rootDict, PeripheralUsageKey);

            plist.WriteToFile(plistPath);
        }

        private static void EnsureKeyWithMessage(PlistElementDict rootDict, string key)
        {
            if (!rootDict.values.TryGetValue(key, out var element) ||
                element is not PlistElementString stringElement ||
                string.IsNullOrEmpty(stringElement.value))
            {
                rootDict.SetString(key, BluetoothUsageMessage);
            }
        }

        private static void EnsureCoreBluetoothFramework(string buildPath)
        {
            var projectPath = PBXProject.GetPBXProjectPath(buildPath);
            if (!File.Exists(projectPath))
            {
                Debug.LogWarning($"Xcode project not found at {projectPath}; skipping CoreBluetooth linking.");
                return;
            }

            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            var unityMainTargetGuid = project.GetUnityMainTargetGuid();
            if (!string.IsNullOrEmpty(unityMainTargetGuid))
            {
                project.AddFrameworkToProject(unityMainTargetGuid, CoreBluetoothFramework, false);
            }

            var unityFrameworkTargetGuid = project.GetUnityFrameworkTargetGuid();
            if (!string.IsNullOrEmpty(unityFrameworkTargetGuid))
            {
                project.AddFrameworkToProject(unityFrameworkTargetGuid, CoreBluetoothFramework, false);
            }

            project.WriteToFile(projectPath);
        }
    }
}
#endif
