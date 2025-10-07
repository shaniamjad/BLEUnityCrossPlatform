# Recommended BLE App Workflow

This document outlines a suggested end-to-end workflow for the Unity BLE sample, including scanning, pairing, reconnection, and UI/UX considerations.

## 1. App launch & permissions
1. Display a lightweight loading state while requesting permissions.
2. Request Bluetooth, location, and notification permissions on launch. Defer heavy UI interactions until `BLEPlugin.PermissionGranted` is `true` to avoid confusing failures.
3. If permissions are denied, surface a modal with retry instructions and a deep link to system settings.

_Implementation hook_: `BLEManager.Start()` already waits for `BLEPlugin.Instance.PermissionGranted` before calling `Init()`. 【F:BLEUnity/Assets/Scripts/BLEManager.cs†L56-L75】

## 2. Scanning flow
1. Auto-start a **short discovery scan** after initialization (5–10 seconds) to populate known devices without requiring the user to press "Start Scan".
2. Keep manual scan controls accessible for troubleshooting or when a user wants to find a brand-new sensor.
3. Display scan state in the UI (e.g., progress indicator or "Scanning…" label) and disable duplicate scans while one is in flight.

_Implementation details_: `BLEManager` orchestrates the automatic launch scan, manual scan throttling, and countdown updates via `BeginScan`, `ScanRoutine`, and the `ScanStateChanged` event, while `UI_BLEScanStatus` surfaces the state in the HUD. 【F:BLEUnity/Assets/Scripts/BLEManager.cs†L56-L165】【F:BLEUnity/Assets/Scripts/UI_BLEScanStatus.cs†L1-L38】

## 3. Device discovery list
1. Show all discovered devices with connection status, RSSI, and device-type-specific iconography.
2. Collapse unknown/unsupported peripherals into a secondary list to reduce clutter. The existing `UI_BLEDeviceList.Refresh()` already removes entries whose profile is unknown—keep that behavior but provide an informational banner so users understand why devices disappear. 【F:BLEUnity/Assets/Scripts/UI_BLEDeviceList.cs†L17-L45】
3. Surface a "Last seen" timestamp and sort by signal strength to help users pick the right device quickly.

## 4. Connection & pairing
1. When the user taps **Connect** for the first time, persist the device ID and profile in local storage (e.g., `PlayerPrefs` or a lightweight JSON file).
2. On subsequent app launches, automatically reconnect to previously trusted devices as soon as they are discovered. Allow users to opt out from settings.
3. If auto-connection fails (e.g., device not found within 15 seconds), show a non-blocking toast and prompt the user to retry manually.
4. Keep connect/disconnect controls visible per device. `UI_BLEDeviceItem` already toggles button state based on connection status—retain this responsive feedback. 【F:BLEUnity/Assets/Scripts/UI_BLEDeviceItem.cs†L43-L107】

_Implementation details_: Trusted devices are cached with `BLETrustedDeviceStore`, and `BLEManager.TryQueueAutoConnect` reconnects to them after each scan result while respecting manual disconnects. 【F:BLEUnity/Assets/Scripts/BLETrustedDeviceStore.cs†L1-L109】【F:BLEUnity/Assets/Scripts/BLEManager.cs†L207-L350】【F:BLEUnity/Assets/Scripts/BLEManager.cs†L415-L440】

## 5. Measurement lifecycle
1. Once a device reports `ready`, kick off measurement automatically when the profile requires it. This already exists via `BleDeviceProfiles` and `AutoStartAfterDelay`; ensure profile metadata stays accurate. 【F:BLEUnity/Assets/Scripts/BLEManager.cs†L299-L321】【F:BLEUnity/Assets/Scripts/Profiles/BleDeviceProfiles.cs†L5-L42】
2. Provide explicit **Start**, **Pause**, and **Stop** controls for advanced users. Surface the command feedback (e.g., "Sampling…", "Paused") near the data readout.
3. When disconnecting, clear data panels but persist the last values until a new reading arrives to avoid flicker.

## 6. Background resilience
1. Handle app suspension by pausing measurements and releasing the scan to conserve battery.
2. On resume, re-run a quick scan and reconnect to trusted devices.
3. Log reconnection attempts so testers can diagnose hardware stability.

_Implementation details_: `BLEManager.OnApplicationPause` pauses active measurements, stops scanning when the app is backgrounded, and relaunches a short recovery scan on resume. 【F:BLEUnity/Assets/Scripts/BLEManager.cs†L77-L105】

## 7. Settings & maintenance
1. Provide a settings screen with:
   - Trusted devices list (with remove/forget action).
   - Scan duration and auto-reconnect toggles.
   - Debug logging level switch for support builds.
2. Offer a "Report issue" shortcut that exports recent logs.

## 8. UX polish checklist
- Always show which state the BLE stack is in (Idle, Scanning, Connecting, Connected, Streaming).
- Disable destructive actions (e.g., Stop Scan) while the underlying native call is outstanding.
- Use toast or snackbar notifications for transient states and error handling.
- Animate transitions (e.g., fade in data panels) to reassure users that the app is responsive.

Following this workflow keeps the onboarding flow simple (automatic scan + reconnection), while preserving manual controls for power users and QA. It also leans on the existing code structure, so the implementation involves incremental updates rather than a full rewrite.
