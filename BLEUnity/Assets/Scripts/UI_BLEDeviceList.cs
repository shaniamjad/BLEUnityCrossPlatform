using UnityEngine;
using System.Collections.Generic;

public class UI_BLEDeviceList : MonoBehaviour
{
    public static UI_BLEDeviceList Instance { get; private set; }

    [SerializeField] private Transform contentRoot;
    [SerializeField] private GameObject deviceItemPrefab;

    private Dictionary<string, UI_BLEDeviceItem> deviceItems = new Dictionary<string, UI_BLEDeviceItem>();

    void Awake()
    {
        Instance = this;
    }

    public void Refresh(IEnumerable<BleDevice> devices)
    {
        Debug.Log("UI_BLEDeviceList::Refresh");

        foreach (var dev in devices)
        {
            if (dev.type == DeviceType.Unknown)
            {
                if (deviceItems.TryGetValue(dev.id, out var existingItem))
                {
                    Destroy(existingItem.gameObject);
                    deviceItems.Remove(dev.id);
                }

                continue;
            }

            if (!deviceItems.ContainsKey(dev.id))
            {
                var item = Instantiate(deviceItemPrefab, contentRoot);
                var comp = item.GetComponent<UI_BLEDeviceItem>();
                comp.Setup(dev);
                deviceItems[dev.id] = comp;
                Debug.Log("UI_BLEDeviceList::AddingDevice::" + dev.id);
            }
            else
            {
                Debug.Log("UI_BLEDeviceList::UpdatingDevice::" + dev.id);
                deviceItems[dev.id].UpdateStatus(dev);
            }
        }
    }
}
