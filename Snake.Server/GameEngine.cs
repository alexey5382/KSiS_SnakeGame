using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Snake.Shared.Enums;
using Snake.Shared.Models;

namespace Snake.Server
{
    public class GameEngine
    {
        private readonly GameState _state = new GameState();
        private PlayerConnection _player1;
        private PlayerConnection _player2;
        private readonly ServerManager _manager;

        private readonly bool _isBotMatch;
        private Direction _botDirection = Direction.Left;

        // ИСПРАВЛЕНИЕ НАСТРОЕК: Теперь это не константы
        private int _gridWidth = 40;
        private int _gridHeight = 30;
        private readonly Random _random = new Random();

        private double _cellsPerSecond = 4.0;
        private readonly int _countdownSeconds = 3;
        private DateTime? _gameStartTime;

        private int _maxNormalFood = 3;
        private int _maxGoldFood = 1;
        private int _maxPurpleFood = 2;

        private int _normalFoodEffect = 1;
        private int _goldFoodEffect = 3;
        private int _purpleFoodEffect = -1;

        private int _normalFoodSpawnDelay = 0;
        private int _goldFoodSpawnDelay = 10;
        private int _purpleFoodSpawnDelay = 5;

        private DateTime _lastNormalEatTime = DateTime.MinValue;
        private DateTime _lastGoldEatTime = DateTime.MinValue;
        private DateTime _lastPurpleEatTime = DateTime.MinValue;

        private int _p1PendingGrowth = 0;
        private int _p2PendingGrowth = 0;

        public GameEngine(PlayerConnection p1, PlayerConnection p2, ServerManager manager, bool isBotMatch = false)
        {
            _player1 = p1;
            _player2 = p2;
            _manager = manager;
            _isBotMatch = isBotMatch;

            _state.Status = GameStatus.RoomLobby;
            _state.Player1Name = _player1.Name;
            _state.Player2Name = _isBotMatch ? "🤖 БОТ" : (_player2?.Name ?? "Ожидание...");

            // ИСПРАВЛЕНИЕ: Дефолтное имя лобби с самого старта
            _state.LobbyName = string.IsNullOrEmpty(_player1.CustomLobbyName) ? $"Лобби {_state.Player1Name}" : _player1.CustomLobbyName;
            _state.Settings = new GameSettingsConfig();
        }

        private void ApplySettings(GameSettingsConfig config)
        {
            _state.Settings = config;
            _gridWidth = config.GridWidth;
            _gridHeight = config.GridHeight;
            _cellsPerSecond = config.Speed;

            _maxNormalFood = config.NormalFoodCount;
            _normalFoodEffect = config.NormalFoodEffect;
            _normalFoodSpawnDelay = config.NormalFoodDelay; // <== ИСПРАВЛЕНО

            _maxGoldFood = config.GoldFoodCount;
            _goldFoodEffect = config.GoldFoodEffect;
            _goldFoodSpawnDelay = config.GoldFoodDelay;     // <== ИСПРАВЛЕНО

            _maxPurpleFood = config.PurpleFoodCount;
            _purpleFoodEffect = config.PurpleFoodEffect;
            _purpleFoodSpawnDelay = config.PurpleFoodDelay; // <== ИСПРАВЛЕНО
        }

