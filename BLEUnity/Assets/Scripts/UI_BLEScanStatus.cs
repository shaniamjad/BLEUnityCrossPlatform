using TMPro;
using UnityEngine;

/// <summary>
/// Displays the current scan state (Idle/Scanning) and optional countdown.
/// Attach to a banner or status widget in the UI.
/// </summary>
public class UI_BLEScanStatus : MonoBehaviour
{
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private GameObject scanningIndicator;
    [SerializeField] private GameObject idleIndicator;

    private void OnEnable()
    {
        if (BLEManager.Instance != null)
        {
            BLEManager.Instance.ScanStateChanged += HandleScanStateChanged;
            HandleScanStateChanged(BLEManager.Instance.IsScanning, 0f);
        }
    }

    private void OnDisable()
    {
        if (BLEManager.Instance != null)
        {
            BLEManager.Instance.ScanStateChanged -= HandleScanStateChanged;
        }
    }

    private void HandleScanStateChanged(bool scanning, float remainingSeconds)
    {
        if (statusLabel != null)
        {
            statusLabel.text = scanning
                ? (remainingSeconds > 0f
                    ? $"Scanning… ({Mathf.CeilToInt(remainingSeconds)}s)"
                    : "Scanning…")
                : "Idle";
        }

        if (scanningIndicator != null)
            scanningIndicator.SetActive(scanning);
        if (idleIndicator != null)
            idleIndicator.SetActive(!scanning);
    }
}
