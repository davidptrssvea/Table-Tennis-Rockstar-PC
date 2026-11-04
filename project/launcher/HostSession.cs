// Player-hosted relay supervisor. No shared game service or third-party IP lookup.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TableTennisLauncher {
    public sealed class RouterMapping {
        public int Port, InternalPort, LeaseSeconds;
        public bool Enabled=true;
        public string Client, Description, External;
    }
    public interface IHostRouter : IDisposable {
        RouterMapping Find(int port);
        RouterMapping Add(int port,string client,string description);
        void Remove(int port);
    }
    public sealed class WindowsHostRouter : IHostRouter {
        object nat, mappings;
        static object Get(object target,string name) {return target.GetType().InvokeMember(name,BindingFlags.GetProperty,null,target,null);}
        public WindowsHostRouter() {
            try {
                nat=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.NATUPnP",true));
                mappings=Get(nat,"StaticPortMappingCollection");
                if(mappings==null) throw new InvalidOperationException("The router did not offer UPnP port mapping.");
            } catch {Dispose();throw;}
        }
        static RouterMapping Read(object item) {
            return new RouterMapping {Port=Convert.ToInt32(Get(item,"ExternalPort")),InternalPort=Convert.ToInt32(Get(item,"InternalPort")),
                Client=(string)Get(item,"InternalClient"),Description=(string)Get(item,"Description"),External=(string)Get(item,"ExternalIPAddress")};
        }
        public RouterMapping Find(int port) {
            foreach(object item in (IEnumerable)mappings) try {
                if(Convert.ToInt32(Get(item,"ExternalPort"))==port && string.Equals((string)Get(item,"Protocol"),"UDP",StringComparison.OrdinalIgnoreCase)) return Read(item);
            } finally {if(Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);}
            return null;
        }
        public RouterMapping Add(int port,string client,string description) {
            throw new NotSupportedException("Permanent UPnP rules are disabled. This adapter only cleans up legacy rules.");
        }
        public void Remove(int port) {mappings.GetType().InvokeMember("Remove",BindingFlags.InvokeMethod,null,mappings,new object[]{port,"UDP"});}
        public void Dispose() {
            if(mappings!=null && Marshal.IsComObject(mappings)) Marshal.ReleaseComObject(mappings);
            if(nat!=null && Marshal.IsComObject(nat)) Marshal.ReleaseComObject(nat);
            mappings=null;nat=null;
        }
    }
    public static class HostSession {
        public const int Port=27360;
        public static string Quote(string text) {
            if(text.IndexOfAny(new[]{'"','\r','\n','\0'})>=0) throw new ArgumentException("Invalid path");
            return "\""+text.TrimEnd('\\')+"\"";
        }
        public static string Folder(string root) {
            root=Path.GetFullPath(root).TrimEnd('\\');
            if(!File.Exists(Path.Combine(root,"dist","settings.ini"))) throw new IOException("Choose an installed game folder.");
            string folder=Path.Combine(root,".updates","hosting");
            for(string p=folder;p!=null;p=Path.GetDirectoryName(p))
                if((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked hosting folders are not supported.");
            Directory.CreateDirectory(folder);return folder;
        }
        static string Identity(string root) {
            using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName+"|"+Path.GetFullPath(root).ToUpperInvariant()))).Replace("-","").Substring(0,20);
        }
        public static bool PublicIPv4(string text) {
            IPAddress address;
            if(!IPAddress.TryParse(text,out address) || address.AddressFamily!=AddressFamily.InterNetwork) return false;
            byte[] b=address.GetAddressBytes();
            return b[0]!=0 && b[0]!=10 && b[0]!=127 && b[0]<224 &&
                !(b[0]==100 && b[1]>=64 && b[1]<=127) && !(b[0]==169 && b[1]==254) &&
                !(b[0]==172 && b[1]>=16 && b[1]<=31) && !(b[0]==192 && b[1]==168) &&
                !(b[0]==192 && b[1]==0) && !(b[0]==192 && b[1]==2) &&
                !(b[0]==198 && (b[1]==18 || b[1]==19 || b[1]==51 && b[2]==100)) && !(b[0]==203 && b[1]==0 && b[2]==113);
        }
        public static bool OwnMapping(RouterMapping map,int port,string client,string description) {
            return map!=null && map.Port==port && map.InternalPort==port && map.Client==client && map.Description==description;
        }
        public static string OpenRouter(IHostRouter router,int port,string client,string description) {
            var existing=router.Find(port);
            if(existing!=null && !OwnMapping(existing,port,client,description)) throw new IOException("The router port belongs to another application. Use VPN/LAN or choose manual hosting.");
            var mapping=existing??router.Add(port,client,description);
            if(!OwnMapping(mapping,port,client,description) || !PublicIPv4(mapping.External) || !mapping.Enabled || mapping.LeaseSeconds<1 || mapping.LeaseSeconds>GatewayLeaseRouter.LeaseSeconds) throw new IOException("The router did not confirm a public IPv4 and finite five-minute lease. Use VPN/manual hosting; permanent rules are refused.");
            return mapping.External+":"+port;
        }
        public static void RemoveRouter(IHostRouter router,int port,string client,string description) {
            if(OwnMapping(router.Find(port),port,client,description)) {
                router.Remove(port);
                if(OwnMapping(router.Find(port),port,client,description)) throw new IOException("The router did not remove the rule; cleanup must be retried.");
            }
        }
        public static string RenewRouter(IHostRouter router,int port,string client,string description) {
            if(!OwnMapping(router.Find(port),port,client,description)) throw new IOException("The router rule changed. Hosting stopped without overwriting another rule.");
            var mapping=router.Add(port,client,description);
            if(!OwnMapping(mapping,port,client,description) || !mapping.Enabled || mapping.LeaseSeconds<1 || mapping.LeaseSeconds>GatewayLeaseRouter.LeaseSeconds)
                throw new IOException("The router no longer confirms a finite port lease. Hosting stopped.");
            return mapping.External+":"+port;
        }
        public static Dictionary<string,string> Fields(string file) {
            var result=new Dictionary<string,string>();
            if(File.Exists(file)) foreach(string line in ReadShared(file).Split('\n')) {int p=line.IndexOf('=');if(p>0) {
                string key=line.Substring(0,p);if(result.ContainsKey(key)) throw new InvalidDataException("Duplicate hosting state field");
                result[key]=line.Substring(p+1).TrimEnd('\r');
            }}
            return result;
        }
        static string ReadShared(string file) {
            for(int i=0;;i++) try {
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                using(var reader=new StreamReader(stream)) {
                    if(stream.Length>16384) throw new InvalidDataException("Hosting state exceeds its size limit");
                    char[] buffer=new char[16385];int count=reader.ReadBlock(buffer,0,buffer.Length);
                    if(count>16384) throw new InvalidDataException("Hosting state exceeds its size limit");
                    return new string(buffer,0,count);
                }
            } catch(IOException) {if(i>=5) throw;Thread.Sleep(20);}
        }
        static void Write(string path,string text) {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                byte[] bytes=new UTF8Encoding(false).GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
            }
            for(int i=0;;i++) try {if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);break;}
            catch(IOException) {if(i>=10) throw;Thread.Sleep(20);}
        }
        public static string Active(string root) {
            string file=Path.Combine(Folder(root),"active.txt");
            string token=File.Exists(file)?ReadShared(file).Trim():"";
            return Regex.IsMatch(token,@"\A[0-9a-f]{32}\z")?token:null;
        }
        public static string SessionFolder(string root,string token) {
            if(!Regex.IsMatch(token??"",@"\A[0-9a-f]{32}\z")) throw new ArgumentException("Invalid host session");
            string folder=Path.Combine(Folder(root),token);
            if(Directory.Exists(folder) && (File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked host sessions are not supported.");
            return folder;
        }
        public static void Stop(string root) {
            string token=Active(root);if(token!=null) Write(Path.Combine(SessionFolder(root,token),"stop"),"stop");
        }
        public static bool Running(string root) {
            using(var mutex=new Mutex(false,"Local\\TTHost-"+Identity(root))) {
                bool acquired=false;
                try {try {acquired=mutex.WaitOne(0);} catch(AbandonedMutexException) {acquired=true;}return !acquired;}
                finally {if(acquired) mutex.ReleaseMutex();}
            }
        }
        public static string InvitationAddress(string root,string selected) {
            string token=Active(root);if(token==null) return selected;
            var fields=Fields(Path.Combine(SessionFolder(root,token),"status.txt"));
            return fields.ContainsKey("state") && fields["state"]=="ready" && fields["local"]==selected?fields["address"]:selected;
        }
        public static string InvitationRoom(string root,string selected,string fallback) {
            string token=Active(root);if(token==null) return fallback;
            var fields=Fields(Path.Combine(SessionFolder(root,token),"status.txt"));
            return fields.ContainsKey("state") && fields["state"]=="ready" && fields["local"]==selected && fields.ContainsKey("room")?fields["room"]:fallback;
        }
        public static void WatchGame(string root,Process game) {
            string token=Active(root);if(token==null) return;
            string folder=SessionFolder(root,token);
            var fields=Fields(Path.Combine(folder,"status.txt"));
            if(fields.ContainsKey("state") && fields["state"]=="ready")
                Write(Path.Combine(folder,"game.txt"),game.Id+"\n"+game.StartTime.ToUniversalTime().Ticks+"\n");
        }
        public static string Start(string root,string client,string mode,string advertised,string room) {
            string folder=Folder(root),token=Active(root);
            using(var mutex=new Mutex(false,"Local\\TTHost-"+Identity(root))) {
                bool available=false;
                try {try {available=mutex.WaitOne(0);} catch(AbandonedMutexException) {available=true;}
                    if(!available && token!=null) {
                        var existing=Fields(Path.Combine(SessionFolder(root,token),"status.txt"));
                        if(!existing.ContainsKey("scope") || existing["scope"]!=mode || !existing.ContainsKey("room") || existing["room"]!=room || existing["local"]!=client+":"+Port)
                            throw new IOException("Stop the current host before changing connection mode, adapter or room.");
                        return token;
                    }
                } finally {if(available) mutex.ReleaseMutex();}
            }
            token=Guid.NewGuid().ToString("N");Directory.CreateDirectory(SessionFolder(root,token));
            string source=Path.Combine(root,"dist","tabletennis_launcher.exe"),helper=Path.Combine(folder,"tabletennis_host.exe");
            for(int i=0;;i++) try {File.Copy(source,helper,true);break;} catch(IOException) {if(i>=10) throw;Thread.Sleep(100);}
            Process.Start(new ProcessStartInfo(helper,"--host-session "+Quote(root)+" "+Quote(client)+" "+mode+" "+Quote(advertised)+" "+token+" "+room) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root}).Dispose();
            return token;
        }
        static bool Ready(string client) {
            try {using(var udp=new UdpClient(AddressFamily.InterNetwork)) {
                udp.Connect(client,Port);udp.Client.ReceiveTimeout=250;
                byte[] request=new byte[17];request[0]=84;request[1]=84;request[2]=86;request[3]=49;request[4]=4;
                udp.Send(request,request.Length);IPEndPoint peer=null;byte[] reply=udp.Receive(ref peer);
                return reply.Length>=7 && reply[0]==84 && reply[1]==84 && reply[2]==86 && reply[3]==49 && reply[4]==12 && reply[5]==5 && reply.Length==7+reply[6];
            }} catch {return false;}
        }
        static void CleanupRouter(string root,string folder,IHostRouter current=null) {
            string file=Path.Combine(folder,"router.txt");if(!File.Exists(file)) return;
            var old=Fields(file);
            string prefix="TableTennis-"+Identity(root);
            if(old["description"]==prefix || Regex.IsMatch(old["description"],"\\A"+prefix+"-[0-9a-f]{32}\\z")) {
                if(!IsLocal(IPAddress.Parse(old["client"]))) throw new IOException("Return to the original host network to retry old router cleanup.");
                if(current!=null) RemoveRouter(current,Port,old["client"],old["description"]);
                else using(var router=old.ContainsKey("provider") && old["provider"]=="lease"?(IHostRouter)new GatewayLeaseRouter(old["client"]):new WindowsHostRouter()) RemoveRouter(router,Port,old["client"],old["description"]);
            }
            File.Delete(file);
        }
        public static int Run(string[] args) {
            if(args.Length!=7 || !Regex.IsMatch(args[6],@"\A[ABCDEFGHJKMNPQRSTUVWXYZ23456789]{6}\z")) return 1;
            string root=Path.GetFullPath(args[1]),client=args[2],mode=args[3],advertised=args[4],token=args[5];
            string room=args[6];
            string folder=Folder(root),session=SessionFolder(root,token);Directory.CreateDirectory(session);
            Action<string,string,string> status=(state,address,message)=>Write(Path.Combine(session,"status.txt"),"state="+state+"\naddress="+address+"\nlocal="+client+":"+Port+"\nscope="+mode+"\nroom="+room+"\nmessage="+message.Replace('\n',' ') +"\n");
            using(var mutex=new Mutex(false,"Local\\TTHost-"+Identity(root))) {
                bool acquired=false,completed=false;Process relay=null,game=null;
                IHostRouter leaseRouter=null;
                try {
                    try {acquired=mutex.WaitOne(0);} catch(AbandonedMutexException) {acquired=true;}
                    if(!acquired) throw new IOException("A host session is already running.");
                    IPAddress ip;
                    if(!IPAddress.TryParse(client,out ip) || ip.AddressFamily!=AddressFamily.InterNetwork ||
                       !(IPAddress.IsLoopback(ip) || IsLocal(ip))) throw new IOException("Choose this PC's LAN or VPN address.");
                    client=ip.ToString();
                    if(mode!="lan" && mode!="upnp" && mode!="manual") throw new IOException("Invalid hosting mode");
                    Write(Path.Combine(folder,"active.txt"),token);
                    status("starting","","Starting your private host...");
                    // Cleanup only our journaled mapping from an interrupted previous session.
                    bool cleanupPending=false;
                    if(File.Exists(Path.Combine(folder,"router.txt"))) try {CleanupRouter(root,folder);}
                    catch {if(mode=="upnp") throw;cleanupPending=true;}
                    if(Ready(client)) throw new IOException("A server already uses this port. Stop the old server first.");
                    string python=Path.Combine(root,"dist","python","python.exe"),script=Path.Combine(root,"dist","server","ttlink_server.py");
                    if(!File.Exists(python)||!File.Exists(script)) throw new IOException("The bundled hosting tools are missing. Restore the complete test installation.");
                    string parentIdentity;
                    using(var self=Process.GetCurrentProcess()) parentIdentity=" --parent-pid "+self.Id+" --parent-ticks "+self.StartTime.ToUniversalTime().Ticks;
                    relay=new Process {StartInfo=new ProcessStartInfo(python,"-I -B "+Quote(script)+" --bind "+client+" --port "+Port+" --quiet --no-status --room "+room+" --max-clients 4"+parentIdentity) {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true}};
                    object logLock=new object();
                    DataReceivedEventHandler log=(sender,e)=>{if(e.Data!=null) try {lock(logLock) {
                        string logFile=Path.Combine(session,"server.log");
                        if(!File.Exists(logFile) || new FileInfo(logFile).Length<1048576) File.AppendAllText(logFile,e.Data.Substring(0,Math.Min(e.Data.Length,2048))+Environment.NewLine);
                    }} catch { }};
                    relay.OutputDataReceived+=log;relay.ErrorDataReceived+=log;
                    relay.Start();relay.BeginOutputReadLine();relay.BeginErrorReadLine();
                    File.WriteAllLines(Path.Combine(root,".updates","server.pid"),new[]{relay.Id.ToString(),relay.StartTime.ToUniversalTime().Ticks.ToString(),Path.GetFullPath(python)});
                    bool ready=false;
                    for(int i=0;i<25 && !relay.HasExited;i++) {if(Ready(client)){ready=true;break;}Thread.Sleep(100);}
                    if(!ready) throw new IOException("Your local server did not start. Check Windows network permission and occupied ports.");
                    string address=client+":"+Port;
                    if(mode=="upnp") {
                        status("starting","","Asking your router for an internet connection...");
                        string description="TableTennis-"+Identity(root)+"-"+token;
                        leaseRouter=new GatewayLeaseRouter(client);
                        Write(Path.Combine(folder,"router.txt"),"provider=lease\nclient="+client+"\ndescription="+description+"\n");
                        address=OpenRouter(leaseRouter,Port,client,description);
                    } else if(mode=="manual") {
                        string[] parts=advertised.Split(':');int port;
                        if(parts.Length!=2 || !PublicIPv4(parts[0]) || !int.TryParse(parts[1],out port) || port<1 || port>65535) throw new IOException("Enter a public IPv4:port for your forwarded game port.");
                        address=IPAddress.Parse(parts[0]).ToString()+":"+port;
                    }
                    status("ready",address,(mode=="lan"?"Hosting on your LAN/VPN. Share the invitation with your friend.":"Hosting prepared. Your friend must test the invitation from their connection; outside reachability is not verified here.")+(cleanupPending?" Previous router cleanup is pending; retry it on the original router.":""));
                    DateTime waitingSince=DateTime.UtcNow;
                    var lifetime=Stopwatch.StartNew();TimeSpan renewed=TimeSpan.Zero;
                    while(!relay.HasExited && !File.Exists(Path.Combine(session,"stop"))) {
                        if(lifetime.Elapsed>TimeSpan.FromHours(4)) break;
                        if(leaseRouter!=null && lifetime.Elapsed-renewed>TimeSpan.FromSeconds(90)) {
                            if(RenewRouter(leaseRouter,Port,client,"TableTennis-"+Identity(root)+"-"+token)!=address) throw new IOException("Your public address changed. Start a new host and send a new invitation.");
                            renewed=lifetime.Elapsed;
                        }
                        string gameFile=Path.Combine(session,"game.txt");
                        if(game==null && File.Exists(gameFile)) {
                            string[] details=File.ReadAllLines(gameFile);int pid;long ticks;
                            if(details.Length==2 && int.TryParse(details[0],out pid) && long.TryParse(details[1],out ticks)) try {
                                var candidate=Process.GetProcessById(pid);
                                if(candidate.StartTime.ToUniversalTime().Ticks==ticks && Path.GetFullPath(candidate.MainModule.FileName).Equals(Path.Combine(root,"dist","tabletennis.exe"),StringComparison.OrdinalIgnoreCase)) game=candidate;
                                else candidate.Dispose();
                            } catch(ArgumentException) {break;}
                        }
                        if(game!=null && game.HasExited || game==null && DateTime.UtcNow-waitingSince>TimeSpan.FromMinutes(30)) break;
                        Thread.Sleep(250);
                    }
                    completed=true;return 0;
                } catch(Exception error) {status("error","",error.GetBaseException().Message);return 1;}
                finally {
                    if(relay!=null) {try {if(!relay.HasExited) {relay.Kill();relay.WaitForExit(5000);}} catch { }relay.Dispose();}
                    if(game!=null) game.Dispose();
                    if(acquired) {
                        try {CleanupRouter(root,folder,leaseRouter);} catch(Exception error) {completed=false;status("error","","Hosting stopped. Router cleanup needs retry on next host start: "+error.GetBaseException().Message);}
                        if(leaseRouter!=null) leaseRouter.Dispose();
                        if(completed) status("stopped","","Hosting stopped; router cleanup completed.");
                        mutex.ReleaseMutex();
                    }
                }
            }
        }
        static bool IsLocal(IPAddress ip) {
            foreach(var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                foreach(var entry in adapter.GetIPProperties().UnicastAddresses) if(entry.Address.Equals(ip)) return true;
            return false;
        }
    }
}
