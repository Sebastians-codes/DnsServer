using Dns.Server;

try
{
    DnsServer server = new();

    Console.WriteLine("Simple DNS Server starting...");
    Console.WriteLine("Access the web interface at:");
    Console.WriteLine("  http://localhost:8080/register - To register domains");
    Console.WriteLine("  http://localhost:8080/list - To list registered domains");
    Console.WriteLine("\nImportant: Set this server as your DNS server in network settings!");
    Console.WriteLine("Press Ctrl+C to stop the server");

    await server.StartAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Server error: {ex.Message}");
}