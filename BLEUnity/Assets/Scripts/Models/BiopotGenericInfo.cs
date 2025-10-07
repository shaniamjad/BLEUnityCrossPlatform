using System;


public class BiopotGenericInfo : IEquatable<BiopotGenericInfo>
{
    public const uint DefaultBioImpedanceChannelCount = 2;
    public const uint DefaultAccelerometerChannelCount = 3;

    public string DeviceName { get; set; } = "Unknown";
    public uint ChannelsNumber { get; set; } = 0;
    public uint SamplesPerChannelNumber { get; set; } = 7;
    public bool IsBioImpedancePresent { get; set; } = true;
    public uint BioImpedanceChannelNumber { get; set; } = DefaultBioImpedanceChannelCount;
    public bool IsAccelerometerPresent { get; set; } = true;
    public bool ExternalMemoryEnabled { get; set; } = true;
    public uint AccelerometerMode { get; set; } = 2;
    public uint AccelerometerChannelNumber { get; set; } = DefaultAccelerometerChannelCount;
    public int SamplingRate { get; set; } = 500;

    public bool Equals(BiopotGenericInfo other)
    {
        if (ReferenceEquals(null, other)) return false;

        return ChannelsNumber == other.ChannelsNumber &&
                SamplesPerChannelNumber == other.SamplesPerChannelNumber &&
                IsBioImpedancePresent == other.IsBioImpedancePresent &&
                IsAccelerometerPresent == other.IsAccelerometerPresent &&
                AccelerometerMode == other.AccelerometerMode &&
                DeviceName == other.DeviceName &&
                BioImpedanceChannelNumber == other.BioImpedanceChannelNumber &&
                AccelerometerChannelNumber == other.AccelerometerChannelNumber;
    }

    public override bool Equals(object obj)
    {
        return obj is BiopotGenericInfo other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Tuple.Create(
            DeviceName,
            ChannelsNumber,
            SamplesPerChannelNumber,
            IsBioImpedancePresent,
            BioImpedanceChannelNumber,
            IsAccelerometerPresent,
            AccelerometerMode,
            AccelerometerChannelNumber
        ).GetHashCode();
    }
}

