public interface IDataParser
{
    public bool TryParse(byte[] packet, out IParsedData result);

}
