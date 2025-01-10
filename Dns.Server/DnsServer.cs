using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Dns.Server;

public class DnsServer
{
    private readonly Dictionary<string, IPAddress> _hostRecords;
    private readonly UdpClient _udpServer;
    private readonly HttpListener _httpServer;
    private bool _isRunning;

    public DnsServer(int dnsPort = 53, int httpPort = 8080)
    {
        _hostRecords = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
        _udpServer = new UdpClient(dnsPort);
        _httpServer = new HttpListener();
        _httpServer.Prefixes.Add($"http://+:{httpPort}/");
        _isRunning = false;
    }

    public async Task StartAsync()
    {
        _isRunning = true;
        _httpServer.Start();

        Console.WriteLine($"DNS Server started on port 53");
        Console.WriteLine($"Web interface available at:");
        Console.WriteLine($"  Register: http://localhost:8080/register");
        Console.WriteLine($"  List: http://localhost:8080/list");

        Task dnsTask = RunDnsServerAsync();
        Task httpTask = RunHttpServerAsync();

        await Task.WhenAll(dnsTask, httpTask);
    }

    private async Task RunDnsServerAsync()
    {
        while (_isRunning)
        {
            try
            {
                UdpReceiveResult result = await _udpServer.ReceiveAsync();
                string domain = ExtractDomainName(result.Buffer, 12);
                string baseDomain = domain.Split('.')[0].ToLower();
                Console.WriteLine($"DNS Query for: {domain} (base: {baseDomain}) from {result.RemoteEndPoint}");

                byte[] response = BuildResponse(result.Buffer, baseDomain, result.RemoteEndPoint);

                if (response != null)
                    await _udpServer.SendAsync(response, response.Length, result.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DNS Error: {ex.Message}");
            }
        }
    }

    private async Task RunHttpServerAsync()
    {
        while (_isRunning)
        {
            try
            {
                HttpListenerContext context = await _httpServer.GetContextAsync();
                _ = HandleHttpRequestAsync(context);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HTTP Error: {ex.Message}");
            }
        }
    }

    private async Task HandleHttpRequestAsync(HttpListenerContext context)
    {
        try
        {
            HttpListenerResponse response = context.Response;
            HttpListenerRequest request = context.Request;

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            switch (request.Url.LocalPath)
            {
                case "/register":
                    if (request.HttpMethod == "GET")
                    {
                        var html = @"
<!DOCTYPE html>
<html>
<head>
    <title>DNS Registration</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 40px; background-color: #f5f5f5; }
        .form-container { 
            max-width: 500px; 
            margin: 0 auto; 
            background: white;
            padding: 20px;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        input, button { 
            margin: 10px 0; 
            padding: 12px;
            width: 100%;
            border: 1px solid #ddd;
            border-radius: 4px;
            box-sizing: border-box;
        }
        button { 
            background: #007bff; 
            color: white; 
            border: none; 
            cursor: pointer;
            font-weight: bold;
        }
        button:hover { background: #0056b3; }
        .info { 
            background: #f8f9fa; 
            padding: 15px; 
            border-radius: 5px; 
            margin-top: 20px;
            border-left: 4px solid #007bff;
        }
        h2 { color: #333; margin-bottom: 20px; }
        #clientIP {
            background: #e9ecef;
            padding: 10px;
            border-radius: 4px;
            margin: 10px 0;
            font-family: monospace;
        }
    </style>
</head>
<body>
    <div class='form-container'>
        <h2>Register Domain</h2>
        <div id='clientIP'>Your IP: <span id='ip'>Detecting...</span></div>
        <form id='registerForm'>
            <input type='text' id='domain' placeholder='Enter domain name (e.g., mysite)' required>
            <button type='submit'>Register Domain</button>
        </form>
        <div id='result'></div>
        <div class='info'>
            <h3>How to use:</h3>
            <p>1. Enter the name you want (e.g., 'mysite')</p>
            <p>2. Your IP is automatically detected</p>
            <p>3. After registering, others can access your site using just the name</p>
        </div>
    </div>
    <script>
        // Function to get the client's IP address
        async function getClientIP() {
            const response = await fetch('/getip');
            const ip = await response.text();
            document.getElementById('ip').textContent = ip;
        }

        getClientIP();

        document.getElementById('registerForm').onsubmit = async (e) => {
            e.preventDefault();
            const domain = document.getElementById('domain').value.toLowerCase();
            try {
                const response = await fetch('/register', {
                    method: 'POST',
                    headers: {'Content-Type': 'application/x-www-form-urlencoded'},
                    body: `domain=${domain}`
                });
                const result = await response.text();
                document.getElementById('result').innerHTML = 
                    `<p style='color: ${response.ok ? 'green' : 'red'}'>${result}</p>`;
            } catch (err) {
                document.getElementById('result').innerHTML = 
                    `<p style='color: red'>Error: ${err.message}</p>`;
            }
        };
    </script>
</body>
</html>";
                        byte[] buffer = Encoding.UTF8.GetBytes(html);
                        response.ContentType = "text/html";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else if (request.HttpMethod == "POST")
                    {
                        using StreamReader reader = new(request.InputStream);

                        string content = await reader.ReadToEndAsync();
                        var parameters = System.Web.HttpUtility.ParseQueryString(content);
                        string? domain = parameters["domain"]?.ToLower();

                        // Get the client's IP address
                        IPAddress clientIP = context.Request.RemoteEndPoint.Address;

                        // If it's a local address, try to get the actual local network IP
                        if (IPAddress.IsLoopback(clientIP))
                            clientIP = GetLocalIPAddress();

                        if (!string.IsNullOrWhiteSpace(domain) && clientIP != null)
                        {
                            _hostRecords[domain] = clientIP;
                            string successMsg = $"Registered: {domain} -> {clientIP}\nOthers can now access your site using just '{domain}'";
                            Console.WriteLine(successMsg);

                            byte[] buffer = Encoding.UTF8.GetBytes(successMsg);
                            response.ContentType = "text/plain";
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            response.StatusCode = 400;
                            string errorMsg = "Invalid domain or couldn't detect IP";
                            byte[] buffer = Encoding.UTF8.GetBytes(errorMsg);
                            response.ContentType = "text/plain";
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                    }
                    break;

                case "/getip":
                    IPAddress ip = context.Request.RemoteEndPoint.Address;

                    if (IPAddress.IsLoopback(ip))
                        ip = GetLocalIPAddress();

                    byte[] ipBuffer = Encoding.UTF8.GetBytes(ip.ToString());
                    response.ContentType = "text/plain";
                    response.ContentLength64 = ipBuffer.Length;
                    await response.OutputStream.WriteAsync(ipBuffer, 0, ipBuffer.Length);
                    break;

                case "/list":
                    StringBuilder records = new();
                    records.AppendLine("Registered Domains:");

                    foreach (var record in _hostRecords)
                        records.AppendLine($"{record.Key} -> {record.Value}");

                    byte[] listBuffer = Encoding.UTF8.GetBytes(records.ToString());
                    response.ContentType = "text/plain";
                    response.ContentLength64 = listBuffer.Length;
                    await response.OutputStream.WriteAsync(listBuffer, 0, listBuffer.Length);
                    break;

                default:
                    response.StatusCode = 404;
                    break;
            }

            response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HTTP handler error: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    private static IPAddress GetLocalIPAddress()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0);

        socket.Connect("8.8.8.8", 65530);
        IPEndPoint endPoint = socket.LocalEndPoint as IPEndPoint;

        return endPoint.Address;
    }

    private byte[] BuildResponse(byte[] request, string baseDomain, IPEndPoint remoteEndPoint)
    {
        try
        {
            var response = new byte[512];
            Array.Copy(request, response, 12);

            response[2] |= 0x80;
            response[3] |= 0x80;

            if (_hostRecords.TryGetValue(baseDomain, out var ipAddress))
            {
                response[7] = 1;

                var position = 12;

                while (request[position] != 0)
                {
                    response[position] = request[position];
                    position++;
                }
                response[position] = 0;
                position++;

                Array.Copy(request, position, response, position, 4);
                position += 4;

                response[position++] = 0xC0;
                response[position++] = 0x0C;

                response[position++] = 0x00;
                response[position++] = 0x01;

                response[position++] = 0x00;
                response[position++] = 0x01;

                response[position++] = 0x00;
                response[position++] = 0x00;
                response[position++] = 0x00;
                response[position++] = 0x1E;

                response[position++] = 0x00;
                response[position++] = 0x04;

                var addressBytes = ipAddress.GetAddressBytes();
                Array.Copy(addressBytes, 0, response, position, 4);
                position += 4;

                Array.Resize(ref response, position);
                return response;
            }

            response[3] |= 0x83;
            Array.Resize(ref response, request.Length);
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error building response: {ex.Message}");
            return null;
        }
    }

    private string ExtractDomainName(byte[] request, int offset)
    {
        List<string> labels = [];
        int position = offset;

        while (request[position] != 0)
        {
            var length = request[position++];
            var label = Encoding.ASCII.GetString(request, position, length);
            labels.Add(label);
            position += length;
        }

        return string.Join('.', labels);
    }

    public void Stop()
    {
        _isRunning = false;
        _udpServer.Close();
        _httpServer.Stop();
    }
}