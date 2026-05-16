using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Snake.Shared.Models; // Для доступа к LeaderboardEntry

namespace Snake.Server
{
    public class UserData
    {
        public string Login { get; set; }
        public string PasswordHash { get; set; }
        public int MaxScore { get; set; } = 0; // НОВОЕ ПОЛЕ: Максимальная длина змейки
    }

    public class AuthManager
    {
        private readonly string _filePath = "users.json";
        private List<UserData> _users;

        public AuthManager()
        {
            LoadUsers();
        }

        private void LoadUsers()
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    string json = File.ReadAllText(_filePath);
                    _users = JsonSerializer.Deserialize<List<UserData>>(json) ?? new List<UserData>();
                }
                catch
                {
                    _users = new List<UserData>();
                }
            }
            else
            {
                _users = new List<UserData>();
            }
        }

        private void SaveUsers()
        {
            string json = JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }

        // НОВЫЙ МЕТОД: Обновление рекорда длины змейки
        public void UpdateMaxScore(string login, int score)
        {
            var user = _users.Find(u => u.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
            if (user != null && score > user.MaxScore)
            {
                user.MaxScore = score;
                SaveUsers();
            }
        }

        // НОВЫЙ МЕТОД: Получение 5 лучших игроков
        public List<LeaderboardEntry> GetTopPlayers()
        {
            return _users
                .OrderByDescending(u => u.MaxScore)
                .Take(5)
                .Select(u => new LeaderboardEntry { Name = u.Login, Score = u.MaxScore })
                .ToList();
        }

        public bool Register(string login, string password, out string message)
        {
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                message = "Логин и пароль не могут быть пустыми.";
                return false;
            }

            if (_users.Exists(u => u.Login.Equals(login, StringComparison.OrdinalIgnoreCase)))
            {
                message = "Пользователь с таким логином уже существует.";
                return false;
            }

            _users.Add(new UserData
            {
                Login = login,
                PasswordHash = HashPassword(password),
                MaxScore = 0
            });

            SaveUsers();
            message = "Регистрация успешна!";
            return true;
        }

        public bool Login(string login, string password, out string message)
        {
            var user = _users.Find(u => u.Login.Equals(login, StringComparison.OrdinalIgnoreCase));

            if (user == null)
            {
                message = "Пользователь не найден.";
                return false;
            }

            if (user.PasswordHash != HashPassword(password))
            {
                message = "Неверный пароль.";
                return false;
            }

            message = "Вход выполнен.";
            return true;
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (var b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}