        public async Task StartLoopAsync()
        {
            int networkTickMs = 1000 / 30;
            DateTime lastMoveTime = DateTime.Now;

            var p1 = _player1;
            var p2 = _player2;

            while (true)
            {
                if (!p1.IsConnected) _player1 = null;
                if (!_isBotMatch && p2 != null && !p2.IsConnected) _player2 = null;

                if (_player1 == null || (!_isBotMatch && _player2 == null)) break;

                // Применяем настройки, если хост их изменил
                if (_player1.PendingSettings != null)
                {
                    ApplySettings(_player1.PendingSettings);
                    _player1.PendingSettings = null;
                }

                CheckDisconnections();

                // Обработка ПОЛНОГО выхода (в меню)
                if (_player1?.WantsToLeaveRoom == true || (!_isBotMatch && _player2?.WantsToLeaveRoom == true))
                {
                    break;
                }

                // ИСПРАВЛЕНИЕ ВЫХОДА ВО ВРЕМЯ ИГРЫ: "Сдача" возвращает в локальное лобби
                if (_player1?.WantsToRestart == true || (!_isBotMatch && _player2?.WantsToRestart == true))
                {
                    if (_state.Status == GameStatus.Playing || _state.Status == GameStatus.Countdown)
                    {
                        bool p1Left = _player1?.WantsToRestart == true;
                        string leaver = p1Left ? _state.Player1Name : _state.Player2Name;
                        string winner = p1Left ? _state.Player2Name : _state.Player1Name;
                        EndGame($"{leaver} вышел. Победа {winner}!");
                    }
                }

                switch (_state.Status)
                {
                    case GameStatus.RoomLobby:
                        HandleRoomLobby();
                        break;

                    case GameStatus.Playing:
                        if (_gameStartTime == null) _gameStartTime = DateTime.Now;
                        _state.MatchTimer = (int)(DateTime.Now - _gameStartTime.Value).TotalSeconds;

                        double moveIntervalMs = 1000.0 / _cellsPerSecond;
                        if ((DateTime.Now - lastMoveTime).TotalMilliseconds >= moveIntervalMs)
                        {
                            UpdateGameLogic();
                            lastMoveTime = DateTime.Now;
                        }
                        break;

                    case GameStatus.GameOver:
                        // Синхронизируем флаги готовности
                        _state.IsPlayer1Ready = _player1?.IsReady == true;
                        _state.IsPlayer2Ready = _isBotMatch ? _state.IsPlayer1Ready : (_player2?.IsReady == true);

                        bool p1Lobby = _player1 != null && _player1.WantsToRestart;
                        bool p2Lobby = !_isBotMatch && _player2 != null && _player2.WantsToRestart;

                        bool p1Ready = _player1 != null && _player1.IsReady;
                        bool p2Ready = _isBotMatch || (_player2 != null && _player2.IsReady);

                        // ИСПРАВЛЕНИЕ: Сбрасываем лобби, только если ОБА игрока вышли (или это матч с ботом)
                        if (p1Lobby && (p2Lobby || _isBotMatch))
                        {
                            ResetToRoomLobby();
                        }
                        // Если Игрок 2 нажал "В лобби", а Игрок 1 уже ждет в лобби
                        else if (p2Lobby && p1Lobby)
                        {
                            ResetToRoomLobby();
                        }
                        else if (p1Ready && p2Ready)
                        {
                            // Перезапуск матча при обоюдном согласии
                            _state.Snake1.Clear();
                            _state.Snake2.Clear();
                            _state.Food.Clear();
                            _gameStartTime = null;
                            _state.MatchTimer = 0;

                            _player1.IsReady = false;
                            if (!_isBotMatch) _player2.IsReady = false;

                            _player1.CurrentDirection = Direction.Right;
                            if (!_isBotMatch) _player2.CurrentDirection = Direction.Left;
                            else _botDirection = Direction.Left;

                            int spawnY = _gridHeight / 2;
                            int spawnX1 = _gridWidth / 4;
                            int spawnX2 = (_gridWidth * 3) / 4;

                            _state.Snake1.AddRange(new[] { new Position(spawnX1, spawnY), new Position(spawnX1 - 1, spawnY), new Position(spawnX1 - 2, spawnY) });
                            _state.Snake2.AddRange(new[] { new Position(spawnX2, spawnY), new Position(spawnX2 + 1, spawnY), new Position(spawnX2 + 2, spawnY) });

                            _state.IsPlayer1Ready = false;
                            _state.IsPlayer2Ready = false;

                            _state.Status = GameStatus.Countdown;
                            _ = RunCountdownAsync();
                        }
                        else
                        {
                            // ИСПРАВЛЕНИЕ: Формируем служебные сообщения о выходе в лобби
                            if (p1Lobby && !p2Lobby && !_isBotMatch)
                            {
                                _state.Message = $"Игрок {_state.Player1Name} вышел в лобби.";
                            }
                            else if (p2Lobby && !p1Lobby)
                            {
                                _state.Message = $"Игрок {_state.Player2Name} вышел в лобби.";
                            }
                            else if (p1Ready && !p2Ready)
                            {
                                _state.Message = $"Ожидание игрока {_state.Player2Name}...";
                            }
                            else if (p2Ready && !p1Ready)
                            {
                                _state.Message = $"Ожидание игрока {_state.Player1Name}...";
                            }
                            else
                            {
                                _state.Message = "Матч завершен";
                            }
                        }
                        break;
                }

                _state.Player1Name = _player1?.Name ?? "Отключен";
                _state.Player2Name = _isBotMatch ? "🤖 БОТ" : (_player2?.Name ?? "Ожидание...");
                _state.LobbyName = _player1?.CustomLobbyName ?? $"Лобби {_state.Player1Name}";

                _state.TopPlayers = _manager.Auth.GetTopPlayers();

                if (_player1?.IsConnected == true)
                {
                    if (_state.Status == GameStatus.GameOver && _player1.WantsToRestart)
                    {
                        var p1State = new GameState { Status = GameStatus.RoomLobby, Player1Name = _state.Player1Name, Player2Name = _state.Player2Name, IsPlayer1Ready = _player1.IsReady, IsPlayer2Ready = false, TopPlayers = _state.TopPlayers, LobbyName = _state.LobbyName, Settings = _state.Settings, Message = _player1.IsReady ? $"{_player1.Name} ожидает..." : string.Empty };
                        await _player1.SendStateAsync(p1State);
                    }
                    else await _player1.SendStateAsync(_state);
                }

                if (!_isBotMatch && _player2?.IsConnected == true)
                {
                    if (_state.Status == GameStatus.GameOver && _player2.WantsToRestart)
                    {
                        var p2State = new GameState { Status = GameStatus.RoomLobby, Player1Name = _state.Player1Name, Player2Name = _state.Player2Name, IsPlayer1Ready = false, IsPlayer2Ready = _player2.IsReady, TopPlayers = _state.TopPlayers, LobbyName = _state.LobbyName, Settings = _state.Settings, Message = _player2.IsReady ? $"{_player2.Name} ожидает..." : string.Empty };
                        await _player2.SendStateAsync(p2State);
                    }
                    else await _player2.SendStateAsync(_state);
                }

                await Task.Delay(networkTickMs);
            }

            _manager.ReturnToGlobalLobby(p1);
            if (!_isBotMatch) _manager.ReturnToGlobalLobby(p2);
        }

