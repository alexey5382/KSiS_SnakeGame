using Snake.Shared.Enums;
using System.Net;
using System.Net.Sockets;

namespace Snake.Server
{
    class Program
    {
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private static readonly ServerManager _manager = new ServerManager();

        static async Task Main(string[] args)
        {
            Console.Title = "Snake Game Server";

            //методы для ожидания UDP и TCP
            var serverTask = RunServerAsync(_cts.Token);
            var udpTask = RunUdpDiscoveryAsync(_cts.Token);


            while (true)
            {
                if (Console.ReadLine()?.Trim().ToLower() == "exit")
                {
                    Console.WriteLine("\n[СИСТЕМА] Инициализация корректного завершения работы...");
                    //отключение всех клиентов
                    _manager.ShutdownAll();
                    _cts.Cancel();

                    break;
                }
            }
            await Task.Delay(500);
            Environment.Exit(0);
        }
        ///<summary>
        ///обработка broadcast от клиента 
        ///</summary>
        static async Task RunUdpDiscoveryAsync(CancellationToken token)
        {
            try
            {
                using UdpClient udpListener = new UdpClient(5001);
                udpListener.EnableBroadcast = true;

                using (token.Register(() => udpListener.Close()))
                {
                    while (!token.IsCancellationRequested)
                    {
                        var result = await udpListener.ReceiveAsync();
                        string msg = System.Text.Encoding.UTF8.GetString(result.Buffer).Trim();

                        if (msg == "SNAKE_DISCOVERY_REQUEST")
                        {
                            Console.WriteLine($"[АВТОПОИСК] Получен UDP-запрос от клиента {result.RemoteEndPoint.Address}");

                            byte[] response = System.Text.Encoding.UTF8.GetBytes("SNAKE_SERVER_HERE");

                            try
                            {
                                await udpListener.SendAsync(response, response.Length, result.RemoteEndPoint);
                                var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, result.RemoteEndPoint.Port);
                                //await udpListener.SendAsync(response, response.Length, broadcastEndPoint);

                                Console.WriteLine($"[АВТОПОИСК] Отправлен UDP-ответ клиенту {result.RemoteEndPoint.Address}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ОШИБКА АВТОПОИСКА] Не удалось отправить ответ: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch { }
        }
        ///<summary>
        ///бесконечный цикл ожидания игроков по TCP
        ///</summary>
        static async Task RunServerAsync(CancellationToken token)
        {
            TcpListener listener = new TcpListener(IPAddress.Any, 5000);
            listener.Start();
            Console.WriteLine("Сервер запущен. Ожидание подключений...");

            int i = 1;
            //закрывает слушателя если введен exit
            using (token.Register(() => listener.Stop()))
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var client = await listener.AcceptTcpClientAsync();

                        var player = new PlayerConnection(client, Direction.Right, i, _manager);
                        _manager.AddPlayer(player);

                        Console.WriteLine($"[ПОДКЛЮЧЕНИЕ] Новый клиент #{i} подключился и ожидает авторизации.");
                        i++;
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SocketException)
                {
                }
            }
            Console.WriteLine("[СИСТЕМА] Сетевой слушатель успешно остановлен.");
        }
        
    }
}