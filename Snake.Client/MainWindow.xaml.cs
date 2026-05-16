using Snake.Shared.Enums;
using Snake.Shared.Models;
using Snake.Shared.Networking;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

namespace Snake.Client
{
    public partial class MainWindow : Window
    {
        private readonly NetworkClient _networkClient;

        private const int GridWidth = 40;
        private const int GridHeight = 30;
        private int _cellSize = 15;
        private bool _amIPlayer1 = false;

        // === ПЕРЕМЕННЫЕ ДЛЯ ЦВЕТОВ ИЗ ПАЛИТРЫ ===
        private Brush _colorSnake1;
        private Brush _colorSnake2;
        private Brush _colorFoodNormal;
        private Brush _colorFoodGold;
        private Brush _colorFoodPurple;
        private Brush _colorTextSuccess;
        private Brush _colorTextError;
        private Brush _colorTextInfo;

        public MainWindow()
        {
            InitializeComponent();

            // Загружаем цвета из нашей XAML-палитры в C# переменные
            LoadPalette();

            _networkClient = new NetworkClient();
            _networkClient.OnStateReceived += RenderGameState;
            _networkClient.OnDisconnected += msg => Dispatcher.Invoke(() =>
            {
                StatusText.Text = msg;
                StatusText.Foreground = _colorTextError;
                RestartBtn.Content = "Переподключить";
                RestartBtn.IsEnabled = true;
                GameCanvas.Children.Clear();
                LeaderboardPanel.Visibility = Visibility.Collapsed;
            });
        }

        private void LoadPalette()
        {
            _colorSnake1 = (Brush)FindResource("Snake1Color");
            _colorSnake2 = (Brush)FindResource("Snake2Color");
            _colorFoodNormal = (Brush)FindResource("FoodNormalColor");
            _colorFoodGold = (Brush)FindResource("FoodGoldColor");
            _colorFoodPurple = (Brush)FindResource("FoodPurpleColor");

            _colorTextSuccess = (Brush)FindResource("TextSuccess");
            _colorTextError = (Brush)FindResource("TextError");
            _colorTextInfo = (Brush)FindResource("TextInfo");
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CalculateDynamicScale();

            MainMenuPanel.Visibility = Visibility.Collapsed;
            RoomLobbyPanel.Visibility = Visibility.Collapsed;
            CountdownPanel.Visibility = Visibility.Collapsed;
            HostWaitingPanel.Visibility = Visibility.Collapsed;
            LeaderboardPanel.Visibility = Visibility.Collapsed;
            AuthPanel.Visibility = Visibility.Visible;

            await ConnectToServer();
        }

        private void CalculateDynamicScale()
        {
            double targetCanvasHeight = SystemParameters.PrimaryScreenHeight / 2.0;
            _cellSize = (int)(targetCanvasHeight / GridHeight);
            if (_cellSize < 5) _cellSize = 10;

            GameCanvas.Width = GridWidth * _cellSize;
            GameCanvas.Height = GridHeight * _cellSize;
            GameBorder.Width = GameCanvas.Width + 4;
            GameBorder.Height = GameCanvas.Height + 4;
        }

