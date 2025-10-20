public class ParserFactory
{
    private readonly BiopotGenericInfo biopotConfig;

    public ParserFactory(BiopotGenericInfo biopotConfig)
    {
        this.biopotConfig = biopotConfig;
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
                return new BiopotSignalParser(biopotConfig);

            case DeviceType.Unknown:
            default:
                return null; // or a NullParser if you want to avoid nulls
        }
    }
}
