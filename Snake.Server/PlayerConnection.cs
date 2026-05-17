using System;
using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Snake.Shared.Enums;
using Snake.Shared.Models;
using Snake.Shared.Networking;

namespace Snake.Server
{
    public class PlayerConnection
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly AuthManager _authManager;

        public Direction CurrentDirection { get; set; }
        public bool IsConnected { get; private set; } = true;
        public bool WantsToRestart { get; set; } = false;
        public bool WantsToLeaveRoom { get; set; } = false;

        public int SlotId { get; }
        public bool IsReady { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();

        // === РАЗДЕЛЕНИЕ ЛОГИНА И ИМЕНИ ===
        public string Login { get; private set; } = string.Empty; // Навсегда привязан к аккаунту
        public string Name { get; set; } = string.Empty;          // Временное имя в игре

        public string RequestedTargetId { get; set; }
        public bool IsHost { get; set; }

        public bool IsAuthenticated { get; set; } = false;
        public string AuthMessage { get; set; } = "Пожалуйста, авторизуйтесь.";

        public GameSettingsConfig PendingSettings { get; set; } = null;
        public string CustomLobbyName { get; set; }

        public PlayerConnection(TcpClient client, Direction startDirection, int slotId, AuthManager authManager)
        {
            _client = client;
            CurrentDirection = startDirection;
            SlotId = slotId;
            _authManager = authManager;

            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            Console.WriteLine($"[ПОДКЛЮЧЕНИЕ] Принято новое соединение. Выдан ID: {Id.Substring(0, 8)}...");
        }

        public async Task ListenForInputsAsync()
        {
            try
            {
                while (_client.Connected)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line != null)
                    {
                        var input = JsonSerializer.Deserialize<InputUpdate>(line);
                        if (input != null)
                        {
                            if (input.Action == ActionType.Login)
                            {
                                if (_authManager.Login(input.PlayerName, input.Password, out string msg))
                                {
                                    Login = input.PlayerName; // Запоминаем логин как фундамент
                                    Name = input.PlayerName;  // Имя по умолчанию равно логину
                                    IsAuthenticated = true;
                                    AuthMessage = msg;

                                    Console.WriteLine($"[АВТОРИЗАЦИЯ] Пользователь '{Login}' вошел в аккаунт.");
                                }
                                else AuthMessage = msg;
                            }
                            else if (input.Action == ActionType.Register)
                            {
                                if (_authManager.Register(input.PlayerName, input.Password, out string msg))
                                {
                                    AuthMessage = msg;
                                    Console.WriteLine($"[РЕГИСТРАЦИЯ] Создан новый аккаунт: '{input.PlayerName}'.");
                                }
                                else AuthMessage = msg;
                            }
                            else if (IsAuthenticated)
                            {
                                if (input.Action == ActionType.CreateLobby)
                                {
                                    IsHost = true;
                                    Console.WriteLine($"[ЛОББИ] Пользователь '{Login}' создал новую комнату.");
                                }
                                else if (input.Action == ActionType.JoinLobby)
                                {
                                    RequestedTargetId = input.TargetId;
                                    Console.WriteLine($"[ЛОББИ] Пользователь '{Login}' пытается подключиться к {input.TargetId}.");
                                }
                                else if (input.Action == ActionType.Ready) IsReady = true;
                                else if (input.Action == ActionType.Restart) WantsToRestart = true;
                                else if (input.Action == ActionType.LeaveRoom)
                                {
                                    WantsToLeaveRoom = true;
                                    Console.WriteLine($"[ВЫХОД] Пользователь '{Login}' покинул лобби или сдался.");
                                }
                                else if (input.Action == ActionType.Move)
                                {
                                    if (!IsOpposite(CurrentDirection, input.Direction)) CurrentDirection = input.Direction;
                                }
                                else if (input.Action == ActionType.UpdateInfo)
                                {
                                    if (!string.IsNullOrWhiteSpace(input.NewPlayerName)) Name = input.NewPlayerName;
                                    if (IsHost && !string.IsNullOrWhiteSpace(input.LobbyName)) CustomLobbyName = input.LobbyName;

                                    Console.WriteLine($"[НАСТРОЙКИ] Пользователь '{Login}' изменил имя в игре на '{Name}'.");
                                }
                                else if (input.Action == ActionType.UpdateSettings && IsHost)
                                {
                                    PendingSettings = input.NewSettings;
                                    Console.WriteLine($"[НАСТРОЙКИ] Пользователь '{Login}' изменил параметры матча.");
                                }
                            }
                        }
                    }
                    else break;
                }
            }
            catch { /* Игнорируем резкий обрыв связи */ }
            finally
            {
                IsConnected = false;

                if (IsAuthenticated)
                {
                    // === КРИТИЧЕСКИ ВАЖНО: Освобождаем логин из онлайна ===
                    _authManager.LogoutUser(Login);
                    Console.WriteLine($"[ОТКЛЮЧЕНИЕ] Пользователь '{Login}' вышел из сети.");
                }
                else
                {
                    Console.WriteLine($"[ОТКЛЮЧЕНИЕ] Неавторизованный клиент отключился.");
                }
            }
        }

        public async Task SendStateAsync(GameState state)
        {
            try
            {
                if (!IsConnected) return;
                var json = JsonSerializer.Serialize(state);
                await _writer.WriteLineAsync(json);
            }
            catch { IsConnected = false; }
        }

        private bool IsOpposite(Direction current, Direction next)
        {
            return (current == Direction.Up && next == Direction.Down) ||
                   (current == Direction.Down && next == Direction.Up) ||
                   (current == Direction.Left && next == Direction.Right) ||
                   (current == Direction.Right && next == Direction.Left);
        }
    }
}