using Snake.Shared.Enums;
using Snake.Shared.Models;

namespace Snake.Server
{
    public class ServerManager
    {
        private readonly List<PlayerConnection> _waitingPlayers = new List<PlayerConnection>();
        private readonly List<PlayerConnection> _allConnections = new List<PlayerConnection>();
        private readonly object _lock = new object();

        public AuthManager Auth { get; } = new AuthManager();
        ///<summary>
        ///добавляет подключившегося клиента в работу
        ///</summary>
        public void AddPlayer(PlayerConnection player)
        {
            lock (_lock)
            {
                _waitingPlayers.Add(player);
                _allConnections.Add(player);
            }
            _ = player.ListenForInputsAsync();
        }
        ///<summary>
        ///рассылка всей информации о главном меню
        ///</summary>
        public void BroadcastMenuUpdate()
        {
            lock (_lock)
            {
                _waitingPlayers.RemoveAll(p => !p.IsConnected);

                var topPlayersList = Auth.GetTopPlayers();

                //формируется список доступных лобби
                var availableLobbies = _waitingPlayers
                    .Where(p => p.IsAuthenticated && p.IsHost && string.IsNullOrEmpty(p.RequestedTargetId))
                    .Select(p => new PlayerInfo
                    {
                        Id = p.Id,
                        Name = string.IsNullOrEmpty(p.CustomLobbyName) ? $"Лобби {p.Name}" : p.CustomLobbyName
                    })
                    .ToList();
                //обновление состояния меню
                var menuState = new GameState
                {
                    Status = GameStatus.MainMenu,
                    AvailablePlayers = availableLobbies,
                    TopPlayers = topPlayersList
                };

                //рассылка меню свободным игрокам
                foreach (var player in _waitingPlayers)
                {
                    if (player.IsAuthenticated && !player.IsHost && string.IsNullOrEmpty(player.RequestedTargetId))
                    {
                        _ = player.SendStateAsync(menuState);
                    }
                }
            }
        }
        ///<summary>
        ///состояние комнаты при ожидании игрока
        ///</summary>
        public void UpdateHostState(PlayerConnection host)
        {
            var topPlayersList = Auth.GetTopPlayers();
            var hostState = new GameState
            {
                Status = GameStatus.HostWaiting,
                TopPlayers = topPlayersList,
                Player1Name = host.Name,
                Player2Name = "Ожидание...",
                IsPlayer1Ready = host.IsReady,
                LobbyName = string.IsNullOrEmpty(host.CustomLobbyName) ? $"Лобби {host.Name}" : host.CustomLobbyName,
                Message = host.IsReady ? $"{host.Name} ожидает подключения игрока..." : string.Empty
            };
            _ = host.SendStateAsync(hostState);
        }
        ///<summary>
        ///создание лобби
        ///</summary>
        public void HandleCreateLobby(PlayerConnection host)
        {
            lock (_lock)
            {
                host.IsHost = true;
                host.IsReady = false;
            }
            UpdateHostState(host);   
            BroadcastMenuUpdate();
        }
        ///<summary>
        ///обработка входа в лобби с ботом или с человеком
        ///</summary>
        public void HandleJoinLobby(PlayerConnection joiner, string targetId)
        {
            PlayerConnection host = null;

            lock (_lock)
            {
                if (targetId == "BOT_ID")
                {
                    joiner.IsHost = true;
                    _waitingPlayers.Remove(joiner);

                    var engine = new GameEngine(joiner, null, this, true);
                    engine.StartLobby();
                    return;
                }

                host = _waitingPlayers.FirstOrDefault(p => p.Id == targetId && p.IsHost);

                if (host != null)
                {
                    _waitingPlayers.Remove(host);
                    _waitingPlayers.Remove(joiner);
                }
            }

            if (host != null)
            {
                //создание игрового движка
                var engine = new GameEngine(host, joiner, this, false);
                engine.StartLobby();
                //обновление меню. лобби с 2 игроками пропадает из списка доступных
                BroadcastMenuUpdate();
            }
            else
            {
                //если лобби закрылось во время клика, пользователь возвращается в меню
                var topPlayersList = Auth.GetTopPlayers();
                _ = joiner.SendStateAsync(new GameState { Status = GameStatus.MainMenu, TopPlayers = topPlayersList });
            }
        }
        ///<summary>
        ///обработка выхода пользователя из комнаты
        ///</summary>
        public void HandleLeaveRoom(PlayerConnection player)
        {
            lock (_lock)
            {
                if (player.IsHost)
                {
                    player.IsHost = false;
                    player.IsReady = false;
                    player.CustomLobbyName = null;
                }
            }
            //возвращение меню
            var topPlayersList = Auth.GetTopPlayers();
            _ = player.SendStateAsync(new GameState { Status = GameStatus.MainMenu, TopPlayers = topPlayersList });

            //бродкаст клиентам об удалении комнаты
            BroadcastMenuUpdate();
        }

        ///<summary>
        ///обработка отключения при закрытии сервера
        ///</summary>
        public void HandleDisconnect(PlayerConnection player)
        {
            bool needMenuUpdate = false;
            lock (_lock)
            {
                needMenuUpdate = player.IsHost;
                _waitingPlayers.Remove(player);
                _allConnections.Remove(player);
            }

            if (needMenuUpdate)
            {
                BroadcastMenuUpdate();
            }
        }

        ///<summary>
        ///возврат пользователей в глобальное лобби после закрытия движка игры
        ///</summary>
        public void ReturnToGlobalLobby(PlayerConnection player)
        {
            if (player != null && player.IsConnected)
            {
                player.IsReady = false;
                player.WantsToRestart = false;
                player.IsHost = false;
                player.WantsToLeaveRoom = false;
                player.RequestedTargetId = null;
                player.InGameEngine = false;


                lock (_lock)
                {
                    if (!_waitingPlayers.Contains(player))
                        _waitingPlayers.Add(player);
                }

                //возвращение интерфейса меню пользователю
                var topPlayersList = Auth.GetTopPlayers();
                _ = player.SendStateAsync(new GameState { Status = GameStatus.MainMenu, TopPlayers = topPlayersList });

                BroadcastMenuUpdate();
            }
        }
        ///<summary>
        ///отключение сервера
        ///</summary>
        public void ShutdownAll()
        {
            lock (_lock)
            {
                Console.WriteLine("Отключение всех активных клиентов...");
                foreach (var player in _allConnections)
                {
                    player.Disconnect();
                }
                _allConnections.Clear();
                _waitingPlayers.Clear();
            }
        }
    }
}