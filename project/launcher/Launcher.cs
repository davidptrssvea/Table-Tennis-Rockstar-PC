using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TableTennisLauncher
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if(args.Length>0 && args[0]=="--host-session") {try {Environment.Exit(HostSession.Run(args));} catch {Environment.Exit(1);}return;}
            string dist=AppDomain.CurrentDomain.BaseDirectory;
            string root=Path.GetDirectoryName(dist.TrimEnd('\\'));
            if(File.Exists(Path.Combine(root,".updates","in-progress"))) {
                string recovery=Path.Combine(root,".updates","runner","tabletennis_updater.exe");
                if(File.Exists(recovery)) Process.Start(new ProcessStartInfo(recovery,"--recover --root "+"\""+root+"\"") {UseShellExecute=false});
                else MessageBox.Show("An update was interrupted. Restore the updater support files or re-run the installer.","Table Tennis update");
                return;
            }
            var app = new Application();
            app.Run(new MainWindow {PlayAfterUpdate=Array.IndexOf(args,"--play-after-update")>=0});
        }
    }

    public class MainWindow : Window
    {
        public bool PlayAfterUpdate {get;set;}
        private bool checkingLaunch, checkingUpdates;
        private CheckBox chkAutoUpdates;
        private TextBlock updateNotice, updateStatus;
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
            Loaded+=async (sender,args)=>{
                Task background=chkAutoUpdates.IsChecked==true?CheckUpdates(false):Task.FromResult(false);
                if(PlayAfterUpdate) LaunchGame();
                await background;
            };
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
            updateNotice=new TextBlock {Foreground=Brushes.Gold,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap,
                Margin=new Thickness(0,8,0,0),Visibility=Visibility.Collapsed};
            headerPanel.Children.Add(updateNotice);
            Grid.SetRow(headerPanel, 0);
            root.Children.Add(headerPanel);

            // --- CONTENT ---
            // Reserve a DPI-scaled gutter inside the viewport, before the scrollbar.
            var contentPanel = new StackPanel { Margin = new Thickness(0, 0, 30, 0) };

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
            txtPlayer = NetworkField(contentPanel, "Player name", 15);
            var advancedOnline=new StackPanel();
            txtServer = NetworkField(advancedOnline, "Server (advanced/manual connection)", 253);
            txtRoom = NetworkField(advancedOnline, "Private room (empty = public)", 6);
            if (HasCommunityEngine) { AddFriendTools(contentPanel); AddOnlineHelp(advancedOnline); }
            contentPanel.Children.Add(new Expander {Header="Advanced network settings / manual server",Content=advancedOnline,Foreground=Brushes.LightGray,Margin=new Thickness(0,5,0,10)});
            AddUpdateHelp(contentPanel);

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
                Text = "- ESC : Exit game to Windows (leave Photo mode first).\n- F10 : Show FPS, frame-time graph and presentation timing.\n- F9 / both stick clicks : Photo mode during offline matches.\n- F7 / hold Back : Open achievements.",
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

            var scroll = new ScrollViewer { Content = contentPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

        private static string NewPrivateRoom() {
            const string alphabet="ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            byte[] value=new byte[1];char[] code=new char[6];
            using(var random=RandomNumberGenerator.Create()) for(int i=0;i<6;i++) {
                do {random.GetBytes(value);} while(value[0]>=248);
                code[i]=alphabet[value[0]%alphabet.Length];
            }
            return new string(code);
        }
        private static string MakeInvitation(string server,string room,string build) {
            if(server==null || server.Length>253 || !ValidServer(server) || !ValidRoom(room) || room.Length!=6 || !Regex.IsMatch(build,@"\A[0-9A-F]{12}\z"))
                throw new ArgumentException("Enter a server and a six-character private room first. A matching game build is required.");
            return "TT1."+Convert.ToBase64String(Encoding.UTF8.GetBytes(server+"|"+room+"|"+build)).TrimEnd('=').Replace('+','-').Replace('/','_');
        }
        private static string[] ReadInvitation(string value) {
            if(value==null || value.Length>512 || !Regex.IsMatch(value,@"\ATT1\.[A-Za-z0-9_-]+\z"))
                throw new ArgumentException("Paste the complete TT1 invitation from your friend.");
            string encoded=value.Substring(4).Replace('-','+').Replace('_','/');
            encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
            string[] fields=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(encoded)).Split('|');
            if(fields.Length!=3 || !ValidServer(fields[0]) || fields[0].Length>253 || !ValidRoom(fields[1]) || fields[1].Length!=6 ||
               !Regex.IsMatch(fields[2],@"\A[0-9A-F]{12}\z")) throw new ArgumentException("This invitation is invalid or belongs to a newer format.");
            return fields;
        }
        private static bool SelectedRelayResponds(string server) {
            if(!ValidServer(server)) return false;
            string[] parts=server.Split(':');int port=parts.Length==2?int.Parse(parts[1]):27360;
            try {
                IPAddress literal;
                IPAddress[] addresses;
                if(IPAddress.TryParse(parts[0],out literal)) addresses=new[]{literal};
                else {
                    var lookup=Task.Run(()=>Dns.GetHostAddresses(parts[0]));
                    if(!lookup.Wait(4000)) return false;
                    addresses=lookup.Result;
                }
                int tried=0;
                foreach(var address in addresses) {
                    if(address.AddressFamily!=AddressFamily.InterNetwork) continue;
                    if(++tried>4) break;
                    for(int attempt=0;attempt<3;attempt++) if(RelayResponds(new IPEndPoint(address,port))) return true;
                }
            } catch { }
            return false;
        }
        private string[] ApplyInvitation(string value) {
            string[] fields=ReadInvitation(value);
            if(fields[2]!=OnlineBuildId()) throw new InvalidOperationException("Different game builds. Your friend uses "+fields[2]+". Update BOTH PCs to the same build before joining.");
            txtServer.Text=fields[0];txtRoom.Text=fields[1];chkNetwork.IsChecked=true;
            return fields;
        }
        private void AddFriendTools(StackPanel parent) {
            parent.Children.Add(new TextBlock {Text="PLAY WITH A FRIEND",Foreground=Brushes.Cyan,FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,5)});
            parent.Children.Add(new TextBlock {Text="HOST: start your own session and send its invitation. FRIEND: paste it and Join. Both use ONLINE > PLAYER MATCH in the game.",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,0,0,7)});
            var mode=new ComboBox {Height=28,Margin=new Thickness(0,0,0,6)};
            mode.Items.Add("Same network / shared VPN (default)");mode.Items.Add("Internet - UPnP (EXPERIMENTAL, opt-in)");mode.Items.Add("Internet - I forwarded the port manually");mode.SelectedIndex=0;parent.Children.Add(mode);
            var addresses=new List<string>();
            var adapterBox=new ComboBox {Height=28,Margin=new Thickness(0,0,0,6)};
            string preferred=LocalServerAddress().Split(':')[0];
            try {foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                if(adapter.OperationalStatus!=OperationalStatus.Up || adapter.NetworkInterfaceType==NetworkInterfaceType.Loopback) continue;
                foreach(var entry in adapter.GetIPProperties().UnicastAddresses) {
                    string address=entry.Address.ToString();
                    if(entry.Address.AddressFamily!=AddressFamily.InterNetwork || IPAddress.IsLoopback(entry.Address) || address.StartsWith("169.254.")) continue;
                    addresses.Add(address);adapterBox.Items.Add(adapter.Name+" - "+address);
                    if(address==preferred) adapterBox.SelectedIndex=addresses.Count-1;
                }
            }} catch { }
            if(adapterBox.SelectedIndex<0 && addresses.Count>0) adapterBox.SelectedIndex=0;
            parent.Children.Add(new TextBlock {Text="Host connection (choose your VPN adapter for VPN play)",Foreground=Brushes.LightGray,FontSize=11});
            parent.Children.Add(adapterBox);
            var manual=NetworkField(parent,"Your forwarded public IPv4:port",253);manual.Visibility=Visibility.Collapsed;
            // Keep the manual address label with the field when switching modes.
            var manualLabel=parent.Children[parent.Children.Count-2] as TextBlock;manualLabel.Visibility=Visibility.Collapsed;
            var routerConsent=new CheckBox {IsChecked=false,Content=new TextBlock {Text="EXPERIMENTAL: allow a five-minute, renewable UDP port 27360 rule for this host session. This exposes the game server to internet traffic. Permanent rules are refused. Use only with trusted friends and your own trusted router.",TextWrapping=TextWrapping.Wrap},Foreground=Brushes.Gold,Margin=new Thickness(0,0,0,6),Visibility=Visibility.Collapsed};
            parent.Children.Add(routerConsent);
            mode.SelectionChanged+=(sender,args)=>{routerConsent.IsChecked=false;manual.Visibility=manualLabel.Visibility=mode.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;routerConsent.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;};
            var invite=NetworkField(parent,"Invitation (paste your friend's TT1 code)",512);
            var status=new TextBlock {Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,5,0,8)};
            var actions=new WrapPanel();
            var host=new Button {Content="Host & copy invitation",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var stop=new Button {Content="Stop hosting",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var create=new Button {Content="Copy invitation",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var join=new Button {Content="Join invitation",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var room=new Button {Content="New private room",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var test=new Button {Content="Test connection",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            room.Click+=(sender,args)=>{
                if(HostSession.InvitationRoom(Path.GetDirectoryName(distDir.TrimEnd('\\')),txtServer.Text.Trim(),"")!="") {status.Text="Stop hosting before creating a new private room.";return;}
                txtRoom.Text=NewPrivateRoom();status.Text="New room selected. Start hosting to share a new invitation.";
            };
            string root=Path.GetDirectoryName(distDir.TrimEnd('\\'));
            try {
                if(File.Exists(Path.Combine(root,".updates","hosting","active.txt"))) {
                    string active=HostSession.Active(root);
                    var existing=HostSession.Fields(Path.Combine(HostSession.SessionFolder(root,active),"status.txt"));
                    if(existing.ContainsKey("state") && existing["state"]=="ready" && HostSession.Running(root)) status.Text="An existing host session is still running ("+(existing.ContainsKey("scope")?existing["scope"]:"unknown mode")+"). Use Stop hosting to close it before changing modes. UPnP permission is not retained.";
                }
            } catch {status.Text="Previous hosting state needs checking. Stop hosting before starting another session.";}
            host.Click+=async (sender,args)=>{
                if(adapterBox.SelectedIndex<0) {status.Text="No host connection was detected. Connect your network/VPN and reopen the launcher.";return;}
                if(mode.SelectedIndex==1 && routerConsent.IsChecked!=true) {status.Text="Tick the router permission box to try UPnP, or choose LAN/VPN/manual hosting.";return;}
                if(!Regex.IsMatch(OnlineBuildId(),@"\A[0-9A-F]{12}\z")) {status.Text="The game build is incomplete. Restore the full installation.";return;}
                string selected=addresses[adapterBox.SelectedIndex],selectedMode=mode.SelectedIndex==1?"upnp":mode.SelectedIndex==2?"manual":"lan",external=manual.Text.Trim();
                string hostRoom=txtRoom.Text.Trim().ToUpperInvariant();if(hostRoom.Length!=6 || !ValidRoom(hostRoom)) hostRoom=NewPrivateRoom();
                host.IsEnabled=join.IsEnabled=create.IsEnabled=room.IsEnabled=false;
                status.Text="Starting your host...";
                try {
                    string token=await Task.Run(()=>HostSession.Start(root,selected,selectedMode,external,hostRoom));
                    string folder=HostSession.SessionFolder(root,token);
                    for(int i=0;i<180;i++) {
                        var fields=HostSession.Fields(Path.Combine(folder,"status.txt"));string state,message;
                        if(fields.TryGetValue("message",out message)) status.Text=message;
                        if(fields.TryGetValue("state",out state)) {
                            if(state=="error" || state=="stopped") throw new IOException(message??"Hosting stopped.");
                            if(state=="ready") {
                                string code=fields["room"];
                                string invitation=MakeInvitation(fields["address"],code,OnlineBuildId());
                                txtServer.Text=fields["local"];txtRoom.Text=code;chkNetwork.IsChecked=true;invite.Text=invitation;
                                string warning=fields.ContainsKey("message") && fields["message"].Contains("cleanup")?" "+fields["message"]:"";
                                try {Clipboard.SetText(invitation);status.Text="Hosting ready - invitation copied. Send it to your friend, then Launch Game. Hosting stops when your game exits. Internet invitations must be tested by your friend."+warning;}
                                catch {status.Text="Hosting ready. Copy the invitation from the box and send it to your friend. Clipboard access was unavailable."+warning;}
                                SaveSettings();return;
                            }
                        }
                        await Task.Delay(250);
                    }
                    HostSession.Stop(root);throw new IOException("Host setup timed out. Stop hosting, then try shared VPN/LAN or manual port forwarding.");
                } catch(Exception error) {status.Text="Could not host: "+error.GetBaseException().Message;}
                finally {routerConsent.IsChecked=false;host.IsEnabled=join.IsEnabled=create.IsEnabled=room.IsEnabled=true;}
            };
            stop.Click+=async (sender,args)=>{
                stop.IsEnabled=false;
                try {
                    string token=HostSession.Active(root);if(token==null) {status.Text="No host session has been started here.";return;}
                    if(!HostSession.Running(root)) {status.Text="No host process is running. Unrenewed leases should expire; the next host start retries journaled cleanup.";return;}
                    HostSession.Stop(root);status.Text="Stopping your host and removing its router rule...";
                    for(int i=0;i<60;i++) {
                        var fields=HostSession.Fields(Path.Combine(HostSession.SessionFolder(root,token),"status.txt"));
                        if(fields.ContainsKey("state") && (fields["state"]=="stopped" || fields["state"]=="error")) {status.Text=fields["message"];return;}
                        await Task.Delay(250);
                    }
                    status.Text="Stop requested; router cleanup is still pending. The next host start will retry interrupted cleanup.";
                } catch(Exception error) {status.Text=error.Message;}
                finally {stop.IsEnabled=true;}
            };
            create.Click+=(sender,args)=>{
                try {
                    string code=HostSession.InvitationRoom(root,txtServer.Text.Trim(),txtRoom.Text.Trim().ToUpperInvariant());
                    if(code.Length==0) code=NewPrivateRoom();
                    string text=MakeInvitation(HostSession.InvitationAddress(root,txtServer.Text.Trim()),code,OnlineBuildId());
                    Clipboard.SetText(text);invite.Text=text;txtRoom.Text=code;chkNetwork.IsChecked=true;
                    status.Text="Invitation copied. It contains the server address, private room and build ID. Share it with your friend. The server must be running; local IPs only work on the same network or VPN.";
                } catch(Exception error) {status.Text="Could not create invitation: "+error.Message;}
            };
            join.Click+=async (sender,args)=>{
                join.IsEnabled=false;
                try {
                    string[] fields=ApplyInvitation(invite.Text.Trim());
                    SaveSettings();status.Text="Checking your friend's host...";
                    bool ready=await Task.Run(()=>SelectedRelayResponds(fields[0]));
                    status.Text=ready?"Connected to your friend's host. Click Launch Game, then ONLINE > PLAYER MATCH > QUICK MATCH.":"Your friend's host did not answer. Ask them to keep hosting and check the selected LAN/VPN or internet setup. Joining is configured; use Test connection to retry.";
                } catch(Exception error) {status.Text="Could not use invitation: "+error.Message;}
                finally {join.IsEnabled=true;}
            };
            test.Click+=async (sender,args)=>{
                string selected=txtServer.Text.Trim();
                if(!ValidServer(selected)) {status.Text="Enter a server or use your friend's invitation first.";return;}
                test.IsEnabled=false;status.Text="Testing UDP connection to "+selected+"...";
                try {bool ready=await Task.Run(()=>SelectedRelayResponds(selected));
                    status.Text=ready?"The Table Tennis server responds at "+selected+". Both players need the same room and game build. This checks the relay, not a complete match.":
                        "No Table Tennis UDP response from "+selected+". Start the host's server and check its address/UDP port. A local IP cannot connect across the internet without a shared VPN; internet hosting needs a reachable public server.";
                } finally {test.IsEnabled=true;}
            };
            foreach(var button in new[]{host,join,stop}) actions.Children.Add(button);
            parent.Children.Add(actions);parent.Children.Add(status);
            var extra=new WrapPanel();foreach(var button in new[]{create,room,test}) extra.Children.Add(button);
            parent.Children.Add(new Expander {Header="More connection tools",Content=extra,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,7)});
            parent.Children.Add(new TextBlock {Text="UPnP is EXPERIMENTAL and off by default. No Windows firewall settings are changed. Only the invitation room is accepted (up to four clients). Share invitations only with trusted friends: traffic is not encrypted. Stop hosting removes the owned rule; a router honoring its lease expires it within five minutes without renewal after a crash. Hosting ends with the host game, after 30 idle minutes, or after four hours. CGNAT/unsupported routers may require a shared VPN/manual setup.",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,0,0,8)});
        }

        private Process StartLocalRelay(int port) {
            string privateRoom=txtRoom.Text.Trim().ToUpperInvariant();
            if(privateRoom.Length==0) privateRoom=NewPrivateRoom();
            if(privateRoom.Length!=6 || !ValidRoom(privateRoom)) throw new InvalidOperationException("Choose a valid private room before hosting.");
            txtRoom.Text=privateRoom;
            string root = Path.GetDirectoryName(distDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string python = Path.Combine(root, "tools", "python", "python.exe");
            if (!File.Exists(python)) python = Path.Combine(root, ".tools", "python314", "python.exe");
            string server = Path.Combine(root, "server", "ttlink_server.py");
            if(File.Exists(Path.Combine(distDir,"python","python.exe"))) python=Path.Combine(distDir,"python","python.exe");
            if(File.Exists(Path.Combine(distDir,"server","ttlink_server.py"))) server=Path.Combine(distDir,"server","ttlink_server.py");
            if (!File.Exists(python) || !File.Exists(server))
                throw new InvalidOperationException("The bundled server tools are missing. Restore the complete installation or enter the other PC's server address.");
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners())
                if (endpoint.Port == port) throw new InvalidOperationException("UDP port " + port + " is already in use. Check the existing server window; another server will not be started.");
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                if (endpoint.Port == port) throw new InvalidOperationException("TCP port " + port + " is already in use. Close the conflicting application or choose a different server port.");
            var process = Process.Start(new ProcessStartInfo(python, "-I -B \"" + server + "\" --bind 0.0.0.0 --port " + port+" --room "+privateRoom+" --max-clients 4 --no-status") {
                WorkingDirectory = root, UseShellExecute = true });
            if (process == null) throw new InvalidOperationException("Windows did not start the server.");
            try {
                Directory.CreateDirectory(Path.Combine(root,".updates"));
                File.WriteAllLines(Path.Combine(root,".updates","server.pid"),new[]{process.Id.ToString(),process.StartTime.ToUniversalTime().Ticks.ToString(),Path.GetFullPath(python)});
            } catch { }
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

        private string OnlineBuildId() { return OnlineBuildIdForFolder(distDir); }
        private static string OnlineBuildIdForFolder(string folder) {
            try {
                if(!File.Exists(Path.Combine(folder,"tabletennis.exe"))) return "unavailable";
                string versionPath=Path.Combine(folder,"install-version.txt");
                string version=File.Exists(versionPath) && new FileInfo(versionPath).Length<=64
                    ? File.ReadAllText(versionPath).Trim() : "";
                bool released=Regex.IsMatch(version,@"\A[0-9]+\.[0-9]+\.[0-9]+\z");
                using (var hash = SHA256.Create()) {
                    // Local builds contain timestamps/compiler paths. Release identity
                    // plus exact engine bytes is portable between independent installs.
                    if(released) {
                        byte[] identity=Encoding.UTF8.GetBytes("TableTennis-compat-v1\n"+version+"\n");
                        hash.TransformBlock(identity,0,identity.Length,identity,0);
                    }
                    byte[] buffer = new byte[65536];
                    string[] files=released ? new[]{"rexruntime.dll","rexgpu-xenos.dll"}
                        : new[]{"tabletennis.exe","rexruntime.dll","rexgpu-xenos.dll"};
                    foreach (string name in files)
                        using (var input = File.OpenRead(Path.Combine(folder, name))) {
                            int count;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                                hash.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(hash.Hash).Replace("-", "").Substring(0,12);
                }
            } catch { return "unavailable"; }
        }

        private void AddUpdateHelp(StackPanel parent) {
            parent.Children.Add(CreateSectionTitle("UPDATES"));
            chkAutoUpdates=new CheckBox {Content="Check for updates at startup",Foreground=Brushes.White,
                IsChecked=true,Margin=new Thickness(0,3,0,6)};
            parent.Children.Add(chkAutoUpdates);
            var actions=new StackPanel {Orientation=Orientation.Horizontal};
            var check=new Button {Content="Check now",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,0)};
            check.Click+=async (sender,args)=>await CheckUpdates(true);
            var open=new Button {Content="Open updater",Padding=new Thickness(10,5,10,5)};
            open.Click+=(sender,args)=>OpenUpdater();
            actions.Children.Add(check);actions.Children.Add(open);parent.Children.Add(actions);
            updateStatus=new TextBlock {Text="Updates are installed only after you approve them.",Foreground=Brushes.LightGray,
                TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,6,0,10)};
            parent.Children.Add(updateStatus);
        }
        private async Task CheckUpdates(bool force) {
            if(checkingUpdates) return;
            string updater=Path.Combine(distDir,"tabletennis_updater.exe");
            if(!File.Exists(updater)) {updateStatus.Text="Install the full release once to enable update checks.";return;}
            checkingUpdates=true;
            updateStatus.Text="Checking for updates...";
            try {
                await Task.Run(()=>{
                    using(var process=Process.Start(new ProcessStartInfo(updater,"--check-only"+(force?" --force":"")) {
                        WorkingDirectory=distDir,UseShellExecute=false,CreateNoWindow=true})) {
                        if(process==null) throw new IOException("Could not start the update check.");
                        process.WaitForExit();
                    }
                });
                string root=Path.GetDirectoryName(distDir.TrimEnd('\\'));
                var fields=new Dictionary<string,string>();
                foreach(string line in File.ReadAllLines(Path.Combine(root,".updates","status.txt"))) {
                    int split=line.IndexOf('=');if(split>0) fields[line.Substring(0,split)]=line.Substring(split+1);
                }
                string message,state;
                updateStatus.Text=fields.TryGetValue("message",out message)?message:"Update check completed.";
                if(fields.TryGetValue("state",out state) && state=="available") {
                    updateNotice.Text=updateStatus.Text+" Open UPDATES below or OPTIONS > PC SETTINGS > UPDATES.";
                    updateNotice.Visibility=Visibility.Visible;
                } else updateNotice.Visibility=Visibility.Collapsed;
            } catch(Exception error) {updateStatus.Text="Update check unavailable. You can keep playing. "+error.Message;}
            finally {checkingUpdates=false;}
        }
        private void OpenUpdater() {
            SaveSettings();
            string updater=Path.Combine(distDir,"tabletennis_updater.exe");
            if(!File.Exists(updater)) {updateStatus.Text="Update support is missing. Install the current full release once.";return;}
            try {Process.Start(new ProcessStartInfo(updater) {WorkingDirectory=distDir,UseShellExecute=false});}
            catch(Exception error) {updateStatus.Text="Could not open updater: "+error.Message;}
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
            if(File.Exists(Path.Combine(distDir,"python","python.exe"))) python=Path.Combine(distDir,"python","python.exe");
            if(File.Exists(Path.Combine(distDir,"server","ttlink_server.py"))) server=Path.Combine(distDir,"server","ttlink_server.py");
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
                Text = "Online compatibility ID: " + OnlineBuildId() + ". Use the same release and engine on BOTH PCs.",
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
            bool network = false, checkUpdates = true;
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
                        if (key == "check_updates") bool.TryParse(val,out checkUpdates);
                        else if (key == "button_prompts") buttons = val;
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
            chkAutoUpdates.IsChecked=checkUpdates;

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
                    "room_code=" + txtRoom.Text.Trim().ToUpperInvariant(),
                    "check_updates=" + (chkAutoUpdates.IsChecked==true).ToString().ToLowerInvariant()
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
            string root=Path.GetDirectoryName(distDir.TrimEnd('\\'));
            if(File.Exists(Path.Combine(root,".updates","in-progress"))) {
                updateStatus.Text="An update is in progress. Finish it or reopen the launcher to recover.";return;
            }
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
            if(HasCommunityEngine && chkNetwork.IsChecked==true) {
                string selected=txtServer.Text.Trim();
                if(!await Task.Run(()=>SelectedRelayResponds(selected))) {
                    MessageBox.Show(this,"The Table Tennis server at "+selected+" did not respond over UDP.\n\nAsk the host to start the server and check the address. Internet play needs a reachable public server or a shared VPN; a home-network IP alone is not enough.","Cannot reach online server",MessageBoxButton.OK,MessageBoxImage.Warning);
                    return;
                }
            }
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
                var game=Process.Start(psi);
                try {HostSession.WatchGame(Path.GetDirectoryName(distDir.TrimEnd('\\')),game);} catch { }
                game.Dispose();
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start the game:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
