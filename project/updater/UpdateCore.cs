// Project-authored updater protocol. No game material or private signing key.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TableTennisUpdates {
    public class Asset { public string asset; public string sha256; public long size; }
    public class Manifest {
        public int schema; public int minimum_updater; public string repository;
        public string version; public string notes; public Asset source; public Asset sdk;
    }
    public class UpdateResult {
        public string state, version, message, notes, manifestPath, signaturePath, sourceUrl, sdkUrl;
        public Manifest manifest;
    }
    public static class UpdateCore {
        public const string Repository = "davidptrssvea/Table-Tennis-Rockstar-PC";
        public const string Api = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const int Protocol = 1;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 32 };
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern bool MoveFileEx(string from, string to, int flags);
        public static string Quote(string value) {
            if (value.IndexOfAny(new[]{'"','\r','\n','\0'}) >= 0) throw new ArgumentException("Invalid path");
            return "\"" + value.TrimEnd('\\') + "\"";
        }
        public static string Hash(string path) {
            using (var h=SHA256.Create()) using (var input=File.OpenRead(path))
                return BitConverter.ToString(h.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        public static string Root(string input) {
            string root=Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar);
            if(root==Path.GetPathRoot(root).TrimEnd('\\') || !Directory.Exists(root) ||
               !File.Exists(Path.Combine(root,"dist","settings.ini")))
                throw new InvalidDataException("Select an existing Table Tennis installation.");
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("Use the actual installation folder, not a directory link.");
            SafePath(root,".updates"); return root;
        }
        public static string SafePath(string root, params string[] parts) {
            string path=root;
            foreach(string part in parts) path=Path.Combine(path,part);
            path=Path.GetFullPath(path);
            if(!path.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Update path escapes installation.");
            for(string p=path;p!=null && !p.Equals(root,StringComparison.OrdinalIgnoreCase);p=Path.GetDirectoryName(p))
                if((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("Directory links are not supported in update folders.");
            return path;
        }
        public static void AtomicText(string path,string content) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".new";
            using(var file=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)) {
                byte[] bytes=new UTF8Encoding(false).GetBytes(content);file.Write(bytes,0,bytes.Length);file.Flush(true);
            }
            if(!MoveFileEx(temporary,path,9)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public static string InstalledVersion(string root) {
            string path=SafePath(root,"dist","install.json");
            if(!File.Exists(path)) throw new InvalidDataException("Version information is missing. Re-run the current installer once to enable updates.");
            var data=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
            if(!data.ContainsKey("repository") || (string)data["repository"]!=Repository)
                throw new InvalidDataException("This installation belongs to a different project.");
            string version=(string)data["version"]; ParseVersion(version); return version;
        }
        public static Version ParseVersion(string value) {
            if(!Regex.IsMatch(value??"",@"\A\d+\.\d+\.\d+\z")) throw new InvalidDataException("Invalid release version.");
            return Version.Parse(value);
        }
        public static void VerifyManifest(string manifest,string signature) {
            if(new FileInfo(manifest).Length>1048576 || new FileInfo(signature).Length>1024)
                throw new InvalidDataException("Update signature files are too large.");
            string key;
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("update-public-key.xml"))
            using(var reader=new StreamReader(input)) key=reader.ReadToEnd();
            using(var rsa=new RSACryptoServiceProvider(new CspParameters(24))) {
                rsa.PersistKeyInCsp=false; rsa.FromXmlString(key);
                if(!rsa.VerifyData(File.ReadAllBytes(manifest),"SHA256",File.ReadAllBytes(signature)))
                    throw new CryptographicException("The update is not signed by this project's trusted release key. Nothing was installed.");
            }
        }
        static bool AssetValid(Asset asset) {
            return asset!=null && Regex.IsMatch(asset.asset??"",@"\A[A-Za-z0-9][A-Za-z0-9._-]*\.zip\z") &&
                Regex.IsMatch(asset.sha256??"",@"\A[0-9a-f]{64}\z") && asset.size>0 && asset.size<=1073741824;
        }
        public static Manifest ReadManifest(string path) {
            var m=Json.Deserialize<Manifest>(File.ReadAllText(path));
            if(m==null || m.schema!=Protocol || m.repository!=Repository || m.minimum_updater>Protocol ||
               !AssetValid(m.source) || !AssetValid(m.sdk)) throw new InvalidDataException("Unsupported signed update package.");
            ParseVersion(m.version); return m;
        }
        public static string AssetUrl(string url,string tag,string name,string api) {
            Uri uri;
            if(!Uri.TryCreate(url,UriKind.Absolute,out uri) || uri.UserInfo.Length!=0 || uri.Query.Length!=0 || uri.Fragment.Length!=0)
                throw new InvalidDataException("Invalid release asset URL.");
            string expected="/"+Repository+"/releases/download/"+tag+"/"+name;
            bool valid=uri.Scheme=="https" && uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase) &&
                       uri.AbsolutePath.Equals(expected,StringComparison.Ordinal);
#if UPDATE_TESTING
            var feed=new Uri(api);
            valid |= feed.IsLoopback && uri.IsLoopback && uri.Scheme=="http" && uri.Authority==feed.Authority && uri.AbsolutePath==expected;
#endif
            if(!valid) throw new InvalidDataException("The asset does not belong to this project's release.");
            return uri.AbsoluteUri;
        }
        static Stream OpenUrl(string url) {
            var request=(HttpWebRequest)WebRequest.Create(url);
            request.UserAgent="TableTennis-Updater/1";
            request.Accept="application/vnd.github+json";
            request.Timeout=8000; request.ReadWriteTimeout=10000;
            request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
            return request.GetResponse().GetResponseStream();
        }
        static byte[] Fetch(string url,int limit) {
            using(var input=OpenUrl(url)) using(var output=new MemoryStream()) {
                byte[] buffer=new byte[16384];int count;
                while((count=input.Read(buffer,0,buffer.Length))>0) {
                    if(output.Length+count>limit) throw new InvalidDataException("Release information exceeds its size limit.");
                    output.Write(buffer,0,count);
                }
                return output.ToArray();
            }
        }
        public static void WriteStatus(string root,UpdateResult result) {
            Func<string,string> line=s=>(s??"").Replace("\r"," ").Replace("\n"," ");
            AtomicText(SafePath(root,".updates","status.txt"),"state="+line(result.state)+"\nversion="+line(result.version)+
                       "\nmessage="+line(result.message)+"\n");
        }
        public static UpdateResult Check(string root,bool force,string api=Api) {
            string installed=InstalledVersion(root),status=SafePath(root,".updates","status.txt");
            if(!force && File.Exists(status) && DateTime.UtcNow-File.GetLastWriteTimeUtc(status)<TimeSpan.FromHours(6)) {
                var fields=new Dictionary<string,string>();
                foreach(string line in File.ReadAllLines(status)) {int p=line.IndexOf('=');if(p>0) fields[line.Substring(0,p)]=line.Substring(p+1);}
                string state,version,message;
                if(fields.TryGetValue("state",out state) && fields.TryGetValue("version",out version) && fields.TryGetValue("message",out message) &&
                   (state=="up_to_date" || state=="available") &&
                   (state!="available" || ParseVersion(version)>ParseVersion(installed)))
                    return new UpdateResult {state=state,version=version,message=message};
            }
            var release=Json.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(Fetch(api,1048576)));
            if((bool)release["draft"] || (bool)release["prerelease"]) throw new InvalidDataException("Only published stable releases are accepted.");
            string tag=(string)release["tag_name"];
            if(!Regex.IsMatch(tag??"",@"\Av\d+\.\d+\.\d+\z")) throw new InvalidDataException("Unsupported release tag.");
            var assets=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var value in (System.Collections.IEnumerable)release["assets"]) {
                var item=(Dictionary<string,object>)value;
                string name=(string)item["name"];
                if(assets.ContainsKey(name)) throw new InvalidDataException("Ambiguous release assets.");
                assets.Add(name,(string)item["browser_download_url"]);
            }
            var result=new UpdateResult {state="up_to_date",version=installed,message="No newer signed update is available."};
            if(!assets.ContainsKey("tabletennis-update.json") || !assets.ContainsKey("tabletennis-update.json.sig")) {
                WriteStatus(root,result);return result;
            }
            string folder=SafePath(root,".updates","downloads",tag);Directory.CreateDirectory(folder);
            string manifest=Path.Combine(folder,"tabletennis-update.json"),signature=manifest+".sig";
            byte[] manifestBytes=Fetch(AssetUrl(assets["tabletennis-update.json"],tag,"tabletennis-update.json",api),1048576);
            byte[] signatureBytes=Fetch(AssetUrl(assets["tabletennis-update.json.sig"],tag,"tabletennis-update.json.sig",api),1024);
            File.WriteAllBytes(manifest,manifestBytes);File.WriteAllBytes(signature,signatureBytes);
            VerifyManifest(manifest,signature);var m=ReadManifest(manifest);
            if("v"+m.version!=tag) throw new InvalidDataException("Release tag and signed version disagree.");
            if(ParseVersion(m.version)>ParseVersion(installed)) {
                if(!assets.ContainsKey(m.source.asset) || !assets.ContainsKey(m.sdk.asset)) throw new InvalidDataException("The signed release is incomplete.");
                result=new UpdateResult {state="available",version=m.version,message="Update "+m.version+" is available.",notes=m.notes,
                    manifest=m,manifestPath=manifest,signaturePath=signature,
                    sourceUrl=AssetUrl(assets[m.source.asset],tag,m.source.asset,api),sdkUrl=AssetUrl(assets[m.sdk.asset],tag,m.sdk.asset,api)};
            }
            WriteStatus(root,result);return result;
        }
        public static string Download(string root,UpdateResult result,Asset asset,string url,Func<bool> cancelled) {
            string path=SafePath(root,".updates","downloads","v"+result.version,asset.asset);
            if(File.Exists(path) && new FileInfo(path).Length==asset.size && Hash(path)==asset.sha256) return path;
            string temporary=path+".download";Directory.CreateDirectory(Path.GetDirectoryName(path));
            try {
                using(var input=OpenUrl(url)) using(var output=File.Create(temporary)) {
                    byte[] buffer=new byte[65536];int count;long total=0;
                    while((count=input.Read(buffer,0,buffer.Length))>0) {
                        if(cancelled()) throw new OperationCanceledException("Update cancelled.");
                        total+=count;if(total>asset.size) throw new InvalidDataException("Downloaded package exceeds its signed size.");
                        output.Write(buffer,0,count);
                    }
                    if(total!=asset.size) throw new InvalidDataException("Downloaded package is incomplete.");
                }
                if(Hash(temporary)!=asset.sha256) throw new CryptographicException("Downloaded package checksum failed.");
                if(!MoveFileEx(temporary,path,9)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return path;
            } finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void CloseGame(string root) {
            CloseRecordedServer(root);
            foreach(string name in new[]{"tabletennis","tabletennis_launcher","tabletennis_updater"}) foreach(var process in Process.GetProcessesByName(name)) {
                using(process) {
                    string expected=Path.Combine(root,"dist",name+".exe");
                    string file;
                    try {file=process.MainModule.FileName;} catch {continue;}
                    if(!Path.GetFullPath(file).Equals(expected,StringComparison.OrdinalIgnoreCase)) continue;
                    if(name=="tabletennis_updater") {
                        if(!process.WaitForExit(30000)) throw new IOException("An older update check is still running. Wait and try again.");
                        continue;
                    }
                    if(!process.CloseMainWindow() || !process.WaitForExit(15000))
                        throw new IOException("Close the game and launcher in this installation, then try again. No process was force-terminated.");
                }
            }
        }
        static void CloseRecordedServer(string root) {
            string record=SafePath(root,".updates","server.pid");
            if(!File.Exists(record)) return;
            string[] lines=File.ReadAllLines(record);int pid;long ticks;
            if(lines.Length<3 || !int.TryParse(lines[0],out pid) || !long.TryParse(lines[1],out ticks)) return;
            Process process;
            try {process=Process.GetProcessById(pid);} catch(ArgumentException) {return;}
            using(process) {
                if(process.StartTime.ToUniversalTime().Ticks!=ticks) return;
                string executable=Path.GetFullPath(process.MainModule.FileName);
                bool owned=false;
                foreach(string relative in new[]{"dist/python/python.exe",".tools/python314/python.exe","tools/python/python.exe"})
                    owned |= executable.Equals(SafePath(root,relative.Replace('/',Path.DirectorySeparatorChar)),StringComparison.OrdinalIgnoreCase);
                if(!owned || !executable.Equals(lines[2],StringComparison.OrdinalIgnoreCase)) return;
                if(!process.CloseMainWindow() || !process.WaitForExit(3000)) {process.Kill();process.WaitForExit(10000);}
            }
            File.Delete(record);
        }
        public static string Python(string root) {
            foreach(string name in new[]{"dist/python/python.exe",".tools/python314/python.exe","tools/python/python.exe",".updates/runner/python/python.exe"}) {
                string file=SafePath(root,name.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(file)) return file;
            }
            throw new FileNotFoundException("The bundled Python runtime is missing. Re-run the installer.");
        }
        static void CopyTree(string source,string target) {
            Directory.CreateDirectory(target);
            foreach(string file in Directory.GetFiles(source)) File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
            foreach(string folder in Directory.GetDirectories(source)) {
                if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0) throw new IOException("Runtime directory links are not supported.");
                CopyTree(folder,Path.Combine(target,Path.GetFileName(folder)));
            }
        }
        public static string PrepareRunner(string root) {
            string runner=SafePath(root,".updates","runner");Directory.CreateDirectory(runner);
            string self=Assembly.GetExecutingAssembly().Location;
            string target=Path.Combine(runner,"tabletennis_updater.exe");
            if(!Path.GetFullPath(self).Equals(target,StringComparison.OrdinalIgnoreCase)) File.Copy(self,target,true);
            string backend=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update_backend.py");
            if(!Path.GetFullPath(backend).Equals(Path.Combine(runner,"update_backend.py"),StringComparison.OrdinalIgnoreCase))
                File.Copy(backend,Path.Combine(runner,"update_backend.py"),true);
            string python=Python(root),pythonTarget=Path.Combine(runner,"python");
            if(!Path.GetDirectoryName(python).Equals(pythonTarget,StringComparison.OrdinalIgnoreCase)) CopyTree(Path.GetDirectoryName(python),pythonTarget);
            return runner;
        }
        public static string BuildToolsArguments(string installPath,string channelId) {
            const string components="--passive --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended "+
                "--add Microsoft.VisualStudio.Component.VC.Llvm.Clang --add Microsoft.VisualStudio.Component.VC.CMake.Project";
            if(string.IsNullOrEmpty(installPath)) return components;
            if(!Path.IsPathRooted(installPath) || Path.GetPathRoot(installPath).Length<3 || installPath.IndexOfAny(new[]{'"','\r','\n'})>=0)
                throw new InvalidDataException("Invalid Build Tools installation path.");
            if(!Regex.IsMatch(channelId??"",@"\AVisualStudio\.17\.[A-Za-z0-9_.-]+\z"))
                throw new InvalidDataException("Invalid Visual Studio 2022 channel.");
            return "modify --installPath "+Quote(installPath.TrimEnd('\\'))+" --channelId "+channelId+" "+components;
        }
        static string[] ExistingBuildTools() {
            string vswhere=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft Visual Studio","Installer","vswhere.exe");
            if(!File.Exists(vswhere)) return null;
            var info=new ProcessStartInfo(vswhere,"-latest -version [17.0,18.0) -products Microsoft.VisualStudio.Product.BuildTools -format json -utf8") {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,StandardOutputEncoding=Encoding.UTF8};
            using(var process=Process.Start(info)) {
                string output=process.StandardOutput.ReadToEnd();process.WaitForExit();
                if(process.ExitCode!=0) throw new InvalidDataException("Could not discover existing Visual Studio Build Tools. Open Visual Studio Installer and retry.");
                foreach(var instance in Json.Deserialize<List<Dictionary<string,object>>>(output)) {
                    object path,channel,product,number;Version version;
                    if(instance.TryGetValue("productId",out product) && (string)product=="Microsoft.VisualStudio.Product.BuildTools" &&
                       instance.TryGetValue("installationVersion",out number) && Version.TryParse((string)number,out version) && version.Major==17 &&
                       instance.TryGetValue("installationPath",out path) && Directory.Exists((string)path)) {
                        if(!instance.TryGetValue("channelId",out channel)) throw new InvalidDataException("Existing Build Tools channel is missing. Open Visual Studio Installer and repair it.");
                        return new[]{(string)path,(string)channel};
                    }
                }
            }
            return null;
        }
        public static int InstallTools(string root) {
            string path=SafePath(root,".updates","components","vs_buildtools.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path,Fetch("https://aka.ms/vs/17/release/vs_buildtools.exe",33554432));
            // Read-only Authenticode verification; no execution-policy or
            // Windows security settings are changed.
            string literal="'"+path.Replace("'","''")+"'";
            string command="$s=Get-AuthenticodeSignature -LiteralPath "+literal+
                ";if($s.Status -ne 'Valid' -or $s.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation'){exit 1}";
            using(var verify=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -Command "+Quote(command)) {
                UseShellExecute=false,CreateNoWindow=true})) {
                verify.WaitForExit();if(verify.ExitCode!=0) throw new CryptographicException("The Microsoft installer signature could not be verified.");
            }
            var existing=ExistingBuildTools();
            string arguments=BuildToolsArguments(existing==null?null:existing[0],existing==null?null:existing[1]);
            using(var process=Process.Start(new ProcessStartInfo(path,arguments) {
                UseShellExecute=true,Verb="runas"})) {
                process.WaitForExit();return process.ExitCode;
            }
        }
    }
}
