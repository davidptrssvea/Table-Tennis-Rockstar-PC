using System;
using System.Diagnostics;
using System.IO;
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
        private TextBox txtIsoPath;
        private TextBlock txtIsoStatus;
        private RadioButton rb720, rb1080, rb1440, rb4k;
        private CheckBox chkFullscreen, chkShowFps;
        private ComboBox comboFps;
        private ComboBox comboAA, comboPost;
        private TextBlock txtStatus;

        private string configFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");
        private string distDir = AppDomain.CurrentDomain.BaseDirectory;

        public MainWindow()
        {
            Title = "Rockstar Games presents Table Tennis - Settings & Launcher";
            Width = 640;
            Height = 820;
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
            var secRes = CreateSectionTitle("DISPLAY RESOLUTION (NATIVE INTERNAL RENDERING)");
            contentPanel.Children.Add(secRes);

            var resGrid = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            resGrid.ColumnDefinitions.Add(new ColumnDefinition());
            resGrid.ColumnDefinitions.Add(new ColumnDefinition());
            resGrid.RowDefinitions.Add(new RowDefinition());
            resGrid.RowDefinitions.Add(new RowDefinition());

            rb720 = CreateRadioButton("720p (1280x720) - 1x Native", "resGroup");
            rb1080 = CreateRadioButton("1080p (1920x1080) - Full HD", "resGroup");
            rb1440 = CreateRadioButton("1440p (2560x1440) - Native 2K QHD (2x)", "resGroup");
            rb4k = CreateRadioButton("4K (3840x2160) - Native Ultra HD (3x)", "resGroup");

            Grid.SetRow(rb720, 0); Grid.SetColumn(rb720, 0);
            Grid.SetRow(rb1080, 0); Grid.SetColumn(rb1080, 1);
            Grid.SetRow(rb1440, 1); Grid.SetColumn(rb1440, 0);
            Grid.SetRow(rb4k, 1); Grid.SetColumn(rb4k, 1);

            resGrid.Children.Add(rb720);
            resGrid.Children.Add(rb1080);
            resGrid.Children.Add(rb1440);
            resGrid.Children.Add(rb4k);
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
                Text = "- ESC : Exit game to Windows.\n- F10 : Toggle game submission rate, intervals and UI redraw rate.",
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

            Grid.SetRow(contentPanel, 1);
            root.Children.Add(contentPanel);

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

        private void LoadSettings()
        {
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
                        if (key.Equals("iso_path", StringComparison.OrdinalIgnoreCase)) iso = val;
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
            else rb1080.IsChecked = true;

            if (framerate == 60) comboFps.SelectedIndex = 0;
            else if (framerate == 120) comboFps.SelectedIndex = 1;
            else if (framerate == 144) comboFps.SelectedIndex = 2;
            else if (framerate == 0) comboFps.SelectedIndex = 4;
            else comboFps.SelectedIndex = 3; // 240 FPS

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
                File.WriteAllLines(configFile, new string[]
                {
                    "iso_path=" + iso,
                    "resolution=" + res,
                    "framerate=" + targetFps,
                    "fullscreen=" + fullscreen.ToString().ToLower(),
                    "show_fps=" + showFps.ToString().ToLower(),
                    "aa=" + aa,
                    "post=" + post
                });
            }
            catch { }
        }

        private void LaunchGame()
        {
            string selectedIso = txtIsoPath.Text.Trim();
            if (string.IsNullOrEmpty(selectedIso) || !File.Exists(selectedIso))
            {
                MessageBox.Show("Please select a valid Xbox 360 ISO image file using 'Browse...' before launching the game!", "ISO Image Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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