        private void CheckDisconnections()
        {
            bool dropP1 = _player1 != null && !_player1.IsConnected;
            bool dropP2 = !_isBotMatch && (_player2 != null && !_player2.IsConnected);

            if (dropP1) _player1 = null;
            if (dropP2) _player2 = null;

            if ((dropP1 || dropP2) && (_state.Status == GameStatus.Playing || _state.Status == GameStatus.Countdown || _state.Status == GameStatus.RoomLobby))
            {
                _state.Status = GameStatus.GameOver;
                _state.IsGameOver = true;
                _state.Message = "Игрок отключился! Конец игры.";
            }
        }

        private void HandleRoomLobby()
        {
            _state.IsPlayer1Ready = _player1?.IsReady == true;
            _state.IsPlayer2Ready = _isBotMatch ? _state.IsPlayer1Ready : _player2?.IsReady == true;

            if (_state.IsPlayer1Ready && !_state.IsPlayer2Ready)
                _state.Message = $"{_state.Player1Name} ожидает начала игры...";
            else if (_state.IsPlayer2Ready && !_state.IsPlayer1Ready)
                _state.Message = $"{_state.Player2Name} ожидает начала игры...";
            else
                _state.Message = string.Empty;

            if (_state.IsPlayer1Ready && _state.IsPlayer2Ready)
            {
                _player1.CurrentDirection = Direction.Right;
                if (!_isBotMatch) _player2.CurrentDirection = Direction.Left;
                else _botDirection = Direction.Left;

                // === ИСПРАВЛЕНО: Динамический расчет стартовых точек змеек ===
                int spawnY = _gridHeight / 2;
                int spawnX1 = _gridWidth / 4;
                int spawnX2 = (_gridWidth * 3) / 4;

                _state.Snake1.Clear();
                _state.Snake2.Clear();

                _state.Snake1.AddRange(new[] { new Position(spawnX1, spawnY), new Position(spawnX1 - 1, spawnY), new Position(spawnX1 - 2, spawnY) });
                _state.Snake2.AddRange(new[] { new Position(spawnX2, spawnY), new Position(spawnX2 + 1, spawnY), new Position(spawnX2 + 2, spawnY) });

                if (_player1 != null) _player1.IsReady = false;
                if (!_isBotMatch && _player2 != null) _player2.IsReady = false;
                _state.IsPlayer1Ready = false;
                _state.IsPlayer2Ready = false;
                _state.Status = GameStatus.Countdown;
                _ = RunCountdownAsync();
            }
        }

