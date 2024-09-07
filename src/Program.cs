
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using MQTTnet;

const int PERMANENT_FAILURE = 2;
const int TEMPORARY_FAILURE = 1;
const int SUCCESS = 0;
const string ECO_HOST = "https://api-a.ecoflow.com";


Console.WriteLine("EcoFlow2Influx starting...");

CancellationTokenSource _ctExit = new();
Console.CancelKeyPress += (s, e) => 
{
    Console.WriteLine("Attempting to exit...");
    _ctExit.Cancel(); // Signals the awaiting loop to exit.
    e.Cancel = true;
};


var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddJsonFile("appsettings.json", true, true)
    .AddJsonFile("appsettings.local.json", true, true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

string? accessKey = config["AccessKey"];
string? secretKey = config["SecretKey"];


if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
{
    Console.Error.WriteLine("AccessKey and SecretKey are required.");
    return PERMANENT_FAILURE;
}



try
{
    using var ecoClient = new EcoFlowHttpClient(ECO_HOST, accessKey, secretKey);
    using var influx = new InfluxWriter(new InfluxDbConfig(config["InfluxDbUrl"]!, config["InfluxDbDatabase"]!, config["InfluxDbUsername"]!, config["InfluxDbPassword"]!));

    // Try to get device list
    var deviceList = await ecoClient.GetDeviceListAsync();
    if(deviceList.Count == 0)
    {
        Console.Error.WriteLine("No devices found on given ecoflow account.");
        return PERMANENT_FAILURE;
    }

    Console.WriteLine($"Got {deviceList.Count} device(s) from account: {string.Join(", ", deviceList.Values.Select(x=> $"{x.Name} ({x.SerialNumber})"))}");

    // Try to get mqtt info
    var mqttInfo = await ecoClient.GetMqttInfoAsync();
    Console.WriteLine($"Got MQTT info. Host: {mqttInfo.Host}, Port: {mqttInfo.Port}, ClientId: {mqttInfo.ClientId}");

    // Try listening to device quotas on mqtt
    var ecoMqtt = new EcoFlowMqttListener(mqttInfo);
    await ecoMqtt.ListenForQuotaReportsAsync(deviceList,
     async r => 
     {
        try
        {
            await influx.Write(r);
        }
        catch (Exception ex) // Just log and swallow
        {
            Console.Error.WriteLine($"Error writing to InfluxDB: {ex.Message}");
        }
     }, 
     _ctExit.Token);
    
    Console.WriteLine("Exiting normally.");
    return SUCCESS;

}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return TEMPORARY_FAILURE;
}
