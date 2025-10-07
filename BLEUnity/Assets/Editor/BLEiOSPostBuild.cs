using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace BLEUnity.Editor
{
    internal static class BLEiOSPostBuild
    {
        private const string BluetoothAlwaysUsageKey = "NSBluetoothAlwaysUsageDescription";
        private const string BluetoothPeripheralUsageKey = "NSBluetoothPeripheralUsageDescription";
        private const string BluetoothUsageDescription = "Bluetooth access is required to connect to nearby BLE accessories.";

        [PostProcessBuild]
        private static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            var plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning($"[BLE] Unable to locate Info.plist at '{plistPath}'. Skipping Bluetooth permission injection.");
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            PlistElementDict rootDict = plist.root;
            rootDict.SetString(BluetoothAlwaysUsageKey, BluetoothUsageDescription);
            rootDict.SetString(BluetoothPeripheralUsageKey, BluetoothUsageDescription);

            File.WriteAllText(plistPath, plist.WriteToString());
        }
    }
}
