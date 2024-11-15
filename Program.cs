using System.Net;
using System.Net.Sockets;
using System.Text;

namespace dontconnect
{
    internal class Program
    {
        private static IPAddress proxIP = IPAddress.Parse("127.0.0.1");
        private static int proxPort = 8080;
        private static List<string> BlockedDomains = new List<string>
        {
            "example.com",
            "0xbruno.dev"
        };

        static async Task Main(string[] args)
        {
            TcpListener listener = new TcpListener(proxIP, proxPort);
            listener.Start();

            Console.WriteLine($"[*] DONTCONNECT started on {proxIP}:{proxPort}");

            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(async () => { await HandleClientAsync(client); });
            }

        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                NetworkStream clientStream = client.GetStream();
                StreamReader clientReader = new StreamReader(clientStream, Encoding.ASCII);
                StreamWriter clientWriter = new StreamWriter(clientStream, Encoding.ASCII) { AutoFlush = true };

                // Read the CONNECT request
                string requestLine = await clientReader.ReadLineAsync();
                Console.WriteLine($"Request received: {requestLine}");

                if (requestLine.StartsWith("CONNECT"))
                {
                    // Extract host and port from the CONNECT request
                    string[] requestParts = requestLine.Split(' ');
                    string[] hostPort = requestParts[1].Split(':');
                    string targetHost = hostPort[0];
                    int targetPort = int.Parse(hostPort[1]);

                    Console.WriteLine($"Connecting to {targetHost}:{targetPort}");

                    // Check if the host matches drop rule
                    if (BlockedDomains.Contains(targetHost.ToLower()))
                    {
                        Console.WriteLine("Blocking access >:)");

                        // Send 403 Forbidden response to the client
                        await clientWriter.WriteLineAsync("HTTP/1.1 403 Forbidden");
                        await clientWriter.WriteLineAsync("Content-Type: text/plain");
                        await clientWriter.WriteLineAsync("Connection: close");
                        await clientWriter.WriteLineAsync();                      

                        // Close the connection
                        return;
                    }

                    try
                    {
                        // Connect to the target server
                        TcpClient targetClient = new TcpClient(targetHost, targetPort);
                        using (targetClient)
                        {
                            NetworkStream targetStream = targetClient.GetStream();

                            // Send 200 Connection Established response to the client
                            await clientWriter.WriteLineAsync("HTTP/1.1 200 Connection Established");
                            await clientWriter.WriteLineAsync("Proxy-Agent: DONTCONNECT");
                            await clientWriter.WriteLineAsync();

                            // Set up bi-directional data transfer
                            Task clientToTarget = TransferDataAsync(clientStream, targetStream);
                            Task targetToClient = TransferDataAsync(targetStream, clientStream);

                            // Wait for either task to complete (if one side closes, we stop both)
                            await Task.WhenAny(clientToTarget, targetToClient);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error connecting to target: {ex.Message}");
                        await clientWriter.WriteLineAsync("HTTP/1.1 502 Bad Gateway");
                        await clientWriter.WriteLineAsync("Content-Type: text/plain");
                        await clientWriter.WriteLineAsync();
                        await clientWriter.WriteLineAsync("Failed to connect to target server.");
                    }
                }
                else
                {
                    // If the request is not CONNECT, respond with 405 Method Not Allowed
                    await clientWriter.WriteLineAsync("HTTP/1.1 405 Method Not Allowed");
                    await clientWriter.WriteLineAsync("Content-Type: text/plain");
                    await clientWriter.WriteLineAsync();
                    await clientWriter.WriteLineAsync("Only CONNECT method is supported by this proxy.");
                }
            }
        }

        private static async Task TransferDataAsync(Stream source, Stream destination)
        {
            byte[] buffer = new byte[4096];
            try
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead);
                    await destination.FlushAsync();
                }
            }
            catch (IOException)
            {
                // Handle disconnection
            }
        }
    }
}