"""Authored UI translation coverage and live launcher language changes."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

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


if __name__=='__main__':unittest.main()
