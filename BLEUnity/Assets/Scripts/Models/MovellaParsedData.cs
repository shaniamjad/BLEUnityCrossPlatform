public class MovellaParsedData
{
    public int Timestamp { get; set; }
    public float[] Quaternion { get; set; }
    public float[] FreeAcceleration { get; set; }
    public byte Status { get; set; }
    public byte ClippingCountAccelerometer { get; set; }
    public byte ClippingCountGyroscope { get; set; }
}
