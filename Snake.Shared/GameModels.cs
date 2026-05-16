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

        public string Player1Name { get; set; } = string.Empty;
        public string Player2Name { get; set; } = string.Empty;

        public bool IsPlayer1Ready { get; set; }
        public bool IsPlayer2Ready { get; set; }

        public List<Position> Snake1 { get; set; } = new List<Position>();
        public List<Position> Snake2 { get; set; } = new List<Position>();

        // ОБНОВЛЕНО: Теперь список хранит объекты FoodItem, а не просто координаты
        public List<FoodItem> Food { get; set; } = new List<FoodItem>();

        public bool IsGameOver { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<PlayerInfo> AvailablePlayers { get; set; } = new List<PlayerInfo>();
        public List<LeaderboardEntry> TopPlayers { get; set; } = new List<LeaderboardEntry>();
    }
}

namespace Snake.Shared.Networking
{
    using Snake.Shared.Enums;

    public enum ActionType
    {
        Move, Restart, Ready, CreateLobby, JoinLobby, LeaveRoom, Login, Register
    }

    public class InputUpdate
    {
        public ActionType Action { get; set; } = ActionType.Move;
        public Direction Direction { get; set; }
        public string PlayerName { get; set; }
        public string Password { get; set; }
        public string TargetId { get; set; }
    }
}