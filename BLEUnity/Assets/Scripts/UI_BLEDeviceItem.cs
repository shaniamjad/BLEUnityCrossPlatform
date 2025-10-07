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

    [Header("Measurement Controls")]
    [SerializeField] private Button startMeasurementButton;
    [SerializeField] private Button pauseMeasurementButton;
    [SerializeField] private Button stopMeasurementButton;
    [SerializeField] private TMP_Text measurementStatusText;

    [Header("Data Output")]
    [SerializeField] private TMP_Text line1Text; // For main parsed data (EEG/Quaternion)
    [SerializeField] private TMP_Text line2Text; // For secondary data (Accel, etc.)
    [SerializeField] private TMP_Text line3Text; // For debug or misc info

    private BleDevice device;
    private MeasurementState currentMeasurementState = MeasurementState.Idle;

    private string lastLine1;
    private string lastLine2;
    private string lastLine3;
    private bool hasData;
    private bool dataStale;

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

        lastLine1 = lastLine2 = lastLine3 = string.Empty;
        hasData = false;
        dataStale = false;

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

        if (startMeasurementButton != null)
        {
            startMeasurementButton.onClick.RemoveAllListeners();
            startMeasurementButton.onClick.AddListener(() =>
            {
                if (BLEManager.Instance != null)
                    BLEManager.Instance.StartMeasurement(dev.id);
            });
        }

        if (pauseMeasurementButton != null)
        {
            pauseMeasurementButton.onClick.RemoveAllListeners();
            pauseMeasurementButton.onClick.AddListener(() =>
            {
                if (BLEManager.Instance != null)
                    BLEManager.Instance.PauseMeasurement(dev.id);
            });
        }

        if (stopMeasurementButton != null)
        {
            stopMeasurementButton.onClick.RemoveAllListeners();
            stopMeasurementButton.onClick.AddListener(() =>
            {
                if (BLEManager.Instance != null)
                    BLEManager.Instance.StopMeasurement(dev.id);
            });
        }

        bool hasProfile = BleDeviceProfiles.TryGetProfile(dev.type) != null;
        connectButton.interactable = hasProfile;

        BLEManager.Instance.AddListener(dev.id, this);

        currentMeasurementState = dev.measurementState;
        UpdateMeasurementUI(currentMeasurementState);
        RefreshDataLabels();
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

        UpdateMeasurementUI(dev.measurementState);
    }

    private void UpdateMeasurementUI(MeasurementState state)
    {
        currentMeasurementState = state;
        bool connected = device != null && device.isConnected;

        if (measurementStatusText != null)
        {
            if (!connected)
            {
                measurementStatusText.text = "<color=#888888>Disconnected</color>";
            }
            else
            {
                switch (state)
                {
                    case MeasurementState.Sampling:
                        measurementStatusText.text = "<color=green>Sampling…</color>";
                        break;
                    case MeasurementState.Paused:
                        measurementStatusText.text = "<color=#ffcc00>Paused</color>";
                        break;
                    case MeasurementState.Starting:
                        measurementStatusText.text = "Starting…";
                        break;
                    default:
                        measurementStatusText.text = "<color=#888888>Stopped</color>";
                        break;
                }
            }
        }

        if (startMeasurementButton != null)
            startMeasurementButton.interactable = connected && (state == MeasurementState.Idle || state == MeasurementState.Paused);
        if (pauseMeasurementButton != null)
            pauseMeasurementButton.interactable = connected && state == MeasurementState.Sampling;
        if (stopMeasurementButton != null)
            stopMeasurementButton.interactable = connected && (state == MeasurementState.Sampling || state == MeasurementState.Paused || state == MeasurementState.Starting);

        bool shouldMarkStale = !connected || state != MeasurementState.Sampling;
        dataStale = shouldMarkStale && hasData;

        if (!hasData && connected && state == MeasurementState.Sampling)
        {
            dataStale = true;
        }

        RefreshDataLabels();
    }

    private void RefreshDataLabels()
    {
        bool connected = device != null && device.isConnected;

        if (!hasData)
        {
            if (line1Text != null)
            {
                if (connected)
                {
                    line1Text.text = dataStale
                        ? "<color=#888888>Waiting for data…</color>"
                        : "Waiting for data…";
                }
                else
                {
                    line1Text.text = string.Empty;
                }
            }

            if (line2Text != null)
                line2Text.text = string.Empty;
            if (line3Text != null)
                line3Text.text = string.Empty;
            return;
        }

        SetDataText(line1Text, lastLine1);
        SetDataText(line2Text, lastLine2);
        SetDataText(line3Text, lastLine3);
    }

    private void SetDataText(TMP_Text label, string value)
    {
        if (label == null)
            return;

        if (string.IsNullOrEmpty(value))
        {
            label.text = string.Empty;
            return;
        }

        label.text = dataStale
            ? $"{value} <color=#888888><i>(last)</i></color>"
            : value;
    }

    #region IBLEDeviceListener Implementation

    public void OnConnected(BleDevice dev)
    {
        Debug.Log($"[UI_BLEDeviceItem] {dev.name} connected");
        UpdateStatus(dev);
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

        bool updated = false;

        switch (dev.type)
        {
            case DeviceType.Movella:
                if (movellaParser != null && movellaParser.TryParse(rawData))
                {
                    // Display parsed Movella sensor data
                    lastLine1 = $"<b>Quat:</b> {string.Join(", ", movellaParser.Quaternion)}";
                    lastLine2 = $"<b>Accel:</b> {string.Join(", ", movellaParser.FreeAcceleration)}";
                    lastLine3 = $"<b>Status:</b> {movellaParser.Status} | ClipA:{movellaParser.ClippingCountAccelerometer} ClipG:{movellaParser.ClippingCountGyroscope}";
                    updated = true;
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
                        lastLine1 = $"<b>EEG (Ch0):</b> {eegValues}";
                        updated = true;
                    }

                    if (acc != null && acc.Length > 0)
                    {
                        string accValues = "";
                        for (int i = 0; i < Mathf.Min(acc.Length, 3); i++)
                            accValues += acc[i].ToString("F2") + ", ";
                        lastLine2 = $"<b>Accel:</b> {accValues}";
                        updated = true;
                    }

                    if (bio != null && bio.Length > 0)
                    {
                        string bioValues = "";
                        for (int i = 0; i < Mathf.Min(bio.Length, 3); i++)
                            bioValues += bio[i].ToString("F2") + ", ";
                        lastLine3 = $"<b>Bio:</b> {bioValues}";
                        updated = true;
                    }
                }
                break;
        }

        if (updated)
        {
            hasData = true;
            dataStale = false;
            UpdateMeasurementUI(currentMeasurementState);
        }
    }

    public void OnMeasurementStateChanged(BleDevice dev, MeasurementState state)
    {
        if (device == null || dev.id != device.id)
            return;

        UpdateMeasurementUI(state);
    }

    #endregion
}
