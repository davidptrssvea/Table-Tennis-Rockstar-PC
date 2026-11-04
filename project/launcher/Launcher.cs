using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TableTennisLauncher
{
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.Run(new MainWindow());
        }
    }

    public class MainWindow : Window
    {
        private bool checkingLaunch;
        private ComboBox comboButtons;
        private CheckBox chkNetwork;
        private TextBox txtServer, txtPlayer, txtRoom;
        private bool HasCommunityEngine { get { return File.Exists(Path.Combine(distDir, "community-engine.json")); } }
        private TextBox txtIsoPath;
        private TextBlock txtIsoStatus;
        private RadioButton rb720, rb1080, rb1440, rb4k, rbUltra;
        private CheckBox chkFullscreen, chkShowFps;
        private ComboBox comboFps, comboUltra;
        private ComboBox comboAA, comboPost;
        private TextBlock txtStatus;

        private string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
        private string distDir = AppDomain.CurrentDomain.BaseDirectory;

        public MainWindow()
        {
            Title = "Rockstar Games presents Table Tennis - Settings & Launcher";
            Width = 640;
            Height = Math.Min(860, SystemParameters.WorkArea.Height - 32);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(18, 20, 26));

            BuildUI();
            LoadSettings();
        }

        private void BuildUI()
        {
            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer buttons

            // --- HEADER ---
            var headerPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            var title = new TextBlock
            {
                Text = "ROCKSTAR GAMES PRESENTS TABLE TENNIS",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 184, 0)),
                TextAlignment = TextAlignment.Center
            };
            var subtitle = new TextBlock
            {
                Text = "PC Port (ReXGlue SDK) - Configuration & Launcher",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 170)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            };
            headerPanel.Children.Add(title);
            headerPanel.Children.Add(subtitle);
            Grid.SetRow(headerPanel, 0);
            root.Children.Add(headerPanel);

            // --- CONTENT ---
            var contentPanel = new StackPanel();

            // Section: Game Image (Xbox 360 ISO)
            contentPanel.Children.Add(CreateSectionTitle("GAME DISC IMAGE (XBOX 360 ISO)"));

            var isoGrid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            isoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            isoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            txtIsoPath = new TextBox
            {
                Height = 30,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(26, 30, 40)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 75, 100)),
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            txtIsoPath.TextChanged += (s, e) => UpdateIsoStatus();

            var btnBrowse = new Button
            {
                Content = "Browse...",
                Height = 30,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(45, 60, 85)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            btnBrowse.Click += (s, e) => BrowseForIso();

            Grid.SetColumn(txtIsoPath, 0);
            Grid.SetColumn(btnBrowse, 1);
            isoGrid.Children.Add(txtIsoPath);
            isoGrid.Children.Add(btnBrowse);
            contentPanel.Children.Add(isoGrid);

            txtIsoStatus = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(2, 0, 0, 8)
            };
            contentPanel.Children.Add(txtIsoStatus);

            // Section: Display Resolution
            var secRes = CreateSectionTitle("DISPLAY RESOLUTION & 3D SCALE");
            contentPanel.Children.Add(secRes);

            var resGrid = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            resGrid.ColumnDefinitions.Add(new ColumnDefinition());
            resGrid.ColumnDefinitions.Add(new ColumnDefinition());
            resGrid.RowDefinitions.Add(new RowDefinition());
            resGrid.RowDefinitions.Add(new RowDefinition());

            rb720 = CreateRadioButton("720p (1280x720) - 1x Native", "resGroup");
            rb1080 = CreateRadioButton("1080p (1920x1080) - Full HD", "resGroup");
            rb1440 = CreateRadioButton("1440p (2560x1440) - 2x 3D scale", "resGroup");
            rb4k = CreateRadioButton("4K (3840x2160) - 3x 3D scale", "resGroup");

            Grid.SetRow(rb720, 0); Grid.SetColumn(rb720, 0);
            Grid.SetRow(rb1080, 0); Grid.SetColumn(rb1080, 1);
            Grid.SetRow(rb1440, 1); Grid.SetColumn(rb1440, 0);
            Grid.SetRow(rb4k, 1); Grid.SetColumn(rb4k, 1);

            resGrid.Children.Add(rb720);
            resGrid.Children.Add(rb1080);
            resGrid.Children.Add(rb1440);
            resGrid.Children.Add(rb4k);
            resGrid.RowDefinitions.Add(new RowDefinition());
            rbUltra = CreateRadioButton("Ultrawide 21:9", "resGroup");
            comboUltra = new ComboBox { Height = 28, Margin = new Thickness(4), FontSize = 12 };
            comboUltra.Items.Add("2560 x 1080 (2x 3D scale)");
            comboUltra.Items.Add("3440 x 1440 (3x 3D scale)");
            comboUltra.Items.Add("3840 x 1600 (3x 3D scale)");
            comboUltra.SelectedIndex = 0;
            comboUltra.SelectionChanged += (sender, args) => { if(rbUltra != null) rbUltra.IsChecked = true; };
            Grid.SetRow(rbUltra, 2); Grid.SetColumn(rbUltra, 0);
            Grid.SetRow(comboUltra, 2); Grid.SetColumn(comboUltra, 1);
            resGrid.Children.Add(rbUltra);resGrid.Children.Add(comboUltra);
            contentPanel.Children.Add(resGrid);

            // Section: Target Framerate / Refresh Rate
            var secFps = CreateSectionTitle("FRAME PACING");
            contentPanel.Children.Add(secFps);

            var fpsGrid = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            fpsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            fpsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fpsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            var lblFps = new TextBlock { Text = "Framerate Limit (FPS):", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            comboFps = new ComboBox { Height = 26, VerticalAlignment = VerticalAlignment.Center };
            comboFps.Items.Add("60 FPS (recommended)");
            comboFps.Items.Add("120 requested / paced at 60");
            comboFps.Items.Add("144 requested / paced at 60");
            comboFps.Items.Add("240 requested / paced at 60");
            comboFps.Items.Add("No cap requested / paced at 60");
            comboFps.SelectedIndex = 0;

            Grid.SetRow(lblFps, 0); Grid.SetColumn(lblFps, 0);
            Grid.SetRow(comboFps, 0); Grid.SetColumn(comboFps, 1);
            fpsGrid.Children.Add(lblFps);
            fpsGrid.Children.Add(comboFps);
            contentPanel.Children.Add(fpsGrid);

            var lblFpsHint = new TextBlock
            {
                Text = "All selections currently run at 60 FPS to preserve game speed. F10 opens a separate timing window with a graph. Submission and Present-call timing do not measure GPU render duration or physical display scanout.",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 175, 190)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            contentPanel.Children.Add(lblFpsHint);

            // Section: Window Mode
            var secMode = CreateSectionTitle("WINDOW MODE");
            contentPanel.Children.Add(secMode);

            chkFullscreen = new CheckBox
            {
                Content = "Fullscreen Mode",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(4, 3, 0, 8),
                IsChecked = true
            };
            contentPanel.Children.Add(chkFullscreen);

            // Section: Overlay & Performance Metrics
            var secPerf = CreateSectionTitle("PERFORMANCE OVERLAY");
            contentPanel.Children.Add(secPerf);

            chkShowFps = new CheckBox
            {
                Content = "Show game-submission timing (F10 to toggle)",
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(4, 3, 0, 8),
                IsChecked = false
            };
            contentPanel.Children.Add(chkShowFps);

            // Section: Graphics & Anti-Aliasing
            var secGraph = CreateSectionTitle("ANTI-ALIASING & POST-PROCESSING");
            contentPanel.Children.Add(secGraph);

            var graphGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            graphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            graphGrid.ColumnDefinitions.Add(new ColumnDefinition());
            graphGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            graphGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            var lblAA = new TextBlock { Text = "Anti-Aliasing (AA):", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            comboAA = new ComboBox { Height = 26, VerticalAlignment = VerticalAlignment.Center };
            comboAA.Items.Add("None (Off)");
            comboAA.Items.Add("FXAA (Fast Approximate AA)");
            comboAA.Items.Add("FXAA Extreme (Maximum Quality)");
            comboAA.SelectedIndex = 1;

            var lblPost = new TextBlock { Text = "Scaling / Sharpening:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            comboPost = new ComboBox { Height = 26, VerticalAlignment = VerticalAlignment.Center };
            comboPost.Items.Add("AMD FidelityFX Super Resolution (FSR)");
            comboPost.Items.Add("Contrast Adaptive Sharpening (CAS)");
            comboPost.Items.Add("Bilinear (Standard)");
            comboPost.SelectedIndex = 0;

            Grid.SetRow(lblAA, 0); Grid.SetColumn(lblAA, 0);
            Grid.SetRow(comboAA, 0); Grid.SetColumn(comboAA, 1);
            Grid.SetRow(lblPost, 1); Grid.SetColumn(lblPost, 0);
            Grid.SetRow(comboPost, 1); Grid.SetColumn(comboPost, 1);

            graphGrid.Children.Add(lblAA);
            graphGrid.Children.Add(comboAA);
            graphGrid.Children.Add(lblPost);
            graphGrid.Children.Add(comboPost);
            contentPanel.Children.Add(graphGrid);

            contentPanel.Children.Add(CreateSectionTitle("CONTROLLER BUTTON ICONS"));
            comboButtons = new ComboBox { Height = 28, Margin = new Thickness(0,4,0,8) };
            comboButtons.Items.Add("Automatic (controller type)");
            comboButtons.Items.Add("Xbox"); comboButtons.Items.Add("PlayStation");
            comboButtons.SelectedIndex = 0;
            contentPanel.Children.Add(comboButtons);
            if (!HasCommunityEngine) contentPanel.Children.Add(new TextBlock {
                Text = "Icon selection applies to achievements in this build.", Foreground = Brushes.LightGray, FontSize = 11 });
            contentPanel.Children.Add(CreateSectionTitle("ONLINE PLAY (EXPERIMENTAL)"));
            chkNetwork = new CheckBox { Content = "Enable ONLINE and SYSTEM LINK", Foreground = Brushes.White,
                IsEnabled = HasCommunityEngine, Margin = new Thickness(0,4,0,8) };
            contentPanel.Children.Add(chkNetwork);
            if (!HasCommunityEngine) contentPanel.Children.Add(new TextBlock {
                Text = "Online play is unavailable in this build.", Foreground = Brushes.LightGray, FontSize = 11 });
            txtServer = NetworkField(contentPanel, "Matchmaking server (host or host:port)", 253);
            txtPlayer = NetworkField(contentPanel, "Player name", 15);
            txtRoom = NetworkField(contentPanel, "Private room code (6 characters; empty = public)", 6);

            if (HasCommunityEngine) AddOnlineHelp(contentPanel);

            // Section: Shortcuts Callout Card
            var callout = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(25, 32, 45)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 4, 0, 6)
            };
            var calloutStack = new StackPanel();
            var calloutHead = new TextBlock
            {
                Text = "ℹ IN-GAME SHORTCUTS & CONTROLS",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            var calloutBody = new TextBlock
            {
                Text = "- ESC : Exit game to Windows.\n- F10 : Show FPS, frame-time graph and presentation timing.\n- F7 / hold Back : Open achievements.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                LineHeight = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 230, 245))
            };
            calloutStack.Children.Add(calloutHead);
            calloutStack.Children.Add(calloutBody);
            callout.Child = calloutStack;
            contentPanel.Children.Add(callout);

            txtStatus = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(70, 220, 130)),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };
            contentPanel.Children.Add(txtStatus);

            var scroll = new ScrollViewer { Content = contentPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // --- FOOTER BUTTONS ---
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };

            var btnSave = new Button
            {
                Content = "Save Settings",
                Width = 140,
                Height = 38,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(40, 50, 70)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            btnSave.Click += (s, e) => { SaveSettings(); txtStatus.Text = "Settings saved to settings.ini!"; };

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 38,
                Margin = new Thickness(0, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(50, 55, 65)),
                Foreground = Brushes.White
            };
            btnCancel.Click += (s, e) => Close();

            var btnLaunch = new Button
            {
                Content = "LAUNCH GAME",
                Width = 170,
                Height = 38,
                Background = new SolidColorBrush(Color.FromRgb(35, 134, 54)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14
            };
            btnLaunch.Click += (s, e) => LaunchGame();

            btnPanel.Children.Add(btnSave);
            btnPanel.Children.Add(btnCancel);
            btnPanel.Children.Add(btnLaunch);

            Grid.SetRow(btnPanel, 2);
            root.Children.Add(btnPanel);

            Content = root;
        }

        private TextBlock CreateSectionTitle(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 255)),
                Margin = new Thickness(0, 4, 0, 2)
            };
        }

        private RadioButton CreateRadioButton(string text, string group)
        {
            return new RadioButton
            {
                Content = text,
                GroupName = group,
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(4, 3, 4, 3)
            };
        }

        private void BrowseForIso()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Rockstar Games Table Tennis ISO Image",
                Filter = "Xbox 360 ISO Image (*.iso)|*.iso|All files (*.*)|*.*",
                InitialDirectory = distDir
            };

            if (dlg.ShowDialog() == true)
            {
                txtIsoPath.Text = dlg.FileName;
            }
        }

        private void UpdateIsoStatus()
        {
            string path = txtIsoPath != null ? txtIsoPath.Text.Trim() : "";
            if (txtIsoStatus == null) return;

            if (string.IsNullOrEmpty(path))
            {
                txtIsoStatus.Text = "⚠ Please select your ISO file to launch the game.";
                txtIsoStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 80));
            }
            else if (File.Exists(path))
            {
                txtIsoStatus.Text = "✓ Valid ISO image found: " + Path.GetFileName(path);
                txtIsoStatus.Foreground = new SolidColorBrush(Color.FromRgb(70, 220, 130));
            }
            else
            {
                txtIsoStatus.Text = "✖ Specified ISO image not found on this computer.";
                txtIsoStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 90, 90));
            }
        }

        private static string LocalServerAddress() {
            try {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var properties = adapter.GetIPProperties();
                    bool gateway = false;
                    foreach (var route in properties.GatewayAddresses)
                        if (route.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !route.Address.Equals(IPAddress.Any)) gateway = true;
                    if (!gateway) continue;
                    foreach (var address in properties.UnicastAddresses)
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(address.Address) &&
                            !address.Address.ToString().StartsWith("169.254."))
                            return address.Address + ":27360";
                }
            } catch { }
            return "";
        }

        private static bool ValidServer(string value) {
            var match = Regex.Match(value, @"\A[A-Za-z0-9.-]+(?::([0-9]{1,5}))?\z");
            if (!match.Success) return false;
            if (!match.Groups[1].Success) return true;
            int port;
            return int.TryParse(match.Groups[1].Value, out port) && port >= 1 && port <= 65535;
        }

        private static bool ValidRoom(string value) {
            return value.Length == 0 || Regex.IsMatch(value, @"\A[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}\z");
        }

        private static IPEndPoint LocalServerEndpoint(string value) {
            if (!ValidServer(value)) return null;
            string[] parts = value.Split(':');
            int port = parts.Length == 2 ? int.Parse(parts[1]) : 27360;
            IPAddress address;
            if (parts[0].Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                parts[0].Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
                address = IPAddress.Loopback;
            else if (!IPAddress.TryParse(parts[0], out address) ||
                     address.AddressFamily != AddressFamily.InterNetwork) return null;
            if (IPAddress.IsLoopback(address)) return new IPEndPoint(address, port);
            try {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var entry in adapter.GetIPProperties().UnicastAddresses)
                        if (entry.Address.Equals(address)) return new IPEndPoint(address, port);
                }
            } catch { }
            return null;
        }

        private static bool RelayResponds(IPEndPoint endpoint) {
            try {
                using (var socket = new UdpClient(AddressFamily.InterNetwork)) {
                    socket.Client.ReceiveTimeout = 400;
                    socket.Connect(endpoint);
                    // An unknown-client keepalive gets ERROR/ERR_UNKNOWN without
                    // joining a room or changing any real player's session.
                    byte[] request = new byte[17];
                    request[0]=84; request[1]=84; request[2]=86; request[3]=49; request[4]=4;
                    socket.Send(request, request.Length);
                    IPEndPoint source = null;
                    byte[] reply = socket.Receive(ref source);
                    return reply.Length >= 7 && reply[0]==84 && reply[1]==84 &&
                        reply[2]==86 && reply[3]==49 && reply[4]==12 && reply[5]==5 &&
                        reply.Length == 7 + reply[6];
                }
            } catch { return false; }
        }

        private Process StartLocalRelay(int port) {
            string root = Path.GetDirectoryName(distDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string python = Path.Combine(root, "tools", "python", "python.exe");
            if (!File.Exists(python)) python = Path.Combine(root, ".tools", "python314", "python.exe");
            string server = Path.Combine(root, "server", "ttlink_server.py");
            if (!File.Exists(python) || !File.Exists(server))
                throw new InvalidOperationException("The bundled server tools are missing. Restore the complete installation or enter the other PC's server address.");
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners())
                if (endpoint.Port == port) throw new InvalidOperationException("UDP port " + port + " is already in use. Check the existing server window; another server will not be started.");
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                if (endpoint.Port == port) throw new InvalidOperationException("TCP port " + port + " is already in use. Close the conflicting application or choose a different server port.");
            var process = Process.Start(new ProcessStartInfo(python, "\"" + server + "\" --bind 0.0.0.0 --port " + port) {
                WorkingDirectory = root, UseShellExecute = true });
            if (process == null) throw new InvalidOperationException("Windows did not start the server.");
            return process;
        }

        private async Task<bool> EnsureLocalRelay() {
            var endpoint = LocalServerEndpoint(txtServer.Text.Trim());
            if (endpoint == null) return true; // Another PC hosts it.
            if (await Task.Run(() => RelayResponds(endpoint))) return true;
            if (MessageBox.Show(this,
                "The server address points to THIS PC, but the Table Tennis server is not responding.\n\nStart the server on this PC now? Keep its window open while playing.",
                "Online server is not running", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;
            try {
                using (var process = StartLocalRelay(endpoint.Port)) {
                    for (int attempt=0; attempt<12; ++attempt) {
                        if (await Task.Run(() => RelayResponds(endpoint))) return true;
                        if (process.HasExited) break;
                        await Task.Delay(200);
                    }
                }
                MessageBox.Show(this, "The server did not become ready. Check its window and any Windows network permission prompt, then try Launch Game again.",
                    "Server not ready", MessageBoxButton.OK, MessageBoxImage.Warning);
            } catch (Exception ex) {
                MessageBox.Show(this, "Could not start the local server: " + ex.Message,
                    "Server not ready", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }

        private string OnlineBuildId() {
            try {
                using (var hash = SHA256.Create()) {
                    byte[] buffer = new byte[65536];
                    foreach (string name in new[]{"tabletennis.exe", "rexruntime.dll", "rexgpu-xenos.dll"})
                        using (var input = File.OpenRead(Path.Combine(distDir, name))) {
                            int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                                hash.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(hash.Hash).Replace("-", "").Substring(0,12);
                }
            } catch { return "unavailable"; }
        }

        private void AddOnlineHelp(StackPanel parent) {
            var box = new StackPanel { Margin = new Thickness(0,6,0,12) };
            box.Children.Add(new TextBlock {
                Text = "PLAY WITH A FRIEND ON YOUR LOCAL NETWORK",
                Foreground = new SolidColorBrush(Color.FromRgb(0,210,255)),
                FontWeight = FontWeights.Bold, FontSize = 11 });
            box.Children.Add(new TextBlock {
                Text = "1. ONE player starts the server below and leaves its window open.\n" +
                    "2. BOTH players enable online play and enter the SAME server address.\n" +
                    "3. Choose different player names and the SAME private room code (e.g. ABC234).\n" +
                    "4. In the game: ONLINE > PLAYER MATCH. One creates a match; the other uses QUICK MATCH.\n" +
                    "Launch Game checks a server address belonging to this PC and offers to start the server if it is not responding.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray,
                FontSize = 11, Margin = new Thickness(0,6,0,8) });
            var root = Path.GetDirectoryName(distDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string python = Path.Combine(root, "tools", "python", "python.exe");
            if (!File.Exists(python)) python = Path.Combine(root, ".tools", "python314", "python.exe");
            string server = Path.Combine(root, "server", "ttlink_server.py");
            bool bundled = File.Exists(python) && File.Exists(server);
            string address = LocalServerAddress();
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var start = new Button { Content = "Start server on this PC", Padding = new Thickness(10,5,10,5),
                IsEnabled = bundled, Margin = new Thickness(0,0,8,0) };
            var copy = new Button { Content = "Copy this PC's server address", Padding = new Thickness(10,5,10,5),
                IsEnabled = address.Length != 0 };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray,
                FontSize = 11, Margin = new Thickness(0,6,0,0) };
            copy.Click += (sender, args) => {
                try { Clipboard.SetText(address); status.Text = "Copied " + address + ". Use it on BOTH PCs if this PC hosts the server."; }
                catch (Exception ex) { status.Text = "Could not copy: " + ex.Message; }
            };
            start.Click += (sender, args) => {
                try {
                    var process = StartLocalRelay(27360);
                    start.IsEnabled = false;
                    process.Exited += (source, eventArgs) => {
                        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => {
                            start.IsEnabled = true;
                            status.Text = "The server window closed. Start it again before hosting a match.";
                        }));
                        process.Dispose();
                    };
                    process.EnableRaisingEvents = true;
                    chkNetwork.IsChecked = true;
                    if (address.Length != 0) txtServer.Text = address;
                    status.Text = "Server start requested. Keep its window open; it should say 'listening'. " +
                        "Enter " + (address.Length == 0 ? "this PC's IPv4 address followed by :27360" : address) + " on BOTH PCs. " +
                        "Handle any Windows network permission prompt yourself.";
                } catch (Exception ex) { status.Text = "Could not start server: " + ex.Message; }
            };
            actions.Children.Add(start); actions.Children.Add(copy); box.Children.Add(actions);
            box.Children.Add(new TextBlock {
                Text = address.Length == 0 ? "This PC's address could not be detected. Use the server PC's local IPv4 address followed by :27360." :
                    "If THIS PC hosts the server, enter on BOTH PCs: " + address,
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 11,
                Margin = new Thickness(0,7,0,0) });
            if (!bundled) box.Children.Add(new TextBlock {
                Text = "Server tools are not included in this installation. Ask the host for their running server's address.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11,
                Margin = new Thickness(0,6,0,0) });
            box.Children.Add(new TextBlock {
                Text = "A local address only works on your own network. Internet play needs a reachable server; no public server is provided.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11,
                Margin = new Thickness(0,6,0,0) });
            box.Children.Add(status);
            box.Children.Add(new TextBlock {
                Text = "Online build ID: " + OnlineBuildId() + ". Use the same build on BOTH PCs.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11,
                Margin = new Thickness(0,7,0,0) });
            var logs = new Button { Content = "Open game logs", HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10,4,10,4), Margin = new Thickness(0,6,0,0) };
            logs.Click += (sender, args) => {
                try {
                    string directory = Path.Combine(distDir, "logs");
                    Directory.CreateDirectory(directory);
                    Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                } catch (Exception ex) { status.Text = "Could not open logs: " + ex.Message; }
            };
            box.Children.Add(logs);
            parent.Children.Add(box);
        }

        private TextBox NetworkField(StackPanel parent, string label, int length) {
            parent.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 11 });
            var box = new TextBox { Height = 26, MaxLength = length, Margin = new Thickness(0,3,0,7), IsEnabled = HasCommunityEngine };
            parent.Children.Add(box); return box;
        }

        private void LoadSettings()
        {
            string buttons = "auto", server = "", player = "Player", room = "";
            bool network = false;
            string iso = "";
            string res = "1080p";
            int framerate = 60;
            bool fullscreen = true;
            bool showFps = false;
            string aa = "fxaa";
            string post = "fsr";

            if (File.Exists(configFile))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(configFile))
                    {
                        var parts = line.Split(new char[] { '=' }, 2);
                        if (parts.Length != 2) continue;
                        string key = parts[0].Trim();
                        string val = parts[1].Trim();
                        if (key == "button_prompts") buttons = val;
                        else if (key == "online_server") server = val;
                        else if (key == "player_name") player = val;
                        else if (key == "room_code") room = val;
                        else if (key == "system_link") bool.TryParse(val, out network);
                        else if (key.Equals("iso_path", StringComparison.OrdinalIgnoreCase)) iso = val;
                        else if (key.Equals("resolution", StringComparison.OrdinalIgnoreCase)) res = val;
                        else if (key.Equals("framerate", StringComparison.OrdinalIgnoreCase)) int.TryParse(val, out framerate);
                        else if (key.Equals("fullscreen", StringComparison.OrdinalIgnoreCase)) bool.TryParse(val, out fullscreen);
                        else if (key.Equals("show_fps", StringComparison.OrdinalIgnoreCase)) bool.TryParse(val, out showFps);
                        else if (key.Equals("aa", StringComparison.OrdinalIgnoreCase)) aa = val;
                        else if (key.Equals("post", StringComparison.OrdinalIgnoreCase)) post = val;
                    }
                }
                catch { }
            }

            // Detect ISO automatically if missing from settings.ini or moved
            if (string.IsNullOrEmpty(iso) || !File.Exists(iso))
            {
                try
                {
                    var isoFiles = Directory.GetFiles(distDir, "*.iso");
                    if (isoFiles.Length > 0)
                    {
                        iso = isoFiles[0];
                    }
                    else
                    {
                        string parent = Path.GetDirectoryName(distDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        {
                            var parentIsos = Directory.GetFiles(parent, "*.iso");
                            if (parentIsos.Length > 0) iso = parentIsos[0];
                        }
                    }
                }
                catch { }
            }

            txtIsoPath.Text = iso;
            UpdateIsoStatus();

            if (res == "720p") rb720.IsChecked = true;
            else if (res == "1440p") rb1440.IsChecked = true;
            else if (res == "4k") rb4k.IsChecked = true;
            else if (res == "2560x1080" || res == "3440x1440" || res == "3840x1600") {
                comboUltra.SelectedIndex = res == "2560x1080" ? 0 : res == "3440x1440" ? 1 : 2;
                rbUltra.IsChecked = true;
            }
            else rb1080.IsChecked = true;

            if (framerate == 60) comboFps.SelectedIndex = 0;
            else if (framerate == 120) comboFps.SelectedIndex = 1;
            else if (framerate == 144) comboFps.SelectedIndex = 2;
            else if (framerate == 0) comboFps.SelectedIndex = 4;
            else comboFps.SelectedIndex = 3; // 240 FPS

            comboButtons.SelectedIndex = buttons == "xbox" ? 1 : buttons == "playstation" ? 2 : 0;
            txtServer.Text = server; txtPlayer.Text = player; txtRoom.Text = room;
            chkNetwork.IsChecked = HasCommunityEngine && network;
            chkFullscreen.IsChecked = fullscreen;
            chkShowFps.IsChecked = showFps;

            if (aa == "none") comboAA.SelectedIndex = 0;
            else if (aa == "fxaa_extreme") comboAA.SelectedIndex = 2;
            else comboAA.SelectedIndex = 1;

            if (post == "cas") comboPost.SelectedIndex = 1;
            else if (post == "bilinear") comboPost.SelectedIndex = 2;
            else comboPost.SelectedIndex = 0;
        }

        private void SaveSettings()
        {
            string iso = txtIsoPath.Text.Trim();

            string res = "1080p";
            if (rb720.IsChecked == true) res = "720p";
            else if (rb1440.IsChecked == true) res = "1440p";
            else if (rb4k.IsChecked == true) res = "4k";
            else if (rbUltra.IsChecked == true) res = comboUltra.SelectedIndex == 0 ? "2560x1080" : comboUltra.SelectedIndex == 1 ? "3440x1440" : "3840x1600";

            int targetFps = 240;
            if (comboFps.SelectedIndex == 0) targetFps = 60;
            else if (comboFps.SelectedIndex == 1) targetFps = 120;
            else if (comboFps.SelectedIndex == 2) targetFps = 144;
            else if (comboFps.SelectedIndex == 3) targetFps = 240;
            else if (comboFps.SelectedIndex == 4) targetFps = 0;

            bool fullscreen = chkFullscreen.IsChecked == true;
            bool showFps = chkShowFps.IsChecked == true;

            string aa = "fxaa";
            if (comboAA.SelectedIndex == 0) aa = "none";
            else if (comboAA.SelectedIndex == 2) aa = "fxaa_extreme";

            string post = "fsr";
            if (comboPost.SelectedIndex == 1) post = "cas";
            else if (comboPost.SelectedIndex == 2) post = "bilinear";

            try
            {
                var lines = new List<string>(new string[]
                {
                    "iso_path=" + iso,
                    "resolution=" + res,
                    "framerate=" + targetFps,
                    "fullscreen=" + fullscreen.ToString().ToLower(),
                    "show_fps=" + showFps.ToString().ToLower(),
                    "aa=" + aa,
                    "post=" + post,
                    "button_prompts=" + (comboButtons.SelectedIndex == 1 ? "xbox" : comboButtons.SelectedIndex == 2 ? "playstation" : "auto"),
                    "system_link=" + (HasCommunityEngine && chkNetwork.IsChecked == true).ToString().ToLower(),
                    "online_server=" + txtServer.Text.Trim(), "player_name=" + txtPlayer.Text.Trim(),
                    "room_code=" + txtRoom.Text.Trim().ToUpperInvariant()
                });
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines) keys.Add(line.Split('=')[0]);
                if (File.Exists(configFile)) foreach (var line in File.ReadAllLines(configFile)) {
                    int equal = line.IndexOf('=');
                    if (equal < 0 || !keys.Contains(line.Substring(0,equal).Trim())) lines.Add(line);
                }
                File.WriteAllLines(configFile, lines);
            }
            catch { }
        }

        private async void LaunchGame() {
            if (checkingLaunch) return;
            checkingLaunch = true;
            var content = (UIElement)Content;
            content.IsEnabled = false;
            try { await LaunchGameChecked(); }
            finally { checkingLaunch = false; content.IsEnabled = true; }
        }

        private async Task LaunchGameChecked()
        {
            string selectedIso = txtIsoPath.Text.Trim();
            if (string.IsNullOrEmpty(selectedIso) || !File.Exists(selectedIso))
            {
                MessageBox.Show("Please select a valid Xbox 360 ISO image file using 'Browse...' before launching the game!", "ISO Image Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (chkNetwork.IsChecked == true &&
                (!ValidServer(txtServer.Text.Trim()) ||
                 !Regex.IsMatch(txtPlayer.Text.Trim(), @"^[A-Za-z0-9 _-]{1,15}$") ||
                 !ValidRoom(txtRoom.Text.Trim().ToUpperInvariant()))) {
                MessageBox.Show("Enter a server host (optional port: 1-65535), a player name (1-15 letters/numbers/spaces), and either no room code or exactly 6 characters from ABCDEFGHJKMNPQRSTUVWXYZ23456789. Example: ABC234.", "Online settings", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (HasCommunityEngine && chkNetwork.IsChecked == true && !await EnsureLocalRelay()) return;
            SaveSettings();

            int width = 1920;
            int height = 1080;
            int resScale = 1;

            if (rb720.IsChecked == true)
            {
                width = 1280;
                height = 720;
                resScale = 1;
            }
            else if (rb1080.IsChecked == true)
            {
                width = 1920;
                height = 1080;
                resScale = 1;
            }
            else if (rb1440.IsChecked == true)
            {
                width = 2560;
                height = 1440;
                resScale = 2;
            }
            else if (rb4k.IsChecked == true)
            {
                width = 3840;
                height = 2160;
                resScale = 3;
            }

            if (rbUltra.IsChecked == true) {
                width = comboUltra.SelectedIndex == 0 ? 2560 : comboUltra.SelectedIndex == 1 ? 3440 : 3840;
                height = comboUltra.SelectedIndex == 0 ? 1080 : comboUltra.SelectedIndex == 1 ? 1440 : 1600;
                resScale = comboUltra.SelectedIndex == 0 ? 2 : 3;
            }

            int targetFps = 240;
            if (comboFps.SelectedIndex == 0) targetFps = 60;
            else if (comboFps.SelectedIndex == 1) targetFps = 120;
            else if (comboFps.SelectedIndex == 2) targetFps = 144;
            else if (comboFps.SelectedIndex == 3) targetFps = 240;
            else if (comboFps.SelectedIndex == 4) targetFps = 0;

            bool isFullscreen = chkFullscreen.IsChecked == true;
            bool showFps = chkShowFps.IsChecked == true;

            string aa = "fxaa";
            if (comboAA.SelectedIndex == 0) aa = "none";
            else if (comboAA.SelectedIndex == 2) aa = "fxaa_extreme";

            string post = "fsr";
            if (comboPost.SelectedIndex == 1) post = "cas";
            else if (comboPost.SelectedIndex == 2) post = "bilinear";

            string exePath = Path.Combine(distDir, "tabletennis.exe");
            if (!File.Exists(exePath))
            {
                MessageBox.Show("tabletennis.exe is missing at: " + exePath, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string args = string.Format(
                "--game_data_root=\"{0}\" --resolution_scale={1} --draw_resolution_scale_x={1} --draw_resolution_scale_y={1} --window_width={2} --window_height={3} --fullscreen={4} --swap_post_effect={5} --present_effect={6} --show_fps={7} --framerate={8} --protect_zero=false --gpu_allow_invalid_fetch_constants=true",
                selectedIso, resScale, width, height, isFullscreen.ToString().ToLower(), aa, post, showFps.ToString().ToLower(), targetFps);

            args += " --button_prompts=" + (comboButtons.SelectedIndex == 1 ? "xbox" : comboButtons.SelectedIndex == 2 ? "playstation" : "auto");
            if (HasCommunityEngine) {
                args += " --online=off --system_link=" + (chkNetwork.IsChecked == true).ToString().ToLower();
                // Only pass validated strings when networking is enabled.
                if (chkNetwork.IsChecked == true) args += " --online_server=" + txtServer.Text.Trim() +
                    " --user_name=\"" + txtPlayer.Text.Trim() + "\" --room_code=\"" + txtRoom.Text.Trim().ToUpperInvariant() + "\"";
            }
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = distDir,
                UseShellExecute = false
            };

            try
            {
                Process.Start(psi);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start the game:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
