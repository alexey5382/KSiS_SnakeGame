using System.Collections.Generic;

namespace Snake.Shared.Enums
{
    public enum Direction { Up, Down, Left, Right }

    // НОВОЕ: Перечисление типов яблок
    public enum FoodType { Normal, Gold, Purple }

    public enum GameStatus
    {
        AuthScreen,
        MainMenu,
        HostWaiting,
        RoomLobby,
        Countdown,
        Playing,
        GameOver
    }
}

namespace Snake.Shared.Models
{
    using Snake.Shared.Enums;

    public struct Position
    {
        public int X { get; set; }
        public int Y { get; set; }
        public Position(int x, int y) { X = x; Y = y; }
        public override string ToString() => $"({X}, {Y})";
    }

    public class PlayerInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class LeaderboardEntry
    {
        public string Name { get; set; }
        public int Score { get; set; }
    }

    // НОВОЕ: Класс для яблока (хранит координаты и тип)
    public class FoodItem
    {
        public Position Position { get; set; }
        public FoodType Type { get; set; }
    }

    public class GameState
    {
        public GameStatus Status { get; set; } = GameStatus.AuthScreen;
        public int CountdownValue { get; set; }
        public int MatchTimer { get; set; }

        public string LobbyName { get; set; } = string.Empty;
        public string Player1Name { get; set; } = string.Empty;
        public string Player2Name { get; set; } = string.Empty;

        public bool IsPlayer1Ready { get; set; }
        public bool IsPlayer2Ready { get; set; }
        public int Player1Record { get; set; }
        public int Player2Record { get; set; }

        public List<Position> Snake1 { get; set; } = new List<Position>();
        public List<Position> Snake2 { get; set; } = new List<Position>();
        public List<FoodItem> Food { get; set; } = new List<FoodItem>();

        public bool IsGameOver { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<PlayerInfo> AvailablePlayers { get; set; } = new List<PlayerInfo>();
        public List<LeaderboardEntry> TopPlayers { get; set; } = new List<LeaderboardEntry>();

        // НОВОЕ: Текущие настройки комнаты
        public GameSettingsConfig Settings { get; set; } = new GameSettingsConfig();
    }
    public class GameSettingsConfig
    {
        public int GridWidth { get; set; } = 40;
        public int GridHeight { get; set; } = 30;
        public double Speed { get; set; } = 4.0;

        public int NormalFoodCount { get; set; } = 3;
        public int NormalFoodEffect { get; set; } = 1;
        public int NormalFoodDelay { get; set; } = 0;

        public int GoldFoodCount { get; set; } = 1;
        public int GoldFoodEffect { get; set; } = 3;
        public int GoldFoodDelay { get; set; } = 10;

        public int PurpleFoodCount { get; set; } = 2;
        public int PurpleFoodEffect { get; set; } = -1;
        public int PurpleFoodDelay { get; set; } = 5;
    }
}

namespace Snake.Shared.Networking
{
    using Snake.Shared.Enums;
    using Snake.Shared.Models; // Для доступа к GameSettingsConfig

    public enum ActionType
    {
        Move, Restart, Ready, CreateLobby, JoinLobby, LeaveRoom, Login, Register, UpdateInfo,
        UpdateSettings // НОВОЕ: Действие для изменения настроек
    }

    public class InputUpdate
    {
        public ActionType Action { get; set; } = ActionType.Move;
        public Direction Direction { get; set; }
        public string PlayerName { get; set; }
        public string Password { get; set; }
        public string TargetId { get; set; }
        public string NewPlayerName { get; set; }
        public string LobbyName { get; set; }

        // НОВОЕ: Пакет с настройками
        public GameSettingsConfig NewSettings { get; set; }
    }
}