package com.gaminations.bleplugin;

import android.Manifest;
import android.app.Activity;
import android.app.Fragment;
import android.app.FragmentManager;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;

import com.unity3d.player.UnityPlayer;

/**
 * Works with UnityPlayerActivity (not a FragmentActivity)
 */
public class PermissionFragment extends Fragment {

    private static String UNITY_OBJECT = "BLEPlugin";
    private static String UNITY_METHOD = "OnPermissionResult";

    private static final int REQUEST_CODE = 1001;

    public static void requestPermissions(String unityObject, String unityMethod) {
        UNITY_OBJECT = unityObject;
        UNITY_METHOD = unityMethod;

        Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            FragmentManager fm = activity.getFragmentManager();
            PermissionFragment fragment = new PermissionFragment();
            fm.beginTransaction().add(fragment, "PermissionFragment").commitAllowingStateLoss();
            fm.executePendingTransactions();
            fragment.askPermissions();
        });
    }

    private void askPermissions() {
        String[] permissions;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            permissions = new String[]{
                    Manifest.permission.BLUETOOTH_SCAN,
                    Manifest.permission.BLUETOOTH_CONNECT
            };
        } else {
            permissions = new String[]{
                    Manifest.permission.ACCESS_FINE_LOCATION
            };
        }

        requestPermissions(permissions, REQUEST_CODE);
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        if (requestCode == REQUEST_CODE) {
            boolean granted = true;
            for (int res : grantResults) {
                if (res != PackageManager.PERMISSION_GRANTED) {
                    granted = false;
                    break;
                }
            }

            String response = granted ? "granted" : "denied";
            UnityPlayer.UnitySendMessage(UNITY_OBJECT, UNITY_METHOD, response);

            // remove the fragment after result
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
        }
    }
}
