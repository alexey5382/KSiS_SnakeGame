using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Snake.Shared.Enums;
using Snake.Shared.Models;

namespace Snake.Server
{
    public class ServerManager
    {
        private readonly List<PlayerConnection> _waitingPlayers = new List<PlayerConnection>();
        private readonly object _lock = new object();

        public AuthManager Auth { get; } = new AuthManager();

        public void AddPlayer(PlayerConnection player)
        {
            lock (_lock) _waitingPlayers.Add(player);
            _ = player.ListenForInputsAsync();
        }

        public async Task RunGlobalLoopAsync()
        {
            while (true)
            {
                lock (_lock)
                {
                    _waitingPlayers.RemoveAll(p => !p.IsConnected);

                    foreach (var player in _waitingPlayers)
                    {
                        if (player.WantsToLeaveRoom)
                        {
                            player.IsHost = false;
                            player.WantsToLeaveRoom = false;
                            player.RequestedTargetId = null;
                            player.IsReady = false;
                        }
                    }

                    var pairsToStart = new List<(PlayerConnection host, PlayerConnection joiner)>();

                    foreach (var player in _waitingPlayers.ToList())
                    {
                        if (player.IsAuthenticated && !string.IsNullOrEmpty(player.RequestedTargetId))
                        {
                            // === НОВОЕ: Перехват подключения к боту ===
                            if (player.RequestedTargetId == "BOT_ID")
                            {
                                // Создаем игру с флагом isBotMatch = true (player2 будет null)
                                var engine = new GameEngine(player, null, this, true);
                                _ = engine.StartLoopAsync();
                                _waitingPlayers.Remove(player);
                                player.RequestedTargetId = null;
                                continue;
                            }
                            // ============================================

                            var target = _waitingPlayers.FirstOrDefault(p => p.Id == player.RequestedTargetId && p.IsHost && p.IsAuthenticated);
                            if (target != null && target != player)
                            {
                                pairsToStart.Add((target, player));
                                _waitingPlayers.Remove(player);
                                _waitingPlayers.Remove(target);
                            }
                            player.RequestedTargetId = null;
                        }
                    }

                    foreach (var pair in pairsToStart)
                    {
                        var engine = new GameEngine(pair.host, pair.joiner, this);
                        _ = engine.StartLoopAsync();
                    }

                    var availableLobbies = _waitingPlayers
                        .Where(p => p.IsAuthenticated && p.IsHost)
                        .Select(p => new PlayerInfo { Id = p.Id, Name = p.Name })
                        .ToList();

                    // === НОВОЕ: Добавляем бота в самое начало списка комнат ===
                    availableLobbies.Insert(0, new PlayerInfo { Id = "BOT_ID", Name = "🤖 Бот-тренер" });
                    // ==========================================================

                    var topPlayersList = Auth.GetTopPlayers();
                    var menuState = new GameState { Status = GameStatus.MainMenu, AvailablePlayers = availableLobbies, TopPlayers = topPlayersList };
                    var hostState = new GameState { Status = GameStatus.HostWaiting, TopPlayers = topPlayersList };

                    foreach (var player in _waitingPlayers)
                    {
                        if (!player.IsAuthenticated)
                        {
                            var authState = new GameState { Status = GameStatus.AuthScreen, Message = player.AuthMessage };
                            _ = player.SendStateAsync(authState);
                        }
                        else if (player.IsHost) _ = player.SendStateAsync(hostState);
                        else _ = player.SendStateAsync(menuState);
                    }
                }

                await Task.Delay(33);
            }
        }

        public void ReturnToGlobalLobby(PlayerConnection player)
        {
            if (player != null && player.IsConnected)
            {
                player.IsReady = false;
                player.WantsToRestart = false;
                player.IsHost = false;
                player.WantsToLeaveRoom = false;
                player.RequestedTargetId = null;
                lock (_lock) _waitingPlayers.Add(player);
            }
        }
    }
}