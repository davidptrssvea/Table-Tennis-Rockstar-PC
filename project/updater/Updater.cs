using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TableTennisUpdates {
    public static class Program {
        static string Option(string[] args,string name) {
            for(int i=0;i<args.Length;++i) if(args[i]==name) {
                if(i+1>=args.Length) throw new ArgumentException("Missing "+name);
                return args[i+1];
            }
            return null;
        }
        public static string Identity(string root) {
            using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-","").Substring(0,24);
        }
        static string CopyToCache(string root) {
            string self=Assembly.GetExecutingAssembly().Location;
            string backend=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update_backend.py");
            if(!File.Exists(backend)) throw new FileNotFoundException("Update support is missing. Restore the complete installation.");
            string cache=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "TableTennisUpdater",UpdateCore.Hash(self).Substring(0,16)+UpdateCore.Hash(backend).Substring(0,16));
            if(Path.GetDirectoryName(self).Equals(cache,StringComparison.OrdinalIgnoreCase)) return null;
            Directory.CreateDirectory(cache);
            string target=Path.Combine(cache,"tabletennis_updater.exe");
            if(!File.Exists(target) || UpdateCore.Hash(target)!=UpdateCore.Hash(self)) File.Copy(self,target,true);
            if(!File.Exists(Path.Combine(cache,"update_backend.py")) || UpdateCore.Hash(Path.Combine(cache,"update_backend.py"))!=UpdateCore.Hash(backend))
                File.Copy(backend,Path.Combine(cache,"update_backend.py"),true);
            return target;
        }
        [STAThread]
        public static int Main(string[] args) {
            string root=null;
            try {
                System.Net.ServicePointManager.SecurityProtocol=System.Net.SecurityProtocolType.Tls12;
                if(args.Length==3 && args[0]=="--verify-manifest") {UpdateCore.VerifyManifest(args[1],args[2]);return 0;}
                root=UpdateCore.Root(Option(args,"--root") ?? Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')));
                if(Array.IndexOf(args,"--close-game")>=0) {UpdateCore.CloseGame(root);return 0;}
                if(Array.IndexOf(args,"--install-tools")>=0) return UpdateCore.InstallTools(root);
                if(Array.IndexOf(args,"--check-only")>=0) {
                    using(var check=new Mutex(false,"Local\\TTUpdateCheck-"+Identity(root))) {
                        bool acquired=false;
                        try {
                            try {acquired=check.WaitOne(30000);} catch(AbandonedMutexException) {acquired=true;}
                            if(!acquired) return 0;
                            string api=UpdateCore.Api;
#if UPDATE_TESTING
                            string test=Option(args,"--test-api");
                            if(test!=null) {
                                var uri=new Uri(test);
                                if(!uri.IsLoopback || uri.Scheme!="http") throw new ArgumentException("Test feed must be loopback HTTP.");
                                api=test;
                            }
#endif
                            var result=UpdateCore.Check(root,Array.IndexOf(args,"--force")>=0,api);
                            Console.WriteLine(result.message);return 0;
                        } finally {if(acquired) check.ReleaseMutex();}
                    }
                }
                bool recover=Array.IndexOf(args,"--recover")>=0;
                string cached=CopyToCache(root);
                if(cached!=null) {
                    Process.Start(new ProcessStartInfo(cached,"--root "+UpdateCore.Quote(root)+(recover?" --recover":"")) {
                        UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(cached)});
                    return 0;
                }
                using(var single=new Mutex(false,"Local\\TTUpdateInstall-"+Identity(root))) {
                    bool acquired=false;
                    try {
                        try {acquired=single.WaitOne(0);} catch(AbandonedMutexException) {acquired=true;}
                        if(!acquired) {MessageBox.Show("The updater is already open for this installation.","Table Tennis update");return 0;}
                        var app=new Application();app.Run(new UpdateWindow(root,recover));return 0;
                    } finally {if(acquired) single.ReleaseMutex();}
                }
            } catch(Exception error) {
                if(root!=null) try {UpdateCore.WriteStatus(root,new UpdateResult {state="error",message="Update check unavailable: "+error.Message});} catch { }
                if(Array.IndexOf(args,"--check-only")>=0 || Array.IndexOf(args,"--verify-manifest")>=0 || Array.IndexOf(args,"--close-game")>=0 || Array.IndexOf(args,"--install-tools")>=0)
                    Console.Error.WriteLine(error.Message);
                else MessageBox.Show(error.Message,"Table Tennis updater",MessageBoxButton.OK,MessageBoxImage.Warning);
                return 1;
            }
        }
    }
    public class UpdateWindow : Window {
        readonly string root;
        TextBlock status,version;
        TextBox notes,log;
        ProgressBar progress;
        Button install,check,later,cancel,rollback;
        CheckBox allowTools;
        UpdateResult result;
        bool busy;
        volatile bool cancelled;
        Process worker;
        string logPath;
        public UpdateWindow(string root,bool recovery) {
            this.root=root;Title="Table Tennis - Updates";Width=690;Height=690;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=new SolidColorBrush(Color.FromRgb(18,20,26));
            var panel=new StackPanel {Margin=new Thickness(24)};
            panel.Children.Add(new TextBlock {Text="TABLE TENNIS UPDATES",FontSize=24,FontWeight=FontWeights.Bold,Foreground=Brushes.Gold});
            version=new TextBlock {Foreground=Brushes.LightGray,Margin=new Thickness(0,8,0,12)};
            try {version.Text="Installed version: "+UpdateCore.InstalledVersion(root);} catch {version.Text="Installed version: unknown";}
            panel.Children.Add(version);
            status=new TextBlock {Text="Checking for updates...",Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap};panel.Children.Add(status);
            notes=new TextBox {Height=140,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                Margin=new Thickness(0,10,0,10),Background=new SolidColorBrush(Color.FromRgb(28,31,39)),Foreground=Brushes.White};panel.Children.Add(notes);
            allowTools=new CheckBox {Foreground=Brushes.LightGray,Content=new TextBlock {Text="Allow missing Microsoft build components to be installed if needed (Windows may request permission).",TextWrapping=TextWrapping.Wrap}};
            panel.Children.Add(allowTools);
            panel.Children.Add(new TextBlock {Text="Updates rebuild the game locally from your existing ISO. Saves and settings are kept. The game closes after you approve installation.",
                Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10)});
            progress=new ProgressBar {Height=8,Margin=new Thickness(0,0,0,10)};panel.Children.Add(progress);
            log=new TextBox {Height=150,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                Background=Brushes.Black,Foreground=Brushes.LightGray};panel.Children.Add(log);
            var buttons=new WrapPanel {Margin=new Thickness(0,12,0,0)};
            install=Button("Install update");check=Button("Check now");later=Button("Later");cancel=Button("Cancel update");rollback=Button("Restore previous version");
            cancel.Visibility=Visibility.Collapsed;install.IsEnabled=false;
            foreach(var b in new[]{install,check,later,cancel,rollback}) buttons.Children.Add(b);panel.Children.Add(buttons);
            rollback.IsEnabled=File.Exists(UpdateCore.SafePath(root,".updates","previous.json"));
            Content=new ScrollViewer {Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            check.Click+=async (s,e)=>await Check();
            install.Click+=async (s,e)=>await Install();
            later.Click+=(s,e)=>Close();
            cancel.Click+=(s,e)=>Cancel();
            rollback.Click+=async (s,e)=>await Recover(true);
            Loaded+=async (s,e)=>{if(recovery || File.Exists(UpdateCore.SafePath(root,".updates","in-progress"))) await Recover(false);else await Check();};
            Closing+=(s,e)=>{if(busy){e.Cancel=true;Cancel();}};
        }
        static Button Button(string text) {return new Button {Content=text,Padding=new Thickness(12,6,12,6),Margin=new Thickness(0,0,8,6)};}
        void SetBusy(bool value) {
            busy=value;progress.IsIndeterminate=value;check.IsEnabled=later.IsEnabled=allowTools.IsEnabled=!value;
            install.IsEnabled=!value && result!=null && result.state=="available";
            rollback.IsEnabled=!value && File.Exists(UpdateCore.SafePath(root,".updates","previous.json"));
            cancel.Visibility=value?Visibility.Visible:Visibility.Collapsed;
            cancel.IsEnabled=value;
        }
        void Cancel() {
            if(!cancel.IsEnabled) return;
            cancelled=true;status.Text="Cancelling safely...";
            try {UpdateCore.AtomicText(UpdateCore.SafePath(root,".updates","cancel.request"),"cancel\n");} catch { }
        }
        void Line(string text) {
            if(text==null) return;
            if(logPath!=null) lock(this) File.AppendAllText(logPath,text+Environment.NewLine,Encoding.UTF8);
            Dispatcher.BeginInvoke(new Action(()=>{
                if(text.StartsWith("STAGE:")) status.Text=text.Substring(6);
                if(text=="STAGE:Installing verified files") cancel.IsEnabled=false;
                if(log.Text.Length>100000) log.Text=log.Text.Substring(log.Text.Length-50000);
                log.AppendText(text+Environment.NewLine);log.ScrollToEnd();
            }));
        }
        async Task Check() {
            if(busy) return;cancelled=false;SetBusy(true);status.Text="Checking the latest signed release...";
            try {
                result=await Task.Run(()=>UpdateCore.Check(root,true));
                status.Text=result.message;notes.Text=result.notes??"";
                install.Content=result.state=="available"?"Install "+result.version:"Install update";
            } catch(Exception error) {
                result=null;status.Text="Update check unavailable. You can keep playing. "+error.Message;
                UpdateCore.WriteStatus(root,new UpdateResult {state="error",message=status.Text});
            } finally {SetBusy(false);}
        }
        async Task<int> Backend(string action,string arguments) {
            string runner=action=="apply"?UpdateCore.PrepareRunner(root):UpdateCore.SafePath(root,".updates","runner");
            string python=Path.Combine(runner,"python","python.exe"),script=Path.Combine(runner,"update_backend.py");
            worker=new Process {StartInfo=new ProcessStartInfo(python,"-I -B "+UpdateCore.Quote(script)+" "+action+" --root "+UpdateCore.Quote(root)+arguments) {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=runner}};
            worker.OutputDataReceived+=(s,e)=>Line(e.Data);worker.ErrorDataReceived+=(s,e)=>Line(e.Data);
            worker.Start();worker.BeginOutputReadLine();worker.BeginErrorReadLine();
            return await Task.Run(()=>{worker.WaitForExit();return worker.ExitCode;});
        }
        void BeginLog() {
            logPath=UpdateCore.SafePath(root,".updates","logs","update-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
        }
        async Task Install() {
            if(busy || result==null || result.manifest==null) return;
            if(MessageBox.Show(this,"Close the game and this launcher's local server, then install version "+result.version+"?\n\nThe game will be rebuilt locally from your ISO. Saves and settings are kept.",
                "Install Table Tennis update",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
            bool tools=allowTools.IsChecked==true;cancelled=false;SetBusy(true);BeginLog();
            try {
                File.Delete(UpdateCore.SafePath(root,".updates","cancel.request"));
                var selected=result;
                status.Text="Downloading verified update packages...";
                string source=await Task.Run(()=>UpdateCore.Download(root,selected,selected.manifest.source,selected.sourceUrl,()=>cancelled));
                string sdk=await Task.Run(()=>UpdateCore.Download(root,selected,selected.manifest.sdk,selected.sdkUrl,()=>cancelled));
                if(cancelled) throw new OperationCanceledException();
                UpdateCore.VerifyManifest(selected.manifestPath,selected.signaturePath);
                await Task.Run(()=>UpdateCore.CloseGame(root));
                string runner=UpdateCore.PrepareRunner(root);
                string args=" --manifest "+UpdateCore.Quote(selected.manifestPath)+" --signature "+UpdateCore.Quote(selected.signaturePath)+
                    " --source "+UpdateCore.Quote(source)+" --sdk "+UpdateCore.Quote(sdk)+" --verifier "+UpdateCore.Quote(Path.Combine(runner,"tabletennis_updater.exe"));
                if(tools) args+=" --allow-tools";
                if(cancelled) throw new OperationCanceledException();
                int exit=await Backend("apply",args);
                if(exit!=0) throw new IOException(cancelled?"Update cancelled; your installed version was kept.":"The update stopped. Open the log above for details. The launcher can recover an interrupted file change.");
                UpdateCore.WriteStatus(root,new UpdateResult {state="up_to_date",version=selected.version,message="Updated to "+selected.version+"."});
                status.Text="Update complete. Starting the game...";
                StartLauncher(true);SetBusy(false);Close();
            } catch(Exception error) {status.Text=error.Message;Line("UPDATE_ERROR:"+error.Message);}
            finally {SetBusy(false);}
        }
        async Task Recover(bool previous) {
            if(busy) return;
            if(previous && MessageBox.Show(this,"Restore the previous game version? Current settings and saves stay in place.","Restore previous version",
                MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes) return;
            cancelled=false;SetBusy(true);cancel.IsEnabled=false;BeginLog();
            try {
                await Task.Run(()=>UpdateCore.CloseGame(root));
                int exit=await Backend(previous?"rollback":"recover","");
                if(exit!=0) throw new IOException("Recovery did not complete. Keep this log and do not replace files manually.");
                UpdateCore.WriteStatus(root,new UpdateResult {state="up_to_date",version=UpdateCore.InstalledVersion(root),message="Installation recovery completed."});
                SetBusy(false);StartLauncher(false);Close();
            } catch(Exception error) {status.Text=error.Message;Line(error.Message);}
            finally {SetBusy(false);}
        }
        void StartLauncher(bool play) {
            string launcher=Path.Combine(root,"dist","tabletennis_launcher.exe");
            Process.Start(new ProcessStartInfo(launcher,play?"--play-after-update":"") {WorkingDirectory=Path.GetDirectoryName(launcher),UseShellExecute=false});
        }
    }
}
