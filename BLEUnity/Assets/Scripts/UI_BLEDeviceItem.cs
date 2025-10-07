using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI representation of a single BLE device.
/// Displays live parsed data and connection state.
/// </summary>
public class UI_BLEDeviceItem : MonoBehaviour, IBLEDeviceListener
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button disconnectButton;

    [Header("Data Output")]
    [SerializeField] private TMP_Text line1Text; // For main parsed data (EEG/Quaternion)
    [SerializeField] private TMP_Text line2Text; // For secondary data (Accel, etc.)
    [SerializeField] private TMP_Text line3Text; // For debug or misc info

    private BleDevice device;

    // Device-specific parsers
    private MovellaSignalParser movellaParser;
    private BiopotSignalParser biopotParser;

    #region Unity Lifecycle
    private void OnEnable()
    {
        if (device != null)
            BLEManager.Instance.AddListener(device.id, this);
    }

    private void OnDisable()
    {
        if (device != null)
            BLEManager.Instance.RemoveListener(device.id, this);
    }
    #endregion

    /// <summary>
    /// Setup UI for a specific BLE device entry.
    /// </summary>
    public void Setup(BleDevice dev)
    {
        device = dev;

        nameText.text = dev.name ?? "(Unnamed)";
        UpdateStatus(dev);

        // Initialize parser based on device type
        switch (dev.type)
        {
            case DeviceType.Movella:
                movellaParser = new MovellaSignalParser();
                break;
            case DeviceType.BioPot:
                biopotParser = new BiopotSignalParser(
                    new BiopotGenericInfo { ChannelsNumber = 8, SamplesPerChannelNumber = 7 });
                break;
        }

        connectButton.onClick.RemoveAllListeners();
        disconnectButton.onClick.RemoveAllListeners();

        connectButton.onClick.AddListener(() =>
        {
            BLEManager.Instance.Connect(dev.id, dev.type);
        });

        disconnectButton.onClick.AddListener(() =>
        {
            BLEManager.Instance.DisConnect(dev.id);
        });

        bool hasProfile = BleDeviceProfiles.TryGetProfile(dev.type) != null;
        connectButton.interactable = hasProfile;

        BLEManager.Instance.AddListener(dev.id, this);
    }

    /// <summary>
    /// Update the button states and status label.
    /// </summary>
    public void UpdateStatus(BleDevice dev)
    {
        device = dev;
        bool connected = dev.isConnected;

        string rssiText = dev.rssi != 0 ? $" (RSSI {dev.rssi})" : string.Empty;
        statusText.text = connected
            ? "<color=green>Connected</color>"
            : $"<color=red>Disconnected</color>{rssiText}";
        connectButton.gameObject.SetActive(!connected);
        disconnectButton.gameObject.SetActive(connected);

        // Clear data when disconnected
        if (!connected)
        {
            line1Text.text = "";
            line2Text.text = "";
            line3Text.text = "";
        }
    }

    #region IBLEDeviceListener Implementation

    public void OnConnected(BleDevice dev)
    {
        Debug.Log($"[UI_BLEDeviceItem] {dev.name} connected");
        UpdateStatus(dev);
        line1Text.text = "Waiting for data...";
        line2Text.text = "";
        line3Text.text = "";
    }

    public void OnDisconnected(BleDevice dev)
    {
        Debug.Log($"[UI_BLEDeviceItem] {dev.name} disconnected");
        UpdateStatus(dev);
    }

    public void OnData(BleDevice dev, byte[] rawData)
    {
        if (rawData == null || rawData.Length == 0)
            return;

        switch (dev.type)
        {
            case DeviceType.Movella:
                if (movellaParser != null && movellaParser.TryParse(rawData))
                {
                    // Display parsed Movella sensor data
                    line1Text.text = $"<b>Quat:</b> {string.Join(", ", movellaParser.Quaternion)}";
                    line2Text.text = $"<b>Accel:</b> {string.Join(", ", movellaParser.FreeAcceleration)}";
                    line3Text.text = $"<b>Status:</b> {movellaParser.Status} | ClipA:{movellaParser.ClippingCountAccelerometer} ClipG:{movellaParser.ClippingCountGyroscope}";
                }
                break;

            case DeviceType.BioPot:
                if (biopotParser != null && biopotParser.TryParse(rawData))
                {
                    var eeg = biopotParser.SpdData;
                    var acc = biopotParser.AccelerometerData;
                    var bio = biopotParser.BioImpedanceData;

                    if (eeg != null && eeg.Length > 0)
                    {
                        string eegValues = "";
                        for (int s = 0; s < Mathf.Min(eeg.GetLength(1), 5); s++)
                            eegValues += eeg[0, s].ToString("F2") + ", ";
                        line1Text.text = $"<b>EEG (Ch0):</b> {eegValues}";
                    }

                    if (acc != null && acc.Length > 0)
                    {
                        string accValues = "";
                        for (int i = 0; i < Mathf.Min(acc.Length, 3); i++)
                            accValues += acc[i].ToString("F2") + ", ";
                        line2Text.text = $"<b>Accel:</b> {accValues}";
                    }

                    if (bio != null && bio.Length > 0)
                    {
                        string bioValues = "";
                        for (int i = 0; i < Mathf.Min(bio.Length, 3); i++)
                            bioValues += bio[i].ToString("F2") + ", ";
                        line3Text.text = $"<b>Bio:</b> {bioValues}";
                    }
                }
                break;
        }
    }

    #endregion
}
