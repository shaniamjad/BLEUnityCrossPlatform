public class ParserFactory
{

    public ParserFactory()
    {
    }

    /// <summary>
    /// Returns the appropriate parser for the given device type.
    /// </summary>
    public IDataParser Create(DeviceType type)
    {
        switch (type)
        {
            case DeviceType.Movella:
                return new MovellaSignalParser();

            case DeviceType.BioPot:
                return new BiopotSignalParser(new BiopotGenericInfo { ChannelsNumber = 8, SamplesPerChannelNumber = 7 });

            case DeviceType.Unknown:
            default:
                return null; // or a NullParser if you want to avoid nulls
        }
    }
}