        private async Task RunCountdownAsync()
        {
            for (int i = _countdownSeconds; i > 0; i--)
            {
                if (_state.Status != GameStatus.Countdown) return;
                _state.CountdownValue = i;
                await Task.Delay(1000);
            }

            if (_state.Status == GameStatus.Countdown)
            {
                _state.Food.Clear();
                _p1PendingGrowth = 0;
                _p2PendingGrowth = 0;

                for (int i = 0; i < _maxNormalFood; i++) SpawnSingleFood(FoodType.Normal);
                for (int i = 0; i < _maxGoldFood; i++) SpawnSingleFood(FoodType.Gold);
                for (int i = 0; i < _maxPurpleFood; i++) SpawnSingleFood(FoodType.Purple);

                _state.Status = GameStatus.Playing;
            }
        }

        private void ResetToRoomLobby()
        {
            _state.Status = GameStatus.RoomLobby;
            _state.IsGameOver = false;
            _state.Message = string.Empty;
            _state.Snake1.Clear();
            _state.Snake2.Clear();
            _state.Food.Clear();
            _gameStartTime = null;
            _state.MatchTimer = 0;

            if (_player1 != null) { _player1.IsReady = false; _player1.WantsToRestart = false; _player1.CurrentDirection = Direction.Right; }
            if (!_isBotMatch && _player2 != null) { _player2.IsReady = false; _player2.WantsToRestart = false; _player2.CurrentDirection = Direction.Left; }
            else if (_isBotMatch) { _botDirection = Direction.Left; }
        }

        private void UpdateGameLogic()
        {
            CheckAndSpawnFood(FoodType.Normal, _maxNormalFood, _normalFoodSpawnDelay, ref _lastNormalEatTime);
            CheckAndSpawnFood(FoodType.Gold, _maxGoldFood, _goldFoodSpawnDelay, ref _lastGoldEatTime);
            CheckAndSpawnFood(FoodType.Purple, _maxPurpleFood, _purpleFoodSpawnDelay, ref _lastPurpleEatTime);

            if (_isBotMatch) CalculateBotMove();

            Position nextHead1 = GetNextHeadPosition(_state.Snake1[0], _player1.CurrentDirection);
            Position nextHead2 = GetNextHeadPosition(_state.Snake2[0], _isBotMatch ? _botDirection : _player2.CurrentDirection);

            bool p1Crashed = IsCollision(nextHead1, _state.Snake1, _state.Snake2);
            bool p2Crashed = IsCollision(nextHead2, _state.Snake2, _state.Snake1);

            if (nextHead1.X == nextHead2.X && nextHead1.Y == nextHead2.Y)
            {
                p1Crashed = true;
                p2Crashed = true;
            }

            if (p1Crashed && p2Crashed) { EndGame("Ничья! Обе змейки разбились."); return; }
            if (p1Crashed) { EndGame("Победа Игрока 2 (Красный)! Синий врезался."); return; }
            if (p2Crashed) { EndGame("Победа Игрока 1 (Синий)! Красный врезался."); return; }

            ApplyMovement(_state.Snake1, nextHead1, ref _p1PendingGrowth);
            ApplyMovement(_state.Snake2, nextHead2, ref _p2PendingGrowth);
        }

