using System;
using UnityEngine;

public class MovellaSignalParser
{
    private readonly int expectedPacketSize;

    public MovellaSignalParser()
    {
        expectedPacketSize = GetExpectedPacketSize();
    }

    public bool TryParse(byte[] packet, out MovellaParsedData result)
    {
        result = null;

        if (packet == null || packet.Length < expectedPacketSize)
        {
            Debug.LogWarning($"[MovellaParser] Invalid packet length: {packet?.Length}");
            return false;
        }

        int offset = 0;
        var data = new MovellaParsedData();

        // Timestamp (uint32 little-endian)
        data.Timestamp =
            packet[offset + 3] << 24 |
            packet[offset + 2] << 16 |
            packet[offset + 1] << 8 |
            packet[offset + 0];
        offset += 4;

        // Quaternion (4 floats)
        data.Quaternion = new float[4];
        for (int i = 0; i < 4; i++)
        {
            data.Quaternion[i] = BitConverter.ToSingle(packet, offset);
            offset += 4;
        }

        // Free Acceleration (3 floats)
        data.FreeAcceleration = new float[3];
        for (int i = 0; i < 3; i++)
        {
            data.FreeAcceleration[i] = BitConverter.ToSingle(packet, offset);
            offset += 4;
        }

        // Status
        data.Status = packet[offset++];

        // Clipping counts
        data.ClippingCountAccelerometer = packet[offset++];
        data.ClippingCountGyroscope = packet[offset++];

        result = data;
        return true;
    }

    private int GetExpectedPacketSize()
    {
        return 4   // timestamp
             + 16  // quaternion (4 floats)
             + 12  // free accel (3 floats)
             + 1   // status
             + 1   // clip accel
             + 1;  // clip gyro
    }
}
