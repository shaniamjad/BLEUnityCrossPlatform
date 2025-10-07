using System;
using UnityEngine;

public class MovellaSignalParser
{
    public int Timestamp { get; private set; }
    public float[] Quaternion { get; private set; }
    public float[] FreeAcceleration { get; private set; }
    public byte Status { get; private set; }
    public byte ClippingCountAccelerometer { get; private set; }
    public byte ClippingCountGyroscope { get; private set; }

    private readonly int expectedPacketSize;

    public MovellaSignalParser()
    {
        expectedPacketSize = GetExpectedPacketSize();
        ResetData();
    }

    public bool TryParse(byte[] packet)
    {
        ResetData();

        if (packet == null || packet.Length < expectedPacketSize)
        {
            Debug.LogWarning($"[MovellaParser] Invalid packet length: {packet?.Length}");
            return false;
        }

        int offset = 0;

        // Timestamp (uint32 little-endian)
        Timestamp = packet[offset + 3] << 24 |
                    packet[offset + 2] << 16 |
                    packet[offset + 1] << 8 |
                    packet[offset + 0];
        offset += 4;

        // Quaternion (4 floats)
        Quaternion = new float[4];
        for (int i = 0; i < 4; i++)
        {
            Quaternion[i] = BitConverter.ToSingle(packet, offset);
            offset += 4;
        }

        // Free Acceleration (3 floats)
        FreeAcceleration = new float[3];
        for (int i = 0; i < 3; i++)
        {
            FreeAcceleration[i] = BitConverter.ToSingle(packet, offset);
            offset += 4;
        }

        // Status
        Status = packet[offset++];
        // Clipping counts
        ClippingCountAccelerometer = packet[offset++];
        ClippingCountGyroscope = packet[offset++];

        return true;
    }

    private void ResetData()
    {
        Timestamp = 0;
        Quaternion = Array.Empty<float>();
        FreeAcceleration = Array.Empty<float>();
        Status = 0;
        ClippingCountAccelerometer = 0;
        ClippingCountGyroscope = 0;
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
