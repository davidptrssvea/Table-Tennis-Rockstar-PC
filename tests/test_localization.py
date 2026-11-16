"""Authored UI translation coverage and live launcher language changes."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'project/tools'))
import generate_localization
from patch_menu import _movies


class CatalogueTests(unittest.TestCase):
    def test_generated_dictionaries_match_catalogue(self):
        subprocess.run([sys.executable,str(ROOT/'project/tools/generate_localization.py'),'--check'],check=True)
        rows={row[0]:row for row in generate_localization.catalogue()}
        for key in ['PC SETTINGS','PHOTO MODE','ACHIEVEMENTS','GAME LANGUAGE','SAVE & BACK','LAUNCH GAME']:
            self.assertEqual(len(rows[key]),5)
            self.assertTrue(all(rows[key][index]!=key for index in range(1,5)))

    @unittest.skipUnless(os.environ.get('TABLETENNIS_TEST_DIST'),'Opt-in locally generated menu archives')
    def test_authored_menu_labels_in_all_language_packs(self):
        rows={row[0]:row for row in generate_localization.catalogue()}
        folder=Path(os.environ['TABLETENNIS_TEST_DIST'])/'assets/resource/flash'
        for index,code in enumerate(['en','fr','de','es','it']):
            path=(folder/code/'flash.rpf') if index else folder/'flash.rpf'
            movies=_movies(bytearray(path.read_bytes()))
            for name,(movie,*_rest) in movies.items():
                for key in ['PC SETTINGS','QUIT TO DESKTOP']:
                    if key=='QUIT TO DESKTOP' and name!=b'pongui_001.xeck':continue
                    expected=rows[key][index].encode().ljust(len(key.encode()),b' ')+b'\0'
                    self.assertEqual(movie.count(expected),1,(code,name,key))


@unittest.skipUnless(os.name=='nt','Windows WPF launcher')
class LauncherLanguageTests(unittest.TestCase):
    def test_live_switch_preserves_settings_and_user_text(self):
        csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(prefix='Table Tennis localization ') as folder:
            folder=Path(folder);probe=folder/'probe.cs';launcher=folder/'launcher.exe'
            probe.write_text('''using System;using System.IO;using System.Reflection;using System.Windows;using System.Windows.Controls;
class Probe {
 static void Collect(DependencyObject node,System.Collections.Generic.List<DependencyObject> controls) {
  controls.Add(node);
  foreach(object child in LogicalTreeHelper.GetChildren(node)) if(child is DependencyObject) Collect((DependencyObject)child,controls);
 }
 [STAThread] static void Main(string[] args) {
  string ini=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.ini");
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"community-engine.json"),"{}");
  File.WriteAllText(ini,"future_setting=keep\\nresolution=1440p\\ndisplay_aspect=21:9\\nplayer_name=English Player\\nonline_server=192.0.2.1:27360\\nroom_code=ABC234\\n");
  var assembly=Assembly.LoadFrom(args[0]);var t=assembly.GetType("TableTennisLauncher.MainWindow");
  var text=assembly.GetType("TableTennisLauncher.UiText");
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  object window=Activator.CreateInstance(t);
  var controls=new System.Collections.Generic.List<DependencyObject>();Collect((DependencyObject)window,controls);
  ComboBox hostMode=null;CheckBox consent=null;
  foreach(var control in controls) {
   var c=control as ComboBox;
   if(c!=null && c.Items.Count==3 && c.Items[0] is string && ((string)c.Items[0]).StartsWith("Same network / shared VPN")) hostMode=c;
   var check=control as CheckBox;var label=check==null?null:check.Content as TextBlock;
   if(label!=null && label.Text.StartsWith("EXPERIMENTAL: allow a five-minute")) consent=check;
  }
  if(hostMode==null || consent==null || consent.IsChecked!=false) throw new Exception("UPnP must start disabled");
  // Local UI objects only: no server, network request or router is started.
  hostMode.SelectedIndex=1;consent.IsChecked=true;
  Func<string,object> field=name=>t.GetField(name,flags).GetValue(window);
  var language=(ComboBox)field("comboLanguage");
  var codes=new[]{"en","fr","de","es","it"};
  var titles=new[]{"Settings & Launcher","Réglages et lancement","Einstellungen und Start","Ajustes e inicio","Impostazioni e avvio"};
  foreach(int index in new[]{1,2,3,4,0,4,2,0}) {
   language.SelectedIndex=index;
   if(!((Window)window).Title.Contains(titles[index])) throw new Exception("Window title not translated: "+codes[index]);
   if((string)text.GetField("Code").GetValue(null)!=codes[index]) throw new Exception("UI and game language differ");
   if(hostMode.SelectedIndex!=1 || consent.IsChecked!=true) throw new Exception("Language switch changed hosting selection or consent");
   string first="Hosting prepared. Your friend must test the invitation from their connection; outside reachability is not verified here.";
   string last=" Previous router cleanup is pending; retry it on the original router.";
   var translate=text.GetMethod("T");
   string expectedMessage=(string)translate.Invoke(null,new object[]{first})+(string)translate.Invoke(null,new object[]{last});
   if((string)text.GetMethod("Message").Invoke(null,new object[]{first+last})!=expectedMessage) throw new Exception("Combined host status was not translated");
   if(((ComboBox)field("comboAspect")).SelectedIndex!=2 || !(((RadioButton)field("rb1440")).IsChecked==true) || ((RadioButton)field("rbUltra")).IsChecked==true)
    throw new Exception("Changing language changed graphics settings");
   if(((TextBox)field("txtPlayer")).Text!="English Player" || ((TextBox)field("txtServer")).Text!="192.0.2.1:27360" || ((TextBox)field("txtRoom")).Text!="ABC234")
    throw new Exception("Translation modified user data");
   t.GetMethod("SaveSettings",flags).Invoke(window,null);
   string saved=File.ReadAllText(ini);
   foreach(string expected in new[]{"game_language="+codes[index],"future_setting=keep","player_name=English Player","resolution=1440p"})
    if(!saved.Contains(expected)) throw new Exception("Settings lost: "+expected);
  }
  hostMode.SelectedIndex=2;
  if(consent.IsChecked!=false) throw new Exception("An actual hosting mode change must still clear UPnP consent");
  ((Window)window).Close();
 }
}''',encoding='utf-8-sig')
            common=[str(csc),'/nologo','/optimize','/codepage:65001','/lib:'+str(csc.parent/'WPF'),
                    '/r:PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll,System.dll,System.Xaml.dll']
            subprocess.run(common+['/target:winexe','/out:'+str(launcher)]+[str(ROOT/'project/launcher'/s) for s in ['Launcher.cs','HostSession.cs','GatewayLeaseRouter.cs']],check=True)
            exe=folder/'probe.exe'
            subprocess.run(common+['/target:exe','/out:'+str(exe),str(probe)],check=True)
            subprocess.run([str(exe),str(launcher)],check=True,timeout=30)


@unittest.skipUnless(os.name=='nt','Windows installer/updater localization')
class SetupLanguageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp=tempfile.TemporaryDirectory(prefix='Table Tennis localized setup ')
        cls.folder=Path(cls.temp.name)
        csc=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        cls.common=[str(csc),'/nologo','/optimize','/codepage:65001','/lib:'+str(csc.parent/'WPF'),
            '/r:PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll,System.dll,System.Xaml.dll,System.Windows.Forms.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll,System.Web.Extensions.dll']
        payload=cls.folder/'stub.zip'
        # A test-only worker: no downloads, compilation, Microsoft setup, UAC or
        # real disc data. It exercises the production post-success language save.
        with zipfile.ZipFile(payload,'w') as archive:
            archive.writestr('project/tools/bootstrap.ps1','''param([string]$Root,[string]$IsoPath,[switch]$AllowToolInstall,[string]$SdkPath)
Write-Output 'STAGE:Preparing the installer'
$dist=Join-Path $Root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Root 'test-desktop') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $dist 'settings.ini'),"# preserved`r`niso_path=$IsoPath`r`nfuture_setting=keep`r`ngame_language=en`r`n")
[IO.File]::WriteAllText((Join-Path $dist 'tabletennis_launcher.exe'),'Test placeholder; never executed')
Write-Output 'STAGE:Ready'
exit 0
''')
        installer_code=(ROOT/'project/installer/Installer.cs').read_text(encoding='utf-8-sig')
        original='CreateDesktopShortcut(target, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));'
        assert installer_code.count(original)==1
        # The only test source substitution redirects the shortcut away from the
        # user's real desktop. The actual install/language workflow is unchanged.
        installer_code=installer_code.replace(original,'CreateDesktopShortcut(target, Path.Combine(target,"test-desktop"));')
        private_source=cls.folder/'installer.cs';private_source.write_text(installer_code,encoding='utf-8-sig')
        cls.installer=cls.folder/'installer.exe';cls.updater=cls.folder/'updater.exe'
        subprocess.run(cls.common+['/target:winexe','/out:'+str(cls.installer),'/resource:'+str(payload)+',source.zip',str(private_source)],check=True)
        subprocess.run(cls.common+['/target:winexe','/out:'+str(cls.updater),
            '/resource:'+str(ROOT/'project/updater/release-public-key.xml')+',update-public-key.xml',
            str(ROOT/'project/updater/UpdateCore.cs'),str(ROOT/'project/updater/Updater.cs')],check=True)
        probe=cls.folder/'probe.cs'
        probe.write_text('''using System;using System.IO;using System.Reflection;using System.Text;using System.Threading;
using System.Threading.Tasks;using System.Windows;using System.Windows.Controls;using System.Windows.Threading;
class Probe {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static object Field(object window,string name) {return window.GetType().GetField(name,flags).GetValue(window);}
 static string T(Type text,string value) {return (string)text.GetMethod("T").Invoke(null,new object[]{value});}
 static string F(Type text,string value,string arg) {return (string)text.GetMethod("F").Invoke(null,new object[]{value,new object[]{arg}});}
 static void Pump(Task task) {
  var frame=new DispatcherFrame();var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};
  var started=DateTime.UtcNow;
  timer.Tick+=(s,e)=>{if(task.IsCompleted || DateTime.UtcNow-started>TimeSpan.FromSeconds(30))frame.Continue=false;};
  timer.Start();Dispatcher.PushFrame(frame);timer.Stop();
  if(!task.IsCompleted)throw new Exception("Test worker timed out");task.GetAwaiter().GetResult();
 }
 static void Drain() {Dispatcher.CurrentDispatcher.Invoke(new Action(()=>{}),DispatcherPriority.ApplicationIdle);}
 static void Save(Type text,string root,string code) {text.GetMethod("SaveLanguage").Invoke(null,new object[]{root,code});}
 static string Read(Type text,string root) {return (string)text.GetMethod("ReadLanguage").Invoke(null,new object[]{root});}
 [STAThread] static void Main(string[] args) {
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  string root=args[3],dist=Path.Combine(root,"dist"),ini=Path.Combine(dist,"settings.ini");Directory.CreateDirectory(dist);
  File.WriteAllText(ini,"# preserved\\r\\niso_path=English disc=1.iso\\r\\nfuture_setting=keep\\r\\ngame_language=fr\\r\\nGame_Language=fr\\r\\n",new UTF8Encoding(true));
  File.WriteAllText(Path.Combine(dist,"install.json"),@"{""version"":""1.5.3"",""repository"":""davidptrssvea/Table-Tennis-Rockstar-PC""}");
  bool updater=args[2]=="updater";
  var assembly=Assembly.LoadFrom(updater?args[1]:args[0]);var text=assembly.GetType("TableTennisLauncher.UiText");
  if(args[2]=="persistence") {
   foreach(string code in new[]{"en","fr","de","es","it"}) {
    Save(text,root,code);if(Read(text,root)!=code)throw new Exception("Saved language did not reload");
    string saved=File.ReadAllText(ini);
    if(!saved.Contains("# preserved\\r\\n") || !saved.Contains("iso_path=English disc=1.iso") || !saved.Contains("future_setting=keep") || saved.Split(new[]{"game_language="},StringSplitOptions.None).Length!=2)
     throw new Exception("Language save damaged existing settings");
   }
   Save(text,root,"sv");if(Read(text,root)!="en")throw new Exception("Unsupported language must fall back to English");
   if(Directory.GetFiles(dist,"*.tmp").Length!=0)throw new Exception("Temporary settings file left behind");return;
  }
  if(!updater) {
   var type=assembly.GetType("TableTennisSetup.InstallerWindow");
   object window=Activator.CreateInstance(type,new object[]{new[]{"--destination",root}});
   var language=(ComboBox)Field(window,"language");var iso=(TextBox)Field(window,"iso");var consent=(CheckBox)Field(window,"consent");
   if(language.SelectedIndex!=1 || (string)text.GetField("Code").GetValue(null)!="fr")throw new Exception("Installer did not inherit existing French setting");
   iso.Text=Path.Combine(root,"English.iso");consent.IsChecked=true;string before=File.ReadAllText(ini);
   foreach(int i in new[]{2,3,4,0,1}) {
    language.SelectedIndex=i;
    if(((Window)window).Title!=T(text,"Table Tennis - Install & Play") || consent.IsChecked!=true || iso.Text!=Path.Combine(root,"English.iso"))throw new Exception("Installer switch damaged UI state");
    if(File.ReadAllText(ini)!=before)throw new Exception("Previewing a language wrote to installation settings");
   }
   if(args[2]=="install") {
    language.SelectedIndex=3;File.WriteAllText(iso.Text,"Local test ISO placeholder");
    var task=(Task)type.GetMethod("Install",flags).Invoke(window,null);Pump(task);
    if(((TextBlock)Field(window,"status")).Text!=T(text,"Ready to play!") || Read(text,root)!="es")throw new Exception("Installer did not persist chosen Spanish language on success");
    if(!File.Exists(Path.Combine(root,"test-desktop","Table Tennis.lnk")))throw new Exception("Private shortcut fixture was not used");
    string log=File.ReadAllText(Path.Combine(root,"installation.log"));
    if(!log.Contains("Table Tennis local installer v"+assembly.GetName().Version.ToString(3)) || !log.Contains("STAGE:Preparing the installer"))throw new Exception("Canonical log header/stages changed");
    if(!File.ReadAllText(ini).Contains("future_setting=keep"))throw new Exception("Post-install save lost worker settings");
   } else {
    string other=Path.Combine(root,"other");Save(text,other,"it");
    ((TextBox)Field(window,"folder")).Text=other;
    if((string)text.GetField("Code").GetValue(null)!="fr")throw new Exception("Explicit installer choice was overwritten by target folder");
    object fresh=Activator.CreateInstance(type,new object[]{new[]{"--destination",root}});
    ((TextBox)Field(fresh,"folder")).Text=other;
    if((string)text.GetField("Code").GetValue(null)!="it")throw new Exception("New target folder language was not detected");
    ((Window)fresh).Close();
   }
   ((Window)window).Close();return;
  }
  var updateType=assembly.GetType("TableTennisUpdates.UpdateWindow");
  foreach(string code in new[]{"en","fr","de","es","it"}) {
   Save(text,root,code);object window=Activator.CreateInstance(updateType,new object[]{root,false});
   if((string)text.GetField("Code").GetValue(null)!=code || ((Window)window).Title!=T(text,"Table Tennis - Updates"))throw new Exception("Updater did not inherit language");
   if(((TextBlock)Field(window,"version")).Text!=F(text,"Installed version: {0}","1.5.3"))throw new Exception("Version label not localized");
   string canonical="Update 1.5.4 is available.";
   if((string)text.GetMethod("Message").Invoke(null,new object[]{canonical})!=F(text,"Update {0} is available.","1.5.4"))throw new Exception("Dynamic version message not translated");
   string logPath=Path.Combine(root,"worker-"+code+".log");updateType.GetField("logPath",flags).SetValue(window,logPath);
   updateType.GetMethod("Line",flags).Invoke(window,new object[]{"STAGE:Installing verified files"});Drain();
   if(((TextBlock)Field(window,"status")).Text!=T(text,"Installing verified files") || ((Button)Field(window,"cancel")).IsEnabled)throw new Exception("Localization broke final install/cancel guard");
   if(!File.ReadAllText(logPath).Contains("STAGE:Installing verified files"))throw new Exception("Machine log was translated");
   ((TextBox)Field(window,"notes")).Text="Updates are installed only after you approve them.";
   ((Button)Field(window,"install")).Content=F(text,"Install {0}","1.5.4");
   string next=code=="it"?"de":"it";Save(text,root,next);
   typeof(Window).GetMethod("OnActivated",flags).Invoke(window,new object[]{EventArgs.Empty});
   if((string)text.GetField("Code").GetValue(null)!=next || ((Button)Field(window,"install")).Content.ToString()!=F(text,"Install {0}","1.5.4"))throw new Exception("Updater did not follow a changed language on reactivation");
   if(((TextBox)Field(window,"notes")).Text!="Updates are installed only after you approve them.")throw new Exception("Publisher release notes were modified");
   ((Window)window).Close();
  }
 }
}''',encoding='utf-8-sig')
        cls.probe=cls.folder/'probe.exe'
        subprocess.run(cls.common+['/target:exe','/out:'+str(cls.probe),str(probe)],check=True)

    @classmethod
    def tearDownClass(cls):cls.temp.cleanup()

    def run_probe(self,mode):
        with tempfile.TemporaryDirectory(dir=self.folder) as root:
            subprocess.run([str(self.probe),str(self.installer),str(self.updater),mode,root],check=True,timeout=45)

    def test_language_settings_merge_and_utf8(self):self.run_probe('persistence')
    def test_installer_live_language_and_existing_folder(self):self.run_probe('installer')
    def test_installer_success_hands_language_to_game(self):self.run_probe('install')
    def test_updater_language_status_and_cancel_guard(self):self.run_probe('updater')


if __name__=='__main__':unittest.main()
