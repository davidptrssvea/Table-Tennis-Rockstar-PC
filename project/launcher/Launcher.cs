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
                else MessageBox.Show(UiText.T("An update was interrupted. Restore the updater support files or re-run the installer."),UiText.T("Table Tennis update"));
                return;
            }
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var app = new Application();
            app.Run(new MainWindow {PlayAfterUpdate=Array.IndexOf(args,"--play-after-update")>=0});
        }
    }

    public class MainWindow : Window
    {
        public bool PlayAfterUpdate {get;set;}
        private bool checkingLaunch, checkingUpdates, languageReady, localizing;
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
        private ComboBox comboFps, comboUltra, comboAspect, comboLanguage;
        private ComboBox comboAA, comboPost;
        private TextBlock txtStatus;

        private string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
        private string distDir = AppDomain.CurrentDomain.BaseDirectory;

        public MainWindow()
        {
            Title = UiText.T("Rockstar Games presents Table Tennis - Settings & Launcher");
            Width = 640;
            Height = Math.Min(860, SystemParameters.WorkArea.Height - 32);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(Color.FromRgb(18, 20, 26));

            BuildUI();
            LoadSettings();
            languageReady=true;
            ApplyLanguage();
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
                Text = UiText.T("PC Port (ReXGlue SDK) - Configuration & Launcher"),
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
            contentPanel.Children.Add(CreateSectionTitle(UiText.T("GAME DISC IMAGE (XBOX 360 ISO)")));

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
                Content = UiText.T("Browse..."),
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

            contentPanel.Children.Add(CreateSectionTitle(UiText.T("GAME LANGUAGE")));
            comboLanguage = new ComboBox { Height=28, Margin=new Thickness(0,4,0,6) };
            foreach (string label in new[]{"English", "Français", "Deutsch", "Español", "Italiano"})
                comboLanguage.Items.Add(label);
            comboLanguage.SelectedIndex=0;
            comboLanguage.SelectionChanged+=(sender,args)=>{if(languageReady && !localizing) ApplyLanguage();};
            contentPanel.Children.Add(comboLanguage);
            contentPanel.Children.Add(new TextBlock { Text=UiText.T("Language applies to the launcher immediately and to the game on its next start."),
                FontSize=11, Foreground=new SolidColorBrush(Color.FromRgb(160,175,190)), TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,0,0,10) });

            // Section: Display Resolution
            var secRes = CreateSectionTitle(UiText.T("DISPLAY RESOLUTION & 3D SCALE"));
            contentPanel.Children.Add(secRes);

            var resGrid = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            resGrid.ColumnDefinitions.Add(new ColumnDefinition());
            resGrid.ColumnDefinitions.Add(new ColumnDefinition());
            resGrid.RowDefinitions.Add(new RowDefinition());
            resGrid.RowDefinitions.Add(new RowDefinition());

            rb720 = CreateRadioButton(UiText.T("720p (1280x720) - 1x Native"), "resGroup");
            rb1080 = CreateRadioButton("1080p (1920x1080) - Full HD", "resGroup");
            rb1440 = CreateRadioButton(UiText.T("1440p (2560x1440) - 2x 3D scale"), "resGroup");
            rb4k = CreateRadioButton(UiText.T("4K (3840x2160) - 3x 3D scale"), "resGroup");

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
            comboUltra.Items.Add(UiText.T("2560 x 1080 (2x 3D scale)"));
            comboUltra.Items.Add(UiText.T("3440 x 1440 (3x 3D scale)"));
            comboUltra.Items.Add(UiText.T("3840 x 1600 (3x 3D scale)"));
            comboUltra.SelectedIndex = 0;
            comboUltra.SelectionChanged += (sender, args) => { if(rbUltra != null && !localizing) rbUltra.IsChecked = true; };
            Grid.SetRow(rbUltra, 2); Grid.SetColumn(rbUltra, 0);
            Grid.SetRow(comboUltra, 2); Grid.SetColumn(comboUltra, 1);
            resGrid.Children.Add(rbUltra);resGrid.Children.Add(comboUltra);
            contentPanel.Children.Add(resGrid);

            contentPanel.Children.Add(CreateSectionTitle(UiText.T("PICTURE ASPECT RATIO")));
            comboAspect = new ComboBox { Height=26, Margin=new Thickness(0,4,0,6) };
            comboAspect.Items.Add(UiText.T("Auto - follow display (including 32:9)"));
            comboAspect.Items.Add(UiText.T("Lock 16:9 - preserve picture, add black bars"));
            comboAspect.Items.Add(UiText.T("Lock 21:9 - preserve picture, add black bars"));
            comboAspect.Items.Add(UiText.T("Lock 32:9 - preserve picture, add black bars"));
            comboAspect.SelectedIndex=0;
            contentPanel.Children.Add(comboAspect);
            contentPanel.Children.Add(new TextBlock { Text=UiText.T("Separate from resolution. Fullscreen uses the monitor size; the aspect lock controls the game picture inside it."), TextWrapping=TextWrapping.Wrap, Foreground=Brushes.LightGray, FontSize=11, Margin=new Thickness(0,0,0,10) });

            // Section: Target Framerate / Refresh Rate
            var secFps = CreateSectionTitle(UiText.T("FRAME PACING"));
            contentPanel.Children.Add(secFps);

            var fpsGrid = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            fpsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            fpsGrid.ColumnDefinitions.Add(new ColumnDefinition());
            fpsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            var lblFps = new TextBlock { Text = UiText.T("Framerate Limit (FPS):"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            comboFps = new ComboBox { Height = 26, VerticalAlignment = VerticalAlignment.Center };
            comboFps.Items.Add(UiText.T("60 FPS (recommended)"));
            comboFps.Items.Add(UiText.T("120 requested / paced at 60"));
            comboFps.Items.Add(UiText.T("144 requested / paced at 60"));
            comboFps.Items.Add(UiText.T("240 requested / paced at 60"));
            comboFps.Items.Add(UiText.T("No cap requested / paced at 60"));
            comboFps.SelectedIndex = 0;

            Grid.SetRow(lblFps, 0); Grid.SetColumn(lblFps, 0);
            Grid.SetRow(comboFps, 0); Grid.SetColumn(comboFps, 1);
            fpsGrid.Children.Add(lblFps);
            fpsGrid.Children.Add(comboFps);
            contentPanel.Children.Add(fpsGrid);

            var lblFpsHint = new TextBlock
            {
                Text = UiText.T("All selections currently run at 60 FPS to preserve game speed. F10 opens a separate timing window with a graph. Submission and Present-call timing do not measure GPU render duration or physical display scanout."),
                Foreground = new SolidColorBrush(Color.FromRgb(170, 175, 190)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            contentPanel.Children.Add(lblFpsHint);

            // Section: Window Mode
            var secMode = CreateSectionTitle(UiText.T("WINDOW MODE"));
            contentPanel.Children.Add(secMode);

            chkFullscreen = new CheckBox
            {
                Content = UiText.T("Fullscreen Mode"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(4, 3, 0, 8),
                IsChecked = true
            };
            contentPanel.Children.Add(chkFullscreen);

            // Section: Overlay & Performance Metrics
            var secPerf = CreateSectionTitle(UiText.T("PERFORMANCE OVERLAY"));
            contentPanel.Children.Add(secPerf);

            chkShowFps = new CheckBox
            {
                Content = UiText.T("Show game-submission timing (F10 to toggle)"),
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(4, 3, 0, 8),
                IsChecked = false
            };
            contentPanel.Children.Add(chkShowFps);

            // Section: Graphics & Anti-Aliasing
            var secGraph = CreateSectionTitle(UiText.T("ANTI-ALIASING & POST-PROCESSING"));
            contentPanel.Children.Add(secGraph);

            var graphGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            graphGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            graphGrid.ColumnDefinitions.Add(new ColumnDefinition());
            graphGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            graphGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            var lblAA = new TextBlock { Text = UiText.T("Anti-Aliasing (AA):"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            comboAA = new ComboBox { Height = 26, VerticalAlignment = VerticalAlignment.Center };
            comboAA.Items.Add(UiText.T("None (Off)"));
            comboAA.Items.Add(UiText.T("FXAA (Fast Approximate AA)"));
            comboAA.Items.Add(UiText.T("FXAA Extreme (Maximum Quality)"));
            comboAA.SelectedIndex = 1;

            var lblPost = new TextBlock { Text = UiText.T("Scaling / Sharpening:"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            comboPost = new ComboBox { Height = 26, VerticalAlignment = VerticalAlignment.Center };
            comboPost.Items.Add("AMD FidelityFX Super Resolution (FSR)");
            comboPost.Items.Add(UiText.T("Contrast Adaptive Sharpening (CAS)"));
            comboPost.Items.Add(UiText.T("Bilinear (Standard)"));
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

            contentPanel.Children.Add(CreateSectionTitle(UiText.T("CONTROLLER BUTTON ICONS")));
            comboButtons = new ComboBox { Height = 28, Margin = new Thickness(0,4,0,8) };
            comboButtons.Items.Add(UiText.T("Automatic (controller type)"));
            comboButtons.Items.Add("Xbox"); comboButtons.Items.Add("PlayStation");
            comboButtons.SelectedIndex = 0;
            contentPanel.Children.Add(comboButtons);
            if (!HasCommunityEngine) contentPanel.Children.Add(new TextBlock {
                Text = UiText.T("Icon selection applies to achievements in this build."), Foreground = Brushes.LightGray, FontSize = 11 });
            contentPanel.Children.Add(CreateSectionTitle(UiText.T("ONLINE PLAY (EXPERIMENTAL)")));
            chkNetwork = new CheckBox { Content = UiText.T("Enable ONLINE and SYSTEM LINK"), Foreground = Brushes.White,
                IsEnabled = HasCommunityEngine, Margin = new Thickness(0,4,0,8) };
            contentPanel.Children.Add(chkNetwork);
            if (!HasCommunityEngine) contentPanel.Children.Add(new TextBlock {
                Text = UiText.T("Online play is unavailable in this build."), Foreground = Brushes.LightGray, FontSize = 11 });
            txtPlayer = NetworkField(contentPanel, UiText.T("Player name"), 15);
            var advancedOnline=new StackPanel();
            txtServer = NetworkField(advancedOnline, UiText.T("Server (advanced/manual connection)"), 253);
            txtRoom = NetworkField(advancedOnline, UiText.T("Private room (empty = public)"), 6);
            if (HasCommunityEngine) { AddFriendTools(contentPanel); AddOnlineHelp(advancedOnline); }
            contentPanel.Children.Add(new Expander {Header=UiText.T("Advanced network settings / manual server"),Content=advancedOnline,Foreground=Brushes.LightGray,Margin=new Thickness(0,5,0,10)});
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
                Text = UiText.T("ℹ IN-GAME SHORTCUTS & CONTROLS"),
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                Margin = new Thickness(0, 0, 0, 3)
            };
            var calloutBody = new TextBlock
            {
                Text = UiText.T("- ESC : Exit game to Windows (leave Photo mode first).\n- F10 : Show FPS, frame-time graph and presentation timing.\n- F9 / both stick clicks : Photo mode during offline matches.\n- F7 / hold Back : Open achievements."),
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
                Content = UiText.T("Save Settings"),
                Width = 140,
                Height = 38,
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(40, 50, 70)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold
            };
            btnSave.Click += (s, e) => { SaveSettings(); txtStatus.Text = UiText.T("Settings saved to settings.ini!"); };

            var btnCancel = new Button
            {
                Content = UiText.T("Cancel"),
                Width = 90,
                Height = 38,
                Margin = new Thickness(0, 0, 12, 0),
                Background = new SolidColorBrush(Color.FromRgb(50, 55, 65)),
                Foreground = Brushes.White
            };
            btnCancel.Click += (s, e) => Close();

            var btnLaunch = new Button
            {
                Content = UiText.T("LAUNCH GAME"),
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

        private void ApplyLanguage() {
            localizing=true;
            try {
                UiText.Code=SelectedLanguage();
                Title=UiText.T(Title);
                TranslateControls(this);
                UpdateIsoStatus();
            } finally {localizing=false;}
        }
        private static void TranslateControls(DependencyObject node) {
            if(node is TextBox) return; // Paths, names and invitation codes are user data.
            var text=node as TextBlock;
            if(text!=null) {text.Text=UiText.T(text.Text);return;}
            var combo=node as ComboBox;
            if(combo!=null) {
                int selected=combo.SelectedIndex;
                for(int i=0;i<combo.Items.Count;i++) if(combo.Items[i] is string) {
                    string before=(string)combo.Items[i],after=UiText.T(before);
                    if(before!=after) combo.Items[i]=after;
                }
                combo.SelectedIndex=selected;return;
            }
            var header=node as HeaderedContentControl;
            if(header!=null && header.Header is string) header.Header=UiText.T((string)header.Header);
            var content=node as ContentControl;
            if(content!=null && content.Content is string) {content.Content=UiText.T((string)content.Content);return;}
            foreach(object child in LogicalTreeHelper.GetChildren(node)) {
                var element=child as DependencyObject;if(element!=null) TranslateControls(element);
            }
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
                Content = new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap},
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
                Title = UiText.T("Select Rockstar Games Table Tennis ISO Image"),
                Filter = UiText.T("Xbox 360 ISO Image (*.iso)|*.iso|All files (*.*)|*.*"),
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
                txtIsoStatus.Text = UiText.T("⚠ Please select your ISO file to launch the game.");
                txtIsoStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 180, 80));
            }
            else if (File.Exists(path))
            {
                txtIsoStatus.Text = UiText.T("✓ Valid ISO image found: ") + Path.GetFileName(path);
                txtIsoStatus.Foreground = new SolidColorBrush(Color.FromRgb(70, 220, 130));
            }
            else
            {
                txtIsoStatus.Text = UiText.T("✖ Specified ISO image not found on this computer.");
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
                throw new ArgumentException(UiText.T("Enter a server and a six-character private room first. A matching game build is required."));
            return "TT1."+Convert.ToBase64String(Encoding.UTF8.GetBytes(server+"|"+room+"|"+build)).TrimEnd('=').Replace('+','-').Replace('/','_');
        }
        private static string[] ReadInvitation(string value) {
            if(value==null || value.Length>512 || !Regex.IsMatch(value,@"\ATT1\.[A-Za-z0-9_-]+\z"))
                throw new ArgumentException(UiText.T("Paste the complete TT1 invitation from your friend."));
            string encoded=value.Substring(4).Replace('-','+').Replace('_','/');
            encoded=encoded.PadRight((encoded.Length+3)/4*4,'=');
            string[] fields=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(encoded)).Split('|');
            if(fields.Length!=3 || !ValidServer(fields[0]) || fields[0].Length>253 || !ValidRoom(fields[1]) || fields[1].Length!=6 ||
               !Regex.IsMatch(fields[2],@"\A[0-9A-F]{12}\z")) throw new ArgumentException(UiText.T("This invitation is invalid or belongs to a newer format."));
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
            if(fields[2]!=OnlineBuildId()) throw new InvalidOperationException(UiText.T("Different game builds. Your friend uses ")+fields[2]+UiText.T(". Update BOTH PCs to the same build before joining."));
            txtServer.Text=fields[0];txtRoom.Text=fields[1];chkNetwork.IsChecked=true;
            return fields;
        }
        private void AddFriendTools(StackPanel parent) {
            parent.Children.Add(new TextBlock {Text=UiText.T("PLAY WITH A FRIEND"),Foreground=Brushes.Cyan,FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,5)});
            parent.Children.Add(new TextBlock {Text=UiText.T("HOST: start your own session and send its invitation. FRIEND: paste it and Join. Both use ONLINE > PLAYER MATCH in the game."),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,0,0,7)});
            var mode=new ComboBox {Height=28,Margin=new Thickness(0,0,0,6)};
            mode.Items.Add(UiText.T("Same network / shared VPN (default)"));mode.Items.Add(UiText.T("Internet - UPnP (EXPERIMENTAL, opt-in)"));mode.Items.Add(UiText.T("Internet - I forwarded the port manually"));mode.SelectedIndex=0;parent.Children.Add(mode);
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
            parent.Children.Add(new TextBlock {Text=UiText.T("Host connection (choose your VPN adapter for VPN play)"),Foreground=Brushes.LightGray,FontSize=11});
            parent.Children.Add(adapterBox);
            var manual=NetworkField(parent,UiText.T("Your forwarded public IPv4:port"),253);manual.Visibility=Visibility.Collapsed;
            // Keep the manual address label with the field when switching modes.
            var manualLabel=parent.Children[parent.Children.Count-2] as TextBlock;manualLabel.Visibility=Visibility.Collapsed;
            var routerConsent=new CheckBox {IsChecked=false,Content=new TextBlock {Text=UiText.T("EXPERIMENTAL: allow a five-minute, renewable UDP port 27360 rule for this host session. This exposes the game server to internet traffic. Permanent rules are refused. Use only with trusted friends and your own trusted router."),TextWrapping=TextWrapping.Wrap},Foreground=Brushes.Gold,Margin=new Thickness(0,0,0,6),Visibility=Visibility.Collapsed};
            parent.Children.Add(routerConsent);
            mode.SelectionChanged+=(sender,args)=>{if(localizing)return;routerConsent.IsChecked=false;manual.Visibility=manualLabel.Visibility=mode.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;routerConsent.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;};
            var invite=NetworkField(parent,UiText.T("Invitation (paste your friend's TT1 code)"),512);
            var status=new TextBlock {Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,5,0,8)};
            var actions=new WrapPanel();
            var host=new Button {Content=UiText.T("Host & copy invitation"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var stop=new Button {Content=UiText.T("Stop hosting"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var create=new Button {Content=UiText.T("Copy invitation"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var join=new Button {Content=UiText.T("Join invitation"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var room=new Button {Content=UiText.T("New private room"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            var test=new Button {Content=UiText.T("Test connection"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,6,6)};
            room.Click+=(sender,args)=>{
                if(HostSession.InvitationRoom(Path.GetDirectoryName(distDir.TrimEnd('\\')),txtServer.Text.Trim(),"")!="") {status.Text=UiText.T("Stop hosting before creating a new private room.");return;}
                txtRoom.Text=NewPrivateRoom();status.Text=UiText.T("New room selected. Start hosting to share a new invitation.");
            };
            string root=Path.GetDirectoryName(distDir.TrimEnd('\\'));
            try {
                if(File.Exists(Path.Combine(root,".updates","hosting","active.txt"))) {
                    string active=HostSession.Active(root);
                    var existing=HostSession.Fields(Path.Combine(HostSession.SessionFolder(root,active),"status.txt"));
                    if(existing.ContainsKey("state") && existing["state"]=="ready" && HostSession.Running(root)) status.Text=UiText.T("An existing host session is still running (")+(existing.ContainsKey("scope")?existing["scope"]:UiText.T("unknown mode"))+UiText.T("). Use Stop hosting to close it before changing modes. UPnP permission is not retained.");
                }
            } catch {status.Text=UiText.T("Previous hosting state needs checking. Stop hosting before starting another session.");}
            host.Click+=async (sender,args)=>{
                if(adapterBox.SelectedIndex<0) {status.Text=UiText.T("No host connection was detected. Connect your network/VPN and reopen the launcher.");return;}
                if(mode.SelectedIndex==1 && routerConsent.IsChecked!=true) {status.Text=UiText.T("Tick the router permission box to try UPnP, or choose LAN/VPN/manual hosting.");return;}
                if(!Regex.IsMatch(OnlineBuildId(),@"\A[0-9A-F]{12}\z")) {status.Text=UiText.T("The game build is incomplete. Restore the full installation.");return;}
                string selected=addresses[adapterBox.SelectedIndex],selectedMode=mode.SelectedIndex==1?"upnp":mode.SelectedIndex==2?"manual":"lan",external=manual.Text.Trim();
                string hostRoom=txtRoom.Text.Trim().ToUpperInvariant();if(hostRoom.Length!=6 || !ValidRoom(hostRoom)) hostRoom=NewPrivateRoom();
                host.IsEnabled=join.IsEnabled=create.IsEnabled=room.IsEnabled=false;
                status.Text=UiText.T("Starting your host...");
                try {
                    string token=await Task.Run(()=>HostSession.Start(root,selected,selectedMode,external,hostRoom));
                    string folder=HostSession.SessionFolder(root,token);
                    for(int i=0;i<180;i++) {
                        var fields=HostSession.Fields(Path.Combine(folder,"status.txt"));string state,message;
                        if(fields.TryGetValue("message",out message)) status.Text=UiText.Message(message);
                        if(fields.TryGetValue("state",out state)) {
                            if(state=="error" || state=="stopped") throw new IOException(message??UiText.T("Hosting stopped."));
                            if(state=="ready") {
                                string code=fields["room"];
                                string invitation=MakeInvitation(fields["address"],code,OnlineBuildId());
                                txtServer.Text=fields["local"];txtRoom.Text=code;chkNetwork.IsChecked=true;invite.Text=invitation;
                                string warning=fields.ContainsKey("message") && fields["message"].Contains("cleanup")?" "+UiText.Message(fields["message"]):"";
                                try {Clipboard.SetText(invitation);status.Text=UiText.T("Hosting ready - invitation copied. Send it to your friend, then Launch Game. Hosting stops when your game exits. Internet invitations must be tested by your friend.")+warning;}
                                catch {status.Text=UiText.T("Hosting ready. Copy the invitation from the box and send it to your friend. Clipboard access was unavailable.")+warning;}
                                SaveSettings();return;
                            }
                        }
                        await Task.Delay(250);
                    }
                    HostSession.Stop(root);throw new IOException(UiText.T("Host setup timed out. Stop hosting, then try shared VPN/LAN or manual port forwarding."));
                } catch(Exception error) {status.Text=UiText.T("Could not host: ")+UiText.Message(error.GetBaseException().Message);}
                finally {routerConsent.IsChecked=false;host.IsEnabled=join.IsEnabled=create.IsEnabled=room.IsEnabled=true;}
            };
            stop.Click+=async (sender,args)=>{
                stop.IsEnabled=false;
                try {
                    string token=HostSession.Active(root);if(token==null) {status.Text=UiText.T("No host session has been started here.");return;}
                    if(!HostSession.Running(root)) {status.Text=UiText.T("No host process is running. Unrenewed leases should expire; the next host start retries journaled cleanup.");return;}
                    HostSession.Stop(root);status.Text=UiText.T("Stopping your host and removing its router rule...");
                    for(int i=0;i<60;i++) {
                        var fields=HostSession.Fields(Path.Combine(HostSession.SessionFolder(root,token),"status.txt"));
                        if(fields.ContainsKey("state") && (fields["state"]=="stopped" || fields["state"]=="error")) {status.Text=UiText.Message(fields["message"]);return;}
                        await Task.Delay(250);
                    }
                    status.Text=UiText.T("Stop requested; router cleanup is still pending. The next host start will retry interrupted cleanup.");
                } catch(Exception error) {status.Text=UiText.Message(error.Message);}
                finally {stop.IsEnabled=true;}
            };
            create.Click+=(sender,args)=>{
                try {
                    string code=HostSession.InvitationRoom(root,txtServer.Text.Trim(),txtRoom.Text.Trim().ToUpperInvariant());
                    if(code.Length==0) code=NewPrivateRoom();
                    string text=MakeInvitation(HostSession.InvitationAddress(root,txtServer.Text.Trim()),code,OnlineBuildId());
                    Clipboard.SetText(text);invite.Text=text;txtRoom.Text=code;chkNetwork.IsChecked=true;
                    status.Text=UiText.T("Invitation copied. It contains the server address, private room and build ID. Share it with your friend. The server must be running; local IPs only work on the same network or VPN.");
                } catch(Exception error) {status.Text=UiText.T("Could not create invitation: ")+UiText.Message(error.Message);}
            };
            join.Click+=async (sender,args)=>{
                join.IsEnabled=false;
                try {
                    string[] fields=ApplyInvitation(invite.Text.Trim());
                    SaveSettings();status.Text=UiText.T("Checking your friend's host...");
                    bool ready=await Task.Run(()=>SelectedRelayResponds(fields[0]));
                    status.Text=ready?UiText.T("Connected to your friend's host. Click Launch Game, then ONLINE > PLAYER MATCH > QUICK MATCH."):UiText.T("Your friend's host did not answer. Ask them to keep hosting and check the selected LAN/VPN or internet setup. Joining is configured; use Test connection to retry.");
                } catch(Exception error) {status.Text=UiText.T("Could not use invitation: ")+UiText.Message(error.Message);}
                finally {join.IsEnabled=true;}
            };
            test.Click+=async (sender,args)=>{
                string selected=txtServer.Text.Trim();
                if(!ValidServer(selected)) {status.Text=UiText.T("Enter a server or use your friend's invitation first.");return;}
                test.IsEnabled=false;status.Text=UiText.T("Testing UDP connection to ")+selected+"...";
                try {bool ready=await Task.Run(()=>SelectedRelayResponds(selected));
                    status.Text=ready?UiText.T("The Table Tennis server responds at ")+selected+UiText.T(". Both players need the same room and game build. This checks the relay, not a complete match."):
                        UiText.T("No Table Tennis UDP response from ")+selected+UiText.T(". Start the host's server and check its address/UDP port. A local IP cannot connect across the internet without a shared VPN; internet hosting needs a reachable public server.");
                } finally {test.IsEnabled=true;}
            };
            foreach(var button in new[]{host,join,stop}) actions.Children.Add(button);
            parent.Children.Add(actions);parent.Children.Add(status);
            var extra=new WrapPanel();foreach(var button in new[]{create,room,test}) extra.Children.Add(button);
            parent.Children.Add(new Expander {Header=UiText.T("More connection tools"),Content=extra,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,7)});
            parent.Children.Add(new TextBlock {Text=UiText.T("UPnP is EXPERIMENTAL and off by default. No Windows firewall settings are changed. Only the invitation room is accepted (up to four clients). Share invitations only with trusted friends: traffic is not encrypted. Stop hosting removes the owned rule; a router honoring its lease expires it within five minutes without renewal after a crash. Hosting ends with the host game, after 30 idle minutes, or after four hours. CGNAT/unsupported routers may require a shared VPN/manual setup."),Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,0,0,8)});
        }

        private Process StartLocalRelay(int port) {
            string privateRoom=txtRoom.Text.Trim().ToUpperInvariant();
            if(privateRoom.Length==0) privateRoom=NewPrivateRoom();
            if(privateRoom.Length!=6 || !ValidRoom(privateRoom)) throw new InvalidOperationException(UiText.T("Choose a valid private room before hosting."));
            txtRoom.Text=privateRoom;
            string root = Path.GetDirectoryName(distDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string python = Path.Combine(root, "tools", "python", "python.exe");
            if (!File.Exists(python)) python = Path.Combine(root, ".tools", "python314", "python.exe");
            string server = Path.Combine(root, "server", "ttlink_server.py");
            if(File.Exists(Path.Combine(distDir,"python","python.exe"))) python=Path.Combine(distDir,"python","python.exe");
            if(File.Exists(Path.Combine(distDir,"server","ttlink_server.py"))) server=Path.Combine(distDir,"server","ttlink_server.py");
            if (!File.Exists(python) || !File.Exists(server))
                throw new InvalidOperationException(UiText.T("The bundled server tools are missing. Restore the complete installation or enter the other PC's server address."));
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners())
                if (endpoint.Port == port) throw new InvalidOperationException(UiText.T("UDP port ") + port + UiText.T(" is already in use. Check the existing server window; another server will not be started."));
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                if (endpoint.Port == port) throw new InvalidOperationException(UiText.T("TCP port ") + port + UiText.T(" is already in use. Close the conflicting application or choose a different server port."));
            var process = Process.Start(new ProcessStartInfo(python, "-I -B \"" + server + "\" --bind 0.0.0.0 --port " + port+" --room "+privateRoom+" --max-clients 4 --no-status") {
                WorkingDirectory = root, UseShellExecute = true });
            if (process == null) throw new InvalidOperationException(UiText.T("Windows did not start the server."));
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
                UiText.T("The server address points to THIS PC, but the Table Tennis server is not responding.\n\nStart the server on this PC now? Keep its window open while playing."),
                UiText.T("Online server is not running"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return false;
            try {
                using (var process = StartLocalRelay(endpoint.Port)) {
                    for (int attempt=0; attempt<12; ++attempt) {
                        if (await Task.Run(() => RelayResponds(endpoint))) return true;
                        if (process.HasExited) break;
                        await Task.Delay(200);
                    }
                }
                MessageBox.Show(this, UiText.T("The server did not become ready. Check its window and any Windows network permission prompt, then try Launch Game again."),
                    UiText.T("Server not ready"), MessageBoxButton.OK, MessageBoxImage.Warning);
            } catch (Exception ex) {
                MessageBox.Show(this, UiText.T("Could not start the local server: ") + UiText.Message(ex.Message),
                    UiText.T("Server not ready"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            parent.Children.Add(CreateSectionTitle(UiText.T("UPDATES")));
            chkAutoUpdates=new CheckBox {Content=UiText.T("Check for updates at startup"),Foreground=Brushes.White,
                IsChecked=true,Margin=new Thickness(0,3,0,6)};
            parent.Children.Add(chkAutoUpdates);
            var actions=new StackPanel {Orientation=Orientation.Horizontal};
            var check=new Button {Content=UiText.T("Check now"),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,0)};
            check.Click+=async (sender,args)=>await CheckUpdates(true);
            var open=new Button {Content=UiText.T("Open updater"),Padding=new Thickness(10,5,10,5)};
            open.Click+=(sender,args)=>OpenUpdater();
            actions.Children.Add(check);actions.Children.Add(open);parent.Children.Add(actions);
            updateStatus=new TextBlock {Text=UiText.T("Updates are installed only after you approve them."),Foreground=Brushes.LightGray,
                TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,6,0,10)};
            parent.Children.Add(updateStatus);
        }
        private async Task CheckUpdates(bool force) {
            if(checkingUpdates) return;
            string updater=Path.Combine(distDir,"tabletennis_updater.exe");
            if(!File.Exists(updater)) {updateStatus.Text=UiText.T("Install the full release once to enable update checks.");return;}
            checkingUpdates=true;
            updateStatus.Text=UiText.T("Checking for updates...");
            try {
                await Task.Run(()=>{
                    using(var process=Process.Start(new ProcessStartInfo(updater,"--check-only"+(force?" --force":"")) {
                        WorkingDirectory=distDir,UseShellExecute=false,CreateNoWindow=true})) {
                        if(process==null) throw new IOException(UiText.T("Could not start the update check."));
                        process.WaitForExit();
                    }
                });
                string root=Path.GetDirectoryName(distDir.TrimEnd('\\'));
                var fields=new Dictionary<string,string>();
                foreach(string line in File.ReadAllLines(Path.Combine(root,".updates","status.txt"))) {
                    int split=line.IndexOf('=');if(split>0) fields[line.Substring(0,split)]=line.Substring(split+1);
                }
                string message,state;
                updateStatus.Text=fields.TryGetValue("message",out message)?UiText.Message(message):UiText.T("Update check completed.");
                if(fields.TryGetValue("state",out state) && state=="available") {
                    updateNotice.Text=updateStatus.Text+UiText.T(" Open UPDATES below or OPTIONS > PC SETTINGS > UPDATES.");
                    updateNotice.Visibility=Visibility.Visible;
                } else updateNotice.Visibility=Visibility.Collapsed;
            } catch(Exception error) {updateStatus.Text=UiText.T("Update check unavailable. You can keep playing. ")+UiText.Message(error.Message);}
            finally {checkingUpdates=false;}
        }
        private void OpenUpdater() {
            SaveSettings();
            string updater=Path.Combine(distDir,"tabletennis_updater.exe");
            if(!File.Exists(updater)) {updateStatus.Text=UiText.T("Update support is missing. Install the current full release once.");return;}
            try {Process.Start(new ProcessStartInfo(updater) {WorkingDirectory=distDir,UseShellExecute=false});}
            catch(Exception error) {updateStatus.Text=UiText.T("Could not open updater: ")+UiText.Message(error.Message);}
        }

        private void AddOnlineHelp(StackPanel parent) {
            var box = new StackPanel { Margin = new Thickness(0,6,0,12) };
            box.Children.Add(new TextBlock {
                Text = UiText.T("PLAY WITH A FRIEND ON YOUR LOCAL NETWORK"),
                Foreground = new SolidColorBrush(Color.FromRgb(0,210,255)),
                FontWeight = FontWeights.Bold, FontSize = 11 });
            box.Children.Add(new TextBlock {
                Text = UiText.T("1. ONE player starts the server below and leaves its window open.\n") +
                    UiText.T("2. BOTH players enable online play and enter the SAME server address.\n") +
                    UiText.T("3. Choose different player names and the SAME private room code (e.g. ABC234).\n") +
                    UiText.T("4. In the game: ONLINE > PLAYER MATCH. One creates a match; the other uses QUICK MATCH.\n") +
                    UiText.T("Launch Game checks a server address belonging to this PC and offers to start the server if it is not responding."),
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
            var start = new Button { Content = UiText.T("Start server on this PC"), Padding = new Thickness(10,5,10,5),
                IsEnabled = bundled, Margin = new Thickness(0,0,8,0) };
            var copy = new Button { Content = UiText.T("Copy this PC's server address"), Padding = new Thickness(10,5,10,5),
                IsEnabled = address.Length != 0 };
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray,
                FontSize = 11, Margin = new Thickness(0,6,0,0) };
            copy.Click += (sender, args) => {
                try { Clipboard.SetText(address); status.Text = UiText.T("Copied ") + address + UiText.T(". Use it on BOTH PCs if this PC hosts the server."); }
                catch (Exception ex) { status.Text = UiText.T("Could not copy: ") + UiText.Message(ex.Message); }
            };
            start.Click += (sender, args) => {
                try {
                    var process = StartLocalRelay(27360);
                    start.IsEnabled = false;
                    process.Exited += (source, eventArgs) => {
                        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() => {
                            start.IsEnabled = true;
                            status.Text = UiText.T("The server window closed. Start it again before hosting a match.");
                        }));
                        process.Dispose();
                    };
                    process.EnableRaisingEvents = true;
                    chkNetwork.IsChecked = true;
                    if (address.Length != 0) txtServer.Text = address;
                    status.Text = UiText.T("Server start requested. Keep its window open; it should say 'listening'. ") +
                        UiText.T("Enter ") + (address.Length == 0 ? UiText.T("this PC's IPv4 address followed by :27360") : address) + UiText.T(" on BOTH PCs. ") +
                        UiText.T("Handle any Windows network permission prompt yourself.");
                } catch (Exception ex) { status.Text = UiText.T("Could not start server: ") + UiText.Message(ex.Message); }
            };
            actions.Children.Add(start); actions.Children.Add(copy); box.Children.Add(actions);
            box.Children.Add(new TextBlock {
                Text = address.Length == 0 ? UiText.T("This PC's address could not be detected. Use the server PC's local IPv4 address followed by :27360.") :
                    UiText.T("If THIS PC hosts the server, enter on BOTH PCs: ") + address,
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 11,
                Margin = new Thickness(0,7,0,0) });
            if (!bundled) box.Children.Add(new TextBlock {
                Text = UiText.T("Server tools are not included in this installation. Ask the host for their running server's address."),
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11,
                Margin = new Thickness(0,6,0,0) });
            box.Children.Add(new TextBlock {
                Text = UiText.T("A local address only works on your own network. Internet play needs a reachable server; no public server is provided."),
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11,
                Margin = new Thickness(0,6,0,0) });
            box.Children.Add(status);
            box.Children.Add(new TextBlock {
                Text = UiText.T("Online compatibility ID: ") + OnlineBuildId() + UiText.T(". Use the same release and engine on BOTH PCs."),
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 11,
                Margin = new Thickness(0,7,0,0) });
            var logs = new Button { Content = UiText.T("Open game logs"), HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10,4,10,4), Margin = new Thickness(0,6,0,0) };
            logs.Click += (sender, args) => {
                try {
                    string directory = Path.Combine(distDir, "logs");
                    Directory.CreateDirectory(directory);
                    Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
                } catch (Exception ex) { status.Text = UiText.T("Could not open logs: ") + UiText.Message(ex.Message); }
            };
            box.Children.Add(logs);
            parent.Children.Add(box);
        }

        private TextBox NetworkField(StackPanel parent, string label, int length) {
            parent.Children.Add(new TextBlock { Text = label, Foreground = Brushes.LightGray, FontSize = 11 });
            var box = new TextBox { Height = 26, MaxLength = length, Margin = new Thickness(0,3,0,7), IsEnabled = HasCommunityEngine };
            parent.Children.Add(box); return box;
        }

        private static readonly string[] LanguageCodes = { "en", "fr", "de", "es", "it" };
        private string SelectedLanguage() {
            int index=comboLanguage.SelectedIndex;
            return index>=0 && index<LanguageCodes.Length ? LanguageCodes[index] : "en";
        }

        private string SelectedAspect() { return comboAspect.SelectedIndex==1?"16:9":comboAspect.SelectedIndex==2?"21:9":comboAspect.SelectedIndex==3?"32:9":"auto"; }

        private void LoadSettings()
        {
            string buttons = "auto", server = "", player = "Player", room = "";
            bool network = false, checkUpdates = true;
            string iso = "";
            string res = "1080p";
            string aspect = "auto";
            string language = "en";
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
                        else if (key == "display_aspect") aspect = val;
                        else if (key == "game_language") language = val;
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

            comboLanguage.SelectedIndex=Math.Max(0,Array.IndexOf(LanguageCodes,language));
            comboButtons.SelectedIndex = buttons == "xbox" ? 1 : buttons == "playstation" ? 2 : 0;
            txtServer.Text = server; txtPlayer.Text = player; txtRoom.Text = room;
            chkNetwork.IsChecked = HasCommunityEngine && network;
            comboAspect.SelectedIndex=aspect=="16:9"?1:aspect=="21:9"?2:aspect=="32:9"?3:0;
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
                    "display_aspect=" + SelectedAspect(),
                    "game_language=" + SelectedLanguage(),
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
                updateStatus.Text=UiText.T("An update is in progress. Finish it or reopen the launcher to recover.");return;
            }
            string selectedIso = txtIsoPath.Text.Trim();
            if (string.IsNullOrEmpty(selectedIso) || !File.Exists(selectedIso))
            {
                MessageBox.Show(UiText.T("Please select a valid Xbox 360 ISO image file using 'Browse...' before launching the game!"), UiText.T("ISO Image Missing"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (chkNetwork.IsChecked == true &&
                (!ValidServer(txtServer.Text.Trim()) ||
                 !Regex.IsMatch(txtPlayer.Text.Trim(), @"^[A-Za-z0-9 _-]{1,15}$") ||
                 !ValidRoom(txtRoom.Text.Trim().ToUpperInvariant()))) {
                MessageBox.Show(UiText.T("Enter a server host (optional port: 1-65535), a player name (1-15 letters/numbers/spaces), and either no room code or exactly 6 characters from ABCDEFGHJKMNPQRSTUVWXYZ23456789. Example: ABC234."), UiText.T("Online settings"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (HasCommunityEngine && chkNetwork.IsChecked == true && !await EnsureLocalRelay()) return;
            if(HasCommunityEngine && chkNetwork.IsChecked==true) {
                string selected=txtServer.Text.Trim();
                if(!await Task.Run(()=>SelectedRelayResponds(selected))) {
                    MessageBox.Show(this,UiText.T("The Table Tennis server at ")+selected+UiText.T(" did not respond over UDP.\n\nAsk the host to start the server and check the address. Internet play needs a reachable public server or a shared VPN; a home-network IP alone is not enough."),UiText.T("Cannot reach online server"),MessageBoxButton.OK,MessageBoxImage.Warning);
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
                MessageBox.Show(UiText.T("tabletennis.exe is missing at: ") + exePath, UiText.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string args = string.Format(
                "--game_data_root=\"{0}\" --resolution_scale={1} --draw_resolution_scale_x={1} --draw_resolution_scale_y={1} --window_width={2} --window_height={3} --fullscreen={4} --swap_post_effect={5} --present_effect={6} --show_fps={7} --framerate={8} --protect_zero=false --gpu_allow_invalid_fetch_constants=true",
                selectedIso, resScale, width, height, isFullscreen.ToString().ToLower(), aa, post, showFps.ToString().ToLower(), targetFps);

            args += " --display_aspect="+SelectedAspect();
            args += " --game_language="+SelectedLanguage();
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
                MessageBox.Show(UiText.T("Could not start the game:\n") + UiText.Message(ex.Message), UiText.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

// BEGIN GENERATED LOCALIZATION
namespace TableTennisLauncher {
    public static class UiText {
        public static string Code = "en";
        private static readonly string[][] Rows = {
            new[]{" AVAILABLE"," DISPONIBLE"," VERFÜGBAR"," DISPONIBLE"," DISPONIBILE"},
            new[]{" Open UPDATES below or OPTIONS > PC SETTINGS > UPDATES."," Ouvrez MISES À JOUR ci-dessous ou OPTIONS > OPTIONS PC > MISES À JOUR."," Unten UPDATES oder OPTIONEN > PC-OPTIONEN > UPDATES öffnen."," Abre ACTUALIZACIONES abajo u OPCIONES > OPCIONES PC > ACTUALIZACIONES."," Apri AGGIORNAMENTI qui sotto oppure OPZIONI > OPZIONI PC > AGGIORNAMENTI."},
            new[]{" Previous router cleanup is pending; retry it on the original router."," Nettoyage du routeur précédent en attente ; réessayez sur le routeur d'origine."," Alte Router-Bereinigung steht aus; am ursprünglichen Router erneut versuchen."," Queda pendiente limpiar el router anterior; reinténtalo en el router original."," Pulizia del router precedente in sospeso; riprova sul router originale."},
            new[]{" did not respond over UDP.\n\nAsk the host to start the server and check the address. Internet play needs a reachable public server or a shared VPN; a home-network IP alone is not enough."," ne répond pas en UDP.\n\nDemandez à l'hôte de démarrer le serveur et vérifiez l'adresse. Internet nécessite un serveur public accessible ou un VPN partagé ; une IP locale ne suffit pas."," antwortet nicht über UDP.\n\nHost bitten, den Server zu starten, und Adresse prüfen. Internet-Spiel braucht einen erreichbaren öffentlichen Server oder gemeinsames VPN; eine Heimnetz-IP allein reicht nicht."," no respondió por UDP.\n\nPide al anfitrión que inicie el servidor y revisa la dirección. Internet necesita un servidor público accesible o VPN compartida; una IP doméstica sola no basta."," non risponde via UDP.\n\nChiedi all'host di avviare il server e verifica l'indirizzo. Internet richiede un server pubblico raggiungibile o VPN condivisa; un IP domestico da solo non basta."},
            new[]{" is already in use. Check the existing server window; another server will not be started."," est déjà utilisé. Vérifiez la fenêtre du serveur existant ; aucun autre serveur ne sera lancé."," wird bereits verwendet. Bestehendes Serverfenster prüfen; kein weiterer Server wird gestartet."," ya está en uso. Revisa la ventana del servidor existente; no se iniciará otro."," è già in uso. Controlla la finestra del server esistente; non verrà avviato un altro server."},
            new[]{" is already in use. Close the conflicting application or choose a different server port."," est déjà utilisé. Fermez l'application concernée ou choisissez un autre port serveur."," wird bereits verwendet. Störende Anwendung schließen oder anderen Serverport wählen."," ya está en uso. Cierra la aplicación en conflicto o elige otro puerto del servidor."," è già in uso. Chiudi l'applicazione in conflitto o scegli un'altra porta server."},
            new[]{" on BOTH PCs. "," sur les DEUX PC. "," auf BEIDEN PCs. "," en AMBOS equipos. "," su ENTRAMBI i PC. "},
            new[]{"%d / %d UNLOCKED","%d / %d DÉBLOQUÉS","%d / %d FREIGESCHALTET","%d / %d DESBLOQUEADOS","%d / %d SBLOCCATI"},
            new[]{"). Use Stop hosting to close it before changing modes. UPnP permission is not retained.","). Arrêtez l'hébergement avant de changer de mode. L'autorisation UPnP n'est pas conservée.","). Vor dem Moduswechsel das Hosting beenden. Die UPnP-Erlaubnis wird nicht gespeichert.","). Deja de alojar antes de cambiar de modo. El permiso UPnP no se conserva.","). Interrompi l'hosting prima di cambiare modalità. Il permesso UPnP non viene conservato."},
            new[]{"* AFTER RESTART     |     FRAME PACING: 60 FPS","* APRÈS REDÉMARRAGE     |     CADENCE : 60 FPS","* NACH NEUSTART     |     BILDRATE: 60 FPS","* TRAS REINICIAR     |     CADENCIA: 60 FPS","* DOPO IL RIAVVIO     |     CADENZA: 60 FPS"},
            new[]{"- ESC : Exit game to Windows (leave Photo mode first).\n- F10 : Show FPS, frame-time graph and presentation timing.\n- F9 / both stick clicks : Photo mode during offline matches.\n- F7 / hold Back : Open achievements.","- Échap : Quitter vers Windows (quitter d'abord le mode photo).\n- F10 : FPS, graphique et mesures d'affichage.\n- F9 / clic sur les deux sticks : Mode photo en match hors ligne.\n- F7 / maintenir Back : Ouvrir les succès.","- Esc: Spiel beenden (zuerst Fotomodus verlassen).\n- F10: FPS, Frame-Zeit-Diagramm und Ausgabemesswerte.\n- F9 / beide Sticks drücken: Fotomodus in Offline-Spielen.\n- F7 / Back halten: Erfolge öffnen.","- Esc: Salir a Windows (sal antes del modo foto).\n- F10: FPS, gráfico de tiempos y mediciones de presentación.\n- F9 / pulsar ambos sticks: Modo foto en partidas sin conexión.\n- F7 / mantener Back: Abrir logros.","- Esc: Esci a Windows (esci prima dalla modalità foto).\n- F10: FPS, grafico dei tempi e misurazioni di presentazione.\n- F9 / premi entrambi gli stick: Modalità foto nelle partite offline.\n- F7 / tieni premuto Back: Apri gli obiettivi."},
            new[]{". Both players need the same room and game build. This checks the relay, not a complete match.",". Les deux joueurs doivent utiliser le même salon et la même version. Ce test vérifie le relais, pas un match complet.",". Beide Spieler benötigen denselben Raum und dieselbe Version. Dieser Test prüft den Relay, kein vollständiges Spiel.",". Ambos jugadores necesitan la misma sala y versión. Esto comprueba el relé, no una partida completa.",". Entrambi i giocatori devono usare la stessa stanza e versione. Il test verifica il relay, non una partita completa."},
            new[]{". Start the host's server and check its address/UDP port. A local IP cannot connect across the internet without a shared VPN; internet hosting needs a reachable public server.",". Démarrez le serveur hôte et vérifiez son adresse/port UDP. Une IP locale ne traverse pas Internet sans VPN partagé ; le serveur doit être accessible publiquement.",". Host-Server starten und Adresse/UDP-Port prüfen. Eine lokale IP funktioniert übers Internet nur mit gemeinsamem VPN; Internet-Hosting braucht einen erreichbaren öffentlichen Server.",". Inicia el servidor del anfitrión y revisa su dirección/puerto UDP. Una IP local no sirve por Internet sin VPN compartida; hace falta un servidor público accesible.",". Avvia il server host e verifica indirizzo/porta UDP. Un IP locale non funziona su Internet senza VPN condivisa; serve un server pubblico raggiungibile."},
            new[]{". Update BOTH PCs to the same build before joining.",". Mettez les DEUX PC à la même version avant de rejoindre.",". Vor dem Beitritt BEIDE PCs auf dieselbe Version aktualisieren.",". Actualiza AMBOS equipos a la misma versión antes de unirte.",". Aggiorna ENTRAMBI i PC alla stessa versione prima di unirti."},
            new[]{". Use it on BOTH PCs if this PC hosts the server.",". Utilisez-la sur les DEUX PC si celui-ci héberge.",". Auf BEIDEN PCs verwenden, wenn dieser PC hostet.",". Úsala en AMBOS equipos si este aloja el servidor.",". Usalo su ENTRAMBI i PC se questo ospita il server."},
            new[]{". Use the same release and engine on BOTH PCs.",". Utilisez la même version et le même moteur sur les DEUX PC.",". Auf BEIDEN PCs dieselbe Version und Engine verwenden.",". Usa la misma versión y motor en AMBOS equipos.",". Usa la stessa versione e motore su ENTRAMBI i PC."},
            new[]{"1  Choose your game disc image","1  Choisissez l'image de votre disque","1  Spiel-Disc-Abbild wählen","1  Elige la imagen del disco","1  Scegli l'immagine del disco"},
            new[]{"1. ONE player starts the server below and leaves its window open.\n","1. UN joueur lance le serveur ci-dessous et garde sa fenêtre ouverte.\n","1. EIN Spieler startet unten den Server und lässt sein Fenster offen.\n","1. UN jugador inicia el servidor de abajo y deja abierta su ventana.\n","1. UN giocatore avvia il server qui sotto e lascia aperta la finestra.\n"},
            new[]{"120 requested / paced at 60","120 demandés / cadence 60","120 gewählt / auf 60 begrenzt","120 solicitados / limitado a 60","120 richiesti / limitato a 60"},
            new[]{"144 requested / paced at 60","144 demandés / cadence 60","144 gewählt / auf 60 begrenzt","144 solicitados / limitado a 60","144 richiesti / limitato a 60"},
            new[]{"1440p (2560x1440) - 2x 3D scale","1440p (2560x1440) - échelle 3D 2x","1440p (2560x1440) - 2x 3D-Skalierung","1440p (2560x1440) - escala 3D 2x","1440p (2560x1440) - scala 3D 2x"},
            new[]{"2  Install location","2  Dossier d'installation","2  Installationsordner","2  Carpeta de instalación","2  Cartella di installazione"},
            new[]{"2. BOTH players enable online play and enter the SAME server address.\n","2. Les DEUX joueurs activent le jeu en ligne et saisissent la MÊME adresse.\n","2. BEIDE aktivieren Online-Spiel und geben DIESELBE Serveradresse ein.\n","2. AMBOS activan el juego en línea e introducen la MISMA dirección.\n","2. ENTRAMBI abilitano il gioco online e inseriscono lo STESSO indirizzo.\n"},
            new[]{"240 requested / paced at 60","240 demandés / cadence 60","240 gewählt / auf 60 begrenzt","240 solicitados / limitado a 60","240 richiesti / limitato a 60"},
            new[]{"2560 x 1080 (2x 3D scale)","2560 x 1080 (échelle 3D 2x)","2560 x 1080 (2x 3D-Skalierung)","2560 x 1080 (escala 3D 2x)","2560 x 1080 (scala 3D 2x)"},
            new[]{"3. Choose different player names and the SAME private room code (e.g. ABC234).\n","3. Choisissez des noms différents et le MÊME salon privé (ex. ABC234).\n","3. Unterschiedliche Namen und DENSELBEN privaten Raumcode wählen (z. B. ABC234).\n","3. Elegid nombres distintos y el MISMO código de sala privada (p. ej. ABC234).\n","3. Scegliete nomi diversi e lo STESSO codice stanza privata (es. ABC234).\n"},
            new[]{"3440 x 1440 (3x 3D scale)","3440 x 1440 (échelle 3D 3x)","3440 x 1440 (3x 3D-Skalierung)","3440 x 1440 (escala 3D 3x)","3440 x 1440 (scala 3D 3x)"},
            new[]{"3840 x 1600 (3x 3D scale)","3840 x 1600 (échelle 3D 3x)","3840 x 1600 (3x 3D-Skalierung)","3840 x 1600 (escala 3D 3x)","3840 x 1600 (scala 3D 3x)"},
            new[]{"4. In the game: ONLINE > PLAYER MATCH. One creates a match; the other uses QUICK MATCH.\n","4. Dans le jeu : ONLINE > PLAYER MATCH. L'un crée un match, l'autre choisit QUICK MATCH.\n","4. Im Spiel: ONLINE > PLAYER MATCH. Einer erstellt das Spiel, der andere wählt QUICK MATCH.\n","4. En el juego: ONLINE > PLAYER MATCH. Uno crea la partida y el otro usa QUICK MATCH.\n","4. Nel gioco: ONLINE > PLAYER MATCH. Uno crea la partita, l'altro usa QUICK MATCH.\n"},
            new[]{"4K (3840x2160) - 3x 3D scale","4K (3840x2160) - échelle 3D 3x","4K (3840x2160) - 3x 3D-Skalierung","4K (3840x2160) - escala 3D 3x","4K (3840x2160) - scala 3D 3x"},
            new[]{"60 FPS (recommended)","60 FPS (recommandé)","60 FPS (empfohlen)","60 FPS (recomendado)","60 FPS (consigliato)"},
            new[]{"720p (1280x720) - 1x Native","720p (1280x720) - 1x natif","720p (1280x720) - 1x nativ","720p (1280x720) - 1x nativo","720p (1280x720) - 1x nativo"},
            new[]{"A downloaded component failed its checksum check. It was not used.","La somme de contrôle d'un composant téléchargé est invalide. Il n'a pas été utilisé.","Die Prüfsumme einer heruntergeladenen Komponente stimmt nicht. Sie wurde nicht verwendet.","Falló la verificación de un componente descargado. No se utilizó.","La verifica di un componente scaricato non è riuscita. Non è stato utilizzato."},
            new[]{"A host session is already running.","Une session hôte est déjà active.","Eine Host-Sitzung läuft bereits.","Ya hay una sesión de anfitrión activa.","Una sessione host è già attiva."},
            new[]{"A local address only works on your own network. Internet play needs a reachable server; no public server is provided.","Une adresse locale fonctionne seulement sur votre réseau. Internet exige un serveur accessible ; aucun serveur public n'est fourni.","Lokale Adressen funktionieren nur im eigenen Netzwerk. Internet-Spiel braucht einen erreichbaren Server; kein öffentlicher Server wird bereitgestellt.","Una dirección local solo funciona en tu red. Jugar por Internet requiere un servidor accesible; no se proporciona uno público.","Un indirizzo locale funziona solo nella tua rete. Internet richiede un server raggiungibile; non viene fornito un server pubblico."},
            new[]{"A path contains an invalid quote.","Un chemin contient un guillemet invalide.","Ein Pfad enthält ein ungültiges Anführungszeichen.","Una ruta contiene comillas no válidas.","Un percorso contiene virgolette non valide."},
            new[]{"A required component is not cached. Internet access is needed for the first installation.","Un composant requis n'est pas en cache. Internet est nécessaire pour la première installation.","Eine benötigte Komponente ist nicht zwischengespeichert. Die Erstinstallation benötigt Internetzugang.","Un componente necesario no está en caché. La primera instalación necesita Internet.","Un componente necessario non è in cache. La prima installazione richiede Internet."},
            new[]{"A server already uses this port. Stop the old server first.","Un serveur utilise déjà ce port. Arrêtez d'abord l'ancien serveur.","Ein Server nutzt diesen Port bereits. Zuerst den alten Server beenden.","Otro servidor usa este puerto. Detén primero el anterior.","Un server usa già questa porta. Arresta prima il server precedente."},
            new[]{"ACHIEVEMENT SOUND","SON DES SUCCÈS","ERFOLGSTON","SONIDO DE LOGROS","SUONO OBIETTIVI"},
            new[]{"ACHIEVEMENT UNLOCKED","SUCCÈS DÉBLOQUÉ","ERFOLG FREIGESCHALTET","LOGRO DESBLOQUEADO","OBIETTIVO SBLOCCATO"},
            new[]{"ACHIEVEMENTS","SUCCÈS","ERFOLGE","LOGROS","OBIETTIVI"},
            new[]{"ANTI-ALIASING","ANTICRÉNELAGE","KANTENGLÄTTUNG","SUAVIZADO","ANTI-ALIASING"},
            new[]{"ANTI-ALIASING & POST-PROCESSING","ANTICRÉNELAGE ET POST-TRAITEMENT","KANTENGLÄTTUNG UND NACHBEARBEITUNG","SUAVIZADO Y POSPROCESADO","ANTI-ALIASING E POST-ELABORAZIONE"},
            new[]{"AUTO","AUTO","AUTO","AUTO","AUTO"},
            new[]{"AUTO / DISPLAY","AUTO / ÉCRAN","AUTO / BILDSCHIRM","AUTO / PANTALLA","AUTO / SCHERMO"},
            new[]{"AUTO follows the controller in use. Changes apply immediately.","AUTO suit la manette utilisée. Application immédiate.","AUTO folgt dem verwendeten Controller. Änderungen gelten sofort.","AUTO detecta el mando en uso. Los cambios se aplican al instante.","AUTO segue il controller in uso. Le modifiche sono immediate."},
            new[]{"AUTO follows the display. Locks preserve proportions with black bars. Applies after restart.","AUTO suit l'écran. Le format fixe conserve les proportions avec des bandes noires. Après redémarrage.","AUTO folgt dem Bildschirm. Feste Formate erhalten Proportionen mit schwarzen Balken. Nach Neustart.","AUTO se adapta a la pantalla. Los formatos fijos conservan las proporciones con bandas negras. Tras reiniciar.","AUTO segue lo schermo. I formati fissi conservano le proporzioni con bande nere. Dopo il riavvio."},
            new[]{"Adding required components to your existing Visual Studio Build Tools 2022","Ajout des composants requis à votre installation Visual Studio Build Tools 2022","Benötigte Komponenten werden zu vorhandenen Visual Studio Build Tools 2022 hinzugefügt","Añadiendo componentes necesarios a Visual Studio Build Tools 2022 existente","Aggiunta dei componenti necessari a Visual Studio Build Tools 2022 esistente"},
            new[]{"Advanced network settings / manual server","Réglages réseau avancés / serveur manuel","Erweiterte Netzwerkeinstellungen / manueller Server","Ajustes de red avanzados / servidor manual","Impostazioni di rete avanzate / server manuale"},
            new[]{"All selections currently run at 60 FPS to preserve game speed. F10 opens a separate timing window with a graph. Submission and Present-call timing do not measure GPU render duration or physical display scanout.","Tous les choix restent à 60 FPS pour préserver la vitesse du jeu. F10 ouvre une fenêtre de mesures et un graphique. Ces mesures n'indiquent ni le temps de rendu GPU ni le balayage physique de l'écran.","Alle Optionen laufen derzeit mit 60 FPS, damit die Spielgeschwindigkeit stimmt. F10 öffnet ein separates Messfenster mit Diagramm. Die Werte messen weder GPU-Renderdauer noch den physischen Bildaufbau des Monitors.","Todas las opciones funcionan a 60 FPS para conservar la velocidad del juego. F10 abre una ventana de medición con un gráfico. No mide el tiempo de renderizado de la GPU ni el barrido físico de la pantalla.","Tutte le opzioni restano a 60 FPS per mantenere la velocità corretta. F10 apre una finestra di misurazione con grafico. I valori non misurano i tempi di rendering GPU né la scansione fisica dello schermo."},
            new[]{"Allow missing Microsoft build components to be installed if needed (Windows may request permission).","Autoriser l'installation des composants Microsoft manquants si nécessaire (Windows peut demander votre accord).","Fehlende Microsoft-Buildkomponenten bei Bedarf installieren lassen (Windows kann eine Erlaubnis anfordern).","Permitir instalar componentes de Microsoft que falten si es necesario (Windows puede pedir permiso).","Consenti di installare i componenti Microsoft mancanti se necessari (Windows potrebbe chiedere il permesso)."},
            new[]{"Allow required components to be downloaded and installed.","Autoriser le téléchargement et l'installation des composants requis.","Herunterladen und Installieren benötigter Komponenten erlauben.","Permitir la descarga e instalación de los componentes necesarios.","Consenti di scaricare e installare i componenti necessari."},
            new[]{"Allow the required components to be downloaded and installed before continuing.","Autorisez le téléchargement et l'installation des composants requis avant de continuer.","Vor dem Fortfahren das Herunterladen und Installieren benötigter Komponenten erlauben.","Permite descargar e instalar los componentes necesarios antes de continuar.","Consenti di scaricare e installare i componenti necessari prima di continuare."},
            new[]{"Also used by the launcher and game after installation.","Également utilisée par le lanceur et le jeu après l'installation.","Wird nach der Installation auch im Launcher und im Spiel verwendet.","También se usa en el lanzador y el juego tras la instalación.","Usata anche nel programma di avvio e nel gioco dopo l'installazione."},
            new[]{"An existing host session is still running (","Une session hôte est toujours active (","Eine Host-Sitzung läuft noch (","Sigue activa una sesión de anfitrión (","Una sessione host è ancora attiva ("},
            new[]{"An update is in progress. Finish it or reopen the launcher to recover.","Mise à jour en cours. Terminez-la ou rouvrez le lanceur pour récupérer.","Ein Update läuft. Abschließen oder Launcher zur Wiederherstellung neu öffnen.","Hay una actualización en curso. Termínala o vuelve a abrir el lanzador para recuperar.","Aggiornamento in corso. Completalo o riapri il programma di avvio per recuperare."},
            new[]{"An update was interrupted. Restore the updater support files or re-run the installer.","Une mise à jour a été interrompue. Restaurez ses fichiers ou relancez l'installation.","Ein Update wurde unterbrochen. Updater-Dateien wiederherstellen oder Installation erneut ausführen.","Se interrumpió una actualización. Restaura sus archivos o ejecuta de nuevo el instalador.","Un aggiornamento è stato interrotto. Ripristina i file o esegui di nuovo l'installazione."},
            new[]{"Anti-Aliasing (AA):","Anticrénelage (AA) :","Kantenglättung (AA):","Suavizado (AA):","Anti-aliasing (AA):"},
            new[]{"Asking your router for an internet connection...","Demande de connexion Internet au routeur...","Internet-Verbindung wird beim Router angefordert...","Solicitando conexión a Internet al router...","Richiesta di connessione Internet al router..."},
            new[]{"Auto - follow display (including 32:9)","Auto - suit l'écran (32:9 inclus)","Auto - Bildschirm folgen (auch 32:9)","Auto - según la pantalla (incluido 32:9)","Auto - segue lo schermo (anche 32:9)"},
            new[]{"Automatic (controller type)","Automatique (type de manette)","Automatisch (Controller-Typ)","Automático (tipo de mando)","Automatico (tipo di controller)"},
            new[]{"BILINEAR","BILINÉAIRE","BILINEAR","BILINEAL","BILINEARE"},
            new[]{"BUTTON ICONS","ICÔNES DES TOUCHES","TASTENSYMBOLE","ICONOS DE BOTONES","ICONE DEI PULSANTI"},
            new[]{"Bilinear (Standard)","Bilinéaire (standard)","Bilinear (Standard)","Bilineal (estándar)","Bilineare (standard)"},
            new[]{"Browse...","Parcourir...","Durchsuchen...","Examinar...","Sfoglia..."},
            new[]{"Building the updated game locally from your ISO","Compilation locale du jeu mis à jour depuis votre ISO","Aktualisiertes Spiel wird lokal aus Ihrer ISO erstellt","Compilando el juego actualizado localmente desde tu ISO","Compilazione locale del gioco aggiornato dalla tua ISO"},
            new[]{"Building your game - please keep this window open","Compilation du jeu - gardez cette fenêtre ouverte","Spiel wird erstellt - dieses Fenster bitte offen lassen","Compilando el juego - mantén abierta esta ventana","Compilazione del gioco - tieni aperta questa finestra"},
            new[]{"CANNOT SAVE PICTURES HERE","IMPOSSIBLE D'ENREGISTRER ICI","HIER KEINE BILDSPEICHERUNG","NO SE PUEDEN GUARDAR FOTOS AQUÍ","IMPOSSIBILE SALVARE FOTO QUI"},
            new[]{"CHANGE","MODIFIER","ÄNDERN","CAMBIAR","MODIFICA"},
            new[]{"CHECK NOW","VÉRIFIER","JETZT PRÜFEN","COMPROBAR","CONTROLLA ORA"},
            new[]{"CLOSE","FERMER","SCHLIESSEN","CERRAR","CHIUDI"},
            new[]{"CONTROLLER","MANETTE","CONTROLLER","MANDO","CONTROLLER"},
            new[]{"CONTROLLER BUTTON ICONS","ICÔNES DES TOUCHES DE MANETTE","CONTROLLER-TASTENSYMBOLE","ICONOS DE BOTONES DEL MANDO","ICONE DEI PULSANTI DEL CONTROLLER"},
            new[]{"COULD NOT CAPTURE THE PICTURE","CAPTURE IMPOSSIBLE","BILDAUFNAHME FEHLGESCHLAGEN","NO SE PUDO CAPTURAR LA FOTO","IMPOSSIBILE ACQUISIRE LA FOTO"},
            new[]{"COULD NOT SAVE THE PICTURE","ENREGISTREMENT IMPOSSIBLE","BILD KONNTE NICHT GESPEICHERT WERDEN","NO SE PUDO GUARDAR LA FOTO","IMPOSSIBILE SALVARE LA FOTO"},
            new[]{"Cancel","Annuler","Abbrechen","Cancelar","Annulla"},
            new[]{"Cancel update","Annuler la mise à jour","Update abbrechen","Cancelar actualización","Annulla aggiornamento"},
            new[]{"Cancelling safely...","Annulation en cours...","Wird sicher abgebrochen...","Cancelando de forma segura...","Annullamento sicuro..."},
            new[]{"Cannot reach online server","Serveur en ligne inaccessible","Online-Server nicht erreichbar","Servidor en línea inaccesible","Server online non raggiungibile"},
            new[]{"Change...","Modifier...","Ändern...","Cambiar...","Cambia..."},
            new[]{"Check for updates at startup","Vérifier les mises à jour au démarrage","Beim Start nach Updates suchen","Buscar actualizaciones al iniciar","Cerca aggiornamenti all'avvio"},
            new[]{"Check now","Vérifier","Jetzt prüfen","Comprobar ahora","Controlla ora"},
            new[]{"Check signed releases. Installation only starts after you approve it in the updater.","Vérifie les versions signées. L'installation exige votre accord dans l'outil de mise à jour.","Prüft signierte Versionen. Die Installation beginnt erst nach Ihrer Zustimmung im Updater.","Comprueba versiones firmadas. La instalación requiere tu aprobación en el actualizador.","Controlla le versioni firmate. L'installazione richiede la tua conferma nel programma di aggiornamento."},
            new[]{"Checking for updates...","Recherche de mises à jour...","Suche nach Updates...","Buscando actualizaciones...","Ricerca aggiornamenti..."},
            new[]{"Checking required components","Vérification des composants requis","Benötigte Komponenten werden geprüft","Comprobando componentes necesarios","Verifica dei componenti necessari"},
            new[]{"Checking the latest signed release...","Vérification de la dernière version signée...","Neueste signierte Version wird geprüft...","Comprobando la última versión firmada...","Verifica dell'ultima versione firmata..."},
            new[]{"Checking your friend's host...","Vérification de l'hôte de votre ami...","Host Ihres Freundes wird geprüft...","Comprobando el anfitrión de tu amigo...","Verifica dell'host del tuo amico..."},
            new[]{"Checking your game disc","Vérification de votre disque","Spiel-Disc wird geprüft","Comprobando tu disco","Verifica del disco"},
            new[]{"Choose ISO...","Choisir l'ISO...","ISO wählen...","Elegir ISO...","Scegli ISO..."},
            new[]{"Choose a folder, rather than the root of a drive.","Choisissez un dossier, pas la racine d'un lecteur.","Einen Ordner wählen, nicht das Stammverzeichnis eines Laufwerks.","Elige una carpeta, no la raíz de una unidad.","Scegli una cartella, non la radice di un'unità."},
            new[]{"Choose a valid install folder.","Choisissez un dossier d'installation valide.","Gültigen Installationsordner wählen.","Elige una carpeta de instalación válida.","Scegli una cartella di installazione valida."},
            new[]{"Choose a valid private room before hosting.","Choisissez un salon privé valide avant d'héberger.","Vor dem Hosting einen gültigen privaten Raum wählen.","Elige una sala privada válida antes de alojar.","Scegli una stanza privata valida prima di ospitare."},
            new[]{"Choose a writable folder for Table Tennis","Choisissez un dossier accessible en écriture pour Table Tennis","Beschreibbaren Ordner für Table Tennis wählen","Elige una carpeta con permiso de escritura para Table Tennis","Scegli una cartella scrivibile per Table Tennis"},
            new[]{"Choose game disc","Choisir le disque du jeu","Spiel-Disc wählen","Elegir disco del juego","Scegli disco di gioco"},
            new[]{"Choose the output filter. Applies after restarting the game.","Choisissez le filtre d'image. Redémarrez le jeu pour appliquer.","Ausgabefilter wählen. Wird nach einem Spielneustart wirksam.","Elige el filtro de imagen. Se aplica al reiniciar el juego.","Scegli il filtro dell'immagine. Riavvia il gioco per applicare."},
            new[]{"Choose this PC's LAN or VPN address.","Choisissez l'adresse LAN ou VPN de ce PC.","LAN- oder VPN-Adresse dieses PCs wählen.","Elige la dirección LAN o VPN de este equipo.","Scegli l'indirizzo LAN o VPN di questo PC."},
            new[]{"Choose your ISO, then click Install.","Choisissez votre ISO, puis cliquez sur Installer.","ISO wählen und auf Installieren klicken.","Elige tu ISO y pulsa Instalar.","Scegli la tua ISO, poi premi Installa."},
            new[]{"Choose your Table Tennis retail disc image","Choisissez l'image de votre disque commercial Table Tennis","Abbild Ihrer Table-Tennis-Original-Disc wählen","Elige la imagen de tu disco original de Table Tennis","Scegli l'immagine del tuo disco originale di Table Tennis"},
            new[]{"Choose your retail Table Tennis ISO first.","Choisissez d'abord l'ISO de votre disque commercial Table Tennis.","Zuerst die ISO Ihrer Table-Tennis-Original-Disc wählen.","Elige primero la ISO de tu disco original de Table Tennis.","Scegli prima la ISO del tuo disco originale di Table Tennis."},
            new[]{"Close the game and this launcher's local server, then install version {0}?\n\nThe game will be rebuilt locally from your ISO. Saves and settings are kept.","Fermer le jeu et le serveur local du lanceur, puis installer la version {0} ?\n\nLe jeu sera recompilé depuis votre ISO. Sauvegardes et réglages seront conservés.","Spiel und lokalen Server dieses Launchers schließen und Version {0} installieren?\n\nDas Spiel wird lokal aus Ihrer ISO neu erstellt. Spielstände und Einstellungen bleiben erhalten.","¿Cerrar el juego y el servidor local del lanzador e instalar la versión {0}?\n\nSe recompilará el juego localmente desde tu ISO. Se conservarán partidas y ajustes.","Chiudere il gioco e il server locale del programma di avvio, poi installare la versione {0}?\n\nIl gioco verrà ricompilato dalla tua ISO. Salvataggi e impostazioni resteranno."},
            new[]{"Connected to your friend's host. Click Launch Game, then ONLINE > PLAYER MATCH > QUICK MATCH.","Connecté à l'hôte de votre ami. Lancez le jeu puis choisissez ONLINE > PLAYER MATCH > QUICK MATCH.","Mit dem Host Ihres Freundes verbunden. Spiel starten, dann ONLINE > PLAYER MATCH > QUICK MATCH wählen.","Conectado al anfitrión de tu amigo. Inicia el juego y elige ONLINE > PLAYER MATCH > QUICK MATCH.","Connesso all'host del tuo amico. Avvia il gioco, poi scegli ONLINE > PLAYER MATCH > QUICK MATCH."},
            new[]{"Contrast Adaptive Sharpening (CAS)","Netteté adaptative au contraste (CAS)","Kontrastadaptive Schärfung (CAS)","Nitidez adaptativa al contraste (CAS)","Nitidezza adattiva al contrasto (CAS)"},
            new[]{"Copied ","Copié ","Kopiert: ","Copiado ","Copiato "},
            new[]{"Copy invitation","Copier l'invitation","Einladung kopieren","Copiar invitación","Copia invito"},
            new[]{"Copy this PC's server address","Copier l'adresse serveur de ce PC","Serveradresse dieses PCs kopieren","Copiar dirección del servidor de este equipo","Copia indirizzo server di questo PC"},
            new[]{"Could not copy: ","Copie impossible : ","Kopieren fehlgeschlagen: ","No se pudo copiar: ","Impossibile copiare: "},
            new[]{"Could not create invitation: ","Création de l'invitation impossible : ","Einladung konnte nicht erstellt werden: ","No se pudo crear la invitación: ","Impossibile creare l'invito: "},
            new[]{"Could not host: ","Hébergement impossible : ","Hosting fehlgeschlagen: ","No se pudo alojar: ","Impossibile ospitare: "},
            new[]{"Could not open logs: ","Journaux impossibles à ouvrir : ","Protokolle konnten nicht geöffnet werden: ","No se pudieron abrir los registros: ","Impossibile aprire i registri: "},
            new[]{"Could not open updater: ","Impossible d'ouvrir l'outil de mise à jour : ","Updater konnte nicht geöffnet werden: ","No se pudo abrir el actualizador: ","Impossibile aprire gli aggiornamenti: "},
            new[]{"Could not start server: ","Serveur impossible à démarrer : ","Server konnte nicht starten: ","No se pudo iniciar el servidor: ","Impossibile avviare il server: "},
            new[]{"Could not start the game:\n","Impossible de lancer le jeu :\n","Spiel konnte nicht starten:\n","No se pudo iniciar el juego:\n","Impossibile avviare il gioco:\n"},
            new[]{"Could not start the local server: ","Impossible de lancer le serveur local : ","Lokaler Server konnte nicht starten: ","No se pudo iniciar el servidor local: ","Impossibile avviare il server locale: "},
            new[]{"Could not start the update check.","Impossible de rechercher les mises à jour.","Update-Prüfung konnte nicht starten.","No se pudo buscar actualizaciones.","Impossibile avviare il controllo aggiornamenti."},
            new[]{"Could not use invitation: ","Invitation inutilisable : ","Einladung konnte nicht verwendet werden: ","No se pudo usar la invitación: ","Impossibile usare l'invito: "},
            new[]{"Creating your local game - this can take a while","Création de votre jeu local - cela peut prendre du temps","Ihr lokales Spiel wird erstellt - dies kann einige Zeit dauern","Creando tu juego local - puede tardar un rato","Creazione del gioco locale - potrebbe richiedere tempo"},
            new[]{"D-PAD: SELECT / CHANGE","CROIX : CHOISIR / MODIFIER","STEUERKREUZ: AUSWAHL","CRUCETA: ELEGIR / CAMBIAR","CROCE: SCEGLI / MODIFICA"},
            new[]{"D-pad up / down","Croix haut / bas","Kreuz hoch / runter","Cruceta arriba / abajo","Croce su / giù"},
            new[]{"DISPLAY RESOLUTION & 3D SCALE","RÉSOLUTION ET ÉCHELLE 3D","AUFLÖSUNG UND 3D-SKALIERUNG","RESOLUCIÓN Y ESCALA 3D","RISOLUZIONE E SCALA 3D"},
            new[]{"Different game builds. Your friend uses ","Versions différentes. Votre ami utilise ","Unterschiedliche Spielversionen. Ihr Freund nutzt ","Versiones diferentes. Tu amigo usa ","Versioni diverse. Il tuo amico usa "},
            new[]{"Downloaded package checksum failed.","La somme de contrôle du paquet téléchargé est invalide.","Die Prüfsumme des heruntergeladenen Pakets stimmt nicht.","Falló la verificación del paquete descargado.","La verifica del pacchetto scaricato non è riuscita."},
            new[]{"Downloaded package exceeds its signed size.","Le paquet téléchargé dépasse la taille signée.","Das heruntergeladene Paket überschreitet seine signierte Größe.","El paquete descargado supera su tamaño firmado.","Il pacchetto scaricato supera la dimensione firmata."},
            new[]{"Downloaded package is incomplete.","Le paquet téléchargé est incomplet.","Das heruntergeladene Paket ist unvollständig.","El paquete descargado está incompleto.","Il pacchetto scaricato è incompleto."},
            new[]{"Downloading verified update packages...","Téléchargement des paquets vérifiés...","Geprüfte Update-Pakete werden heruntergeladen...","Descargando paquetes verificados...","Download dei pacchetti verificati..."},
            new[]{"ENTER","ENTRÉE","EINGABE","INTRO","INVIO"},
            new[]{"EXPERIMENTAL: allow a five-minute, renewable UDP port 27360 rule for this host session. This exposes the game server to internet traffic. Permanent rules are refused. Use only with trusted friends and your own trusted router.","EXPÉRIMENTAL : autoriser une règle UDP 27360 de cinq minutes, renouvelable pour cette session. Le serveur devient accessible depuis Internet. Les règles permanentes sont refusées. À utiliser uniquement avec des amis et votre propre routeur de confiance.","EXPERIMENTELL: Eine erneuerbare UDP-27360-Regel für fünf Minuten für diese Sitzung erlauben. Dadurch ist der Spielserver aus dem Internet erreichbar. Dauerhafte Regeln werden abgelehnt. Nur mit vertrauten Freunden und Ihrem eigenen vertrauenswürdigen Router verwenden.","EXPERIMENTAL: permitir una regla UDP 27360 de cinco minutos, renovable para esta sesión. Expone el servidor al tráfico de Internet. Se rechazan reglas permanentes. Úsalo solo con amigos de confianza y tu propio router de confianza.","SPERIMENTALE: consenti una regola UDP 27360 di cinque minuti, rinnovabile per questa sessione. Espone il server al traffico Internet. Le regole permanenti sono rifiutate. Usalo solo con amici fidati e il tuo router fidato."},
            new[]{"Enable ONLINE and SYSTEM LINK","Activer ONLINE et SYSTEM LINK","ONLINE und SYSTEM LINK aktivieren","Activar ONLINE y SYSTEM LINK","Abilita ONLINE e SYSTEM LINK"},
            new[]{"Enter ","Saisissez ","Eingeben: ","Introduce ","Inserisci "},
            new[]{"Enter a public IPv4:port for your forwarded game port.","Saisissez une IPv4 publique:port pour le port du jeu redirigé.","Öffentliche IPv4:Port für den weitergeleiteten Spielport eingeben.","Introduce IPv4 pública:puerto para el puerto del juego redirigido.","Inserisci IPv4 pubblico:porta per la porta di gioco inoltrata."},
            new[]{"Enter a server and a six-character private room first. A matching game build is required.","Saisissez un serveur et un salon privé de six caractères. La même version du jeu est requise.","Zuerst Server und sechsstelligen privaten Raum eingeben. Dieselbe Spielversion ist erforderlich.","Introduce un servidor y una sala privada de seis caracteres. Se requiere la misma versión del juego.","Inserisci prima un server e una stanza privata di sei caratteri. Serve la stessa versione del gioco."},
            new[]{"Enter a server host (optional port: 1-65535), a player name (1-15 letters/numbers/spaces), and either no room code or exactly 6 characters from ABCDEFGHJKMNPQRSTUVWXYZ23456789. Example: ABC234.","Saisissez un hôte (port facultatif : 1-65535), un nom (1-15 lettres/chiffres/espaces) et aucun code de salon ou 6 caractères parmi ABCDEFGHJKMNPQRSTUVWXYZ23456789. Exemple : ABC234.","Serverhost (optionaler Port: 1-65535), Spielername (1-15 Buchstaben/Ziffern/Leerzeichen) und keinen Raumcode oder genau 6 Zeichen aus ABCDEFGHJKMNPQRSTUVWXYZ23456789 eingeben. Beispiel: ABC234.","Introduce un servidor (puerto opcional: 1-65535), un nombre (1-15 letras/números/espacios) y ningún código de sala o exactamente 6 caracteres de ABCDEFGHJKMNPQRSTUVWXYZ23456789. Ejemplo: ABC234.","Inserisci un host (porta facoltativa: 1-65535), un nome (1-15 lettere/numeri/spazi) e nessun codice stanza oppure 6 caratteri da ABCDEFGHJKMNPQRSTUVWXYZ23456789. Esempio: ABC234."},
            new[]{"Enter a server or use your friend's invitation first.","Saisissez un serveur ou utilisez d'abord l'invitation de votre ami.","Zuerst einen Server eingeben oder die Einladung Ihres Freundes verwenden.","Introduce un servidor o usa primero la invitación de tu amigo.","Inserisci un server o usa prima l'invito del tuo amico."},
            new[]{"Error","Erreur","Fehler","Error","Errore"},
            new[]{"Extracting verified update source","Extraction des sources de mise à jour vérifiées","Geprüfte Update-Quellen werden entpackt","Extrayendo fuentes verificadas de actualización","Estrazione dei sorgenti di aggiornamento verificati"},
            new[]{"F9 / BOTH STICK CLICKS: LEAVE PHOTO MODE","F9 / CLIC SUR LES DEUX STICKS : QUITTER LE MODE PHOTO","F9 / BEIDE STICKS DRÜCKEN: FOTOMODUS VERLASSEN","F9 / PULSAR AMBOS STICKS: SALIR DEL MODO FOTO","F9 / PREMI ENTRAMBI GLI STICK: ESCI DALLA MODALITÀ FOTO"},
            new[]{"FPS / FRAME TIMES","FPS / TEMPS D'IMAGE","FPS / FRAME-ZEITEN","FPS / TIEMPOS DE FOTOGRAMA","FPS / TEMPI FOTOGRAMMA"},
            new[]{"FRAME PACING","CADENCE DES IMAGES","BILDTAKTUNG","CADENCIA DE FOTOGRAMAS","CADENZA FOTOGRAMMI"},
            new[]{"FREE CAMERA","CAMÉRA LIBRE","FREIE KAMERA","CÁMARA LIBRE","CAMERA LIBERA"},
            new[]{"FULLSCREEN","PLEIN ÉCRAN","VOLLBILD","PANTALLA COMPLETA","SCHERMO INTERO"},
            new[]{"FXAA (Fast Approximate AA)","FXAA (anticrénelage rapide)","FXAA (schnelle Kantenglättung)","FXAA (suavizado rápido)","FXAA (anti-aliasing rapido)"},
            new[]{"FXAA Extreme (Maximum Quality)","FXAA extrême (qualité maximale)","FXAA Extreme (höchste Qualität)","FXAA extremo (calidad máxima)","FXAA estremo (qualità massima)"},
            new[]{"Finishing installation","Finalisation de l'installation","Installation wird abgeschlossen","Finalizando instalación","Completamento dell'installazione"},
            new[]{"Framerate Limit (FPS):","Limite d'images (FPS) :","Bildratenlimit (FPS):","Límite de fotogramas (FPS):","Limite fotogrammi (FPS):"},
            new[]{"Fullscreen Mode","Plein écran","Vollbildmodus","Pantalla completa","Schermo intero"},
            new[]{"GAME DISC IMAGE (XBOX 360 ISO)","IMAGE DU DISQUE (ISO XBOX 360)","SPIEL-DISC-ABBILD (XBOX-360-ISO)","IMAGEN DEL DISCO (ISO XBOX 360)","IMMAGINE DEL DISCO (ISO XBOX 360)"},
            new[]{"GAME LANGUAGE","LANGUE DU JEU","SPIELSPRACHE","IDIOMA DEL JUEGO","LINGUA DEL GIOCO"},
            new[]{"Generating your local game","Génération de votre jeu local","Ihr lokales Spiel wird generiert","Generando tu juego local","Generazione del gioco locale"},
            new[]{"HIDE HELP","MASQUER AIDE","HILFE AUS","OCULTAR AYUDA","NASCONDI AIUTO"},
            new[]{"HOLD VIEW OR PRESS F7 TO OPEN","MAINTENIR VIEW OU APPUYER SUR F7","VIEW HALTEN ODER F7 DRÜCKEN","MANTÉN VIEW O PULSA F7 PARA ABRIR","TIENI PREMUTO VIEW O PREMI F7"},
            new[]{"HOST: start your own session and send its invitation. FRIEND: paste it and Join. Both use ONLINE > PLAYER MATCH in the game.","HÔTE : lancez votre session et envoyez l'invitation. AMI : collez-la puis rejoignez. Dans le jeu, utilisez tous deux ONLINE > PLAYER MATCH.","HOST: Eigene Sitzung starten und Einladung senden. FREUND: Einfügen und beitreten. Beide wählen im Spiel ONLINE > PLAYER MATCH.","ANFITRIÓN: crea tu sesión y envía la invitación. AMIGO: pégala y únete. Ambos usan ONLINE > PLAYER MATCH en el juego.","HOST: avvia la sessione e invia l'invito. AMICO: incollalo e unisciti. Entrambi usano ONLINE > PLAYER MATCH nel gioco."},
            new[]{"Handle any Windows network permission prompt yourself.","Répondez vous-même aux demandes réseau de Windows.","Windows-Netzwerkabfragen selbst beantworten.","Responde tú a los permisos de red de Windows.","Rispondi personalmente alle richieste di rete di Windows."},
            new[]{"Height","Hauteur","Höhe","Altura","Altezza"},
            new[]{"Host & copy invitation","Héberger et copier l'invitation","Hosten und Einladung kopieren","Crear sesión y copiar invitación","Ospita e copia invito"},
            new[]{"Host connection (choose your VPN adapter for VPN play)","Connexion hôte (choisissez l'adaptateur VPN pour jouer via VPN)","Host-Verbindung (zum VPN-Spiel den VPN-Adapter wählen)","Conexión del anfitrión (elige el adaptador VPN si juegas por VPN)","Connessione host (scegli l'adattatore VPN per giocare via VPN)"},
            new[]{"Host setup timed out. Stop hosting, then try shared VPN/LAN or manual port forwarding.","Délai de démarrage dépassé. Arrêtez l'hôte, puis essayez VPN/LAN ou la redirection manuelle.","Host-Einrichtung hat zu lange gedauert. Hosting beenden und VPN/LAN oder manuelle Portweiterleitung versuchen.","Se agotó el tiempo de configuración. Deja de alojar y prueba VPN/LAN o redirección manual de puertos.","Tempo di avvio esaurito. Interrompi l'hosting e prova VPN/LAN o l'inoltro manuale delle porte."},
            new[]{"Hosting on your LAN/VPN. Share the invitation with your friend.","Hébergement LAN/VPN actif. Partagez l'invitation avec votre ami.","Hosting im LAN/VPN aktiv. Einladung mit Ihrem Freund teilen.","Alojando en LAN/VPN. Comparte la invitación con tu amigo.","Hosting su LAN/VPN attivo. Condividi l'invito con il tuo amico."},
            new[]{"Hosting prepared. Your friend must test the invitation from their connection; outside reachability is not verified here.","Hébergement préparé. Votre ami doit tester l'invitation depuis sa connexion ; l'accès extérieur n'est pas vérifié ici.","Hosting vorbereitet. Ihr Freund muss die Einladung über seine Verbindung testen; externe Erreichbarkeit wird hier nicht geprüft.","Alojamiento preparado. Tu amigo debe probar la invitación desde su conexión; no se verifica aquí el acceso externo.","Hosting preparato. Il tuo amico deve provare l'invito dalla sua connessione; l'accessibilità esterna non è verificata qui."},
            new[]{"Hosting ready - invitation copied. Send it to your friend, then Launch Game. Hosting stops when your game exits. Internet invitations must be tested by your friend.","Hôte prêt, invitation copiée. Envoyez-la à votre ami puis lancez le jeu. L'hébergement s'arrête à la fermeture du jeu. Votre ami doit tester les invitations Internet.","Host bereit, Einladung kopiert. An Ihren Freund senden und Spiel starten. Hosting endet mit dem Spiel. Internet-Einladungen muss Ihr Freund testen.","Anfitrión listo, invitación copiada. Envíala a tu amigo e inicia el juego. El alojamiento termina al cerrar el juego. Tu amigo debe probar las invitaciones de Internet.","Host pronto, invito copiato. Invialo al tuo amico e avvia il gioco. L'hosting termina alla chiusura del gioco. Il tuo amico deve provare gli inviti Internet."},
            new[]{"Hosting ready. Copy the invitation from the box and send it to your friend. Clipboard access was unavailable.","Hôte prêt. Copiez l'invitation du champ et envoyez-la à votre ami. Presse-papiers inaccessible.","Host bereit. Einladung aus dem Feld kopieren und an Ihren Freund senden. Zwischenablage nicht verfügbar.","Anfitrión listo. Copia la invitación del campo y envíala a tu amigo. El portapapeles no estaba disponible.","Host pronto. Copia l'invito dal campo e invialo al tuo amico. Appunti non disponibili."},
            new[]{"Hosting stopped.","Hébergement arrêté.","Hosting beendet.","Alojamiento detenido.","Hosting interrotto."},
            new[]{"Hosting stopped; router cleanup completed.","Hébergement arrêté ; nettoyage du routeur terminé.","Hosting beendet; Router-Bereinigung abgeschlossen.","Alojamiento detenido; limpieza del router completada.","Hosting interrotto; pulizia del router completata."},
            new[]{"ISO Image Missing","Image ISO manquante","ISO-Abbild fehlt","Falta la imagen ISO","Immagine ISO mancante"},
            new[]{"Icon selection applies to achievements in this build.","Le choix des icônes s'applique aux succès dans cette version.","Die Symbolauswahl gilt in dieser Version für Erfolge.","La selección de iconos se aplica a los logros en esta versión.","La scelta delle icone si applica agli obiettivi in questa versione."},
            new[]{"If THIS PC hosts the server, enter on BOTH PCs: ","Si CE PC héberge le serveur, saisissez sur les DEUX PC : ","Wenn DIESER PC hostet, auf BEIDEN PCs eingeben: ","Si ESTE EQUIPO aloja el servidor, introduce en AMBOS: ","Se QUESTO PC ospita il server, inserisci su ENTRAMBI: "},
            new[]{"Install","Installer","Installieren","Instalar","Installa"},
            new[]{"Install Table Tennis update","Installer la mise à jour Table Tennis","Table-Tennis-Update installieren","Instalar actualización de Table Tennis","Installa aggiornamento Table Tennis"},
            new[]{"Install the full release once to enable update checks.","Installez une version complète pour activer les vérifications.","Einmal die vollständige Version installieren, um Update-Prüfungen zu aktivieren.","Instala una versión completa para activar la búsqueda de actualizaciones.","Installa una versione completa per attivare il controllo aggiornamenti."},
            new[]{"Install update","Installer la mise à jour","Update installieren","Instalar actualización","Installa aggiornamento"},
            new[]{"Install {0}","Installer {0}","{0} installieren","Instalar {0}","Installa {0}"},
            new[]{"Installation details","Détails de l'installation","Installationsdetails","Detalles de instalación","Dettagli di installazione"},
            new[]{"Installation recovery completed.","Récupération de l'installation terminée.","Installation wiederhergestellt.","Recuperación de la instalación completada.","Ripristino dell'installazione completato."},
            new[]{"Installation stopped. ","Installation arrêtée. ","Installation beendet. ","Instalación detenida. ","Installazione interrotta. "},
            new[]{"Installation stopped: ","Installation arrêtée : ","Installation beendet: ","Instalación detenida: ","Installazione interrotta: "},
            new[]{"Installed version: unknown","Version installée : inconnue","Installierte Version: unbekannt","Versión instalada: desconocida","Versione installata: sconosciuta"},
            new[]{"Installed version: {0}","Version installée : {0}","Installierte Version: {0}","Versión instalada: {0}","Versione installata: {0}"},
            new[]{"Installing Microsoft components - approve the Windows permission prompt if shown","Installation des composants Microsoft - acceptez la demande Windows si elle apparaît","Microsoft-Komponenten werden installiert - Windows-Abfrage bestätigen, falls angezeigt","Instalando componentes de Microsoft - acepta la solicitud de Windows si aparece","Installazione dei componenti Microsoft - approva la richiesta di Windows se compare"},
            new[]{"Installing required Microsoft build components","Installation des composants Microsoft requis","Benötigte Microsoft-Buildkomponenten werden installiert","Instalando componentes de compilación de Microsoft necesarios","Installazione dei componenti di compilazione Microsoft necessari"},
            new[]{"Installing update support","Installation de l'outil de mise à jour","Update-Unterstützung wird installiert","Instalando soporte de actualización","Installazione del supporto aggiornamenti"},
            new[]{"Installing verified files","Installation des fichiers vérifiés","Geprüfte Dateien werden installiert","Instalando archivos verificados","Installazione dei file verificati"},
            new[]{"Internet - I forwarded the port manually","Internet - port redirigé manuellement","Internet - Port manuell weitergeleitet","Internet - puerto redirigido manualmente","Internet - porta inoltrata manualmente"},
            new[]{"Internet - UPnP (EXPERIMENTAL, opt-in)","Internet - UPnP (EXPÉRIMENTAL, facultatif)","Internet - UPnP (EXPERIMENTELL, freiwillig)","Internet - UPnP (EXPERIMENTAL, opcional)","Internet - UPnP (SPERIMENTALE, facoltativo)"},
            new[]{"Invalid installer payload.","Fichiers d'installation invalides.","Ungültige Installationsdateien.","Archivos de instalación no válidos.","File di installazione non validi."},
            new[]{"Invitation (paste your friend's TT1 code)","Invitation (collez le code TT1 de votre ami)","Einladung (TT1-Code Ihres Freundes einfügen)","Invitación (pega el código TT1 de tu amigo)","Invito (incolla il codice TT1 del tuo amico)"},
            new[]{"Invitation copied. It contains the server address, private room and build ID. Share it with your friend. The server must be running; local IPs only work on the same network or VPN.","Invitation copiée : adresse du serveur, salon privé et identifiant de version. Partagez-la avec votre ami. Le serveur doit rester actif ; une IP locale ne fonctionne que sur le même réseau ou VPN.","Einladung kopiert: Serveradresse, privater Raum und Versions-ID. Mit Ihrem Freund teilen. Der Server muss laufen; lokale IPs funktionieren nur im selben Netzwerk oder VPN.","Invitación copiada: dirección del servidor, sala privada e ID de versión. Compártela con tu amigo. El servidor debe estar activo; las IP locales solo sirven en la misma red o VPN.","Invito copiato: indirizzo server, stanza privata e ID versione. Condividilo con il tuo amico. Il server deve essere attivo; gli IP locali funzionano solo nella stessa rete o VPN."},
            new[]{"Join invitation","Rejoindre l'invitation","Einladung beitreten","Unirse por invitación","Accetta invito"},
            new[]{"KEYBOARD & MOUSE","CLAVIER ET SOURIS","TASTATUR UND MAUS","TECLADO Y RATÓN","TASTIERA E MOUSE"},
            new[]{"L1 slow / R1 fast","L1 lent / R1 rapide","L1 langsam / R1 schnell","L1 lento / R1 rápido","L1 lento / R1 veloce"},
            new[]{"LAUNCH GAME","LANCER LE JEU","SPIEL STARTEN","INICIAR JUEGO","AVVIA GIOCO"},
            new[]{"LB slow / RB fast","LB lent / RB rapide","LB langsam / RB schnell","LB lento / RB rápido","LB lento / RB veloce"},
            new[]{"LEAVE","QUITTER","VERLASSEN","SALIR","ESCI"},
            new[]{"LOADING MAIN MENU...","CHARGEMENT DU MENU...","HAUPTMENÜ WIRD GELADEN...","CARGANDO MENÚ PRINCIPAL...","CARICAMENTO MENU..."},
            new[]{"LOCKED","VERROUILLÉ","GESPERRT","BLOQUEADO","BLOCCATO"},
            new[]{"Language","Langue","Sprache","Idioma","Lingua"},
            new[]{"Language applies to the launcher immediately and to the game on its next start.","La langue s'applique immédiatement au lanceur et au prochain démarrage du jeu.","Die Sprache gilt sofort im Launcher und beim nächsten Spielstart im Spiel.","El idioma cambia al instante en el lanzador y al iniciar de nuevo el juego.","La lingua cambia subito nel programma di avvio e al prossimo avvio del gioco."},
            new[]{"Later","Plus tard","Später","Más tarde","Più tardi"},
            new[]{"Launch Game checks a server address belonging to this PC and offers to start the server if it is not responding.","Le lancement vérifie une adresse de ce PC et propose de démarrer le serveur s'il ne répond pas.","Spiel starten prüft eine Serveradresse dieses PCs und bietet bei fehlender Antwort den Serverstart an.","Iniciar juego comprueba una dirección de este equipo y ofrece iniciar el servidor si no responde.","Avvia gioco controlla un indirizzo di questo PC e propone di avviare il server se non risponde."},
            new[]{"Left stick","Stick gauche","Linker Stick","Stick izquierdo","Stick sinistro"},
            new[]{"Lock 16:9 - preserve picture, add black bars","Fixe 16:9 - proportions conservées, bandes noires","Fest 16:9 - Proportionen erhalten, schwarze Balken","Fijo 16:9 - conserva proporciones, añade bandas negras","Fisso 16:9 - conserva proporzioni, aggiunge bande nere"},
            new[]{"Lock 21:9 - preserve picture, add black bars","Fixe 21:9 - proportions conservées, bandes noires","Fest 21:9 - Proportionen erhalten, schwarze Balken","Fijo 21:9 - conserva proporciones, añade bandas negras","Fisso 21:9 - conserva proporzioni, aggiunge bande nere"},
            new[]{"Lock 32:9 - preserve picture, add black bars","Fixe 32:9 - proportions conservées, bandes noires","Fest 32:9 - Proportionen erhalten, schwarze Balken","Fijo 32:9 - conserva proporciones, añade bandas negras","Fisso 32:9 - conserva proporzioni, aggiunge bande nere"},
            new[]{"Look","Regarder","Blick","Mirar","Guarda"},
            new[]{"Microsoft component installation did not complete (code {0}). Open Visual Studio Installer, update Visual Studio Build Tools 2022, then choose Modify to add C++ build tools, Windows SDK, Clang and CMake before retrying.","L'installation des composants Microsoft a échoué (code {0}). Ouvrez Visual Studio Installer, mettez à jour Visual Studio Build Tools 2022 puis choisissez Modifier pour ajouter les outils C++, le SDK Windows, Clang et CMake avant de réessayer.","Microsoft-Komponenten wurden nicht vollständig installiert (Code {0}). Visual Studio Installer öffnen, Visual Studio Build Tools 2022 aktualisieren und über Ändern C++-Buildtools, Windows SDK, Clang und CMake hinzufügen. Dann erneut versuchen.","La instalación de componentes Microsoft no terminó (código {0}). Abre Visual Studio Installer, actualiza Visual Studio Build Tools 2022 y elige Modificar para añadir herramientas C++, Windows SDK, Clang y CMake antes de reintentar.","Installazione dei componenti Microsoft incompleta (codice {0}). Apri Visual Studio Installer, aggiorna Visual Studio Build Tools 2022 e scegli Modifica per aggiungere strumenti C++, Windows SDK, Clang e CMake prima di riprovare."},
            new[]{"Microsoft terms","Conditions Microsoft","Microsoft-Bedingungen","Términos de Microsoft","Condizioni Microsoft"},
            new[]{"More connection tools","Autres outils de connexion","Weitere Verbindungswerkzeuge","Más herramientas de conexión","Altri strumenti di connessione"},
            new[]{"Mouse drag","Glisser la souris","Maus ziehen","Arrastrar ratón","Trascina il mouse"},
            new[]{"Move","Déplacer","Bewegen","Mover","Sposta"},
            new[]{"New private room","Nouveau salon privé","Neuer privater Raum","Nueva sala privada","Nuova stanza privata"},
            new[]{"New room selected. Start hosting to share a new invitation.","Nouveau salon choisi. Lancez l'hébergement pour partager une nouvelle invitation.","Neuer Raum gewählt. Hosting starten, um eine neue Einladung zu teilen.","Nueva sala elegida. Inicia el alojamiento para compartir una invitación nueva.","Nuova stanza scelta. Avvia l'hosting per condividere un nuovo invito."},
            new[]{"No Table Tennis UDP response from ","Aucune réponse UDP Table Tennis depuis ","Keine Table-Tennis-UDP-Antwort von ","Sin respuesta UDP de Table Tennis desde ","Nessuna risposta UDP Table Tennis da "},
            new[]{"No cap requested / paced at 60","Sans limite demandée / cadence 60","Unbegrenzt gewählt / auf 60 begrenzt","Sin límite solicitado / limitado a 60","Nessun limite richiesto / limitato a 60"},
            new[]{"No host connection was detected. Connect your network/VPN and reopen the launcher.","Aucune connexion hôte détectée. Connectez le réseau/VPN et rouvrez le lanceur.","Keine Host-Verbindung erkannt. Netzwerk/VPN verbinden und Launcher neu öffnen.","No se detectó una conexión del anfitrión. Conecta la red/VPN y vuelve a abrir el lanzador.","Nessuna connessione host rilevata. Collega rete/VPN e riapri il programma di avvio."},
            new[]{"No host process is running. Unrenewed leases should expire; the next host start retries journaled cleanup.","Aucun hôte actif. Les baux non renouvelés doivent expirer ; le prochain démarrage retentera le nettoyage enregistré.","Kein Host-Prozess aktiv. Nicht erneuerte Freigaben sollten ablaufen; der nächste Start versucht die protokollierte Bereinigung erneut.","No hay un anfitrión activo. Las reglas sin renovar deberían caducar; el próximo inicio reintentará la limpieza pendiente.","Nessun processo host attivo. Le regole non rinnovate dovrebbero scadere; il prossimo avvio riproverà la pulizia registrata."},
            new[]{"No host session has been started here.","Aucune session hôte n'a été lancée ici.","Hier wurde keine Host-Sitzung gestartet.","No se ha iniciado una sesión de anfitrión aquí.","Nessuna sessione host è stata avviata qui."},
            new[]{"No newer signed update is available.","Aucune mise à jour signée plus récente n'est disponible.","Keine neuere signierte Version verfügbar.","No hay una actualización firmada más reciente.","Non è disponibile un aggiornamento firmato più recente."},
            new[]{"None (Off)","Aucun (désactivé)","Keine (aus)","Ninguno (desactivado)","Nessuno (disattivato)"},
            new[]{"OFF","NON","AUS","NO","NO"},
            new[]{"ON","OUI","EIN","SÍ","SÌ"},
            new[]{"ONLINE PLAY (EXPERIMENTAL)","JEU EN LIGNE (EXPÉRIMENTAL)","ONLINE-SPIEL (EXPERIMENTELL)","JUEGO EN LÍNEA (EXPERIMENTAL)","GIOCO ONLINE (SPERIMENTALE)"},
            new[]{"OPTIONS > PC SETTINGS > UPDATES","OPTIONS > OPTIONS PC > MISES À JOUR","OPTIONEN > PC-OPTIONEN > UPDATES","OPCIONES > OPCIONES PC > ACTUALIZACIONES","OPZIONI > OPZIONI PC > AGGIORNAMENTI"},
            new[]{"Online compatibility ID: ","Identifiant de compatibilité en ligne : ","Online-Kompatibilitäts-ID: ","ID de compatibilidad en línea: ","ID compatibilità online: "},
            new[]{"Online play is unavailable in this build.","Le jeu en ligne est indisponible dans cette version.","Online-Spiel ist in dieser Version nicht verfügbar.","El juego en línea no está disponible en esta versión.","Il gioco online non è disponibile in questa versione."},
            new[]{"Online server is not running","Serveur en ligne arrêté","Online-Server läuft nicht","Servidor en línea detenido","Server online non attivo"},
            new[]{"Online settings","Réglages en ligne","Online-Einstellungen","Ajustes en línea","Impostazioni online"},
            new[]{"Open game logs","Ouvrir les journaux du jeu","Spielprotokolle öffnen","Abrir registros del juego","Apri registri del gioco"},
            new[]{"Open log","Ouvrir le journal","Protokoll öffnen","Abrir registro","Apri registro"},
            new[]{"Open the log for details; you can retry after correcting the problem.","Ouvrez le journal pour les détails ; réessayez après avoir corrigé le problème.","Details im Protokoll ansehen; nach Beheben des Problems erneut versuchen.","Abre el registro para ver detalles; puedes reintentar tras corregir el problema.","Apri il registro per i dettagli; puoi riprovare dopo aver risolto il problema."},
            new[]{"Open the separate F10 timing window. Gameplay stays paced at 60 FPS.","Ouvre la fenêtre de mesures F10. Le jeu reste cadencé à 60 FPS.","Öffnet das separate F10-Messfenster. Das Spiel bleibt auf 60 FPS begrenzt.","Abre la ventana de medición F10. El juego se mantiene a 60 FPS.","Apre la finestra delle misurazioni F10. Il gioco resta a 60 FPS."},
            new[]{"Open updater","Ouvrir l'outil de mise à jour","Updater öffnen","Abrir actualizador","Apri aggiornamenti"},
            new[]{"PC Port (ReXGlue SDK) - Configuration & Launcher","Version PC (ReXGlue SDK) - Réglages et lancement","PC-Version (ReXGlue SDK) - Einstellungen und Start","Versión PC (ReXGlue SDK) - Ajustes e inicio","Versione PC (ReXGlue SDK) - Impostazioni e avvio"},
            new[]{"PC SETTINGS","OPTIONS PC","PC-OPTIONEN","OPCIONES PC","OPZIONI PC"},
            new[]{"PERFORMANCE OVERLAY","AFFICHAGE DES PERFORMANCES","LEISTUNGSANZEIGE","INDICADOR DE RENDIMIENTO","INDICATORE PRESTAZIONI"},
            new[]{"PHOTO MODE","MODE PHOTO","FOTOMODUS","MODO FOTO","MODALITÀ FOTO"},
            new[]{"PHOTO MODE CAMERA IS UNAVAILABLE","CAMÉRA PHOTO INDISPONIBLE","FOTOKAMERA NICHT VERFÜGBAR","CÁMARA DEL MODO FOTO NO DISPONIBLE","CAMERA DELLA MODALITÀ FOTO NON DISPONIBILE"},
            new[]{"PHOTO MODE CLOSED FOR NETWORK PLAY","MODE PHOTO FERMÉ POUR LE JEU EN RÉSEAU","FOTOMODUS FÜR NETZWERKSPIEL GESCHLOSSEN","MODO FOTO CERRADO PARA JUGAR EN RED","MODALITÀ FOTO CHIUSA PER IL GIOCO IN RETE"},
            new[]{"PHOTO MODE COULD NOT INITIALIZE","ÉCHEC DU DÉMARRAGE DU MODE PHOTO","FOTOMODUS KONNTE NICHT STARTEN","NO SE PUDO INICIAR EL MODO FOTO","IMPOSSIBILE AVVIARE LA MODALITÀ FOTO"},
            new[]{"PHOTO MODE IS NOT AVAILABLE IN NETWORK MATCHES","MODE PHOTO INDISPONIBLE EN RÉSEAU","FOTOMODUS IN NETZWERKSPIELEN NICHT VERFÜGBAR","MODO FOTO NO DISPONIBLE EN PARTIDAS EN RED","MODALITÀ FOTO NON DISPONIBILE NELLE PARTITE IN RETE"},
            new[]{"PHOTO MODE OFF","MODE PHOTO DÉSACTIVÉ","FOTOMODUS AUS","MODO FOTO DESACTIVADO","MODALITÀ FOTO DISATTIVATA"},
            new[]{"PHOTO MODE ON","MODE PHOTO ACTIVÉ","FOTOMODUS EIN","MODO FOTO ACTIVADO","MODALITÀ FOTO ATTIVATA"},
            new[]{"PHOTO MODE WORKS DURING A MATCH","MODE PHOTO DISPONIBLE PENDANT UN MATCH","FOTOMODUS IST WÄHREND EINES SPIELS VERFÜGBAR","EL MODO FOTO FUNCIONA DURANTE UNA PARTIDA","LA MODALITÀ FOTO FUNZIONA DURANTE UNA PARTITA"},
            new[]{"PHOTO MODE WORKS DURING AN OFFLINE MATCH","MODE PHOTO DISPONIBLE PENDANT UN MATCH HORS LIGNE","FOTOMODUS IST WÄHREND EINES OFFLINE-SPIELS VERFÜGBAR","EL MODO FOTO FUNCIONA DURANTE UNA PARTIDA SIN CONEXIÓN","LA MODALITÀ FOTO FUNZIONA DURANTE UNA PARTITA OFFLINE"},
            new[]{"PICTURE ASPECT","FORMAT D'IMAGE","BILDFORMAT","RELACIÓN DE ASPECTO","FORMATO IMMAGINE"},
            new[]{"PICTURE ASPECT RATIO","FORMAT DE L'IMAGE","BILDSEITENVERHÄLTNIS","RELACIÓN DE ASPECTO","RAPPORTO D'ASPETTO"},
            new[]{"PICTURE IS STILL SAVING","ENREGISTREMENT EN COURS","BILD WIRD NOCH GESPEICHERT","LA FOTO SE ESTÁ GUARDANDO","SALVATAGGIO FOTO IN CORSO"},
            new[]{"PLAY WITH A FRIEND","JOUER AVEC UN AMI","MIT EINEM FREUND SPIELEN","JUGAR CON UN AMIGO","GIOCA CON UN AMICO"},
            new[]{"PLAY WITH A FRIEND ON YOUR LOCAL NETWORK","JOUER AVEC UN AMI SUR LE RÉSEAU LOCAL","MIT FREUND IM LOKALEN NETZWERK SPIELEN","JUGAR CON UN AMIGO EN LA RED LOCAL","GIOCA CON UN AMICO NELLA RETE LOCALE"},
            new[]{"Paste the complete TT1 invitation from your friend.","Collez l'invitation TT1 complète de votre ami.","Vollständige TT1-Einladung Ihres Freundes einfügen.","Pega la invitación TT1 completa de tu amigo.","Incolla l'invito TT1 completo del tuo amico."},
            new[]{"Play","Jouer","Spielen","Jugar","Gioca"},
            new[]{"Play a sound when an achievement unlocks.","Joue un son lorsqu'un succès est débloqué.","Spielt beim Freischalten eines Erfolgs einen Ton ab.","Reproduce un sonido al desbloquear un logro.","Riproduce un suono quando sblocchi un obiettivo."},
            new[]{"Player name","Nom du joueur","Spielername","Nombre de jugador","Nome giocatore"},
            new[]{"Please select a valid Xbox 360 ISO image file using 'Browse...' before launching the game!","Choisissez une image ISO Xbox 360 valide avec Parcourir avant de lancer le jeu !","Vor dem Spielstart über Durchsuchen ein gültiges Xbox-360-ISO-Abbild wählen!","¡Selecciona una imagen ISO Xbox 360 válida con Examinar antes de iniciar el juego!","Scegli un'immagine ISO Xbox 360 valida con Sfoglia prima di avviare il gioco!"},
            new[]{"Preparing game build support","Préparation des outils de compilation","Build-Unterstützung wird vorbereitet","Preparando herramientas de compilación","Preparazione degli strumenti di compilazione"},
            new[]{"Preparing installation...","Préparation de l'installation...","Installation wird vorbereitet...","Preparando instalación...","Preparazione dell'installazione..."},
            new[]{"Preparing the installer","Préparation du programme d'installation","Installationsprogramm wird vorbereitet","Preparando el instalador","Preparazione del programma di installazione"},
            new[]{"Preparing the matched game engine","Préparation du moteur de jeu correspondant","Passende Spiel-Engine wird vorbereitet","Preparando el motor de juego compatible","Preparazione del motore di gioco compatibile"},
            new[]{"Preparing your local game files","Préparation des fichiers locaux du jeu","Lokale Spieldateien werden vorbereitet","Preparando archivos locales del juego","Preparazione dei file locali del gioco"},
            new[]{"Previous hosting state needs checking. Stop hosting before starting another session.","L'état de l'hébergement précédent doit être vérifié. Arrêtez-le avant une nouvelle session.","Der vorherige Hosting-Status muss geprüft werden. Vor einer neuen Sitzung das Hosting beenden.","Hay que revisar la sesión anterior. Deja de alojar antes de iniciar otra.","Verifica lo stato dell'hosting precedente. Interrompilo prima di avviare un'altra sessione."},
            new[]{"Private room (empty = public)","Salon privé (vide = public)","Privater Raum (leer = öffentlich)","Sala privada (vacío = pública)","Stanza privata (vuoto = pubblica)"},
            new[]{"Project licenses","Licences du projet","Projektlizenzen","Licencias del proyecto","Licenze del progetto"},
            new[]{"Python terms","Conditions Python","Python-Bedingungen","Términos de Python","Condizioni Python"},
            new[]{"QUIT TO DESKTOP","QUITTER LE JEU","SPIEL BEENDEN","SALIR DEL JUEGO","ESCI DAL GIOCO"},
            new[]{"RESET VIEW","RÉINITIALISER","ANSICHT RESET","RESTABLECER","RIPRISTINA"},
            new[]{"RESOLUTION","RÉSOLUTION","AUFLÖSUNG","RESOLUCIÓN","RISOLUZIONE"},
            new[]{"Ready","Prêt","Bereit","Listo","Pronto"},
            new[]{"Ready to play!","Prêt à jouer !","Bereit zum Spielen!","¡Listo para jugar!","Pronto a giocare!"},
            new[]{"Recheck / repair","Revérifier / réparer","Prüfen / reparieren","Comprobar / reparar","Verifica / ripara"},
            new[]{"Recovery did not complete. Keep this log and do not replace files manually.","Récupération incomplète. Conservez ce journal et ne remplacez pas les fichiers manuellement.","Wiederherstellung unvollständig. Dieses Protokoll aufbewahren und Dateien nicht manuell ersetzen.","La recuperación no se completó. Guarda este registro y no reemplaces archivos manualmente.","Ripristino incompleto. Conserva questo registro e non sostituire i file manualmente."},
            new[]{"Required Microsoft components are missing. Allow their installation to continue.","Des composants Microsoft requis sont manquants. Autorisez leur installation pour continuer.","Benötigte Microsoft-Komponenten fehlen. Ihre Installation zum Fortfahren erlauben.","Faltan componentes de Microsoft necesarios. Permite instalarlos para continuar.","Mancano componenti Microsoft necessari. Consenti l'installazione per continuare."},
            new[]{"Required components","Composants requis","Benötigte Komponenten","Componentes necesarios","Componenti necessari"},
            new[]{"Required components are still unavailable. Open the installation log for details.","Les composants requis restent indisponibles. Consultez le journal d'installation.","Benötigte Komponenten weiterhin nicht verfügbar. Details im Installationsprotokoll ansehen.","Los componentes necesarios siguen sin estar disponibles. Consulta el registro de instalación.","I componenti necessari non sono ancora disponibili. Consulta il registro di installazione."},
            new[]{"Restart Windows, then open this installer again to finish.","Redémarrez Windows puis rouvrez ce programme pour terminer.","Windows neu starten und dieses Installationsprogramm zum Abschließen erneut öffnen.","Reinicia Windows y vuelve a abrir este instalador para terminar.","Riavvia Windows e riapri questo programma per completare l'installazione."},
            new[]{"Restore previous version","Restaurer la version précédente","Vorherige Version wiederherstellen","Restaurar versión anterior","Ripristina versione precedente"},
            new[]{"Restore the previous game version? Current settings and saves stay in place.","Restaurer la version précédente ? Les réglages et sauvegardes actuels seront conservés.","Vorherige Spielversion wiederherstellen? Aktuelle Einstellungen und Spielstände bleiben erhalten.","¿Restaurar la versión anterior? Se conservarán los ajustes y partidas actuales.","Ripristinare la versione precedente? Impostazioni e salvataggi attuali resteranno."},
            new[]{"Right stick","Stick droit","Rechter Stick","Stick derecho","Stick destro"},
            new[]{"Rockstar Games presents Table Tennis - Settings & Launcher","Rockstar Games presents Table Tennis - Réglages et lancement","Rockstar Games presents Table Tennis - Einstellungen und Start","Rockstar Games presents Table Tennis - Ajustes e inicio","Rockstar Games presents Table Tennis - Impostazioni e avvio"},
            new[]{"SAVE & BACK","SAUVER / RETOUR","SPEICHERN / ZURÜCK","GUARDAR / VOLVER","SALVA / INDIETRO"},
            new[]{"SAVE PICTURE","ENREGISTRER","BILD SPEICHERN","GUARDAR FOTO","SALVA FOTO"},
            new[]{"SAVED photos/","ENREGISTRÉ photos/","GESPEICHERT photos/","GUARDADO photos/","SALVATO photos/"},
            new[]{"SCROLL","DÉFILER","SCROLLEN","DESPLAZAR","SCORRI"},
            new[]{"SHARPENING / SCALING","NETTETÉ / MISE À L'ÉCHELLE","SCHÄRFE / SKALIERUNG","NITIDEZ / ESCALADO","NITIDEZZA / SCALATURA"},
            new[]{"SPACE","ESPACE","LEERTASTE","ESPACIO","SPAZIO"},
            new[]{"Same network / shared VPN (default)","Même réseau / VPN partagé (par défaut)","Gleiches Netzwerk / gemeinsames VPN (Standard)","Misma red / VPN compartida (predeterminado)","Stessa rete / VPN condivisa (predefinito)"},
            new[]{"Save Settings","Enregistrer","Speichern","Guardar ajustes","Salva impostazioni"},
            new[]{"Scaling / Sharpening:","Échelle / netteté :","Skalierung / Schärfe:","Escalado / nitidez:","Scalatura / nitidezza:"},
            new[]{"Select Rockstar Games Table Tennis ISO Image","Choisir l'image ISO de Rockstar Games Table Tennis","ISO-Abbild von Rockstar Games Table Tennis wählen","Selecciona la imagen ISO de Rockstar Games Table Tennis","Scegli l'immagine ISO di Rockstar Games Table Tennis"},
            new[]{"Select an existing Table Tennis installation.","Choisissez une installation Table Tennis existante.","Eine vorhandene Table-Tennis-Installation wählen.","Selecciona una instalación existente de Table Tennis.","Scegli un'installazione esistente di Table Tennis."},
            new[]{"Separate from resolution. Fullscreen uses the monitor size; the aspect lock controls the game picture inside it.","Indépendant de la résolution. Le plein écran utilise la taille du moniteur ; le format fixe règle l'image à l'intérieur.","Unabhängig von der Auflösung. Vollbild nutzt die Monitorgröße; das feste Format bestimmt das Spielbild darin.","Independiente de la resolución. Pantalla completa usa el tamaño del monitor; el formato fijo controla la imagen dentro.","Indipendente dalla risoluzione. Lo schermo intero usa le dimensioni del monitor; il formato fisso regola l'immagine al suo interno."},
            new[]{"Server (advanced/manual connection)","Serveur (connexion avancée/manuelle)","Server (erweiterte/manuelle Verbindung)","Servidor (conexión avanzada/manual)","Server (connessione avanzata/manuale)"},
            new[]{"Server not ready","Serveur non prêt","Server nicht bereit","Servidor no preparado","Server non pronto"},
            new[]{"Server start requested. Keep its window open; it should say 'listening'. ","Démarrage demandé. Gardez sa fenêtre ouverte ; elle doit indiquer « listening ». ","Serverstart angefordert. Fenster offen lassen; es sollte 'listening' anzeigen. ","Inicio solicitado. Deja abierta la ventana; debería indicar 'listening'. ","Avvio richiesto. Tieni aperta la finestra; dovrebbe indicare 'listening'. "},
            new[]{"Server tools are not included in this installation. Ask the host for their running server's address.","Outils serveur absents. Demandez à l'hôte l'adresse de son serveur actif.","Serverwerkzeuge fehlen. Fragen Sie den Host nach der Adresse seines laufenden Servers.","No se incluyen herramientas de servidor. Pide al anfitrión la dirección de su servidor activo.","Strumenti server non inclusi. Chiedi all'host l'indirizzo del suo server attivo."},
            new[]{"Settings saved to settings.ini!","Réglages enregistrés dans settings.ini !","Einstellungen in settings.ini gespeichert!","¡Ajustes guardados en settings.ini!","Impostazioni salvate in settings.ini!"},
            new[]{"Setup is still running. Stop the local setup? A Microsoft installation already started may continue.","L'installation est en cours. L'arrêter ? Une installation Microsoft déjà lancée peut continuer.","Die Installation läuft noch. Lokale Einrichtung beenden? Eine gestartete Microsoft-Installation kann weiterlaufen.","La instalación sigue en curso. ¿Detenerla? Una instalación de Microsoft ya iniciada podría continuar.","L'installazione è ancora in corso. Interromperla? Un'installazione Microsoft già avviata potrebbe continuare."},
            new[]{"Shift fast / Ctrl slow","Maj rapide / Ctrl lent","Umsch. schnell / Strg langsam","Mayús rápido / Ctrl lento","Maiusc veloce / Ctrl lento"},
            new[]{"Show game-submission timing (F10 to toggle)","Afficher les mesures du jeu (F10)","Spielmesswerte anzeigen (F10)","Mostrar tiempos del juego (F10)","Mostra le misurazioni del gioco (F10)"},
            new[]{"Smooth jagged edges. Applies after restarting the game.","Lisse les contours. Redémarrez le jeu pour appliquer.","Glättet Kanten. Wird nach einem Spielneustart wirksam.","Suaviza los bordes. Se aplica al reiniciar el juego.","Smussa i bordi. Riavvia il gioco per applicare."},
            new[]{"Speed","Vitesse","Tempo","Velocidad","Velocità"},
            new[]{"Start server on this PC","Démarrer le serveur sur ce PC","Server auf diesem PC starten","Iniciar servidor en este equipo","Avvia server su questo PC"},
            new[]{"Starting your host...","Démarrage de l'hôte...","Host wird gestartet...","Iniciando anfitrión...","Avvio dell'host..."},
            new[]{"Starting your private host...","Démarrage de votre hôte privé...","Privater Host wird gestartet...","Iniciando tu anfitrión privado...","Avvio del tuo host privato..."},
            new[]{"Stop hosting","Arrêter l'hébergement","Hosting beenden","Dejar de alojar","Interrompi hosting"},
            new[]{"Stop hosting before creating a new private room.","Arrêtez l'hébergement avant de créer un nouveau salon privé.","Hosting beenden, bevor Sie einen neuen privaten Raum erstellen.","Deja de alojar antes de crear una sala privada nueva.","Interrompi l'hosting prima di creare una nuova stanza privata."},
            new[]{"Stop requested; router cleanup is still pending. The next host start will retry interrupted cleanup.","Arrêt demandé ; nettoyage du routeur en attente. Le prochain démarrage retentera le nettoyage.","Beenden angefordert; Router-Bereinigung steht noch aus. Beim nächsten Start wird sie erneut versucht.","Parada solicitada; sigue pendiente limpiar el router. Se reintentará al iniciar de nuevo el anfitrión.","Arresto richiesto; pulizia del router ancora in sospeso. Verrà ritentata al prossimo avvio dell'host."},
            new[]{"Stop setup?","Arrêter l'installation ?","Installation beenden?","¿Detener instalación?","Interrompere l'installazione?"},
            new[]{"Stopping your host and removing its router rule...","Arrêt de l'hôte et suppression de sa règle du routeur...","Host wird beendet und seine Router-Regel entfernt...","Deteniendo anfitrión y eliminando su regla del router...","Arresto dell'host e rimozione della sua regola del router..."},
            new[]{"Switch between a window and fullscreen immediately.","Bascule immédiatement entre fenêtre et plein écran.","Sofort zwischen Fenster und Vollbild wechseln.","Cambia al instante entre ventana y pantalla completa.","Passa subito da finestra a schermo intero e viceversa."},
            new[]{"TABLE TENNIS UPDATES","MISES À JOUR TABLE TENNIS","TABLE-TENNIS-UPDATES","ACTUALIZACIONES DE TABLE TENNIS","AGGIORNAMENTI TABLE TENNIS"},
            new[]{"TCP port ","Le port TCP ","TCP-Port ","El puerto TCP ","La porta TCP "},
            new[]{"Table Tennis - Install & Play","Table Tennis - Installer et jouer","Table Tennis - Installieren und spielen","Table Tennis - Instalar y jugar","Table Tennis - Installa e gioca"},
            new[]{"Table Tennis - Settings & Play","Table Tennis - Réglages et jeu","Table Tennis - Einstellungen und Spielen","Table Tennis - Ajustes y juego","Table Tennis - Impostazioni e gioco"},
            new[]{"Table Tennis - Updates","Table Tennis - Mises à jour","Table Tennis - Updates","Table Tennis - Actualizaciones","Table Tennis - Aggiornamenti"},
            new[]{"Table Tennis update","Mise à jour Table Tennis","Table-Tennis-Update","Actualización de Table Tennis","Aggiornamento Table Tennis"},
            new[]{"Table Tennis updater","Mises à jour Table Tennis","Table-Tennis-Updater","Actualizador de Table Tennis","Aggiornamenti Table Tennis"},
            new[]{"Test connection","Tester la connexion","Verbindung testen","Probar conexión","Verifica connessione"},
            new[]{"Testing UDP connection to ","Test de la connexion UDP vers ","UDP-Verbindung wird getestet: ","Probando conexión UDP a ","Verifica della connessione UDP a "},
            new[]{"The Microsoft installer signature could not be verified. It was not started.","La signature du programme Microsoft n'a pas pu être vérifiée. Il n'a pas été lancé.","Die Signatur des Microsoft-Installationsprogramms konnte nicht geprüft werden. Es wurde nicht gestartet.","No se pudo verificar la firma del instalador de Microsoft. No se inició.","Impossibile verificare la firma del programma Microsoft. Non è stato avviato."},
            new[]{"The Table Tennis server at ","Le serveur Table Tennis à ","Der Table-Tennis-Server unter ","El servidor de Table Tennis en ","Il server Table Tennis su "},
            new[]{"The Table Tennis server responds at ","Le serveur Table Tennis répond à ","Der Table-Tennis-Server antwortet unter ","El servidor de Table Tennis responde en ","Il server Table Tennis risponde su "},
            new[]{"The asset does not belong to this project's release.","Le fichier n'appartient pas à cette version du projet.","Die Datei gehört nicht zu dieser Projektversion.","El archivo no pertenece a esta versión del proyecto.","Il file non appartiene a questa versione del progetto."},
            new[]{"The bundled server tools are missing. Restore the complete installation or enter the other PC's server address.","Outils serveur manquants. Restaurez l'installation ou saisissez l'adresse du serveur de l'autre PC.","Mitgelieferte Serverwerkzeuge fehlen. Installation wiederherstellen oder Serveradresse des anderen PCs eingeben.","Faltan las herramientas del servidor. Restaura la instalación o introduce la dirección del servidor del otro equipo.","Mancano gli strumenti server inclusi. Ripristina l'installazione o inserisci l'indirizzo del server dell'altro PC."},
            new[]{"The game build is incomplete. Restore the full installation.","L'installation du jeu est incomplète. Restaurez l'installation complète.","Die Spielinstallation ist unvollständig. Vollständige Installation wiederherstellen.","La instalación del juego está incompleta. Restaura la instalación completa.","L'installazione del gioco è incompleta. Ripristina l'installazione completa."},
            new[]{"The installer checks your ISO and prepares everything automatically. First setup may download several GB and take a while. Windows may ask for administrator permission.","Le programme vérifie votre ISO et prépare tout automatiquement. La première installation peut télécharger plusieurs Go et prendre du temps. Windows peut demander une autorisation administrateur.","Das Programm prüft Ihre ISO und bereitet alles automatisch vor. Die Erstinstallation kann mehrere GB herunterladen und einige Zeit dauern. Windows kann Administratorrechte anfordern.","El instalador comprueba tu ISO y lo prepara todo automáticamente. La primera instalación puede descargar varios GB y tardar un rato. Windows puede solicitar permisos de administrador.","Il programma verifica la tua ISO e prepara tutto automaticamente. La prima installazione può scaricare diversi GB e richiedere tempo. Windows potrebbe chiedere i permessi di amministratore."},
            new[]{"The installer is already open.","Le programme d'installation est déjà ouvert.","Das Installationsprogramm ist bereits geöffnet.","El instalador ya está abierto.","Il programma di installazione è già aperto."},
            new[]{"The installer source payload is missing.","Les fichiers source de l'installation sont manquants.","Die Quelldateien des Installationsprogramms fehlen.","Faltan los archivos fuente del instalador.","Mancano i file sorgente del programma di installazione."},
            new[]{"The local game build did not finish. See the log above for the component that failed.","La compilation locale n'a pas abouti. Consultez le journal ci-dessus pour identifier le composant en échec.","Das lokale Spiel wurde nicht fertiggestellt. Fehlgeschlagene Komponente im Protokoll oben nachsehen.","La compilación local no terminó. Consulta el registro de arriba para ver qué componente falló.","La compilazione locale non è terminata. Consulta il registro sopra per il componente non riuscito."},
            new[]{"The server address points to THIS PC, but the Table Tennis server is not responding.\n\nStart the server on this PC now? Keep its window open while playing.","L'adresse désigne CE PC, mais le serveur Table Tennis ne répond pas.\n\nDémarrer le serveur ici ? Gardez sa fenêtre ouverte pendant le jeu.","Die Serveradresse zeigt auf DIESEN PC, aber der Table-Tennis-Server antwortet nicht.\n\nServer hier jetzt starten? Sein Fenster beim Spielen offen lassen.","La dirección apunta a ESTE EQUIPO, pero el servidor de Table Tennis no responde.\n\n¿Iniciar el servidor aquí? Mantén su ventana abierta al jugar.","L'indirizzo indica QUESTO PC, ma il server Table Tennis non risponde.\n\nAvviare il server qui? Tieni aperta la sua finestra durante il gioco."},
            new[]{"The server did not become ready. Check its window and any Windows network permission prompt, then try Launch Game again.","Le serveur n'est pas prêt. Vérifiez sa fenêtre et toute demande réseau Windows, puis relancez le jeu.","Server wurde nicht bereit. Serverfenster und Windows-Netzwerkabfragen prüfen, dann Spiel erneut starten.","El servidor no está listo. Revisa su ventana y los permisos de red de Windows, y vuelve a iniciar el juego.","Il server non è pronto. Controlla la sua finestra e i permessi di rete di Windows, poi riprova ad avviare il gioco."},
            new[]{"The server window closed. Start it again before hosting a match.","La fenêtre du serveur s'est fermée. Relancez-le avant d'héberger un match.","Das Serverfenster wurde geschlossen. Vor dem Hosting erneut starten.","La ventana del servidor se cerró. Reinícialo antes de alojar una partida.","La finestra del server è chiusa. Riavvialo prima di ospitare una partita."},
            new[]{"The signed release is incomplete.","La version signée est incomplète.","Die signierte Version ist unvollständig.","La versión firmada está incompleta.","La versione firmata è incompleta."},
            new[]{"The update is not signed by this project's trusted release key. Nothing was installed.","La mise à jour n'est pas signée par la clé de confiance du projet. Rien n'a été installé.","Das Update ist nicht mit dem vertrauenswürdigen Projektschlüssel signiert. Es wurde nichts installiert.","La actualización no está firmada con la clave de confianza del proyecto. No se instaló nada.","L'aggiornamento non è firmato con la chiave attendibile del progetto. Non è stato installato nulla."},
            new[]{"The update stopped. Open the log above for details. The launcher can recover an interrupted file change.","La mise à jour s'est arrêtée. Consultez le journal ci-dessus. Le lanceur peut récupérer un remplacement de fichiers interrompu.","Update gestoppt. Details im Protokoll oben ansehen. Der Launcher kann einen unterbrochenen Dateiaustausch wiederherstellen.","La actualización se detuvo. Consulta el registro de arriba. El lanzador puede recuperar un cambio de archivos interrumpido.","Aggiornamento interrotto. Consulta il registro sopra. Il programma di avvio può recuperare una sostituzione di file interrotta."},
            new[]{"The updater is already open for this installation.","L'outil de mise à jour est déjà ouvert pour cette installation.","Der Updater ist für diese Installation bereits geöffnet.","El actualizador ya está abierto para esta instalación.","Il programma di aggiornamento è già aperto per questa installazione."},
            new[]{"This PC's address could not be detected. Use the server PC's local IPv4 address followed by :27360.","Adresse de ce PC introuvable. Utilisez l'IPv4 locale du PC serveur suivie de :27360.","Adresse dieses PCs nicht erkannt. Lokale IPv4 des Server-PCs mit :27360 verwenden.","No se detectó la dirección de este equipo. Usa la IPv4 local del equipo servidor seguida de :27360.","Indirizzo di questo PC non rilevato. Usa l'IPv4 locale del PC server seguito da :27360."},
            new[]{"This invitation is invalid or belongs to a newer format.","Cette invitation est invalide ou utilise un format plus récent.","Diese Einladung ist ungültig oder verwendet ein neueres Format.","Esta invitación no es válida o usa un formato más reciente.","Questo invito non è valido o usa un formato più recente."},
            new[]{"Tick the router permission box to try UPnP, or choose LAN/VPN/manual hosting.","Cochez l'autorisation du routeur pour essayer UPnP, ou choisissez LAN/VPN/manuel.","Für UPnP die Router-Erlaubnis aktivieren oder LAN/VPN/manuelles Hosting wählen.","Marca el permiso del router para probar UPnP o elige LAN/VPN/manual.","Seleziona il permesso del router per provare UPnP oppure scegli LAN/VPN/manuale."},
            new[]{"UDP port ","Le port UDP ","UDP-Port ","El puerto UDP ","La porta UDP "},
            new[]{"UNLOCKED ","DÉBLOQUÉ ","FREIGESCHALTET ","DESBLOQUEADO ","SBLOCCATO "},
            new[]{"UPDATE ","MISE À JOUR ","UPDATE ","ACTUALIZACIÓN ","AGGIORNAMENTO "},
            new[]{"UPDATES","MISES À JOUR","UPDATES","ACTUALIZACIONES","AGGIORNAMENTI"},
            new[]{"UPnP is EXPERIMENTAL and off by default. No Windows firewall settings are changed. Only the invitation room is accepted (up to four clients). Share invitations only with trusted friends: traffic is not encrypted. Stop hosting removes the owned rule; a router honoring its lease expires it within five minutes without renewal after a crash. Hosting ends with the host game, after 30 idle minutes, or after four hours. CGNAT/unsupported routers may require a shared VPN/manual setup.","UPnP est EXPÉRIMENTAL et désactivé par défaut. Aucun réglage du pare-feu Windows n'est modifié. Seul le salon de l'invitation est accepté (quatre clients maximum). Partagez uniquement avec des amis de confiance : trafic non chiffré. Arrêter l'hébergement supprime sa règle ; si le routeur respecte le bail, elle expire sous cinq minutes sans renouvellement après un plantage. L'hébergement s'arrête avec le jeu hôte, après 30 minutes d'inactivité ou quatre heures. CGNAT/routeurs non compatibles : VPN partagé ou réglage manuel parfois nécessaire.","UPnP ist EXPERIMENTELL und standardmäßig aus. Keine Windows-Firewall-Einstellungen werden geändert. Nur der Einladungsraum ist zugelassen (höchstens vier Clients). Einladungen nur mit vertrauten Freunden teilen: unverschlüsselter Datenverkehr. Hosting beenden entfernt die eigene Regel; beachtet der Router die Laufzeit, verfällt sie nach einem Absturz ohne Erneuerung binnen fünf Minuten. Hosting endet mit dem Host-Spiel, nach 30 Minuten Inaktivität oder vier Stunden. CGNAT/nicht unterstützte Router können gemeinsames VPN/manuelle Einrichtung erfordern.","UPnP es EXPERIMENTAL y está desactivado de forma predeterminada. No cambia el cortafuegos de Windows. Solo se acepta la sala de la invitación (hasta cuatro clientes). Comparte solo con amigos de confianza: el tráfico no está cifrado. Dejar de alojar elimina la regla propia; si el router respeta la duración, caduca en cinco minutos sin renovación tras un cierre inesperado. El alojamiento termina con el juego anfitrión, tras 30 minutos inactivo o cuatro horas. CGNAT/routers no compatibles pueden requerir VPN compartida o configuración manual.","UPnP è SPERIMENTALE e disattivato per impostazione predefinita. Non modifica il firewall di Windows. È accettata solo la stanza dell'invito (massimo quattro client). Condividi solo con amici fidati: traffico non cifrato. Interrompere l'hosting rimuove la regola creata; se il router rispetta la durata, scade entro cinque minuti senza rinnovo dopo un arresto anomalo. L'hosting termina con il gioco host, dopo 30 minuti inattivi o quattro ore. CGNAT/router non supportati possono richiedere VPN condivisa o configurazione manuale."},
            new[]{"Unsupported signed update package.","Paquet de mise à jour signé non pris en charge.","Nicht unterstütztes signiertes Update-Paket.","Paquete de actualización firmado no compatible.","Pacchetto di aggiornamento firmato non supportato."},
            new[]{"Update cancelled.","Mise à jour annulée.","Update abgebrochen.","Actualización cancelada.","Aggiornamento annullato."},
            new[]{"Update cancelled; your installed version was kept.","Mise à jour annulée ; votre version installée a été conservée.","Update abgebrochen; Ihre installierte Version wurde beibehalten.","Actualización cancelada; se conservó tu versión instalada.","Aggiornamento annullato; la versione installata è stata mantenuta."},
            new[]{"Update check completed.","Vérification terminée.","Update-Prüfung abgeschlossen.","Búsqueda de actualizaciones completada.","Controllo aggiornamenti completato."},
            new[]{"Update check unavailable. You can keep playing. ","Vérification indisponible. Vous pouvez continuer à jouer. ","Update-Prüfung nicht verfügbar. Sie können weiterspielen. ","Búsqueda no disponible. Puedes seguir jugando. ","Controllo non disponibile. Puoi continuare a giocare. "},
            new[]{"Update complete","Mise à jour terminée","Update abgeschlossen","Actualización completada","Aggiornamento completato"},
            new[]{"Update complete. Starting the game...","Mise à jour terminée. Démarrage du jeu...","Update abgeschlossen. Spiel wird gestartet...","Actualización completada. Iniciando el juego...","Aggiornamento completato. Avvio del gioco..."},
            new[]{"Update support is missing. Install the current full release once.","Outil de mise à jour manquant. Installez la version complète actuelle.","Update-Unterstützung fehlt. Aktuelle vollständige Version einmal installieren.","Falta el soporte de actualización. Instala la versión completa actual.","Manca il supporto aggiornamenti. Installa la versione completa attuale."},
            new[]{"Update support is missing. Restore the complete installation.","Outil de mise à jour manquant. Restaurez l'installation complète.","Update-Unterstützung fehlt. Vollständige Installation wiederherstellen.","Falta el soporte de actualización. Restaura la instalación completa.","Manca il supporto aggiornamenti. Ripristina l'installazione completa."},
            new[]{"Update {0} is available.","La mise à jour {0} est disponible.","Update {0} ist verfügbar.","La actualización {0} está disponible.","L'aggiornamento {0} è disponibile."},
            new[]{"Updated to {0}.","Mis à jour vers {0}.","Auf {0} aktualisiert.","Actualizado a {0}.","Aggiornato a {0}."},
            new[]{"Updates are installed only after you approve them.","Les mises à jour exigent votre accord.","Updates werden erst nach Ihrer Zustimmung installiert.","Las actualizaciones requieren tu aprobación.","Gli aggiornamenti richiedono la tua conferma."},
            new[]{"Updates rebuild the game locally from your existing ISO. Saves and settings are kept. The game closes after you approve installation.","Les mises à jour recompilent le jeu localement depuis votre ISO. Sauvegardes et réglages sont conservés. Le jeu se ferme après votre accord.","Updates erstellen das Spiel lokal aus Ihrer vorhandenen ISO neu. Spielstände und Einstellungen bleiben erhalten. Das Spiel schließt nach Ihrer Zustimmung.","Las actualizaciones recompilan el juego localmente desde tu ISO. Se conservan partidas y ajustes. El juego se cierra tras aprobar la instalación.","Gli aggiornamenti ricompilano il gioco localmente dalla tua ISO. Salvataggi e impostazioni restano. Il gioco si chiude dopo la tua conferma."},
            new[]{"Version information is missing. Re-run the current installer once to enable updates.","Informations de version manquantes. Relancez le programme d'installation actuel pour activer les mises à jour.","Versionsinformationen fehlen. Aktuelles Installationsprogramm einmal erneut ausführen, um Updates zu aktivieren.","Falta la información de versión. Ejecuta el instalador actual para activar las actualizaciones.","Mancano le informazioni sulla versione. Esegui di nuovo il programma di installazione attuale per abilitare gli aggiornamenti."},
            new[]{"W A S D / arrows","W A S D / flèches","W A S D / Pfeile","W A S D / flechas","W A S D / frecce"},
            new[]{"WINDOW MODE","MODE D'AFFICHAGE","FENSTERMODUS","MODO DE VENTANA","MODALITÀ FINESTRA"},
            new[]{"Wheel / Page Up / Down","Molette / Page haut-bas","Mausrad / Bild auf-ab","Rueda / Re Pág-Av Pág","Rotella / Pag su-giù"},
            new[]{"Window and 3D rendering size. Applies after restarting the game.","Taille de la fenêtre et du rendu 3D. Redémarrez le jeu pour appliquer.","Fenstergröße und 3D-Auflösung. Wird nach einem Spielneustart wirksam.","Tamaño de ventana y renderizado 3D. Se aplica al reiniciar el juego.","Dimensioni della finestra e del rendering 3D. Riavvia il gioco per applicare."},
            new[]{"Windows did not start the server.","Windows n'a pas démarré le serveur.","Windows hat den Server nicht gestartet.","Windows no inició el servidor.","Windows non ha avviato il server."},
            new[]{"Windows security blocked a required component. Setup stopped; no security settings were changed.","La sécurité Windows a bloqué un composant requis. Installation arrêtée ; aucun réglage de sécurité n'a été modifié.","Die Windows-Sicherheit hat eine benötigte Komponente blockiert. Installation beendet; keine Sicherheitseinstellungen wurden geändert.","La seguridad de Windows bloqueó un componente necesario. Instalación detenida; no se cambió la configuración de seguridad.","La sicurezza di Windows ha bloccato un componente necessario. Installazione interrotta; nessuna impostazione di sicurezza modificata."},
            new[]{"Xbox 360 ISO (*.iso)|*.iso","ISO Xbox 360 (*.iso)|*.iso","Xbox-360-ISO (*.iso)|*.iso","ISO Xbox 360 (*.iso)|*.iso","ISO Xbox 360 (*.iso)|*.iso"},
            new[]{"Xbox 360 ISO Image (*.iso)|*.iso|All files (*.*)|*.*","Image ISO Xbox 360 (*.iso)|*.iso|Tous les fichiers (*.*)|*.*","Xbox-360-ISO-Abbild (*.iso)|*.iso|Alle Dateien (*.*)|*.*","Imagen ISO Xbox 360 (*.iso)|*.iso|Todos los archivos (*.*)|*.*","Immagine ISO Xbox 360 (*.iso)|*.iso|Tutti i file (*.*)|*.*"},
            new[]{"Your disc. One installer. Ready to play.","Votre disque. Un seul programme d'installation. Prêt à jouer.","Ihre Disc. Ein Installationsprogramm. Bereit zum Spielen.","Tu disco. Un instalador. Listo para jugar.","Il tuo disco. Un programma di installazione. Pronto a giocare."},
            new[]{"Your forwarded public IPv4:port","Votre IPv4 publique:port redirigé","Ihre weitergeleitete öffentliche IPv4:Port","Tu IPv4 pública:puerto redirigido","Il tuo IPv4 pubblico:porta inoltrata"},
            new[]{"Your friend's host did not answer. Ask them to keep hosting and check the selected LAN/VPN or internet setup. Joining is configured; use Test connection to retry.","L'hôte de votre ami ne répond pas. Il doit rester actif ; vérifiez LAN/VPN ou Internet. Le mode rejoindre est configuré ; réessayez avec Tester la connexion.","Der Host Ihres Freundes antwortet nicht. Er muss aktiv bleiben; LAN/VPN oder Internet-Einrichtung prüfen. Der Beitritt ist eingerichtet; mit Verbindung testen erneut versuchen.","El anfitrión de tu amigo no responde. Pídele que siga alojando y revisad LAN/VPN o Internet. La unión está configurada; usa Probar conexión para reintentar.","L'host del tuo amico non risponde. Chiedigli di restare attivo e verificate LAN/VPN o Internet. L'accesso è configurato; usa Verifica connessione per riprovare."},
            new[]{"Your game is ready. Click Play to choose your display settings and start. Your ISO has not been modified.","Votre jeu est prêt. Cliquez sur Jouer pour régler l'affichage et démarrer. Votre ISO n'a pas été modifiée.","Ihr Spiel ist bereit. Mit Spielen die Anzeige einstellen und starten. Ihre ISO wurde nicht verändert.","Tu juego está listo. Pulsa Jugar para ajustar la imagen y empezar. Tu ISO no se ha modificado.","Il gioco è pronto. Premi Gioca per scegliere le impostazioni video e iniziare. La ISO non è stata modificata."},
            new[]{"Your local server did not start. Check Windows network permission and occupied ports.","Le serveur local n'a pas démarré. Vérifiez les permissions réseau Windows et les ports occupés.","Lokaler Server nicht gestartet. Windows-Netzwerkfreigabe und belegte Ports prüfen.","El servidor local no se inició. Revisa permisos de red de Windows y puertos ocupados.","Il server locale non è partito. Verifica permessi di rete Windows e porte occupate."},
            new[]{"Zoom","Zoom","Zoom","Zoom","Zoom"},
            new[]{"display avg %5.2f  1%% %5.2f  worst %6.2f ms","écran moy %5.2f  1%% %5.2f  max %6.2f ms","Anzeige Ø %5.2f  1%% %5.2f  max %6.2f ms","pant. med %5.2f  1%% %5.2f  máx %6.2f ms","schermo med %5.2f  1%% %5.2f  max %6.2f ms"},
            new[]{"game   avg %5.2f  1%% %5.2f  worst %6.2f ms","jeu    moy %5.2f  1%% %5.2f  max %6.2f ms","Spiel  Ø %5.2f  1%% %5.2f  max %6.2f ms","juego  med %5.2f  1%% %5.2f  máx %6.2f ms","gioco  med %5.2f  1%% %5.2f  max %6.2f ms"},
            new[]{"game frame time, last %zu frames   green = 16.7 +/- 2 ms","temps d'image, %zu images   vert = 16.7 +/- 2 ms","Frame-Zeit, %zu Frames   grün = 16.7 +/- 2 ms","tiempo, %zu fotogramas   verde = 16.7 +/- 2 ms","tempo, %zu fotogrammi   verde = 16.7 +/- 2 ms"},
            new[]{"spike %.1f ms","pic %.1f ms","Spitze %.1f ms","pico %.1f ms","picco %.1f ms"},
            new[]{"tabletennis.exe is missing at: ","tabletennis.exe est absent de : ","tabletennis.exe fehlt unter: ","Falta tabletennis.exe en: ","tabletennis.exe manca in: "},
            new[]{"this PC's IPv4 address followed by :27360","l'adresse IPv4 de ce PC suivie de :27360","IPv4-Adresse dieses PCs mit :27360 dahinter","la dirección IPv4 de este equipo seguida de :27360","l'indirizzo IPv4 di questo PC seguito da :27360"},
            new[]{"unknown mode","mode inconnu","unbekannter Modus","modo desconocido","modalità sconosciuta"},
            new[]{"ℹ IN-GAME SHORTCUTS & CONTROLS","ℹ RACCOURCIS ET COMMANDES DU JEU","ℹ TASTENKÜRZEL UND STEUERUNG","ℹ ATAJOS Y CONTROLES DEL JUEGO","ℹ SCORCIATOIE E COMANDI DEL GIOCO"},
            new[]{"⚠ Please select your ISO file to launch the game.","⚠ Choisissez votre fichier ISO pour lancer le jeu.","⚠ Wählen Sie Ihre ISO-Datei, um das Spiel zu starten.","⚠ Selecciona tu archivo ISO para iniciar el juego.","⚠ Scegli il file ISO per avviare il gioco."},
            new[]{"✓ Valid ISO image found: ","✓ Image ISO valide : ","✓ Gültiges ISO-Abbild gefunden: ","✓ Imagen ISO válida: ","✓ Immagine ISO valida: "},
            new[]{"✖ Specified ISO image not found on this computer.","✖ Image ISO introuvable sur cet ordinateur.","✖ ISO-Abbild auf diesem PC nicht gefunden.","✖ No se encuentra la imagen ISO en este equipo.","✖ Immagine ISO non trovata su questo computer."},
        };
        private static readonly Dictionary<string,string[]> ByEnglish = new Dictionary<string,string[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string,string> Original = new Dictionary<string,string>(StringComparer.Ordinal);
        static UiText() {
            foreach(var row in Rows) {ByEnglish.Add(row[0],row);Original[row[0]]=row[0];}
            foreach(var row in Rows) foreach(var text in row) if(!Original.ContainsKey(text)) Original.Add(text,row[0]);
        }
        public static string T(string text) {
            if(text==null) return null;
            string english;string[] row;
            if(!Original.TryGetValue(text,out english) || !ByEnglish.TryGetValue(english,out row)) return text;
            int index=Code=="fr"?1:Code=="de"?2:Code=="es"?3:Code=="it"?4:0;
            return row[index];
        }
        public static string Message(string text) {
            string exact=T(text);
            if(exact!=text || text==null) return exact;
            foreach(var row in Rows) foreach(var candidate in row) {
                int at=candidate.IndexOf("{0}",StringComparison.Ordinal);
                if(at<0 || candidate.IndexOf("{0}",at+3,StringComparison.Ordinal)>=0) continue;
                string prefix=candidate.Substring(0,at),suffix=candidate.Substring(at+3);
                int length=text.Length-prefix.Length-suffix.Length;
                if(length>0 && length<=512 && text.StartsWith(prefix,StringComparison.Ordinal) && text.EndsWith(suffix,StringComparison.Ordinal))
                    return F(row[0],text.Substring(prefix.Length,length));
            }
            foreach(var row in Rows) foreach(var candidate in row)
                if(candidate.Length>20 && text.Length>candidate.Length && text.StartsWith(candidate,StringComparison.Ordinal))
                    return T(row[0])+T(text.Substring(candidate.Length));
            return text; // Preserve unknown technical details verbatim.
        }
        public static string F(string text,params object[] values) {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,T(text),values);
        }
        public static string NormalizeCode(string code) {
            return code=="fr" || code=="de" || code=="es" || code=="it" ? code : "en";
        }
        public static string TryReadLanguage(string root) {
            try {
                string path=System.IO.Path.Combine(root,"dist","settings.ini");
                if(!System.IO.File.Exists(path) || new System.IO.FileInfo(path).Length>1048576) return null;
                string result=null;
                foreach(string line in System.IO.File.ReadAllLines(path,System.Text.Encoding.UTF8)) {
                    int at=line.IndexOf('=');
                    if(at<0 || !line.Substring(0,at).Trim().Equals("game_language",StringComparison.OrdinalIgnoreCase)) continue;
                    string value=line.Substring(at+1).Trim();
                    result=value=="en" || value=="fr" || value=="de" || value=="es" || value=="it" ? value : null;
                }
                return result;
            } catch {return null;}
        }
        public static string ReadLanguage(string root) {return NormalizeCode(TryReadLanguage(root));}
        public static void SaveLanguage(string root,string code) {
            string folder=System.IO.Path.Combine(root,"dist"),path=System.IO.Path.Combine(folder,"settings.ini");
            System.IO.Directory.CreateDirectory(folder);
            string original=System.IO.File.Exists(path)?System.IO.File.ReadAllText(path,System.Text.Encoding.UTF8):"";
            string newline=original.Contains("\r\n")?"\r\n":"\n";
            var lines=new System.Collections.Generic.List<string>();bool written=false;
            foreach(string line in System.Text.RegularExpressions.Regex.Split(original,"\r?\n")) {
                int at=line.IndexOf('=');
                if(at>=0 && line.Substring(0,at).Trim().Equals("game_language",StringComparison.OrdinalIgnoreCase)) {
                    if(!written) {lines.Add("game_language="+NormalizeCode(code));written=true;}
                } else lines.Add(line);
            }
            string merged=string.Join(newline,lines);
            if(!written) merged+=(merged.Length>0 && !merged.EndsWith(newline,StringComparison.Ordinal)?newline:"")+"game_language="+NormalizeCode(code)+newline;
            string temp=path+".language-"+Guid.NewGuid().ToString("N")+".tmp";
            try {
                System.IO.File.WriteAllText(temp,merged,new System.Text.UTF8Encoding(false));
                if(System.IO.File.Exists(path)) System.IO.File.Replace(temp,path,null);else System.IO.File.Move(temp,path);
            } finally {if(System.IO.File.Exists(temp)) System.IO.File.Delete(temp);}
        }
        public static void TranslateControls(System.Windows.DependencyObject node) {
            if(node is System.Windows.Controls.TextBox) return;
            var text=node as System.Windows.Controls.TextBlock;
            if(text!=null) {text.Text=Message(text.Text);return;}
            var combo=node as System.Windows.Controls.ComboBox;
            if(combo!=null) {
                int selected=combo.SelectedIndex;
                for(int i=0;i<combo.Items.Count;i++) if(combo.Items[i] is string) {
                    string before=(string)combo.Items[i],after=T(before);if(before!=after) combo.Items[i]=after;
                }
                combo.SelectedIndex=selected;return;
            }
            var header=node as System.Windows.Controls.HeaderedContentControl;
            if(header!=null && header.Header is string) header.Header=T((string)header.Header);
            var content=node as System.Windows.Controls.ContentControl;
            if(content!=null && content.Content is string) {content.Content=Message((string)content.Content);return;}
            foreach(object child in System.Windows.LogicalTreeHelper.GetChildren(node)) {
                var element=child as System.Windows.DependencyObject;if(element!=null) TranslateControls(element);
            }
        }
    }
}
