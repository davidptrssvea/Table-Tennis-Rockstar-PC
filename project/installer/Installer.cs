// Project-authored installer. Its embedded payload contains public source only.
using System;
using System.Collections.Generic;
using TableTennisLauncher;
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
[assembly: AssemblyVersion("1.5.3.0")]
[assembly: AssemblyFileVersion("1.5.3.0")]

namespace TableTennisSetup {
    public class Program {
        [STAThread]
        public static void Main(string[] args) {
            UiText.Code=InstallerWindow.InitialLanguage(args);
            bool created;
            using (var mutex = new Mutex(true, "Local\\TableTennisLocalInstaller", out created)) {
                if (!created) { MessageBox.Show(UiText.T("The installer is already open."), "Table Tennis"); return; }
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
        bool busy, localizing, languageChosen;
        ComboBox language;
        static readonly string[] LanguageCodes={"en","fr","de","es","it"};
        static string DefaultFolder() {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TableTennisPC");}
        public static string InitialLanguage(string[] args) {
            string target=DefaultFolder(),code=null;
            for(int i=0;i+1<args.Length;i++) {
                if(args[i]=="--destination") target=args[++i];
                else if(args[i]=="--language") code=args[++i];
            }
            return UiText.NormalizeCode(code??UiText.TryReadLanguage(target)??System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        void ApplyLanguage(string code) {
            localizing=true;
            try {
                UiText.Code=UiText.NormalizeCode(code);language.SelectedIndex=Array.IndexOf(LanguageCodes,UiText.Code);
                Title=UiText.T(Title);UiText.TranslateControls(this);
            } finally {localizing=false;}
        }
        string sdkPath, logPath, installationError;
        Process worker;
        readonly object logLock = new object();
        readonly Brush muted = new SolidColorBrush(Color.FromRgb(166, 177, 194));

        public InstallerWindow(string[] args) {
            UiText.Code=InitialLanguage(args);
            Title = UiText.T("Table Tennis - Install & Play");
            Width = 760; Height = Math.Min(800,SystemParameters.WorkArea.Height-32); MinWidth = 680; MinHeight = Math.Min(650,SystemParameters.WorkArea.Height-32);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(18, 23, 33));
            Foreground = Brushes.White; FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
            var root = new Grid { Margin = new Thickness(30, 24, 30, 24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
            header.Children.Add(new TextBlock { Text = "TABLE TENNIS", FontSize = 34, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(247, 208, 61)) });
            header.Children.Add(new TextBlock { Text = UiText.T("Your disc. One installer. Ready to play."), Foreground = muted, Margin = new Thickness(0, 5, 0, 0) });
            root.Children.Add(header);

            var form = new StackPanel {Margin=new Thickness(0,0,20,0)};
            var formScroll=new ScrollViewer {Content=form,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            Grid.SetRow(formScroll,1);root.Children.Add(formScroll);
            form.Children.Add(Label(UiText.T("Language")));
            language=new ComboBox {Height=30,FontSize=14,Margin=new Thickness(0,0,0,6)};
            foreach(string label in new[]{"English","Français","Deutsch","Español","Italiano"}) language.Items.Add(label);
            language.SelectedIndex=Array.IndexOf(LanguageCodes,UiText.Code);
            language.SelectionChanged+=(sender,eventArgs)=>{if(!localizing && !busy) {languageChosen=true;ApplyLanguage(LanguageCodes[Math.Max(0,language.SelectedIndex)]);}};
            form.Children.Add(language);
            form.Children.Add(new TextBlock {Text=UiText.T("Also used by the launcher and game after installation."),Foreground=muted,TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,0,0,14)});
            form.Children.Add(Label(UiText.T("1  Choose your game disc image")));
            form.Children.Add(FileRow(out iso, out chooseIso, UiText.T("Choose ISO...")));
            chooseIso.Click += delegate {
                var dialog = new OpenFileDialog { Title = UiText.T("Choose your Table Tennis retail disc image"), Filter = UiText.T("Xbox 360 ISO (*.iso)|*.iso") };
                if (dialog.ShowDialog(this) == true) iso.Text = dialog.FileName;
            };
            form.Children.Add(Label(UiText.T("2  Install location")));
            form.Children.Add(FileRow(out folder, out chooseFolder, UiText.T("Change...")));
            folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TableTennisPC");
            chooseFolder.Click += delegate {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) {
                    dialog.Description = UiText.T("Choose a writable folder for Table Tennis");
                    dialog.SelectedPath = folder.Text;
                    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) folder.Text = dialog.SelectedPath;
                }
            };
            help = new TextBlock { Text = UiText.T("The installer checks your ISO and prepares everything automatically. First setup may download several GB and take a while. Windows may ask for administrator permission."),
                Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12), FontSize = 13 };
            form.Children.Add(help);
            consent = new CheckBox { Foreground = Brushes.White, Content = new TextBlock { Text = UiText.T("Allow required components to be downloaded and installed."), TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 0, 0, 5) };
            form.Children.Add(consent);
            var licenses = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 17) };
            foreach (var item in new[] {
                new[] {UiText.T("Microsoft terms"), "https://visualstudio.microsoft.com/license-terms/"},
                new[] {UiText.T("Python terms"), "https://docs.python.org/3/license.html"},
                new[] {UiText.T("Project licenses"), "https://github.com/davidptrssvea/Table-Tennis-Rockstar-PC#license-and-rights"}}) {
                string url = item[1];
                var button = new Button { Content = item[0], Margin = new Thickness(0, 0, 12, 0), Padding = new Thickness(0),
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = muted, FontSize = 12 };
                button.Click += delegate { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); };
                licenses.Children.Add(button);
            }
            form.Children.Add(licenses);
            status = new TextBlock { Text = UiText.T("Choose your ISO, then click Install."), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            form.Children.Add(status);
            progress = new ProgressBar { Height = 7, Margin = new Thickness(0, 12, 0, 14), Minimum = 0, Maximum = 1 };
            form.Children.Add(progress);

            details = new TextBox { Height=160, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(11, 15, 22)), Foreground = muted, FontFamily = new FontFamily("Consolas"), FontSize = 11, Padding = new Thickness(9) };
            var expander = new Expander { Foreground = Brushes.White, Header = UiText.T("Installation details"), Content = details, IsExpanded = false, Margin = new Thickness(0, 0, 0, 14) };
            Grid.SetRow(expander, 2); root.Children.Add(expander);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(actions, 3); root.Children.Add(actions);
            showLog = ActionButton(UiText.T("Open log"), false); showLog.IsEnabled = false;
            showLog.Click += delegate { if (logPath != null && File.Exists(logPath)) Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true }); };
            install = ActionButton(UiText.T("Install"), true); install.Click += async delegate { await Install(); };
            play = ActionButton(UiText.T("Play"), true); play.Visibility = Visibility.Collapsed;
            play.Click += delegate { LaunchGame(); };
            actions.Children.Add(showLog); actions.Children.Add(install); actions.Children.Add(play);
            for (int i = 0; i + 1 < args.Length; i++) {
                if (args[i] == "--iso") iso.Text = args[++i];
                else if (args[i] == "--destination") folder.Text = args[++i];
                else if (args[i] == "--sdk") sdkPath = args[++i];
            }
            folder.TextChanged+=(sender,eventArgs)=>{
                if(busy || languageChosen) return;
                string saved=UiText.TryReadLanguage(folder.Text);
                if(saved!=null) ApplyLanguage(saved);
            };
            ApplyLanguage(UiText.Code);
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (busy) {
                    if (MessageBox.Show(this, UiText.T("Setup is still running. Stop the local setup? A Microsoft installation already started may continue."),
                        UiText.T("Stop setup?"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) { e.Cancel = true; return; }
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
                if (text.StartsWith("STAGE:")) status.Text = UiText.Message(text.Substring(6));
                else if (text.StartsWith("RESTART:")) status.Text = UiText.Message(text.Substring(8));
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
                MessageBox.Show(this, UiText.T("Choose your retail Table Tennis ISO first."), UiText.T("Choose game disc")); return;
            }
            if (consent.IsChecked != true) { MessageBox.Show(this, UiText.T("Allow the required components to be downloaded and installed before continuing."), UiText.T("Required components")); return; }
            string target, image;
            try { target = Path.GetFullPath(folder.Text).TrimEnd('\\'); image = Path.GetFullPath(iso.Text); }
            catch { MessageBox.Show(this, UiText.T("Choose a valid install folder.")); return; }
            if (target == Path.GetPathRoot(target + "\\").TrimEnd('\\')) { MessageBox.Show(this, UiText.T("Choose a folder, rather than the root of a drive.")); return; }
            string selectedLanguage=UiText.NormalizeCode(UiText.Code);
            busy = true; language.IsEnabled=false; install.IsEnabled = chooseIso.IsEnabled = chooseFolder.IsEnabled = iso.IsEnabled = folder.IsEnabled = consent.IsEnabled = false;
            installationError = null; play.Visibility = Visibility.Collapsed; progress.IsIndeterminate = true; status.Text = "Preparing installation...";
            try {
                int exit = await Task.Run(delegate {
                    Directory.CreateDirectory(target);
                    logPath = Path.Combine(target, "installation.log");
                    File.WriteAllText(logPath, "Table Tennis local installer v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + Environment.NewLine, Encoding.UTF8);
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
                    UiText.SaveLanguage(target,selectedLanguage);
                    status.Text = UiText.T("Ready to play!"); progress.IsIndeterminate = false; progress.Value = 1;
                    play.Visibility = Visibility.Visible; install.Content = UiText.T("Recheck / repair");
                    help.Text = UiText.T("Your game is ready. Click Play to choose your display settings and start. Your ISO has not been modified.");
                    try {
                        CreateDesktopShortcut(target, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                    } catch (Exception shortcutError) { WriteLine("Desktop shortcut could not be created: " + shortcutError.Message); }
                } else if (exit == 3010) status.Text = UiText.T("Restart Windows, then open this installer again to finish.");
                else status.Text = UiText.T("Installation stopped. ") + (installationError==null?UiText.T("Open the log for details; you can retry after correcting the problem."):UiText.Message(installationError));
            } catch (Exception error) { status.Text = UiText.T("Installation stopped: ") + UiText.Message(error.Message); }
            finally {
                busy = false; language.IsEnabled=true; progress.IsIndeterminate = false;
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
            shortcut.GetType().InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { UiText.T("Table Tennis - Settings & Play") });
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        void LaunchGame() {
            string path = Path.Combine(folder.Text, "dist", "tabletennis_launcher.exe");
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { WorkingDirectory = Path.GetDirectoryName(path), UseShellExecute = true });
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
