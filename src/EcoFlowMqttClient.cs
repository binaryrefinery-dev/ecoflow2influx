// A listener for MQTT messages from EcoFlow's MQTT broker

using System.Diagnostics;
using System.Text.Json.Nodes;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Internal;


public class EcoFlowMqttListener(EcoFlowMqttInfo mqttInfo)
{

    public delegate Task ReportHandler(EcoFlowReport report);
    private readonly EcoFlowMqttInfo _mqttInfo = mqttInfo;
    private readonly MQTTnet.MqttFactory _mqttFactory = new();
    private volatile int _validReportsSinceLastCheck = 0;

    // Listens (and blocks until cancelled) for reports from any of the mentioned ecoflow devices.
    public async Task ListenForQuotaReportsAsync(IDictionary<string, EcoFlowDeviceInfo> deviceList, ReportHandler handler, CancellationToken cancelToken)
    {
        using var mqttClient = _mqttFactory.CreateMqttClient();
        {
            var connectionOptions = new MQTTnet.Client.MqttClientOptionsBuilder()
                .WithTcpServer(_mqttInfo.Host, _mqttInfo.Port)
                .WithTlsOptions(o => o.WithTargetHost(_mqttInfo.Host))
                .WithCredentials(_mqttInfo.CertificateAccount, _mqttInfo.CertificatePassword)
                .WithClientId(_mqttInfo.ClientId)
                .Build();

            // Set up various event handlers for receiving messages, connecting, disconnecting, etc.
            mqttClient.ApplicationMessageReceivedAsync += async e =>
            
                await HandleReceivedMessage(deviceList, handler, e.ApplicationMessage);

            mqttClient.DisconnectedAsync += e =>
            {
                Console.WriteLine($"Disconnected from MQTT broker {_mqttInfo.Host}. Reason code: {e.Reason} Reason: {e.ReasonString}."); 
                return Task.CompletedTask; 
            };
        
            // Await here indefinitely or until cancelled
            await ConnectWithRetryAsync(deviceList, mqttClient, connectionOptions, cancelToken);

            // Try for a clean disconnect.
            try
            {
                
                await mqttClient.DisconnectAsync(
                    new MQTTnet.Client.MqttClientDisconnectOptionsBuilder().WithReason(
                        MQTTnet.Client.MqttClientDisconnectOptionsReason.NormalDisconnection).Build(),
                        CancellationToken.None);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error unsubscribing or disconnecting from MQTT broker {_mqttInfo.Host}. {ex.Message}");
            }


        }
    }

