using Snake.Shared.Enums;
using Snake.Shared.Models;
using Snake.Shared.Networking;
using System;
using System.Net.Sockets;
using System.Text.Json;

namespace Snake.Server
{
    public class PlayerConnection
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private readonly ServerManager _serverManager;

        public Direction CurrentDirection { get; set; }
        public bool IsConnected { get; private set; } = true;
        public bool WantsToRestart { get; set; } = false;
        public bool WantsToLeaveRoom { get; set; } = false;

        public int SlotId { get; }
        public bool IsReady { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();

        public string Login { get; private set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public string RequestedTargetId { get; set; }
        public bool IsHost { get; set; }

        public bool IsAuthenticated { get; set; } = false;
        public string AuthMessage { get; set; } = "Пожалуйста, авторизуйтесь.";

        public GameSettingsConfig PendingSettings { get; set; } = null;
        public string CustomLobbyName { get; set; }

        public bool InGameEngine { get; set; } = false;
        public event Action OnRoomStateChanged;
        ///<summary>
        ///создание клиента
        ///</summary>
        public PlayerConnection(TcpClient client, Direction startDirection, int slotId, ServerManager serverManager)
        {
            _client = client;
            CurrentDirection = startDirection;
            SlotId = slotId;
            _serverManager = serverManager;

            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            Console.WriteLine($"[ПОДКЛЮЧЕНИЕ] Принято новое соединение. Выдан ID: {Id.Substring(0, 8)}...");
            //обновление состояния для клиента
            _ = SendStateAsync(new GameState { Status = GameStatus.AuthScreen, Message = AuthMessage });
        }
        ///<summary>
        ///получение данных о действиях пользователя
        ///</summary>
        public async Task ListenForInputsAsync()
        {
            try
            {
                while (_client.Connected)
                {
                    //получение байтов от клиента
                    var line = await _reader.ReadLineAsync();
                    if (line != null)
                    {
                        var input = JsonSerializer.Deserialize<InputUpdate>(line);
                        if (input != null)
                        {
                            //АВТОРИЗАЦИЯ
                            if (input.Action == ActionType.Login)
                            {
                                if (_serverManager.Auth.Login(input.PlayerName, input.Password, out string msg))
                                {
                                    Login = input.PlayerName;
                                    Name = input.PlayerName;
                                    IsAuthenticated = true;
                                    AuthMessage = msg;

                                    Console.WriteLine($"[АВТОРИЗАЦИЯ] Пользователь '{Login}' вошел в систему.");
                                    //обновление состояния меню для пользователя
                                    _serverManager.BroadcastMenuUpdate();
                                }
                                else
                                {
                                    await SendStateAsync(new GameState { Status = GameStatus.AuthScreen, Message = msg });
                                }
                            }
                            //РЕГИСТРАЦИЯ
                            else if (input.Action == ActionType.Register)
                            {
                                bool success = _serverManager.Auth.Register(input.PlayerName, input.Password, out string msg);
                                if (success) Console.WriteLine($"[РЕГИСТРАЦИЯ] Пользователь '{input.PlayerName}' успешно зарегистрирован.");
                                await SendStateAsync(new GameState { Status = GameStatus.AuthScreen, Message = msg });
                            }
                            //если авторизирован  успешно
                            else if (IsAuthenticated)
                            {
                                //создание лобби
                                if (input.Action == ActionType.CreateLobby)
                                {
                                    Console.WriteLine($"[ЛОББИ] Пользователь '{Login}' создал лобби.");
                                    _serverManager.HandleCreateLobby(this);
                                }
                                //вход в лобби
                                else if (input.Action == ActionType.JoinLobby)
                                {
                                    Console.WriteLine($"[ЛОББИ] Пользователь '{Login}' вошел в лобби '{input.TargetId}'.");
                                    _serverManager.HandleJoinLobby(this, input.TargetId);
                                }
                                //выход из лобби
                                else if (input.Action == ActionType.LeaveRoom)
                                {
                                    WantsToLeaveRoom = true;
                                    Console.WriteLine($"[ЛОББИ] Пользователь '{Login}' покинул лобби.");
                                    if (!InGameEngine) _serverManager.HandleLeaveRoom(this);
                                    else OnRoomStateChanged?.Invoke();
                                }
                                //готовность к запуску игры
                                else if (input.Action == ActionType.Ready)
                                {
                                    IsReady = !IsReady;

                                    if (!InGameEngine && IsHost) _serverManager.UpdateHostState(this);
                                    else OnRoomStateChanged?.Invoke();
                                }
                                //рестарт игры
                                else if (input.Action == ActionType.Restart)
                                {
                                    WantsToRestart = true;
                                    OnRoomStateChanged?.Invoke();
                                }
                                //движение
                                else if (input.Action == ActionType.Move)
                                {
                                    if (!IsOpposite(CurrentDirection, input.Direction)) CurrentDirection = input.Direction;
                                }
                                //обновление информации
                                else if (input.Action == ActionType.UpdateInfo)
                                {
                                    if (!string.IsNullOrWhiteSpace(input.NewPlayerName)) Name = input.NewPlayerName;
                                    if (IsHost && !string.IsNullOrWhiteSpace(input.LobbyName)) CustomLobbyName = input.LobbyName;

                                    if (!InGameEngine && IsHost)
                                    {
                                        _serverManager.UpdateHostState(this);
                                        _serverManager.BroadcastMenuUpdate();
                                    }
                                    else OnRoomStateChanged?.Invoke();
                                }
                                else if (input.Action == ActionType.UpdateSettings && IsHost)
                                {
                                    Console.WriteLine($"[НАСТРОЙКИ] Пользователь '{Login}' изменил настройки игры.");
                                    PendingSettings = input.NewSettings;
                                    if (!InGameEngine && IsHost) _serverManager.UpdateHostState(this);
                                    else OnRoomStateChanged?.Invoke();
                                }
                            }
                        }
                    }
                    else break;
                }
            }
            catch { }
            finally
            {
                IsConnected = false;

                if (IsAuthenticated)
                {
                    _serverManager.Auth.LogoutUser(Login);
                    Console.WriteLine($"[АВТОРИЗАЦИЯ] Пользователь '{Login}' вышел из аккаунта.");
                }
                Console.WriteLine($"[ОТКЛЮЧЕНИЕ] Клиент {Id.Substring(0, 8)} закрыл программу и отключился.");
                // Сообщаем о дисконнекте нужной инстанции
                if (!InGameEngine) _serverManager.HandleDisconnect(this);
                else OnRoomStateChanged?.Invoke();
            }
        }
        ///<summary>
        ///отправление состояния игры пользователям
        ///</summary>
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
        ///<summary>
        ///отключение пользователя сервером
        ///</summary>
        public void Disconnect()
        {
            IsConnected = false;
            try
            {
                _writer?.Close();
                _reader?.Close();
                _client?.Close();
            }
            catch {  }
        }
    }
}