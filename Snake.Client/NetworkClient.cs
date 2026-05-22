using Snake.Shared.Models;
using Snake.Shared.Networking;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Snake.Client
{
    public class NetworkClient
    {
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;

        public event Action<GameState> OnStateReceived;
        public event Action<string> OnDisconnected;
        ///<summary>
        ///подключение к серверу по IP + TCP
        ///</summary>
        public async Task<bool> ConnectAsync(string ip, int port)
        {
            try
            {
                _client = new TcpClient();
                await _client.ConnectAsync(ip, port);

                var stream = _client.GetStream();
                _reader = new StreamReader(stream);
                _writer = new StreamWriter(stream) { AutoFlush = true };

                _ = ReceiveLoopAsync();
                return true;
            }
            catch (Exception ex)
            {
                OnDisconnected?.Invoke("Ошибка: Сервер недоступен.");
                return false;
            }
        }
        ///<summary>
        ///отправление Broadcast для поиска сервера
        ///</summary>
        public async Task<string> DiscoverServerAsync()
        {
            try
            {
                using var udpClient = new UdpClient();
                udpClient.EnableBroadcast = true;

                byte[] requestData = System.Text.Encoding.UTF8.GetBytes("SNAKE_DISCOVERY_REQUEST");

                IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, 5001);

                await udpClient.SendAsync(requestData, requestData.Length, endPoint);
                Console.WriteLine($"[КЛИЕНТ] UDP запрос отправлен");
                var timeoutTask = Task.Delay(3000);
                var receiveTask = udpClient.ReceiveAsync();

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                if (completedTask == receiveTask)
                {
                    var result = await receiveTask;
                    string response = System.Text.Encoding.UTF8.GetString(result.Buffer);

                    if (response.Contains("SNAKE_SERVER_HERE"))
                    {
                        return result.RemoteEndPoint.Address.ToString();
                    }
                }
                else
                {
                    Console.WriteLine("[КЛИЕНТ] Тайм-аут: Сервер не ответил на UDP.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[КЛИЕНТ] Ошибка UDP: {ex.Message}");
            }
            return null;
        }

        ///<summary>
        ///отправляет данные о своих действиях серверу
        ///</summary>
        public async Task SendInputAsync(InputUpdate input)
        {
            if (_client == null || !_client.Connected) return;

            try
            {
                var json = JsonSerializer.Serialize(input);
                await _writer.WriteLineAsync(json);
            }
            catch
            {
                OnDisconnected?.Invoke("Связь с сервером потеряна.");
            }
        }
        ///<summary>
        ///бесконечное прослушивание сервера
        ///</summary>
        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (_client.Connected)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line != null)
                    {
                        var state = JsonSerializer.Deserialize<GameState>(line);
                        if (state != null)
                        {
                            OnStateReceived?.Invoke(state);
                        }
                    }
                }
            }
            catch
            {
                OnDisconnected?.Invoke("Отключено от сервера.");
            }
        }
        ///<summary>
        ///отключение от сервера клиентом
        ///</summary>
        public void Disconnect()
        {
            try
            {
                _writer?.Close();
                _reader?.Close();
                _client?.Close();
            }
            catch
            {
            }
        }
    }
}