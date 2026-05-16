using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Snake.Shared.Enums;
using Snake.Shared.Models;
using Snake.Shared.Networking;

namespace Snake.Client
{
    public class NetworkClient
    {
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;

        // Событие, которое срабатывает каждый раз, когда от сервера приходит новый кадр
        public event Action<GameState> OnStateReceived;
        public event Action<string> OnDisconnected;

        // Теперь метод возвращает bool (успешно или нет)
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
                return true; // Подключение удалось!
            }
            catch (Exception ex)
            {
                // Вызываем событие отключения с понятным текстом
                OnDisconnected?.Invoke("Ошибка: Сервер недоступен.");
                return false; // Подключение не удалось!
            }
        }

        // Полностью замените старый метод на этот
        public async Task SendInputAsync(InputUpdate input)
        {
            if (_client == null || !_client.Connected) return;

            try
            {
                // Метод больше не собирает InputUpdate сам, он просто 
                // берет готовый объект, превращает его в JSON и отправляет
                var json = JsonSerializer.Serialize(input);
                await _writer.WriteLineAsync(json);
            }
            catch
            {
                OnDisconnected?.Invoke("Связь с сервером потеряна.");
            }
        }

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
    }
}