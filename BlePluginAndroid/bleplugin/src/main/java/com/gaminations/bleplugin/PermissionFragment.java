package com.gaminations.bleplugin;

import android.Manifest;
import android.app.Activity;
import android.os.Build;

import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.fragment.app.Fragment;
import androidx.annotation.NonNull;

import com.unity3d.player.UnityPlayer;

/**
 * Small fragment used to request runtime Bluetooth/location permissions.
 * Usage: PermissionFragment.requestPermissions(unityObject, unityMethod);
 *
 * Sends a simple "granted" or "denied" string back to Unity via UnitySendMessage(unityObject, unityMethod, response)
 */
public class PermissionFragment extends Fragment {

    private static String UNITY_OBJECT = "BLEPlugin";   // overwritten by caller
    private static String UNITY_METHOD = "OnPermissionResult"; // overwritten by caller

    private final ActivityResultLauncher<String[]> permissionLauncher =
            registerForActivityResult(new ActivityResultContracts.RequestMultiplePermissions(), result -> {
                boolean granted = true;
                for (Boolean res : result.values()) {
                    if (res == null || !res) {
                        granted = false;
                        break;
                    }
                }
                String response = granted ? "granted" : "denied";
                UnityPlayer.UnitySendMessage(UNITY_OBJECT, UNITY_METHOD, response);

                // Cleanup fragment from fragment manager
                if (getActivity() != null) {
                    getActivity().getSupportFragmentManager().beginTransaction().remove(this).commit();
                }
            });

    /**
     * Entry point from Java/Unity — add fragment synchronously and request permissions.
     */
    public static void requestPermissions(String unityObject, String unityMethod) {
        UNITY_OBJECT = unityObject;
        UNITY_METHOD = unityMethod;

        Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            androidx.fragment.app.FragmentManager fm =
                    ((androidx.fragment.app.FragmentActivity) activity).getSupportFragmentManager();
            PermissionFragment fragment = new PermissionFragment();
            // commitNow ensures fragment registered before we call launch
            fm.beginTransaction().add(fragment, "PermissionFragment").commitNow();
            fragment.askPermissions();
        });
    }

    /**
     * Choose which permissions to request depending on Android version.
     */
    private void askPermissions() {
        String[] permissions;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            permissions = new String[]{
                    Manifest.permission.BLUETOOTH_SCAN,
                    Manifest.permission.BLUETOOTH_CONNECT
            };
        } else {
            // Bluetooth scanning historically required location permission on older Android versions
            permissions = new String[]{
                    Manifest.permission.ACCESS_FINE_LOCATION
            };
        }
        permissionLauncher.launch(permissions);
    }
}
