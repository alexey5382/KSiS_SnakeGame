using Snake.Shared.Enums;
using Snake.Shared.Models;
using Snake.Shared.Networking;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Security.Cryptography;
using System.Text;

namespace Snake.Client
{
    public partial class MainWindow : Window
    {
        private readonly NetworkClient _networkClient;

        private int _gridWidth = 40;
        private int _gridHeight = 30;
        private int _cellSize = 15;
        private bool _amIPlayer1 = false;

        private Brush _colorSnake1;
        private Brush _colorSnake2;
        private Brush _colorFoodNormal;
        private Brush _colorFoodGold;
        private Brush _colorFoodPurple;
        private Brush _colorTextSuccess;
        private Brush _colorTextError;
        private Brush _colorTextInfo;
        private Brush _colorTextSecondary;

        public MainWindow()
        {
            InitializeComponent();
            LoadPalette();

            _networkClient = new NetworkClient();
            _networkClient.OnStateReceived += RenderGameState;

            _networkClient.OnDisconnected += msg => Dispatcher.Invoke(() =>
            {
                GlobalStatusText.Text = msg;
                GlobalStatusText.Foreground = _colorTextError;
                GameCanvas.Children.Clear();

                AuthPanel.Visibility = Visibility.Visible;
                MainMenuPanel.Visibility = Visibility.Collapsed;
                RoomLobbyPanel.Visibility = Visibility.Collapsed;
                GamePanel.Visibility = Visibility.Collapsed;
                SettingsPanel.Visibility = Visibility.Collapsed;
                StatsPanel.Visibility = Visibility.Collapsed;
            });
        }
        ///<summary>
        ///загрузка цветов
        ///</summary>
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
            _colorTextSecondary = (Brush)FindResource("TextSecondary");
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            CalculateDynamicScale();

            MainMenuPanel.Visibility = Visibility.Collapsed;
            RoomLobbyPanel.Visibility = Visibility.Collapsed;
            GamePanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            StatsPanel.Visibility = Visibility.Collapsed;

            AuthPanel.Visibility = Visibility.Visible;
            GlobalStatusText.Text = "Инициализация...";