        private void RegisterBtn_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginInput.Text.Trim();
            string password = PasswordInput.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                AuthMessageText.Foreground = _colorTextError;
                AuthMessageText.Text = "Логин и пароль не могут быть пустыми.";
                return;
            }

            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Register, PlayerName = login, Password = password });
        }

        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginInput.Text.Trim();
            string password = PasswordInput.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                AuthMessageText.Foreground = _colorTextError;
                AuthMessageText.Text = "Логин и пароль не могут быть пустыми.";
                return;
            }

            PlayerNameInput.Text = login;
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Login, PlayerName = login, Password = password });
        }

        private async Task ConnectToServer()
        {
            StatusText.Text = "Поиск сервера 127.0.0.1:5000...";
            StatusText.Foreground = (Brush)FindResource("TextPrimary");
            RestartBtn.IsEnabled = false;

            bool isConnected = await _networkClient.ConnectAsync("127.0.0.1", 5000);
            if (isConnected)
            {
                StatusText.Text = "Соединение установлено. Пожалуйста, авторизуйтесь.";
                StatusText.Foreground = _colorTextInfo;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.LeaveRoom });
                return;
            }

            if (MainMenuPanel.Visibility == Visibility.Visible || CountdownPanel.Visibility == Visibility.Visible || RoomLobbyPanel.Visibility == Visibility.Visible || AuthPanel.Visibility == Visibility.Visible)
                return;

            Direction? dir = e.Key switch
            {
                Key.Up or Key.W => Direction.Up,
                Key.Down or Key.S => Direction.Down,
                Key.Left or Key.A => Direction.Left,
                Key.Right or Key.D => Direction.Right,
                _ => null
            };

            if (dir.HasValue) _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Move, Direction = dir.Value });
        }

        private void RenderGameState(GameState state)
        {
            Dispatcher.Invoke(() =>
            {
                if (PlayerNameInput.Text == state.Player1Name) _amIPlayer1 = true;
                else if (PlayerNameInput.Text == state.Player2Name) _amIPlayer1 = false;

                if (state.Status == GameStatus.AuthScreen || state.Status == GameStatus.Playing || state.Status == GameStatus.Countdown)
                {
                    LeaderboardPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    LeaderboardPanel.Visibility = Visibility.Visible;
                    LeaderboardList.ItemsSource = state.TopPlayers;
                }

                if (state.Status == GameStatus.AuthScreen)
                {
                    AuthPanel.Visibility = Visibility.Visible;
                    MainMenuPanel.Visibility = Visibility.Collapsed;
                    HostWaitingPanel.Visibility = Visibility.Collapsed;
                    RoomLobbyPanel.Visibility = Visibility.Collapsed;
                    CountdownPanel.Visibility = Visibility.Collapsed;

                    StatusText.Text = "Авторизация";
                    AuthMessageText.Text = state.Message;

                    if (state.Message.Contains("успешна") || state.Message.Contains("выполнен"))
                        AuthMessageText.Foreground = _colorTextSuccess;
                    else
                        AuthMessageText.Foreground = _colorTextError;
                }
                else if (state.Status == GameStatus.MainMenu)
                {
                    AuthPanel.Visibility = Visibility.Collapsed;
                    MainMenuPanel.Visibility = Visibility.Visible;
                    HostWaitingPanel.Visibility = Visibility.Collapsed;
                    RoomLobbyPanel.Visibility = Visibility.Collapsed;
                    CountdownPanel.Visibility = Visibility.Collapsed;

                    StatusText.Text = "Главное меню";

                    var currentList = LobbiesList.ItemsSource as List<PlayerInfo>;
                    bool needsUpdate = currentList == null || currentList.Count != state.AvailablePlayers.Count;

                    if (!needsUpdate)
                    {
                        for (int i = 0; i < currentList.Count; i++)
                        {
                            if (currentList[i].Id != state.AvailablePlayers[i].Id || currentList[i].Name != state.AvailablePlayers[i].Name)
                            {
                                needsUpdate = true;
                                break;
                            }
                        }
                    }

                    if (needsUpdate) LobbiesList.ItemsSource = state.AvailablePlayers;
                }
                else if (state.Status == GameStatus.HostWaiting)
                {
                    AuthPanel.Visibility = Visibility.Collapsed;
                    MainMenuPanel.Visibility = Visibility.Collapsed;
                    HostWaitingPanel.Visibility = Visibility.Visible;
                    RoomLobbyPanel.Visibility = Visibility.Collapsed;
                    CountdownPanel.Visibility = Visibility.Collapsed;

                    StatusText.Text = "Создано лобби";
                }
                else if (state.Status == GameStatus.RoomLobby)
                {
                    AuthPanel.Visibility = Visibility.Collapsed;
                    MainMenuPanel.Visibility = Visibility.Collapsed;
                    HostWaitingPanel.Visibility = Visibility.Collapsed;
                    CountdownPanel.Visibility = Visibility.Collapsed;

                    bool amIReady = _amIPlayer1 ? state.IsPlayer1Ready : state.IsPlayer2Ready;
                    RoomLobbyPanel.Visibility = amIReady ? Visibility.Collapsed : Visibility.Visible;

                    StatusText.Text = string.IsNullOrEmpty(state.Message) ? $"{state.Player1Name} vs {state.Player2Name}" : state.Message;

                    if (RestartBtn.Content.ToString() != "Переподключить")
                    {
                        RestartBtn.Content = "Начать заново";
                        RestartBtn.IsEnabled = false;
                    }
                }
                else if (state.Status == GameStatus.Countdown)
                {
                    CountdownPanel.Visibility = Visibility.Visible;
                    CountdownText.Text = state.CountdownValue.ToString();
                    StatusText.Text = $"{state.Player1Name} vs {state.Player2Name}";
                }
                else if (state.Status == GameStatus.Playing)
                {
                    CountdownPanel.Visibility = Visibility.Collapsed;

                    int minutes = state.MatchTimer / 60;
                    int seconds = state.MatchTimer % 60;

                    StatusText.Text = $"🟦 {state.Player1Name}: {state.Snake1.Count}   |   ⏱️ {minutes:D2}:{seconds:D2}   |   🟥 {state.Player2Name}: {state.Snake2.Count}";
                }
                else if (state.Status == GameStatus.GameOver)
                {
                    StatusText.Text = $"{state.Player1Name} vs {state.Player2Name} | {state.Message}";

                    if (RestartBtn.Content.ToString() != "Ожидание...")
                    {
                        RestartBtn.Content = "В лобби";
                        RestartBtn.IsEnabled = true;
                    }
                }

                GameCanvas.Children.Clear();
                foreach (var pos in state.Snake1) DrawRect(pos, _colorSnake1);
                foreach (var pos in state.Snake2) DrawRect(pos, _colorSnake2);

                foreach (var apple in state.Food)
                {
                    Brush appleColor = _colorFoodNormal;

                    if (apple.Type == FoodType.Gold)
                        appleColor = _colorFoodGold;
                    else if (apple.Type == FoodType.Purple)
                        appleColor = _colorFoodPurple;

                    DrawRect(apple.Position, appleColor);
                }
            });
        }

        private void DrawRect(Position pos, Brush color)
        {
            var rect = new Rectangle { Width = _cellSize, Height = _cellSize, Fill = color, Margin = new Thickness(1) };
            Canvas.SetLeft(rect, pos.X * _cellSize);
            Canvas.SetTop(rect, pos.Y * _cellSize);
            GameCanvas.Children.Add(rect);
        }

        private void CreateLobbyBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.CreateLobby, PlayerName = PlayerNameInput.Text });
        }

        private void JoinLobbyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string targetId)
            {
                _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.JoinLobby, PlayerName = PlayerNameInput.Text, TargetId = targetId });
            }
        }

        private void StartGameBtn_Click(object sender, RoutedEventArgs e)
        {
            RoomLobbyPanel.Visibility = Visibility.Collapsed;
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Ready });
            this.Focus();
        }

        private void LeaveRoomBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.LeaveRoom });
        }

        private void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            PlayerNameInput.Text = "";
            PasswordInput.Password = "";
            _ = ConnectToServer();
        }

        private async void RestartBtn_Click(object sender, RoutedEventArgs e)
        {
            RestartBtn.IsEnabled = false;
            if (RestartBtn.Content.ToString() == "Переподключить")
            {
                RestartBtn.Content = "Ожидание...";
                await ConnectToServer();
            }
            else
            {
                _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Restart });
                RestartBtn.Content = "Ожидание...";
                this.Focus();
            }
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}