using System;

/// <summary>
/// Parses biopotential (EEG/EMG/SPD) BLE signal packets into structured data arrays.
/// </summary>
public class BiopotSignalParser
{
    private static readonly int[,] EmptyDataSamples = new int[0, 0];
    private static readonly short[] EmptyAccelerometerSamples = new short[0];
    private static readonly int[] EmptyBioImpSamples = new int[0];

    private readonly BiopotGenericInfo _biopotParams;
    private readonly uint _expectedPacketSize;

    private readonly int[] _minValues = new int[24];
    private readonly int[] _maxValues = new int[24];

    public BiopotSignalParser(BiopotGenericInfo biopotParams)
    {
        _biopotParams = biopotParams ?? throw new ArgumentNullException(nameof(biopotParams));
        _expectedPacketSize = GetExpectedPacketSize(biopotParams);

        for (int i = 0; i < 24; i++)
        {
            _minValues[i] = int.MaxValue;
            _maxValues[i] = int.MinValue;
        }
    }

    public int Timestamp { get; private set; }
    public int[,] SpdData { get; private set; } = EmptyDataSamples;
    public int[] BioImpedanceData { get; private set; } = EmptyBioImpSamples;
    public short[] AccelerometerData { get; private set; } = EmptyAccelerometerSamples;

    /// <summary>
    /// Parses a BLE packet according to BiopotGenericInfo configuration.
    /// </summary>
    public bool TryParse(byte[] packet)
    {
        ResetData();

        if (packet == null || packet.Length < _expectedPacketSize)
            return false;

        int offset = 0;

        // Timestamp
        Timestamp = BitConverter.ToInt32(packet, offset);
        offset += 4;

        // SPD/EEG/EMG data
        int[,] spd = new int[_biopotParams.ChannelsNumber, _biopotParams.SamplesPerChannelNumber];

        if (BLEConstants.biopotBits == 16)
        {
            for (int i = 0; i < _biopotParams.SamplesPerChannelNumber * _biopotParams.ChannelsNumber; i++)
            {
                int sample = packet[offset] | (packet[offset + 1] << 8);
                if ((packet[offset + 1] & 0x80) != 0)
                    sample |= unchecked((int)0xFFFF0000);

                int sampleId = i / (int)_biopotParams.ChannelsNumber;
                int channelId = i % (int)_biopotParams.ChannelsNumber;
                spd[channelId, sampleId] = sample;
                UpdateTestRange(channelId, sample);

                offset += 2;
            }
        }
        else // 24-bit or 22-bit support
        {
            for (int i = 0; i < _biopotParams.SamplesPerChannelNumber * _biopotParams.ChannelsNumber; i++)
            {
                int sample = (packet[offset] | (packet[offset + 1] << 8) | (packet[offset + 2] << 16));
                if ((packet[offset + 2] & 0x80) != 0)
                    sample |= unchecked((int)0xFF000000);

                int sampleId = i / (int)_biopotParams.ChannelsNumber;
                int channelId = i % (int)_biopotParams.ChannelsNumber;
                spd[channelId, sampleId] = sample;
                UpdateTestRange(channelId, sample);

                offset += 3;
            }
        }

        SpdData = spd;
        FinalizeSignalTest();

        // Accelerometer data
        if (_biopotParams.IsAccelerometerPresent)
        {
            int accelSamples = (int)(_biopotParams.SamplesPerChannelNumber / 2 * _biopotParams.AccelerometerChannelNumber);
            short[] accel = new short[accelSamples];
            for (int i = 0; i < accelSamples; i++)
            {
                accel[i] = (short)(packet[offset] | (packet[offset + 1] << 8));
                offset += 2;
            }
            AccelerometerData = accel;
        }

        // Bio-impedance data
        if (_biopotParams.IsBioImpedancePresent)
        {
            int biCount = (int)(_biopotParams.SamplesPerChannelNumber * 2 * _biopotParams.BioImpedanceChannelNumber);
            int[] bioImp = new int[biCount];
            const double bitResolution = 1.2 / 2097152.0; // (2^21)

            for (int i = 0; i < biCount; i++)
            {
                byte signExtend = (packet[offset] & 0x80) != 0 ? (byte)0xFF : (byte)0x00;
                int sample = (packet[offset + 2]) | (packet[offset + 1] << 8) | (packet[offset] << 16) | (signExtend << 24);
                float voltage = (float)(-sample * bitResolution);
                bioImp[i] = (int)(voltage * 1_000_000); // Convert V → µV
                offset += 3;
            }

            BioImpedanceData = bioImp;
        }

        return true;
    }

    private void UpdateTestRange(long channelId, int sample)
    {
        if (BLEConstants.EEGSignal.EEG_SIGNAL_TYPE != BLEConstants.EEGSignal.TEST_EEG_ACQ)
            return;

        if (BLEConstants.SignalTest.TestBlockCount < BLEConstants.SignalTest.TotalTestBlockCount - BLEConstants.SignalTest.InitialTestBlocksSkip &&
            BLEConstants.SignalTest.TestBlockCount > BLEConstants.SignalTest.FinalTestBlocksSkip)
        {
            if (sample < _minValues[channelId]) _minValues[channelId] = sample;
            if (sample > _maxValues[channelId]) _maxValues[channelId] = sample;
        }
    }

    private void FinalizeSignalTest()
    {
        if (BLEConstants.EEGSignal.EEG_SIGNAL_TYPE != BLEConstants.EEGSignal.TEST_EEG_ACQ)
            return;

        BLEConstants.SignalTest.TestBlockCount--;
        if (BLEConstants.SignalTest.TestBlockCount <= 0)
        {
            for (int i = 0; i < _biopotParams.ChannelsNumber; i++)
            {
                double range = (_maxValues[i] - _minValues[i]) * 0.02384;
                if (Math.Abs(range) > 4000 || Math.Abs(range) < 1000)
                    BLEConstants.SignalTest.SuccessResult = false;

                _minValues[i] = int.MaxValue;
                _maxValues[i] = int.MinValue;
            }

            BLEConstants.EEGSignal.EEG_SIGNAL_TYPE = BLEConstants.EEGSignal.NORMAL_EEG_ACQ;
            BLEConstants.SignalTest.ResultReady = true;
        }
    }

    private void ResetData()
    {
        Timestamp = 0;
        SpdData = EmptyDataSamples;
        BioImpedanceData = EmptyBioImpSamples;
        AccelerometerData = EmptyAccelerometerSamples;
    }

    private uint GetExpectedPacketSize(BiopotGenericInfo info)
    {
        uint size = 4; // timestamp

        size += (uint)(BLEConstants.biopotBits == 24
            ? 3 * info.ChannelsNumber * info.SamplesPerChannelNumber
            : 2 * info.ChannelsNumber * info.SamplesPerChannelNumber);

        if (info.IsAccelerometerPresent)
            size += 2 * info.AccelerometerChannelNumber * (info.SamplesPerChannelNumber / 2);

        if (info.IsBioImpedancePresent)
            size += 3 * info.BioImpedanceChannelNumber * info.SamplesPerChannelNumber * 2;

        return size;
    }
}