            await Task.Delay(100);
            await ConnectToServer();
        }
        ///<summary>
        ///расчет размера окна
        ///</summary>
        private void CalculateDynamicScale()
        {
            double targetCanvasHeight = SystemParameters.PrimaryScreenHeight / 2.0;
            if (targetCanvasHeight <= 0) targetCanvasHeight = 400;

            _cellSize = (int)(targetCanvasHeight / _gridHeight);
            if (_cellSize < 10) _cellSize = 15;

            GameCanvas.Width = _gridWidth * _cellSize;
            GameCanvas.Height = _gridHeight * _cellSize;
            GameBorder.Width = GameCanvas.Width + 4;
            GameBorder.Height = GameCanvas.Height + 4;

            this.Width = 220 + 20 + GameBorder.Width + 40;
            this.Height = GameBorder.Height + 160;

            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;
        }
        ///<summary>
        ///обработка адреса и подключение к серверу
        ///</summary>
        private async Task ConnectToServer(string manualIp = null)
        {
            GlobalStatusText.Text = "Поиск сервера...";
            GlobalStatusText.Foreground = _colorTextSecondary;

            string serverIp = manualIp;

            if (string.IsNullOrWhiteSpace(serverIp))
            {
                serverIp = await _networkClient.DiscoverServerAsync();
            }

            if (string.IsNullOrEmpty(serverIp))
            {
                serverIp = "127.0.0.1";
                GlobalStatusText.Text = "Сервер не найден в LAN. Пробуем 127.0.0.1...";
            }
            else
            {
                GlobalStatusText.Text = $"Подключение к: {serverIp}...";
            }

            try
            {
                // Подключаемся к итоговому IP-адресу
                bool isConnected = await _networkClient.ConnectAsync(serverIp, 5000);

                if (isConnected)
                {
                    GlobalStatusText.Text = $"Подключено к серверу ({serverIp}). Пожалуйста, авторизуйтесь.";
                    GlobalStatusText.Foreground = _colorTextInfo;
                }
                else
                {
                    GlobalStatusText.Text = "Сервер недоступен.";
                    GlobalStatusText.Foreground = _colorTextError;
                }
            }
            catch
            {
                GlobalStatusText.Text = "Ошибка сети: Сервер выключен.";
                GlobalStatusText.Foreground = _colorTextError;
            }
        }
        ///<summary>
        ///обработка управления змейкой
        ///</summary>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Restart });
                return;
            }

            if (GamePanel.Visibility != Visibility.Visible) return;

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
        ///<summary>
        ///главный метод отрисовки окна
        ///</summary>
        private void RenderGameState(GameState state)
        {
            Dispatcher.Invoke(() =>
            {
                //обновление настроек, если параметры были изменены
                if (state.Settings != null && (_gridWidth != state.Settings.GridWidth || _gridHeight != state.Settings.GridHeight))
                {
                    _gridWidth = state.Settings.GridWidth;
                    _gridHeight = state.Settings.GridHeight;
                    CalculateDynamicScale();
                }
                //скрытие неактивных панелей
                AuthPanel.Visibility = Visibility.Collapsed;
                MainMenuPanel.Visibility = Visibility.Collapsed;
                RoomLobbyPanel.Visibility = Visibility.Collapsed;
                GamePanel.Visibility = Visibility.Collapsed;
                StatsPanel.Visibility = Visibility.Collapsed;
                //обработка меню авторизации
                if (state.Status == GameStatus.AuthScreen)
                {
                    AuthPanel.Visibility = Visibility.Visible;
                    GlobalStatusText.Text = "Авторизация";
                    AuthMessageText.Text = state.Message;
                    AuthMessageText.Foreground = (state.Message.Contains("успешна") || state.Message.Contains("выполнен")) ? _colorTextSuccess : _colorTextError;
                }
                //обработка главного меню
                else if (state.Status == GameStatus.MainMenu)
                {
                    MainMenuPanel.Visibility = Visibility.Visible;
                    GlobalStatusText.Text = "Глобальное лобби";

                    // изменение топа, только если были изменения
                    var currentTop = LeaderboardList.ItemsSource as List<LeaderboardEntry>;
                    bool topChanged = currentTop == null || currentTop.Count != state.TopPlayers.Count;
                    if (!topChanged)
                    {
                        for (int i = 0; i < currentTop.Count; i++)
                            if (currentTop[i].Name != state.TopPlayers[i].Name || currentTop[i].Score != state.TopPlayers[i].Score) { topChanged = true; break; }
                    }
                    if (topChanged) LeaderboardList.ItemsSource = state.TopPlayers;

                    var realLobbies = new List<PlayerInfo>();
                    if (state.AvailablePlayers != null)
                    {
                        foreach (var lobby in state.AvailablePlayers)
                            if (lobby.Id != "BOT_ID") realLobbies.Add(lobby);
                    }
                    //обработка списка комнат
                    if (realLobbies.Count == 0)
                    {
                        EmptyLobbyText.Visibility = Visibility.Visible;
                        LobbiesList.Visibility = Visibility.Collapsed;
                        LobbiesList.ItemsSource = null;
                    }
                    //если существуют свободные комнаты
                    else
                    {
                        EmptyLobbyText.Visibility = Visibility.Collapsed;
                        LobbiesList.Visibility = Visibility.Visible;

                        var currentLobbies = LobbiesList.ItemsSource as List<PlayerInfo>;
                        bool lobbiesChanged = currentLobbies == null || currentLobbies.Count != realLobbies.Count;
                        if (!lobbiesChanged)
                        {
                            for (int i = 0; i < currentLobbies.Count; i++)
                                if (currentLobbies[i].Id != realLobbies[i].Id || currentLobbies[i].Name != realLobbies[i].Name) { lobbiesChanged = true; break; }
                        }
                        if (lobbiesChanged) LobbiesList.ItemsSource = realLobbies;
                    }
                }
                //обработка локального лобби
                else if (state.Status == GameStatus.RoomLobby || state.Status == GameStatus.HostWaiting)
                {
                    RoomLobbyPanel.Visibility = Visibility.Visible;
                    LobbyLeaderboardList.ItemsSource = state.TopPlayers;
                    //сообщение статуса
                    if (!string.IsNullOrEmpty(state.Message))
                    {
                        GlobalStatusText.Text = state.Message;
                        GlobalStatusText.Foreground = _colorTextInfo;
                    }
                    else
                    {
                        GlobalStatusText.Text = "Локальное лобби";
                        GlobalStatusText.Foreground = _colorTextSecondary;
                    }

                    //поля ввода имени лобби и игроков
                    if (!LobbyNameInput.IsFocused) LobbyNameInput.Text = state.LobbyName;
                    if (!LobbyPlayer1Input.IsFocused) LobbyPlayer1Input.Text = state.Player1Name;
                    if (!LobbyPlayer2Input.IsFocused) LobbyPlayer2Input.Text = state.Player2Name;

                    bool isHost = _amIPlayer1;
                    LobbyNameInput.IsReadOnly = !isHost;
                    LobbyPlayer1Input.IsReadOnly = !isHost;
                    LobbyPlayer2Input.IsReadOnly = isHost;
                    SettingsBtn.Visibility = isHost ? Visibility.Visible : Visibility.Collapsed;

                    bool amIReady = _amIPlayer1 ? state.IsPlayer1Ready : state.IsPlayer2Ready;
                    StartGameBtn.Content = amIReady ? "Отмена" : "Играть";
                    StartGameBtn.IsEnabled = true;
                }
                //обработка игрового поля
                else if (state.Status == GameStatus.Countdown || state.Status == GameStatus.Playing || state.Status == GameStatus.GameOver)
                {
                    GamePanel.Visibility = Visibility.Visible;
                    int minutes = state.MatchTimer / 60;
                    int seconds = state.MatchTimer % 60;
                    //обратный отсчет
                    if (state.Status == GameStatus.Countdown)
                    {
                        CountdownPanel.Visibility = Visibility.Visible;
                        StatsPanel.Visibility = Visibility.Collapsed;
                        CountdownText.Text = state.CountdownValue.ToString();
                        GlobalStatusText.Text = "Приготовьтесь к старту";
                        GlobalStatusText.Foreground = _colorTextInfo;
                    }
                    //игровой процесс
                    else if (state.Status == GameStatus.Playing)
                    {
                        CountdownPanel.Visibility = Visibility.Collapsed;
                        StatsPanel.Visibility = Visibility.Collapsed;
                        GlobalStatusText.Text = "Матч активен";
                        GlobalStatusText.Foreground = _colorTextSuccess;
                    }
                    //завершение игры
                    else if (state.Status == GameStatus.GameOver)
                    {
                        GamePanel.Visibility = Visibility.Visible;

                        CountdownPanel.Visibility = Visibility.Collapsed;
                        bool amIReady = _amIPlayer1 ? state.IsPlayer1Ready : state.IsPlayer2Ready;

                        StatsPanel.Visibility = Visibility.Visible;
                        GlobalStatusText.Text = "Матч завершен";
                        GlobalStatusText.Foreground = _colorTextSecondary;

                        bool isOtherReady = _amIPlayer1 ? state.IsPlayer2Ready : state.IsPlayer1Ready;
                        string otherName = _amIPlayer1 ? state.Player2Name : state.Player1Name;
                        //обработка состояний готовности
                        if (amIReady && !isOtherReady && !state.Message.Contains("вышел в лобби"))
                        {
                            StatsStatusText.Text = "Ожидание ответа соперника...";
                            StatsStatusText.Visibility = Visibility.Visible;
                            StatsRematchBtn.IsEnabled = false;
                        }
                        else if (isOtherReady)
                        {
                            StatsStatusText.Text = $"Игрок {otherName} ожидает реванша!";
                            StatsStatusText.Visibility = Visibility.Visible;
                            StatsRematchBtn.IsEnabled = true;
                        }
                        else if (state.Message != null && state.Message.Contains("вышел в лобби"))
                        {
                            StatsStatusText.Text = state.Message;
                            StatsStatusText.Visibility = Visibility.Visible;
                            StatsRematchBtn.IsEnabled = false;
                        }
                        else
                        {
                            StatsStatusText.Text = state.Message;
                            StatsStatusText.Visibility = Visibility.Visible;
                            StatsRematchBtn.IsEnabled = true;
                        }

                        // меню статистики матча
                        StatsTimerText.Text = $"Время матча: {minutes:D2}:{seconds:D2}";
                        StatsP1Name.Text = state.Player1Name;
                        StatsP1Score.Text = state.Snake1.Count.ToString();
                        StatsP1Record.Text = state.Player1Record.ToString();

                        StatsP2Name.Text = state.Player2Name;
                        StatsP2Score.Text = state.Snake2.Count.ToString();
                        StatsP2Record.Text = state.Player2Record.ToString();
                    }

                    GameTimerText.Text = $"{minutes:D2}:{seconds:D2}";
                    GamePlayer1Name.Text = state.Player1Name;
                    GamePlayer1Score.Text = state.Snake1.Count.ToString();
                    GamePlayer2Name.Text = state.Player2Name;
                    GamePlayer2Score.Text = state.Snake2.Count.ToString();
                }
                //отрисовка змеек и яблок на поле
                if (GamePanel.Visibility == Visibility.Visible)
                {
                    GameCanvas.Children.Clear();
                    foreach (var pos in state.Snake1) DrawRect(pos, _colorSnake1);
                    foreach (var pos in state.Snake2) DrawRect(pos, _colorSnake2);

                    foreach (var apple in state.Food)
                    {
                        Brush appleColor = _colorFoodNormal;
                        if (apple.Type == FoodType.Gold) appleColor = _colorFoodGold;
                        else if (apple.Type == FoodType.Purple) appleColor = _colorFoodPurple;
                        DrawRect(apple.Position, appleColor);
                    }
                }
            });
        }
        ///<summary>
        ///отрисовка квадратов (яблоки, сегменты змеек)
        ///</summary>
        private void DrawRect(Position pos, Brush color)
        {
            var rect = new Rectangle { Width = _cellSize, Height = _cellSize, Fill = color, Margin = new Thickness(1) };
            Canvas.SetLeft(rect, pos.X * _cellSize);
            Canvas.SetTop(rect, pos.Y * _cellSize);
            GameCanvas.Children.Add(rect);
        }

        private void ExitBtn_Click(object sender, RoutedEventArgs e)
        {
            _networkClient.Disconnect();
            Application.Current.Shutdown();
        }
        private void RegisterBtn_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginInput.Text.Trim();
            string rawPassword = PasswordInput.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(rawPassword))
            {
                AuthMessageText.Foreground = _colorTextError;
                AuthMessageText.Text = "Логин и пароль не могут быть пустыми.";
                return;
            }

            string hashedPassword = HashPassword(rawPassword);
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Register, PlayerName = login, Password = hashedPassword });
        }

        private void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            string login = LoginInput.Text.Trim();
            string rawPassword = PasswordInput.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(rawPassword))
            {
                AuthMessageText.Foreground = _colorTextError;
                AuthMessageText.Text = "Логин и пароль не могут быть пустыми.";
                return;
            }

            if (string.IsNullOrWhiteSpace(PlayerNameInput.Text)) PlayerNameInput.Text = login;

            string hashedPassword = HashPassword(rawPassword);
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Login, PlayerName = login, Password = hashedPassword });
        }

        private void LogoutBtn_Click(object sender, RoutedEventArgs e)
        {
            PlayerNameInput.Text = "";
            PasswordInput.Password = "";
            _networkClient.Disconnect();
            _ = ConnectToServer();
        }

        private void CreateLobbyBtn_Click(object sender, RoutedEventArgs e)
        {
            _amIPlayer1 = true;
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.CreateLobby, PlayerName = PlayerNameInput.Text });
        }

        private void JoinLobbyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                _amIPlayer1 = false;
                _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.JoinLobby, PlayerName = PlayerNameInput.Text, TargetId = btn.Tag.ToString() });
            }
        }

        private void PlayWithBotBtn_Click(object sender, RoutedEventArgs e)
        {
            _amIPlayer1 = true;
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.JoinLobby, PlayerName = PlayerNameInput.Text, TargetId = "BOT_ID" });
        }

        private void StartGameBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Ready });
            this.Focus();
        }

        private void LeaveRoomBtn_Click(object sender, RoutedEventArgs e)
        {
                _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.LeaveRoom });
        }

        private void StatsLobbyBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Restart });
        }

        private void StatsRematchBtn_Click(object sender, RoutedEventArgs e)
        {
            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.Ready });
        }

        private void LobbyInfo_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RoomLobbyPanel.Visibility != Visibility.Visible) return;
            string newName = _amIPlayer1 ? LobbyPlayer1Input.Text : LobbyPlayer2Input.Text;
            string lobbyName = _amIPlayer1 ? LobbyNameInput.Text : null;

            if (!string.IsNullOrWhiteSpace(newName)) PlayerNameInput.Text = newName;

            _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.UpdateInfo, NewPlayerName = newName, LobbyName = lobbyName });
        }
        private void ToggleIpBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ManualIpPanel.Visibility == Visibility.Visible)
            {
                ManualIpPanel.Visibility = Visibility.Collapsed;
                ToggleIpBtn.Content = "Настройки IP";
            }
            else
            {
                ManualIpPanel.Visibility = Visibility.Visible;
                ToggleIpBtn.Content = "Скрыть IP";
            }
        }

        private async void ReconnectServerBtn_Click(object sender, RoutedEventArgs e)
        {
            _networkClient.Disconnect();

            string ipToConnect = (ManualIpPanel.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(ServerIpInput.Text))
                ? ServerIpInput.Text.Trim()
                : null;

            await ConnectToServer(ipToConnect);
        }

        private async void ManualConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            _networkClient.Disconnect();
            await ConnectToServer(ServerIpInput.Text.Trim());
        }
        ///<summary>
        ///открыть настройки параметров игры
        ///</summary>
        private void OpenSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Visible;
            RoomLobbyPanel.IsEnabled = false;
        }
        ///<summary>
        ///сброс параметров игры в настройках
        ///</summary>
        private void ResetSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            SetWidthInput.Text = "40";
            SetHeightInput.Text = "30";
            SetSpeedInput.Text = "4";
            SetNormCount.Text = "3";
            SetNormEffect.Text = "1";
            SetNormDelay.Text = "0";
            SetGoldCount.Text = "1";
            SetGoldEffect.Text = "3";
            SetGoldDelay.Text = "10";
            SetPurpCount.Text = "2";
            SetPurpEffect.Text = "-1";
            SetPurpDelay.Text = "5";
            SettingsErrorText.Text = string.Empty;
        }
        ///<summary>
        ///применить параметры игры в настройках
        ///</summary>
        private void ApplySettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = new GameSettingsConfig
                {
                    GridWidth = int.Parse(SetWidthInput.Text),
                    GridHeight = int.Parse(SetHeightInput.Text),
                    Speed = double.Parse(SetSpeedInput.Text.Replace(".", ",")),
                    NormalFoodCount = int.Parse(SetNormCount.Text),
                    NormalFoodEffect = int.Parse(SetNormEffect.Text),
                    NormalFoodDelay = int.Parse(SetNormDelay.Text),
                    GoldFoodCount = int.Parse(SetGoldCount.Text),
                    GoldFoodEffect = int.Parse(SetGoldEffect.Text),
                    GoldFoodDelay = int.Parse(SetGoldDelay.Text),
                    PurpleFoodCount = int.Parse(SetPurpCount.Text),
                    PurpleFoodEffect = int.Parse(SetPurpEffect.Text),
                    PurpleFoodDelay = int.Parse(SetPurpDelay.Text)
                };

                if (config.GridWidth < 20 || config.GridHeight < 20) throw new Exception("Минимальный размер поля 20х20.");
                if (config.Speed <= 0) throw new Exception("Скорость должна быть больше 0.");

                _ = _networkClient.SendInputAsync(new InputUpdate { Action = ActionType.UpdateSettings, NewSettings = config });

                SettingsErrorText.Text = string.Empty;
                SettingsPanel.Visibility = Visibility.Collapsed;
                RoomLobbyPanel.IsEnabled = true;
            }
            catch (Exception ex)
            {
                SettingsErrorText.Text = ex.Message.Contains("Input string") ? "Ошибка: Вводите только целые числа!" : ex.Message;
            }
        }
        ///<summary>
        ///хеширование
        ///</summary>
        private string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
        ///<summary>
        ///обработка закрытия окна клиента
        ///</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _networkClient.Disconnect();
            base.OnClosing(e);
        }
    }
}