    private async Task ConnectWithRetryAsync(IDictionary<string, EcoFlowDeviceInfo> deviceList, IMqttClient mqttClient, MqttClientOptions connectionOptions, CancellationToken cancelToken)
    {
        // Instead of connecting once to the broker, put it in a retry loop that
        // monitors the connection status every minute and tries to reconnect a certain number of times
        int connectAttempts = 0;
        while (!cancelToken.IsCancellationRequested)
        {
            if(0< _validReportsSinceLastCheck)
            {
                Console.WriteLine($"Received {_validReportsSinceLastCheck} valid reports since last connection check."); // TODO: If 0, should we fall back to HTTP?
                Interlocked.Exchange(ref _validReportsSinceLastCheck, 0);
            }

            if (!await mqttClient.TryPingAsync(cancelToken))
            {   
                connectAttempts++;
                Console.WriteLine($"{(1 == connectAttempts ? "Connecting" : "Reconnecting")} to MQTT broker {_mqttInfo.Host}... Attempt #{connectAttempts}.");
                var connectResult = await mqttClient.ConnectAsync(connectionOptions, CancellationToken.None); // TODO: Timeout?

                switch (connectResult.ResultCode)
                {
                    case MQTTnet.Client.MqttClientConnectResultCode.Success:

                        Console.WriteLine($"Connected to MQTT broker {_mqttInfo.Host}.");
                        await SubscribeToTopics(deviceList, mqttClient);
                        break;

                    case MQTTnet.Client.MqttClientConnectResultCode.NotAuthorized:
                        Console.WriteLine($"Failed to connect to MQTT broker {_mqttInfo.Host}. Reason: Not Authorized. Stopping retry.");
                        return; // Will exit retry loop from here

                    default:
                        Console.WriteLine($"Failed to connect to MQTT broker {_mqttInfo.Host}. Result Code: {connectResult.ResultCode} Reason: {connectResult.ReasonString}. Retrying in 1 minute.");
                        break;
                }
            }
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancelToken);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Exiting connection retry loop because of cancellation request.");
                return;
            }
        }
    }

    private async Task SubscribeToTopics(IDictionary<string, EcoFlowDeviceInfo> deviceList, IMqttClient mqttClient)
    {
        var subOptionsBuilder = _mqttFactory.CreateSubscribeOptionsBuilder();

        foreach(var device in deviceList.Values)
        {
            subOptionsBuilder = subOptionsBuilder.WithTopicFilter($"/open/{_mqttInfo.CertificateAccount}/{device.SerialNumber}/quota");     
        }
        var subOptions = subOptionsBuilder.Build();
        await mqttClient.SubscribeAsync(subOptions, CancellationToken.None); // TODO: Timeout?
        Console.WriteLine($"Subscribed to Quota Reports for Serial Numbers: {string.Join(", ", deviceList.Values.Select(x=>x.SerialNumber))}.");
    }

    private async Task HandleReceivedMessage(IDictionary<string, EcoFlowDeviceInfo> deviceList, ReportHandler handler, MqttApplicationMessage applicationMessage)
    {
        var payload = applicationMessage.ConvertPayloadToString();
            JsonNode data = System.Text.Json.JsonSerializer.Deserialize<JsonNode>(payload)!;
            EcoFlowReport? report = null;

            var reportData = data["params"];
            if(null == reportData)
            {
                Console.WriteLine($"No report data available. Module type: {data?["moduleType"]?.GetValue<int>()}. Data: {payload}");
                return;
            }

            // Get the device it's referring to from the topic
            var device = GetDeviceFromTopic(applicationMessage.Topic, deviceList);
            if (null == device)
            {
                Console.WriteLine($"Received message for unrecognized serial number. Topic: {applicationMessage.Topic} Payload: {payload}");
                return;
            }

            switch (data?["moduleType"]?.GetValue<int>())
            {
                case 1: // PD

                    report = new EcoFlowPDReport(
                        DateTimeOffset.UtcNow, 
                        device,
                        reportData["wattsInSum"]?.GetValue<int>(),
                        reportData["wattsOutSum"]?.GetValue<int>(),
                        reportData["invInWatts"]?.GetValue<int>(),
                        reportData["invOutWatts"]?.GetValue<int>(),
                        reportData["pv1ChargeWatts"]?.GetValue<int>(),
                        reportData["pv2ChargeWatts"]?.GetValue<int>(),
                        reportData["soc"]?.GetValue<int>());

                    break;

                case 2:  // BMS

                    report = new EcoFlowBMSReport(
                        DateTimeOffset.UtcNow,
                        device,
                        reportData["f32ShowSoc"]?.GetValue<float>(),
                        reportData["maxChargeSoc"]?.GetValue<float>(),
                        reportData["minDsgSoc"]?.GetValue<float>());

            
                    break;

                case 3: // Inverter

                    report = new EcoFlowInverterReport(
                        DateTimeOffset.UtcNow,
                        device,
                        reportData["invOutAmp"]?.GetValue<float>() / 1000,
                        reportData["invOutVol"]?.GetValue<float>() / 1000,
                        reportData["acInAmp"]?.GetValue<float>() / 1000,
                        reportData["acInVol"]?.GetValue<float>() / 1000,
                        reportData["dcInAmp"]?.GetValue<float>() / 1000,
                        reportData["dcInVol"]?.GetValue<float>() / 1000,
                        reportData["inputWatts"]?.GetValue<float>(),
                        reportData["outputWatts"]?.GetValue<float>(),
                        reportData["outTemp"]?.GetValue<float>());

                    break;

                default:
                    Console.WriteLine($"Unsupported module type for device {device.SerialNumber}: {payload}"); 
                    break;
            }

            if(null != report)
            {
                if(report.HasAnyData)
                {
                    Debug.WriteLine($"Got report {report.GetType().Name} for device: {report.Device.Name} ({report.Device.SerialNumber})");
                    Interlocked.Increment(ref _validReportsSinceLastCheck);
                    await handler(report);
                }
                else
                {
                    Console.WriteLine($"No data to write in report {report.GetType().Name}. Skipping. Data: {payload}");
                }
            }   
    }

    private EcoFlowDeviceInfo? GetDeviceFromTopic(string topic, IDictionary<string, EcoFlowDeviceInfo> deviceList)
    {
        // Parse the serial number from the topic which is in the form:
        // "/open/{account}/{serialNumber}/quota"

        var parts = topic.Split('/');
        
        if(5 != parts.Length) // Include the leading slash in the count and index
        {
            return null;
        }
        else
        {
            var serialNumber = parts[3];
            deviceList.TryGetValue(serialNumber, out EcoFlowDeviceInfo? device);
            return device; // May be null;
        }
        
        
    }
}