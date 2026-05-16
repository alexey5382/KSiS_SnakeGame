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

        // === ПЕРЕМЕННЫЕ ДЛЯ БОТА ===
        private readonly bool _isBotMatch;
        private Direction _botDirection = Direction.Left;
        // ===========================

        private const int GridWidth = 40;
        private const int GridHeight = 30;
        private readonly Random _random = new Random();

        private readonly double _cellsPerSecond = 4.0;
        private readonly int _countdownSeconds = 3;
        private DateTime? _gameStartTime;

        private readonly int _maxNormalFood = 3;
        private readonly int _maxGoldFood = 1;
        private readonly int _maxPurpleFood = 2;

        private readonly int _normalFoodEffect = 1;
        private readonly int _goldFoodEffect = 3;
        private readonly int _purpleFoodEffect = -1;

        private readonly int _normalFoodSpawnDelay = 0;
        private readonly int _goldFoodSpawnDelay = 10;
        private readonly int _purpleFoodSpawnDelay = 5;

        private DateTime _lastNormalEatTime = DateTime.MinValue;
        private DateTime _lastGoldEatTime = DateTime.MinValue;
        private DateTime _lastPurpleEatTime = DateTime.MinValue;

        private int _p1PendingGrowth = 0;
        private int _p2PendingGrowth = 0;

        // ИЗМЕНЕНО: Добавлен параметр isBotMatch
        public GameEngine(PlayerConnection p1, PlayerConnection p2, ServerManager manager, bool isBotMatch = false)
        {
            _player1 = p1;
            _player2 = p2;
            _manager = manager;
            _isBotMatch = isBotMatch;

            _state.Status = GameStatus.RoomLobby;
            _state.Player1Name = _player1.Name;

            // Если игра с ботом, задаем ему имя вручную
            _state.Player2Name = _isBotMatch ? "🤖 БОТ" : _player2.Name;
        }

        public async Task StartLoopAsync()
        {
            int networkTickMs = 1000 / 30;
            double moveIntervalMs = 1000.0 / _cellsPerSecond;
            DateTime lastMoveTime = DateTime.Now;

            var p1 = _player1;
            var p2 = _player2; // Если это бот, переменная останется null, это нормально

            while (true)
            {
                if (!p1.IsConnected) _player1 = null;
                if (!_isBotMatch && p2 != null && !p2.IsConnected) _player2 = null;

                if (_player1 == null || (!_isBotMatch && _player2 == null)) break;

                CheckDisconnections();

                if (_player1?.WantsToLeaveRoom == true || (!_isBotMatch && _player2?.WantsToLeaveRoom == true))
                {
                    bool p1Left = _player1?.WantsToLeaveRoom == true;
                    string leaver = p1Left ? _state.Player1Name : _state.Player2Name;
                    string winner = p1Left ? _state.Player2Name : _state.Player1Name;

                    if (_state.Status == GameStatus.Playing)
                    {
                        EndGame($"{leaver} сдался. Победа {winner}!");
                        if (_player1 != null) _player1.WantsToLeaveRoom = false;
                        if (_player2 != null) _player2.WantsToLeaveRoom = false;
                    }
                    else break;
                }

                switch (_state.Status)
                {
                    case GameStatus.RoomLobby:
                        HandleRoomLobby();
                        break;

                    case GameStatus.Playing:
                        if (_gameStartTime == null) _gameStartTime = DateTime.Now;
                        _state.MatchTimer = (int)(DateTime.Now - _gameStartTime.Value).TotalSeconds;

                        if ((DateTime.Now - lastMoveTime).TotalMilliseconds >= moveIntervalMs)
                        {
                            UpdateGameLogic();
                            lastMoveTime = DateTime.Now;
                        }
                        break;

                    case GameStatus.GameOver:
                        bool p1Ready = _player1 == null || _player1.WantsToRestart;
                        // Бот всегда готов к перезапуску
                        bool p2Ready = _isBotMatch || (_player2 == null || _player2.WantsToRestart);

                        if (_player1 != null && (_isBotMatch || _player2 != null))
                        {
                            if (_player1.WantsToRestart && !_isBotMatch && !_player2.WantsToRestart)
                                _state.Message = $"{_player1.Name} готов выйти. Ждем {_player2.Name}...";
                            else if (!_isBotMatch && _player2.WantsToRestart && !_player1.WantsToRestart)
                                _state.Message = $"{_player2.Name} готов выйти. Ждем {_player1.Name}...";
                        }

                        if (p1Ready && p2Ready && (_player1 != null || _player2 != null))
                        {
                            ResetToRoomLobby();
                        }
                        break;
                }

                _state.TopPlayers = _manager.Auth.GetTopPlayers();

                if (_player1?.IsConnected == true) await _player1.SendStateAsync(_state);
                // Отправляем пакет второму игроку, только если это не бот
                if (!_isBotMatch && _player2?.IsConnected == true) await _player2.SendStateAsync(_state);

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
            // Бот автоматически "нажимает готовность", когда готов игрок
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

                _state.Snake1.AddRange(new[] { new Position(10, 15), new Position(9, 15), new Position(8, 15) });
                _state.Snake2.AddRange(new[] { new Position(30, 15), new Position(31, 15), new Position(32, 15) });

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

            // Если играем с ботом - заставляем его вычислить ход перед движением
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

        // === ИСКУССТВЕННЫЙ ИНТЕЛЛЕКТ БОТА ===
        private void CalculateBotMove()
        {
            if (_state.Snake2.Count == 0 || _state.Food.Count == 0) return;

            var head = _state.Snake2[0];

            // Ищем первую попавшуюся не фиолетовую еду, или хотя бы любую
            var targetFood = _state.Food.FirstOrDefault(f => f.Type != FoodType.Purple) ?? _state.Food[0];
            Position target = targetFood.Position;

            var possibleMoves = new List<Direction> { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

            // Бот не может развернуться на 180 градусов
            if (_botDirection == Direction.Up) possibleMoves.Remove(Direction.Down);
            if (_botDirection == Direction.Down) possibleMoves.Remove(Direction.Up);
            if (_botDirection == Direction.Left) possibleMoves.Remove(Direction.Right);
            if (_botDirection == Direction.Right) possibleMoves.Remove(Direction.Left);

            // Сортируем возможные ходы по близости к цели (Жадный алгоритм)
            possibleMoves.Sort((a, b) =>
            {
                var posA = GetNextHeadPosition(head, a);
                var posB = GetNextHeadPosition(head, b);
                int distA = Math.Abs(posA.X - target.X) + Math.Abs(posA.Y - target.Y);
                int distB = Math.Abs(posB.X - target.X) + Math.Abs(posB.Y - target.Y);
                return distA.CompareTo(distB);
            });

            // Выбираем первый ход, который не приводит к смерти
            foreach (var move in possibleMoves)
            {
                var nextPos = GetNextHeadPosition(head, move);
                if (!IsCollision(nextPos, _state.Snake2, _state.Snake1))
                {
                    _botDirection = move;
                    return;
                }
            }
            // Если все пути ведут к смерти - бот сохраняет направление и разбивается
        }
        // ===================================

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
            if (nextHead.X < 0 || nextHead.X >= GridWidth || nextHead.Y < 0 || nextHead.Y >= GridHeight) return true;
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
                newFood = new Position(_random.Next(0, GridWidth), _random.Next(0, GridHeight));
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

            if (_player1 != null) _manager.Auth.UpdateMaxScore(_player1.Name, _state.Snake1.Count);
            if (!_isBotMatch && _player2 != null) _manager.Auth.UpdateMaxScore(_player2.Name, _state.Snake2.Count);
        }
    }
}