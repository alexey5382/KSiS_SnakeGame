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

        private readonly object _engineLock = new object();
        private bool _isLoopRunning = false;

        private readonly bool _isBotMatch;
        private Direction _botDirection = Direction.Left;

        private int _gridWidth = 40;
        private int _gridHeight = 30;
        private readonly Random _random = new Random();

        private double _cellsPerSecond = 4.0;
        private readonly int _countdownSeconds = 3;
        private DateTime? _countdownStartTime;
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
            _state.LobbyName = string.IsNullOrEmpty(_player1.CustomLobbyName) ? $"Лобби {_state.Player1Name}" : _player1.CustomLobbyName;
            _state.Settings = new GameSettingsConfig();
        }
        ///<summary>
        ///запуск лобби
        ///</summary>
        public void StartLobby()
        {
            lock (_engineLock)
            {
                if (_player1 != null)
                {
                    _player1.InGameEngine = true;
                    _player1.OnRoomStateChanged += HandlePlayerInput;
                }
                if (!_isBotMatch && _player2 != null)
                {
                    _player2.InGameEngine = true;
                    _player2.OnRoomStateChanged += HandlePlayerInput;
                }

                ProcessRoomLobby();
                BroadcastState();
            }
        }
        ///<summary>
        ///обработчик событий от пользователей
        ///</summary>
        private void HandlePlayerInput()
        {
            lock (_engineLock)
            {
                if (CheckRoomDestruction()) return;

                if (_player1?.PendingSettings != null)
                {
                    ApplySettings(_player1.PendingSettings);
                    _player1.PendingSettings = null;
                }

                CheckSurrender();

                if (!_isLoopRunning)
                {
                    if (_state.Status == GameStatus.RoomLobby) ProcessRoomLobby();
                    else if (_state.Status == GameStatus.GameOver) ProcessGameOver();

                    if (_isLoopRunning) _ = ActiveGameLoopAsync();
                    else BroadcastState();
                }
            }
        }
        ///<summary>
        ///активный режим, 30Гц
        ///</summary>
        private async Task ActiveGameLoopAsync()
        {
            int networkTickMs = 1000 / 30;
            DateTime lastMoveTime = DateTime.Now;

            while (_isLoopRunning)
            {
                lock (_engineLock)
                {
                    if (CheckRoomDestruction()) break;
                    CheckSurrender();

                    if (!_isLoopRunning) break;

                    if (_state.Status == GameStatus.Countdown)
                    {
                        if (_countdownStartTime == null) _countdownStartTime = DateTime.Now;
                        int elapsed = (int)(DateTime.Now - _countdownStartTime.Value).TotalSeconds;
                        int remaining = _countdownSeconds - elapsed;

                        if (remaining > 0)
                        {
                            _state.CountdownValue = remaining;
                        }
                        else
                        {
                            _state.Status = GameStatus.Playing;
                            _gameStartTime = DateTime.Now;
                            _p1PendingGrowth = 0;
                            _p2PendingGrowth = 0;
                            _state.Food.Clear();
                            //создание еды
                            for (int i = 0; i < _maxNormalFood; i++) SpawnSingleFood(FoodType.Normal);
                            for (int i = 0; i < _maxGoldFood; i++) SpawnSingleFood(FoodType.Gold);
                            for (int i = 0; i < _maxPurpleFood; i++) SpawnSingleFood(FoodType.Purple);

                            lastMoveTime = DateTime.Now;
                        }
                    }
                    else if (_state.Status == GameStatus.Playing)
                    {
                        if (_gameStartTime == null) _gameStartTime = DateTime.Now;
                        _state.MatchTimer = (int)(DateTime.Now - _gameStartTime.Value).TotalSeconds;

                        double moveIntervalMs = 1000.0 / _cellsPerSecond;
                        if ((DateTime.Now - lastMoveTime).TotalMilliseconds >= moveIntervalMs)
                        {
                            UpdateGameLogic();
                            lastMoveTime = DateTime.Now;
                        }
                    }

                    BroadcastState();
                }

                await Task.Delay(networkTickMs);
            }

            lock (_engineLock)
            {
                if (_player1 != null || _player2 != null)
                {
                    ProcessGameOver();
                    BroadcastState();
                }
            }
        }
        ///<summary>
        ///обработка лобби. проверка на готовность игроков
        ///</summary>
        private void ProcessRoomLobby()
        {
            _state.Player1Name = _player1?.Name ?? "Отключен";
            _state.Player2Name = _isBotMatch ? "🤖 БОТ" : (_player2?.Name ?? "Ожидание...");
            _state.LobbyName = _player1?.CustomLobbyName ?? $"Лобби {_state.Player1Name}";

            _state.IsPlayer1Ready = _player1?.IsReady == true;
            _state.IsPlayer2Ready = _isBotMatch ? _state.IsPlayer1Ready : (_player2?.IsReady == true);

            if (_state.IsPlayer1Ready && _state.IsPlayer2Ready)
            {
                InitializeSnakes();
                _state.Status = GameStatus.Countdown;
                _countdownStartTime = DateTime.Now;
                _isLoopRunning = true;
            }
            else
            {
                if (_state.IsPlayer1Ready && !_state.IsPlayer2Ready) _state.Message = $"{_state.Player1Name} ожидает...";
                else if (_state.IsPlayer2Ready && !_state.IsPlayer1Ready) _state.Message = $"{_state.Player2Name} ожидает...";
                else _state.Message = string.Empty;
            }
        }


        ///<summary>
        ///сброс змеек и подготовка их перед началом матча
        ///</summary>
        private void InitializeSnakes()
        {
            _state.IsPlayer1Ready = false;
            _state.IsPlayer2Ready = false;
            if (_player1 != null) _player1.IsReady = false;
            if (!_isBotMatch && _player2 != null) _player2.IsReady = false;

            _player1.CurrentDirection = Direction.Right;
            if (!_isBotMatch) _player2.CurrentDirection = Direction.Left;
            else _botDirection = Direction.Left;

            int spawnY = _gridHeight / 2;
            int spawnX1 = _gridWidth / 4;
            int spawnX2 = (_gridWidth * 3) / 4;

            _state.Snake1.Clear();
            _state.Snake2.Clear();
            _state.Snake1.AddRange(new[] { new Position(spawnX1, spawnY), new Position(spawnX1 - 1, spawnY), new Position(spawnX1 - 2, spawnY) });
            _state.Snake2.AddRange(new[] { new Position(spawnX2, spawnY), new Position(spawnX2 + 1, spawnY), new Position(spawnX2 + 2, spawnY) });
        }
        ///<summary>
        ///выход в лобби и очистка игрового экрана
        ///</summary>
        private void ResetToRoomLobby()
        {
            _state.Status = GameStatus.RoomLobby;
            _state.IsGameOver = false;
            _state.Snake1.Clear();
            _state.Snake2.Clear();
            _state.Food.Clear();
            _gameStartTime = null;
            _countdownStartTime = null;
            _state.MatchTimer = 0;

            if (_player1 != null) { _player1.IsReady = false; _player1.WantsToRestart = false; }
            if (!_isBotMatch && _player2 != null) { _player2.IsReady = false; _player2.WantsToRestart = false; }

            ProcessRoomLobby();
        }

        ///<summary>
        ///управление статусом комнаты
        ///</summary>
        private bool CheckRoomDestruction()
        {
            bool p1Leave = _player1 != null && _player1.WantsToLeaveRoom && _state.Status != GameStatus.Playing && _state.Status != GameStatus.Countdown;
            bool p2Leave = !_isBotMatch && _player2 != null && _player2.WantsToLeaveRoom && _state.Status != GameStatus.Playing && _state.Status != GameStatus.Countdown;

            bool p1Dead = _player1 == null || !_player1.IsConnected || p1Leave;
            bool p2Dead = !_isBotMatch && (_player2 == null || !_player2.IsConnected || p2Leave);

            if (p1Dead || p2Dead)
            {
                _isLoopRunning = false;
                if (_player1 != null) _player1.OnRoomStateChanged -= HandlePlayerInput;
                if (_player2 != null) _player2.OnRoomStateChanged -= HandlePlayerInput;

                _manager.ReturnToGlobalLobby(_player1);
                if (!_isBotMatch) _manager.ReturnToGlobalLobby(_player2);

                _player1 = null;
                _player2 = null;
                return true;
            }
            return false;
        }
        ///<summary>
        ///проверка на досрочный выход игрока
        ///</summary>
        private void CheckSurrender()
        {
            if (_state.Status == GameStatus.Playing || _state.Status == GameStatus.Countdown)
            {
                bool p1Surrender = _player1 != null && (_player1.WantsToRestart || _player1.WantsToLeaveRoom);
                bool p2Surrender = !_isBotMatch && _player2 != null && (_player2.WantsToRestart || _player2.WantsToLeaveRoom);

                if (p1Surrender || p2Surrender)
                {
                    PlayerConnection leaver = p1Surrender ? _player1 : _player2;
                    PlayerConnection winner = p1Surrender ? _player2 : _player1;

                    leaver.WantsToRestart = true;
                    leaver.WantsToLeaveRoom = false;

                    string leaverName = leaver?.Name ?? "Игрок 1";
                    string winnerName = winner?.Name ?? "Игрок 2";

                    EndGame($"Игрок {leaverName} вышел в лобби. Победа {winnerName}!");
                }
            }
        }
        ///<summary>
        ///обработка завершения игры
        ///</summary>
        private void ProcessGameOver()
        {
            _state.IsPlayer1Ready = _player1?.IsReady == true;
            _state.IsPlayer2Ready = _isBotMatch ? _state.IsPlayer1Ready : (_player2?.IsReady == true);

            bool p1Lobby = _player1 != null && _player1.WantsToRestart;
            bool p2Lobby = !_isBotMatch && _player2 != null && _player2.WantsToRestart;

            if (p1Lobby && (p2Lobby || _isBotMatch))
            {
                ResetToRoomLobby();
                return;
            }

            if (p1Lobby && !p2Lobby)
            {
                _state.Message = $"Игрок {_player1.Name} вышел в лобби.";
            }
            else if (p2Lobby && !p1Lobby)
            {
                _state.Message = $"Игрок {_player2.Name} вышел в лобби.";
            }
            else if (_state.IsPlayer1Ready && !_state.IsPlayer2Ready)
            {
                _state.Message = $"Игрок {_player1.Name} готов играть еще раз.";
            }
            else if (_state.IsPlayer2Ready && !_state.IsPlayer1Ready)
            {
                _state.Message = $"Игрок {_player2.Name} готов играть еще раз.";
            }
        }
        ///<summary>
        ///рассылка состояния. асимметрия в случае если один в лобби, а второй на экране завершения матча
        ///</summary>
        private void BroadcastState()
        {
            _state.TopPlayers = _manager.Auth.GetTopPlayers();

            if (_player1 != null && _player1.IsConnected)
            {
                var p1State = _state;
                if (_state.Status == GameStatus.GameOver && _player1.WantsToRestart)
                {
                    p1State = System.Text.Json.JsonSerializer.Deserialize<GameState>(System.Text.Json.JsonSerializer.Serialize(_state));
                    p1State.Status = GameStatus.RoomLobby;
                }
                _ = _player1.SendStateAsync(p1State);
            }

            if (!_isBotMatch && _player2 != null && _player2.IsConnected)
            {
                var p2State = _state;
                if (_state.Status == GameStatus.GameOver && _player2.WantsToRestart)
                {
                    p2State = System.Text.Json.JsonSerializer.Deserialize<GameState>(System.Text.Json.JsonSerializer.Serialize(_state));
                    p2State.Status = GameStatus.RoomLobby;
                }
                _ = _player2.SendStateAsync(p2State);
            }
        }


        ///<summary>
        ///завершение матча
        ///</summary>
        private void EndGame(string message)
        {
            _state.IsGameOver = true;
            _state.Message = message;
            _state.Status = GameStatus.GameOver;
            _isLoopRunning = false;

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
            else if (_isBotMatch) _state.Player2Record = _state.Snake2.Count;
        }
        ///<summary>
        ///механики игры
        ///</summary>
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

            if (nextHead1.X == nextHead2.X && nextHead1.Y == nextHead2.Y) { p1Crashed = true; p2Crashed = true; }

            if (p1Crashed && p2Crashed) { EndGame("Ничья! Обе змейки разбились."); return; }
            if (p1Crashed) { EndGame("Победа Игрока 2 (Красный)! Синий врезался."); return; }
            if (p2Crashed) { EndGame("Победа Игрока 1 (Синий)! Красный врезался."); return; }

            ApplyMovement(_state.Snake1, nextHead1, ref _p1PendingGrowth);
            ApplyMovement(_state.Snake2, nextHead2, ref _p2PendingGrowth);
        }


        ///<summary>
        ///движение змейки
        ///</summary>
        private void ApplyMovement(List<Position> snake, Position nextHead, ref int pendingGrowth)
        {
            snake.Insert(0, nextHead);
            int foodIndex = _state.Food.FindIndex(f => f.Position.X == nextHead.X && f.Position.Y == nextHead.Y);

            if (foodIndex != -1)
            {
                var eatenFood = _state.Food[foodIndex];
                _state.Food.RemoveAt(foodIndex);

                if (eatenFood.Type == FoodType.Normal) { pendingGrowth += _normalFoodEffect - 1; _lastNormalEatTime = DateTime.Now; }
                else if (eatenFood.Type == FoodType.Gold) { pendingGrowth += _goldFoodEffect - 1; _lastGoldEatTime = DateTime.Now; }
                else if (eatenFood.Type == FoodType.Purple) { pendingGrowth += _purpleFoodEffect - 1; _lastPurpleEatTime = DateTime.Now; }
            }
            else pendingGrowth--;

            while (pendingGrowth < 0)
            {
                if (snake.Count > 3) { snake.RemoveAt(snake.Count - 1); pendingGrowth++; }
                else { pendingGrowth = 0; break; }
            }
        }
        ///<summary>
        ///расчет движения бота
        ///</summary>
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
            //проверка коллизии на следующем кадре
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
        ///<summary>
        ///проверка коллизий змейки и поля/змейки2
        ///</summary>
        private bool IsCollision(Position nextHead, List<Position> myBody, List<Position> enemyBody)
        {
            if (nextHead.X < 0 || nextHead.X >= _gridWidth || nextHead.Y < 0 || nextHead.Y >= _gridHeight) return true;
            for (int i = 0; i < myBody.Count - 1; i++) if (myBody[i].X == nextHead.X && myBody[i].Y == nextHead.Y) return true;
            foreach (var part in enemyBody) if (part.X == nextHead.X && part.Y == nextHead.Y) return true;
            return false;
        }
        ///<summary>
        ///позиция головы в следующий кадр
        ///</summary>
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
        ///<summary>
        ///проверка необходимости создать еду
        ///</summary>
        private void CheckAndSpawnFood(FoodType type, int maxCount, int delaySeconds, ref DateTime lastEatTime)
        {
            int currentCount = _state.Food.Count(f => f.Type == type);
            if (currentCount < maxCount && (DateTime.Now - lastEatTime).TotalSeconds >= delaySeconds)
            {
                SpawnSingleFood(type);
                lastEatTime = DateTime.Now;
            }
        }
        ///<summary>
        ///создание еды
        ///</summary>
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
        ///<summary>
        ///обновление игровых параметров, введенных в меню настроек
        ///</summary>
        private void ApplySettings(GameSettingsConfig config)
        {
            _state.Settings = config;
            _gridWidth = Math.Abs(config.GridWidth);
            _gridHeight = Math.Abs(config.GridHeight);
            _cellsPerSecond = Math.Abs(config.Speed);

            _maxNormalFood = Math.Abs(config.NormalFoodCount);
            _normalFoodEffect = config.NormalFoodEffect;
            _normalFoodSpawnDelay = Math.Abs(config.NormalFoodDelay);

            _maxGoldFood = Math.Abs(config.GoldFoodCount);
            _goldFoodEffect = config.GoldFoodEffect;
            _goldFoodSpawnDelay = Math.Abs(config.GoldFoodDelay);

            _maxPurpleFood = Math.Abs(config.PurpleFoodCount);
            _purpleFoodEffect = config.PurpleFoodEffect;
            _purpleFoodSpawnDelay = Math.Abs(config.PurpleFoodDelay);
        }
    }
}