// Project-authored installer. Its embedded payload contains public source only.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

[assembly: AssemblyTitle("Table Tennis Installer")]
[assembly: AssemblyDescription("Local game build installer; requires your own retail disc image.")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace TableTennisSetup {
    public class Program {
        [STAThread]
        public static void Main(string[] args) {
            bool created;
            using (var mutex = new Mutex(true, "Local\\TableTennisLocalInstaller", out created)) {
                if (!created) { MessageBox.Show("The installer is already open.", "Table Tennis"); return; }
                new Application().Run(new InstallerWindow(args));
            }
        }
    }

    public class InstallerWindow : Window {
        TextBox iso, folder, details;
        TextBlock status, help;
        Button install, play, chooseIso, chooseFolder, showLog;
        CheckBox consent;
        ProgressBar progress;
        bool busy;
        string sdkPath, logPath, installationError;
        Process worker;
        readonly object logLock = new object();
        readonly Brush muted = new SolidColorBrush(Color.FromRgb(166, 177, 194));

        public InstallerWindow(string[] args) {
            Title = "Table Tennis - Install & Play";
            Width = 760; Height = 720; MinWidth = 680; MinHeight = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(18, 23, 33));
            Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
            var root = new Grid { Margin = new Thickness(30, 24, 30, 24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            header.Children.Add(new TextBlock { Text = "TABLE TENNIS", FontSize = 34, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(247, 208, 61)) });
            header.Children.Add(new TextBlock { Text = "Your disc. One installer. Ready to play.", Foreground = muted, Margin = new Thickness(0, 5, 0, 0) });
            root.Children.Add(header);

            var form = new StackPanel(); Grid.SetRow(form, 1); root.Children.Add(form);
            form.Children.Add(Label("1  Choose your game disc image"));
            form.Children.Add(FileRow(out iso, out chooseIso, "Choose ISO..."));
            chooseIso.Click += delegate {
                var dialog = new OpenFileDialog { Title = "Choose your Table Tennis retail disc image", Filter = "Xbox 360 ISO (*.iso)|*.iso" };
                if (dialog.ShowDialog(this) == true) iso.Text = dialog.FileName;
            };
            form.Children.Add(Label("2  Install location"));
            form.Children.Add(FileRow(out folder, out chooseFolder, "Change..."));
            folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableTennisPC");
            chooseFolder.Click += delegate {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                    dialog.Description = "Choose a writable folder for Table Tennis";
                    dialog.SelectedPath = folder.Text;
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) folder.Text = dialog.SelectedPath;
                }
            };
            help = new TextBlock { Text = "The installer checks your ISO and prepares everything automatically. First setup may download several GB and take a while. Windows may ask for administrator permission.",
                Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), FontSize = 13 };
            form.Children.Add(help);
            consent = new CheckBox { Foreground = Brushes.White, Content = new TextBlock { Text = "Allow required components to be downloaded and installed.", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 0, 0, 5) };
            form.Children.Add(consent);
            var licenses = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 17) };
            foreach (var item in new[] {
                new[] {"Microsoft terms", "https://visualstudio.microsoft.com/license-terms/"},
                new[] {"Python terms", "https://docs.python.org/3/license.html"},
                new[] {"Project licenses", "https://github.com/davidptrssvea/Table-Tennis-Rockstar-PC#license-and-rights"}}) {
                string url = item[1];
                var button = new Button { Content = item[0], Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(0),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = muted, FontSize = 12 };
                button.Click += delegate { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); };
                licenses.Children.Add(button);
            }
            form.Children.Add(licenses);
            status = new TextBlock { Text = "Choose your ISO, then click Install.", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            form.Children.Add(status);
            progress = new ProgressBar { Height = 7, Margin = new Thickness(0, 12, 0, 14), Minimum = 0, Maximum = 1 };
            form.Children.Add(progress);

            details = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(11, 15, 22)), Foreground = muted, FontFamily = new FontFamily("Consolas"), FontSize = 11, Padding = new Thickness(9) };
            var expander = new Expander { Foreground = Brushes.White, Header = "Installation details", Content = details, IsExpanded = false, Margin = new Thickness(0, 0, 0, 14) };
            Grid.SetRow(expander, 2); root.Children.Add(expander);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(actions, 3); root.Children.Add(actions);
            showLog = ActionButton("Open log", false); showLog.IsEnabled = false;
            showLog.Click += delegate { if (logPath != null && File.Exists(logPath)) Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true }); };
            install = ActionButton("Install", true); install.Click += async delegate { await Install(); };
            play = ActionButton("Play", true); play.Visibility = Visibility.Collapsed;
            play.Click += delegate { LaunchGame(); };
            actions.Children.Add(showLog); actions.Children.Add(install); actions.Children.Add(play);
            for (int i = 0; i + 1 < args.Length; i++) {
                if (args[i] == "--iso") iso.Text = args[++i];
                else if (args[i] == "--destination") folder.Text = args[++i];
                else if (args[i] == "--sdk") sdkPath = args[++i];
            }
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (busy) {
                    if (MessageBox.Show(this, "Setup is still running. Stop the local setup? A Microsoft installation already started may continue.",
                        "Stop setup?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
                    try { if (worker != null && !worker.HasExited) Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + worker.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
                }
            };
        }

        TextBlock Label(string text) { return new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }; }
        Grid FileRow(out TextBox text, out Button button, string title) {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            text = new TextBox { Padding = new Thickness(8), VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            button = new Button { Content = title, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(button, 1); row.Children.Add(text); row.Children.Add(button); return row;
        }
        Button ActionButton(string text, bool primary) {
            return new Button { Content = text, Padding = new Thickness(22, 10, 22, 10), MinWidth = 110,
                Margin = new Thickness(10, 0, 0, 0), FontWeight = FontWeights.SemiBold,
                Background = primary ? new SolidColorBrush(Color.FromRgb(247, 208, 61)) : Brushes.White, Foreground = Brushes.Black };
        }
        static string Quote(string text) {
            // Paths are passed to PowerShell as argv, never interpolated into commands.
            if (text.IndexOf('"') >= 0) throw new ArgumentException("A path contains an invalid quote.");
            return "\"" + text.TrimEnd('\\') + "\"";
        }
        void WriteLine(string text) {
            if (text == null) return;
            if (text.StartsWith("INSTALL_ERROR:") && (installationError == null || !installationError.StartsWith("Windows security"))) installationError = text.Substring(14);
            if (text.IndexOf("virus", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("WinError 225", StringComparison.OrdinalIgnoreCase) >= 0)
                installationError = "Windows security blocked a required component. Setup stopped; no security settings were changed.";
            lock (logLock) { File.AppendAllText(logPath, text + Environment.NewLine, Encoding.UTF8); }
            Dispatcher.BeginInvoke(new Action(delegate {
                if (text.StartsWith("STAGE:")) status.Text = text.Substring(6);
                else if (text.StartsWith("RESTART:")) status.Text = text.Substring(8);
                if (details.Text.Length > 120000) details.Text = details.Text.Substring(details.Text.Length - 60000);
                details.AppendText(text + Environment.NewLine); details.ScrollToEnd();
            }));
        }
        static void ExtractSource(string destination) {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("source.zip")) {
                if (resource == null) throw new InvalidDataException("The installer source payload is missing.");
                using (var archive = new ZipArchive(resource, ZipArchiveMode.Read)) {
                    string prefix = Path.GetFullPath(destination).TrimEnd('\\') + "\\";
                    foreach (var entry in archive.Entries) {
                        string path = Path.GetFullPath(Path.Combine(prefix, entry.FullName));
                        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || entry.Length > 2000000) throw new InvalidDataException("Invalid installer payload.");
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var input = entry.Open()) using (var output = File.Create(path)) input.CopyTo(output);
                    }
                }
            }
        }
        async Task Install() {
            if (!File.Exists(iso.Text) || !iso.Text.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) {
                MessageBox.Show(this, "Choose your retail Table Tennis ISO first.", "Choose game disc"); return;
            }
            if (consent.IsChecked != true) { MessageBox.Show(this, "Allow the required components to be downloaded and installed before continuing.", "Required components"); return; }
            string target, image;
            try { target = Path.GetFullPath(folder.Text).TrimEnd('\\'); image = Path.GetFullPath(iso.Text); }
            catch { MessageBox.Show(this, "Choose a valid install folder."); return; }
            if (target == Path.GetPathRoot(target + "\\").TrimEnd('\\')) { MessageBox.Show(this, "Choose a folder, rather than the root of a drive."); return; }
            busy = true; install.IsEnabled = chooseIso.IsEnabled = chooseFolder.IsEnabled = iso.IsEnabled = folder.IsEnabled = consent.IsEnabled = false;
            installationError = null; play.Visibility = Visibility.Collapsed; progress.IsIndeterminate = true; status.Text = "Preparing installation...";
            try {
                int exit = await Task.Run(delegate {
                    Directory.CreateDirectory(target);
                    logPath = Path.Combine(target, "installation.log");
                    File.WriteAllText(logPath, "Table Tennis local installer v1.1.1" + Environment.NewLine, Encoding.UTF8);
                    ExtractSource(target);
                    string script = Path.Combine(target, "project", "tools", "bootstrap.ps1");
                    string args = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) + " -Root " + Quote(target) + " -IsoPath " + Quote(image) + " -AllowToolInstall";
                    if (!String.IsNullOrEmpty(sdkPath)) args += " -SdkPath " + Quote(sdkPath);
                    worker = new Process { StartInfo = new ProcessStartInfo("powershell.exe", args) { UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = target } };
                    worker.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { WriteLine(e.Data); };
                    worker.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { WriteLine(e.Data); };
                    worker.Start(); worker.BeginOutputReadLine(); worker.BeginErrorReadLine(); worker.WaitForExit(); return worker.ExitCode;
                });
                if (exit == 0 && File.Exists(Path.Combine(target, "dist", "tabletennis_launcher.exe"))) {
                    status.Text = "Ready to play!"; progress.IsIndeterminate = false; progress.Value = 1;
                    play.Visibility = Visibility.Visible; install.Content = "Recheck / repair";
                    help.Text = "Your game is ready. Click Play to choose your display settings and start. Your ISO has not been modified.";
                    try {
                        CreateDesktopShortcut(target, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                    } catch (Exception shortcutError) { WriteLine("Desktop shortcut could not be created: " + shortcutError.Message); }
                } else if (exit == 3010) status.Text = "Restart Windows, then open this installer again to finish.";
                else status.Text = "Installation stopped. " + (installationError ?? "Open the log for details; you can retry after correcting the problem.");
            } catch (Exception error) { status.Text = "Installation stopped: " + error.Message; }
            finally {
                busy = false; progress.IsIndeterminate = false;
                install.IsEnabled = chooseIso.IsEnabled = chooseFolder.IsEnabled = iso.IsEnabled = folder.IsEnabled = consent.IsEnabled = true;
                showLog.IsEnabled = logPath != null && File.Exists(logPath);
            }
        }
        static void CreateDesktopShortcut(string target, string desktop) {
            var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            var shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                new object[] { Path.Combine(desktop, "Table Tennis.lnk") });
            shortcut.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(target, "dist", "tabletennis_launcher.exe") });
            shortcut.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(target, "dist") });
            shortcut.GetType().InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(target, "dist", "tabletennis.ico") + ",0" });
            shortcut.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Table Tennis - Settings & Play" });
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        void LaunchGame() {
            string path = Path.Combine(folder.Text, "dist", "tabletennis_launcher.exe");
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true });
        }
    }
}
