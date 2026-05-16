using Snake.Shared.Enums;
using Snake.Shared.Models;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Snake.Server
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "Snake Game Server";
            _ = RunServerAsync();

            while (true)
            {
                if (Console.ReadLine()?.Trim().ToLower() == "exit") Environment.Exit(0);
            }
        }

        static async Task RunServerAsync()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, 5000);
            listener.Start();
            Console.WriteLine("Сервер запущен. Ожидание подключений...");

            ServerManager manager = new ServerManager();
            _ = manager.RunGlobalLoopAsync(); // Запускаем глобальное лобби
            int i = 1;
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();

                // Передаем manager.Auth четвертым параметром
                var player = new PlayerConnection(client, Direction.Right, i, manager.Auth);
                manager.AddPlayer(player);

                i++;
                Console.WriteLine("Новый клиент подключился и ожидает авторизации.");
            }
        }
    }
}