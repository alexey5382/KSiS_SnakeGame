using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Snake.Server
{
    public class UserAccount
    {
        public string Login { get; set; }
        public string Password { get; set; }
        public int MaxScore { get; set; }
    }

    public class AuthManager
    {
        private List<UserAccount> _users = new List<UserAccount>();
        private readonly HashSet<string> _onlineUsers = new HashSet<string>();
        private readonly object _lock = new object();
        private readonly string _filePath = "users.json";

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
                    _users = JsonSerializer.Deserialize<List<UserAccount>>(json) ?? new List<UserAccount>();
                }
                catch
                {
                    _users = new List<UserAccount>();
                }
            }
            else
            {
                _users = new List<UserAccount>();
            }
        }

        private void SaveUsers()
        {
            lock (_lock)
            {
                string json = JsonSerializer.Serialize(_users, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
        }

        public bool Register(string login, string password, out string msg)
        {
            lock (_lock)
            {
                if (_users.Any(u => u.Login.Equals(login, StringComparison.OrdinalIgnoreCase)))
                {
                    msg = "Ошибка: Такой логин уже существует!";
                    return false;
                }

                _users.Add(new UserAccount { Login = login, Password = password, MaxScore = 0 });
                SaveUsers(); // Сохраняем изменения в файл

                msg = "Регистрация успешна! Теперь войдите.";
                return true;
            }
        }

        public bool Login(string login, string password, out string msg)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Login.Equals(login, StringComparison.OrdinalIgnoreCase));

                if (user == null || user.Password != password)
                {
                    msg = "Ошибка: Неверный логин или пароль.";
                    return false;
                }

                if (_onlineUsers.Contains(user.Login))
                {
                    msg = "Ошибка: Этот аккаунт уже находится в игре!";
                    return false;
                }

                _onlineUsers.Add(user.Login);
                msg = "Авторизация успешна.";
                return true;
            }
        }

        public void LogoutUser(string login)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(login))
                {
                    _onlineUsers.Remove(login);
                }
            }
        }

        public void UpdateMaxScore(string login, int score)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                if (user != null && score > user.MaxScore)
                {
                    user.MaxScore = score;
                    SaveUsers(); // Сохраняем обновленный рекорд в файл
                }
            }
        }

        public int GetUserRecord(string login)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Login.Equals(login, StringComparison.OrdinalIgnoreCase));
                return user?.MaxScore ?? 0;
            }
        }

        public List<Shared.Models.LeaderboardEntry> GetTopPlayers()
        {
            lock (_lock)
            {
                return _users
                    .OrderByDescending(u => u.MaxScore)
                    .Take(5)
                    .Select(u => new Shared.Models.LeaderboardEntry { Name = u.Login, Score = u.MaxScore })
                    .ToList();
            }
        }
    }
}