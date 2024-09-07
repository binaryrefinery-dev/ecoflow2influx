using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;


public record InfluxDbConfig(string ServerUrl, string Database, string UserName, string Password);

public class InfluxWriter : IDisposable
{

    private readonly InfluxDbConfig _config;
    private readonly InfluxDB.Client.InfluxDBClient _client;

    public InfluxWriter(InfluxDbConfig config)
    {
        _config = config;
        _client = GetInfluxDb();
    }


    private InfluxDB.Client.InfluxDBClient GetInfluxDb()
    {
        var client  = new InfluxDB.Client.InfluxDBClient(_config.ServerUrl, _config.UserName, _config.Password, _config.Database, "autogen"); 
        return client;
    }

    public async Task Write(EcoFlowReport report)
    {
        if(report is EcoFlowPDReport pdReport)
        {
            await WritePD(pdReport);
        }
        else if(report is EcoFlowBMSReport bmsReport)
        {
            await WriteBMS(bmsReport);
        }
        else if(report is EcoFlowInverterReport inverterReport)
        {
            await WriteInverter(inverterReport);
        }
        else
        {
            throw new InvalidOperationException("Unsupported report type.");
        }
    }

    private async Task WritePD(EcoFlowPDReport report)
    {
        PointData point = GetPoint(report, "Power");

        point = AddFieldIfNotNull(point, nameof(report.TotalInWatts), report.TotalInWatts);
        point = AddFieldIfNotNull(point, nameof(report.TotalOutWatts), report.TotalOutWatts);
        point = AddFieldIfNotNull(point, nameof(report.InverterInWatts), report.InverterInWatts);
        point = AddFieldIfNotNull(point, nameof(report.InverterOutWatts), report.InverterOutWatts);
        point = AddFieldIfNotNull(point, nameof(report.TotalSolarWatts), report.TotalSolarWatts);
        point = AddFieldIfNotNull(point, nameof(report.Solar1Watts), report.Solar1Watts);
        point = AddFieldIfNotNull(point, nameof(report.Solar2Watts), report.Solar2Watts);
        point = AddFieldIfNotNull(point, nameof(report.StateOfCharge), report.StateOfCharge);

        var writer = _client.GetWriteApiAsync();
        await writer.WritePointAsync(point);

    }

    private async Task WriteInverter(EcoFlowInverterReport report)
    {
        PointData point = GetPoint(report, "Inverter");

        point = AddFieldIfNotNull(point, nameof(report.OutAmps), report.OutAmps);
        point = AddFieldIfNotNull(point, nameof(report.OutVolts), report.OutVolts);
        point = AddFieldIfNotNull(point, nameof(report.AcInAmps), report.AcInAmps);
        point = AddFieldIfNotNull(point, nameof(report.AcInVolts), report.AcInVolts);
        point = AddFieldIfNotNull(point, nameof(report.DcInAmps), report.DcInAmps);
        point = AddFieldIfNotNull(point, nameof(report.DcInVolts), report.DcInVolts);
        point = AddFieldIfNotNull(point, nameof(report.InputWatts), report.InputWatts);
        point = AddFieldIfNotNull(point, nameof(report.OutputWatts), report.OutputWatts);
        point = AddFieldIfNotNull(point, nameof(report.Temperature), report.Temperature);

        var writer = _client.GetWriteApiAsync();
        await writer.WritePointAsync(point);
    }

    private async Task WriteBMS(EcoFlowBMSReport report)
    {

        var point = GetPoint(report, "Battery");

        point = AddFieldIfNotNull(point, nameof(report.StateOfCharge), report.StateOfCharge);
        point = AddFieldIfNotNull(point, nameof(report.MaxChargeSetpoint), report.MaxChargeSetpoint);
        point = AddFieldIfNotNull(point, nameof(report.MinDischargeSetpoint), report.MinDischargeSetpoint);
        

        var writer = _client.GetWriteApiAsync();
        await writer.WritePointAsync(point);
    }

    private PointData GetPoint(EcoFlowReport report, string measurementName)
    {
        return InfluxDB.Client.Writes.PointData.Measurement(measurementName)
                    .Timestamp(report.TimestampUtc.ToUnixTimeMilliseconds(), InfluxDB.Client.Api.Domain.WritePrecision.Ms)
                    .Tag("SerialNumber", report.Device.SerialNumber)
                    .Tag("DeviceName", report.Device.Name);
    }


    private PointData AddFieldIfNotNull<T>(PointData point, string fieldName, T value) 
    {
        if(null != value)
        {
            return point.Field(fieldName, value);
        }
        return point;
    }

    

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
