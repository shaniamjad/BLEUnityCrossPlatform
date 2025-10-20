public class BiopotParsedData : IParsedData
{
    public int Timestamp { get; set; }
    public int[,] SpdData { get; set; }
    public int[] BioImpedanceData { get; set; }
    public short[] AccelerometerData { get; set; }
}

