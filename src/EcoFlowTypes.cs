


// Used for establishing a connection the EcoFlow's MQTT broker
public record EcoFlowMqttInfo(string Host, int Port, string CertificateAccount, string CertificatePassword, string? ClientId = null)
{

    // Init client id to a hash of machine name if it's not supplied
    public string ClientId {get; init;} = ClientId ?? GetDefaultClientId();


    private static string GetDefaultClientId()
    {
        string data = Environment.MachineName;
        using var crypt = System.Security.Cryptography.SHA256.Create();
        var hash = crypt.ComputeHash(System.Text.UTF8Encoding.UTF8.GetBytes(data));
        var hashString = Convert.ToBase64String(hash);
        return hashString;
    }

}


public record EcoFlowDeviceInfo(string SerialNumber, string? Name);

public abstract record EcoFlowReport (
    DateTimeOffset TimestampUtc,
    EcoFlowDeviceInfo Device
)
{
    public abstract bool HasAnyData { get; }
}

public record EcoFlowPDReport  (
    DateTimeOffset TimestampUtc,
    EcoFlowDeviceInfo Device,
    int? TotalInWatts, 
    int? TotalOutWatts, 
    int? InverterInWatts, 
    int? InverterOutWatts, 
    int? Solar1Watts,
    int? Solar2Watts,
    int? StateOfCharge) : EcoFlowReport(TimestampUtc, Device)
    {
        public int? TotalSolarWatts 
        {
            get
            {
                return Solar1Watts + Solar2Watts;
            }
        }

        public override bool HasAnyData
        {
            get
            {
                return TotalInWatts.HasValue || TotalOutWatts.HasValue || InverterInWatts.HasValue || InverterOutWatts.HasValue || Solar1Watts.HasValue || Solar2Watts.HasValue || StateOfCharge.HasValue;
            }
        }

    }


public record EcoFlowBMSReport (
    DateTimeOffset TimestampUtc,
    EcoFlowDeviceInfo Device,
    float? StateOfCharge,
    float? MaxChargeSetpoint,
    float? MinDischargeSetpoint) : EcoFlowReport(TimestampUtc, Device)
    {
        public override bool HasAnyData
        {
            get
            {
                return StateOfCharge.HasValue || MaxChargeSetpoint.HasValue || MinDischargeSetpoint.HasValue;
            }
        }
    }


public record EcoFlowInverterReport (
    DateTimeOffset TimestampUtc,
    EcoFlowDeviceInfo Device,
    float? OutAmps,
    float? OutVolts,
    float? AcInAmps,
    float? AcInVolts,
    float? DcInAmps,
    float? DcInVolts,
    float? InputWatts,
    float? OutputWatts,
    float? Temperature) : EcoFlowReport(TimestampUtc, Device)
    {
        
        public override bool HasAnyData
        {
            get
            {
                return OutAmps.HasValue || OutVolts.HasValue || AcInAmps.HasValue || AcInVolts.HasValue || DcInAmps.HasValue || DcInVolts.HasValue || InputWatts.HasValue || OutputWatts.HasValue || Temperature.HasValue;
            }
        }
    }