        private void CalculateBotMove()
        {
            if (_state.Snake2.Count == 0 || _state.Food.Count == 0) return;

            var head = _state.Snake2[0];
            var targetFood = _state.Food.FirstOrDefault(f => f.Type != FoodType.Purple) ?? _state.Food[0];
            Position target = targetFood.Position;

            var possibleMoves = new List<Direction> { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

            if (_botDirection == Direction.Up) possibleMoves.Remove(Direction.Down);
            if (_botDirection == Direction.Down) possibleMoves.Remove(Direction.Up);
            if (_botDirection == Direction.Left) possibleMoves.Remove(Direction.Right);
            if (_botDirection == Direction.Right) possibleMoves.Remove(Direction.Left);

            possibleMoves.Sort((a, b) =>
            {
                var posA = GetNextHeadPosition(head, a);
                var posB = GetNextHeadPosition(head, b);
                int distA = Math.Abs(posA.X - target.X) + Math.Abs(posA.Y - target.Y);
                int distB = Math.Abs(posB.X - target.X) + Math.Abs(posB.Y - target.Y);
                return distA.CompareTo(distB);
            });

            foreach (var move in possibleMoves)
            {
                var nextPos = GetNextHeadPosition(head, move);
                if (!IsCollision(nextPos, _state.Snake2, _state.Snake1))
                {
                    _botDirection = move;
                    return;
                }
            }
        }

        private Position GetNextHeadPosition(Position currentHead, Direction dir)
        {
            var newHead = new Position(currentHead.X, currentHead.Y);
            switch (dir)
            {
                case Direction.Up: newHead.Y--; break;
                case Direction.Down: newHead.Y++; break;
                case Direction.Left: newHead.X--; break;
                case Direction.Right: newHead.X++; break;
            }
            return newHead;
        }

        private bool IsCollision(Position nextHead, List<Position> myBody, List<Position> enemyBody)
        {
            if (nextHead.X < 0 || nextHead.X >= _gridWidth || nextHead.Y < 0 || nextHead.Y >= _gridHeight) return true;
            for (int i = 0; i < myBody.Count - 1; i++) if (myBody[i].X == nextHead.X && myBody[i].Y == nextHead.Y) return true;
            foreach (var part in enemyBody) if (part.X == nextHead.X && part.Y == nextHead.Y) return true;
            return false;
        }

        private void ApplyMovement(List<Position> snake, Position nextHead, ref int pendingGrowth)
        {
            snake.Insert(0, nextHead);

            int foodIndex = _state.Food.FindIndex(f => f.Position.X == nextHead.X && f.Position.Y == nextHead.Y);

            if (foodIndex != -1)
            {
                var eatenFood = _state.Food[foodIndex];
                _state.Food.RemoveAt(foodIndex);

                if (eatenFood.Type == FoodType.Normal)
                {
                    pendingGrowth += _normalFoodEffect - 1;
                    _lastNormalEatTime = DateTime.Now;
                }
                else if (eatenFood.Type == FoodType.Gold)
                {
                    pendingGrowth += _goldFoodEffect - 1;
                    _lastGoldEatTime = DateTime.Now;
                }
                else if (eatenFood.Type == FoodType.Purple)
                {
                    pendingGrowth += _purpleFoodEffect - 1;
                    _lastPurpleEatTime = DateTime.Now;
                }
            }
            else
            {
                pendingGrowth--;
            }

            while (pendingGrowth < 0)
            {
                if (snake.Count > 3)
                {
                    snake.RemoveAt(snake.Count - 1);
                    pendingGrowth++;
                }
                else
                {
                    pendingGrowth = 0;
                    break;
                }
            }
        }

        private void CheckAndSpawnFood(FoodType type, int maxCount, int delaySeconds, ref DateTime lastEatTime)
        {
            int currentCount = _state.Food.Count(f => f.Type == type);
            if (currentCount < maxCount)
            {
                if ((DateTime.Now - lastEatTime).TotalSeconds >= delaySeconds)
                {
                    SpawnSingleFood(type);
                    lastEatTime = DateTime.Now;
                }
            }
        }

        private void SpawnSingleFood(FoodType type)
        {
            Position newFood;
            while (true)
            {
                newFood = new Position(_random.Next(0, _gridWidth), _random.Next(0, _gridHeight));
                bool inSnake1 = _state.Snake1.Exists(p => p.X == newFood.X && p.Y == newFood.Y);
                bool inSnake2 = _state.Snake2.Exists(p => p.X == newFood.X && p.Y == newFood.Y);
                bool inFood = _state.Food.Exists(f => f.Position.X == newFood.X && f.Position.Y == newFood.Y);

                if (!inSnake1 && !inSnake2 && !inFood) break;
            }
            _state.Food.Add(new FoodItem { Position = newFood, Type = type });
        }

        private void EndGame(string message)
        {
            _state.IsGameOver = true;
            _state.Message = message;
            _state.Status = GameStatus.GameOver;

            // Запись рекордов происходит по постоянному свойству Login, а не Name!
            if (_player1 != null)
            {
                _manager.Auth.UpdateMaxScore(_player1.Login, _state.Snake1.Count);
                _state.Player1Record = _manager.Auth.GetUserRecord(_player1.Login);
            }

            if (!_isBotMatch && _player2 != null)
            {
                _manager.Auth.UpdateMaxScore(_player2.Login, _state.Snake2.Count);
                _state.Player2Record = _manager.Auth.GetUserRecord(_player2.Login);
            }
            else if (_isBotMatch)
            {
                _state.Player2Record = _state.Snake2.Count;
            }
        }
    }
}