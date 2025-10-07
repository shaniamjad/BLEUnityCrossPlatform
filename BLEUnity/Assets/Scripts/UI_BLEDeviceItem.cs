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

    [Header("Measurement Controls")]
    [SerializeField] private Button startMeasurementButton;
    [SerializeField] private Button pauseMeasurementButton;
    [SerializeField] private Button stopMeasurementButton;

    private BleDevice device;

    // Device-specific parsers
    private MovellaSignalParser movellaParser;
    private BiopotSignalParser biopotParser;

    private bool hasProfile;

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
        hasProfile = BleDeviceProfiles.TryGetProfile(dev.type) != null;
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
        if (startMeasurementButton != null) startMeasurementButton.onClick.RemoveAllListeners();
        if (pauseMeasurementButton != null) pauseMeasurementButton.onClick.RemoveAllListeners();
        if (stopMeasurementButton != null) stopMeasurementButton.onClick.RemoveAllListeners();

        connectButton.onClick.AddListener(() =>
        {
            if (device == null)
                return;

            if (hasProfile)
            {
                TrustedDeviceStore.AddOrUpdate(device.id, device.type);
            }

            BLEManager.Instance.Connect(device.id, device.type);
        });

        disconnectButton.onClick.AddListener(() =>
        {
            BLEManager.Instance.DisConnect(dev.id);
        });

        if (startMeasurementButton != null)
            startMeasurementButton.onClick.AddListener(OnStartMeasurementClicked);
        if (pauseMeasurementButton != null)
            pauseMeasurementButton.onClick.AddListener(OnPauseMeasurementClicked);
        if (stopMeasurementButton != null)
            stopMeasurementButton.onClick.AddListener(OnStopMeasurementClicked);

        BLEManager.Instance.AddListener(dev.id, this);
    }

    private void OnStartMeasurementClicked()
    {
        if (device == null || device.measurementState == MeasurementState.Sampling)
            return;

        BLEManager.Instance.StartMeasurement(device.id);
    }

    private void OnPauseMeasurementClicked()
    {
        if (device == null || device.measurementState != MeasurementState.Sampling)
            return;

        BLEManager.Instance.PauseMeasurement(device.id);
    }

    private void OnStopMeasurementClicked()
    {
        if (device == null || device.measurementState == MeasurementState.Idle)
            return;

        BLEManager.Instance.StopMeasurement(device.id);
    }

    /// <summary>
    /// Update the button states and status label.
    /// </summary>
    public void UpdateStatus(BleDevice dev)
    {
        device = dev;
        hasProfile = BleDeviceProfiles.TryGetProfile(dev.type) != null;
        bool connected = dev.isConnected;

        string rssiText = dev.rssi != 0 ? $" (RSSI {dev.rssi})" : string.Empty;
        if (connected)
        {
            statusText.text = "<color=green>Connected</color>";
        }
        else if (dev.isAutoConnecting)
        {
            statusText.text = $"<color=yellow>Auto-connecting...</color>{rssiText}";
        }
        else
        {
            statusText.text = $"<color=red>Disconnected</color>{rssiText}";
        }

        if (!string.IsNullOrEmpty(dev.connectionNote))
        {
            statusText.text += $"\n<color=#cccccc>{dev.connectionNote}</color>";
        }

        connectButton.gameObject.SetActive(!connected);
        disconnectButton.gameObject.SetActive(connected);
        connectButton.interactable = hasProfile && !dev.isAutoConnecting;

    }

    #region IBLEDeviceListener Implementation

    public void OnConnected(BleDevice dev)
    {
        Debug.Log($"[UI_BLEDeviceItem] {dev.name} connected");
        UpdateStatus(dev);
        
        line1Text.text = "Waiting for data...";
        line2Text.text = string.Empty;
        line3Text.text = string.Empty;
    }

    public void OnDisconnected(BleDevice dev)
    {
        Debug.Log($"[UI_BLEDeviceItem] {dev.name} disconnected");
        UpdateStatus(dev);
        line1Text.text = string.Empty;
        line2Text.text = string.Empty;
        line3Text.text = string.Empty;
    }

    public void OnReady(BleDevice dev)
    {
        device = dev;
    }

    public void OnMeasurementStateChanged(BleDevice dev, MeasurementState state)
    {
        device = dev;
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
