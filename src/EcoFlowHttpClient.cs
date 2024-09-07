

// A wrapper around EcoFlow's HTTP Api
using System.Linq;
using System.Text.Json.Nodes;
using InfluxDB.Client.Api.Domain;

public class EcoFlowHttpClient : IDisposable
{
    private readonly HttpClient _client;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly Random _random = new();

    public EcoFlowHttpClient(string host, string accessKey, string secretKey)
    {
        if(string.IsNullOrEmpty(host))
        {
            throw new ArgumentException("Host is required.");
        }
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
        {
            throw new ArgumentException("AccessKey and SecretKey are required.");
        }
        
        _accessKey = accessKey;
        _secretKey = secretKey;
        _client = new HttpClient()
        {
            BaseAddress = new Uri(host),
        };
    }

    // Gets a list of devices for the account
    public async Task<IDictionary<string,EcoFlowDeviceInfo>> GetDeviceListAsync()
    {
        using var request = GetRequest("/iot-open/sign/device/list");
        using var response = await _client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP Error getting Device List: {response.StatusCode}");
        }
        if(response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            throw new ApplicationException($"Expected JSON response from service, but got '{response.Content.Headers.ContentType?.MediaType}'");
        }

        var data = System.Text.Json.JsonSerializer.Deserialize<JsonNode>(response.Content.ReadAsStream())!;
        if("0" != data["code"]?.GetValue<string>())
        {
            throw new ApplicationException($"Unable to get device list info from service. Code: {data["code"]?.GetValue<string>()} Message: {data["message"]?.GetValue<string>()}");
        }

        var devices = (from node in data["data"]!.AsArray()
        
                       select  new EcoFlowDeviceInfo(
                        node["sn"]?.GetValue<string>()!,
                        node["deviceName"]?.GetValue<string>())

        ).ToDictionary(k=> k.SerialNumber);


        return devices;
        
    }

    // Gets MQTT Certificate info to allow connection to EcoFlow's MQTT broker
    public async Task<EcoFlowMqttInfo> GetMqttInfoAsync()
    {
        using var request = GetRequest("/iot-open/sign/certification");
        using var response = await _client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP Error getting mqtt info: {response.StatusCode}");
        }
        if(response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            throw new ApplicationException($"Expected JSON response from service, but got '{response.Content.Headers.ContentType?.MediaType}'");
        }

        var mqttInfo = System.Text.Json.JsonSerializer.Deserialize<JsonNode>(response.Content.ReadAsStream())!;
        if("0" != mqttInfo["code"]?.GetValue<string>())
        {
            throw new ApplicationException($"Unable to get mqtt info from service. Code: {mqttInfo["code"]?.GetValue<string>()} Message: {mqttInfo["message"]?.GetValue<string>()}");
        }

        var info = new EcoFlowMqttInfo(
            mqttInfo["data"]?["host"]?.GetValue<string>() ?? "mqtt.ecoflow.com",
            int.Parse(mqttInfo["data"]?["port"]?.GetValue<string>()?? "8883") ,
            mqttInfo["data"]?["certificateAccount"]?.GetValue<string>() ?? throw new ApplicationException("MQTT Certificate Account not found."),
            mqttInfo["data"]?["certificatePassword"]?.GetValue<string>() ?? throw new ApplicationException("MQTT Certificate Password not found.")
        );

        return info;
    }

    private HttpRequestMessage GetRequest(string path)
    {
        string nonce = _random.Next().ToString();
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        string signature = GetHmacSha256(_secretKey, $"accessKey={_accessKey}&nonce={nonce}&timestamp={timestamp}");

        var request = new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers =
            {
                { "accessKey", _accessKey },
                { "nonce", nonce },
                { "timestamp", timestamp },
                { "sign", signature },
            },
        };
        return request;
    }

   

    // Helper function for signing a request
    private string GetHmacSha256(string secretKey, string data)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.ASCII.GetBytes(secretKey));
        byte[] hash = hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }


    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}