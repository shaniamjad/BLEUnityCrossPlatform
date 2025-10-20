using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_BLEDeviceItem : MonoBehaviour, IBLEDeviceListener
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button connectButton;
    [SerializeField] private Button disconnectButton;

    [Header("Data Output")]
    [SerializeField] private TMP_Text line1Text;
    [SerializeField] private TMP_Text line2Text;
    [SerializeField] private TMP_Text line3Text;

    [Header("Measurement Controls")]
    [SerializeField] private Button startMeasurementButton;
    [SerializeField] private Button pauseMeasurementButton;
    [SerializeField] private Button stopMeasurementButton;

    private BleDevice device;


    private bool hasProfile;

    public void Setup(BleDevice dev)
    {
        device = dev;

        nameText.text = dev.name ?? "(Unnamed)";
        line1Text.text = string.Empty;
        line2Text.text = string.Empty;
        line3Text.text = string.Empty;

        hasProfile = BleDeviceProfiles.TryGetProfile(dev.type) != null;
        UpdateStatus(dev);

        connectButton.onClick.RemoveAllListeners();
        disconnectButton.onClick.RemoveAllListeners();
        startMeasurementButton?.onClick.RemoveAllListeners();
        pauseMeasurementButton?.onClick.RemoveAllListeners();
        stopMeasurementButton?.onClick.RemoveAllListeners();

        connectButton.onClick.AddListener(() =>
        {
            if (device == null) return;

            if (hasProfile)
                TrustedDeviceStore.AddOrUpdate(device.id, device.type);

            device.Connect();
        });

        disconnectButton.onClick.AddListener(() =>
        {
            dev.DisConnect();
        });

        startMeasurementButton?.onClick.AddListener(OnStartMeasurementClicked);
        pauseMeasurementButton?.onClick.AddListener(OnPauseMeasurementClicked);
        stopMeasurementButton?.onClick.AddListener(OnStopMeasurementClicked);

        device.AddListener(this);
    }

    private void OnStartMeasurementClicked()
    {
        if (device == null || device.measurementState == MeasurementState.Sampling)
            return;
        device.StartMeasurement();
    }

    private void OnPauseMeasurementClicked()
    {
        if (device == null || device.measurementState != MeasurementState.Sampling)
            return;
        device.PauseMeasurement();
    }

    private void OnStopMeasurementClicked()
    {
        if (device == null || device.measurementState == MeasurementState.Idle)
            return;
        device.StopMeasurement();
    }

    public void UpdateStatus(BleDevice dev)
    {
        device = dev;
        hasProfile = BleDeviceProfiles.TryGetProfile(dev.type) != null;
        bool connected = dev.isConnected;
        string rssiText = dev.rssi != 0 ? $" (RSSI {dev.rssi})" : string.Empty;

        if (connected)
            statusText.text = "<color=green>Connected</color>";
        else if (dev.isAutoConnecting)
            statusText.text = $"<color=yellow>Auto-connecting...</color>{rssiText}";
        else
            statusText.text = $"<color=red>Disconnected</color>{rssiText}";

        if (!string.IsNullOrEmpty(dev.connectionNote))
            statusText.text += $"\n<color=#cccccc>{dev.connectionNote}</color>";

        connectButton.gameObject.SetActive(!connected);
        disconnectButton.gameObject.SetActive(connected);
        connectButton.interactable = hasProfile && !dev.isAutoConnecting;
    }

    // ─────────────────────────────────────────────
    // IBLEDeviceListener Implementation
    // ─────────────────────────────────────────────
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

    public void OnReady(BleDevice dev) { }
    public void OnMeasurementStateChanged(BleDevice dev, MeasurementState state) => device = dev;

    public void OnData(BleDevice dev, IParsedData parsedData)
    {
        if (parsedData == null)
            return;

        switch (dev.type)
        {
            case DeviceType.Movella:
                MovellaParsedData movellaData = (MovellaParsedData)parsedData;
                line1Text.text = $"<b>Quat:</b> {string.Join(", ", movellaData.Quaternion)}";
                line2Text.text = $"<b>Accel:</b> {string.Join(", ", movellaData.FreeAcceleration)}";
                line3Text.text = $"<b>Status:</b> {movellaData.Status} | ClipA:{movellaData.ClippingCountAccelerometer} ClipG:{movellaData.ClippingCountGyroscope}";
                break;

            case DeviceType.BioPot:
                BiopotParsedData bioData = (BiopotParsedData)parsedData;
                if (bioData.SpdData != null && bioData.SpdData.Length > 0)
                {
                    string eegValues = "";
                    for (int s = 0; s < Mathf.Min(bioData.SpdData.GetLength(1), 5); s++)
                        eegValues += bioData.SpdData[0, s].ToString("F2") + ", ";
                    line1Text.text = $"<b>EEG (Ch0):</b> {eegValues}";
                }

                if (bioData.AccelerometerData != null && bioData.AccelerometerData.Length > 0)
                {
                    string accValues = "";
                    for (int i = 0; i < Mathf.Min(bioData.AccelerometerData.Length, 3); i++)
                        accValues += bioData.AccelerometerData[i].ToString("F2") + ", ";
                    line2Text.text = $"<b>Accel:</b> {accValues}";
                }

                if (bioData.BioImpedanceData != null && bioData.BioImpedanceData.Length > 0)
                {
                    string bioValues = "";
                    for (int i = 0; i < Mathf.Min(bioData.BioImpedanceData.Length, 3); i++)
                        bioValues += bioData.BioImpedanceData[i].ToString("F2") + ", ";
                    line3Text.text = $"<b>Bio:</b> {bioValues}";
                }
                break;
        }
    }
}
