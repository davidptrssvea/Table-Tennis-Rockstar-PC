// Restricted UPnP IGD client: selected interface's gateway only, finite leases only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml;

namespace TableTennisLauncher {
    public sealed class GatewayLeaseRouter : IHostRouter {
        public const int LeaseSeconds=300;
        const int MaxXml=262144;
        readonly IPAddress gateway;
        Uri control;
        string service;
        string external;
        public static bool ServiceAllowed(string value) {
            return value=="urn:schemas-upnp-org:service:WANIPConnection:1" || value=="urn:schemas-upnp-org:service:WANIPConnection:2" || value=="urn:schemas-upnp-org:service:WANPPPConnection:1";
        }
        public static bool Pinned(Uri uri,IPAddress gateway) {
            IPAddress ip;
            return uri!=null && uri.IsAbsoluteUri && uri.Scheme=="http" && uri.UserInfo.Length==0 && uri.Fragment.Length==0 &&
                uri.AbsoluteUri.Length<=2048 && IPAddress.TryParse(uri.Host,out ip) && ip.Equals(gateway) && uri.Port>=1 && uri.Port<=65535;
        }
        static IPAddress Gateway(string client) {
            IPAddress selected=IPAddress.Parse(client);
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
                var properties=adapter.GetIPProperties();
                foreach(var entry in properties.UnicastAddresses) if(entry.Address.Equals(selected))
                    foreach(var route in properties.GatewayAddresses) {
                        var candidate=route.Address;
                        if(candidate.AddressFamily!=AddressFamily.InterNetwork || IPAddress.IsLoopback(candidate)) continue;
                        var a=candidate.GetAddressBytes();
                        if(a[0]==10 || a[0]==192 && a[1]==168 || a[0]==172 && a[1]>=16 && a[1]<=31 || a[0]==100 && a[1]>=64 && a[1]<=127)
                            return candidate;
                    }
            }
            throw new IOException("No private IPv4 gateway for this connection. Use LAN/VPN or manual hosting.");
        }
        public GatewayLeaseRouter(string client) {
            gateway=Gateway(client);
            using(var udp=new UdpClient(AddressFamily.InterNetwork)) {
                var local=IPAddress.Parse(client);
                udp.Client.Bind(new IPEndPoint(local,0));
                udp.Client.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastInterface,local.GetAddressBytes());
                udp.Client.ReceiveTimeout=300;udp.Ttl=2;
                byte[] request=Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: upnp:rootdevice\r\n\r\n");
                udp.Send(request,request.Length,new IPEndPoint(IPAddress.Parse("239.255.255.250"),1900));
                var search=Stopwatch.StartNew();
                while(search.Elapsed<TimeSpan.FromSeconds(3)) {
                    try {
                        IPEndPoint peer=null;byte[] reply=udp.Receive(ref peer);
                        if(!peer.Address.Equals(gateway) || reply.Length>8192) continue;
                        Uri location=null;
                        foreach(string line in Encoding.ASCII.GetString(reply).Split('\n'))
                            if(line.StartsWith("LOCATION:",StringComparison.OrdinalIgnoreCase)) Uri.TryCreate(line.Substring(9).Trim(),UriKind.Absolute,out location);
                        if(!Pinned(location,gateway)) continue;
                        ResolveDescription(Request(location,null,null),location);
                        if(control!=null) break;
                    } catch(SocketException) { }
                }
            }
            if(control==null) throw new IOException("No supported router was found on the selected connection. Use LAN/VPN or manual hosting.");
            external=Value(Action("GetExternalIPAddress"),"NewExternalIPAddress");
            if(!HostSession.PublicIPv4(external)) throw new IOException("The router has no public IPv4 (possibly CGNAT/double NAT). No port was opened.");
        }
        // Used by in-assembly loopback tests; never exposed as launcher options.
        internal GatewayLeaseRouter(IPAddress gateway,Uri control,string service) {
            if(!Pinned(control,gateway) || !ServiceAllowed(service)) throw new IOException("Invalid gateway endpoint");
            this.gateway=gateway;this.control=control;this.service=service;
            external=Value(Action("GetExternalIPAddress"),"NewExternalIPAddress");
            if(!HostSession.PublicIPv4(external)) throw new IOException("The router has no public IPv4. No port was opened.");
        }
        public static XmlDocument ParseXml(string text) {
            if(text.Length>MaxXml) throw new IOException("Router XML is too large");
            var settings=new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxXml};
            using(var check=XmlReader.Create(new StringReader(text),settings)) while(check.Read()) if(check.Depth>32) throw new IOException("Router XML is too deeply nested");
            var document=new XmlDocument {XmlResolver=null};
            using(var reader=XmlReader.Create(new StringReader(text),settings)) document.Load(reader);
            return document;
        }
        void ResolveDescription(string text,Uri location) {
            var document=ParseXml(text);
            Uri basis=location;
            var declared=document.SelectSingleNode("/*[local-name()='root']/*[local-name()='URLBase']");
            Uri declaredUri;
            if(declared!=null && Uri.TryCreate(declared.InnerText,UriKind.Absolute,out declaredUri) && Pinned(declaredUri,gateway)) basis=declaredUri;
            foreach(XmlNode item in document.SelectNodes("//*[local-name()='service']")) {
                string type=null,path=null;
                foreach(XmlNode child in item.ChildNodes) {if(child.LocalName=="serviceType") type=child.InnerText;if(child.LocalName=="controlURL") path=child.InnerText;}
                if(!ServiceAllowed(type) || string.IsNullOrEmpty(path)) continue;
                // Every absolute/relative URL stays on this gateway, including URLBase.
                Uri uri;
                if(Uri.TryCreate(basis,path,out uri) && Pinned(uri,gateway)) {control=uri;service=type;return;}
            }
        }
        string Request(Uri uri,string soapAction,byte[] body) {
            if(!Pinned(uri,gateway)) throw new IOException("Router URL left the selected gateway");
            var request=(HttpWebRequest)WebRequest.Create(uri);
            request.UserAgent="TableTennis-Experimental-UPnP/1";
            request.Proxy=null;request.AllowAutoRedirect=false;request.Timeout=2500;request.ReadWriteTimeout=2500;
            request.Method=body==null?"GET":"POST";
            if(body!=null) {
                request.ContentType="text/xml; charset=utf-8";request.Headers["SOAPAction"]="\""+soapAction+"\"";
                request.ContentLength=body.Length;
                using(var stream=request.GetRequestStream()) stream.Write(body,0,body.Length);
            }
            HttpWebResponse response;
            try {response=(HttpWebResponse)request.GetResponse();}
            catch(WebException error) {response=error.Response as HttpWebResponse;if(response==null) throw;}
            using(response) {
                if(response.StatusCode!=HttpStatusCode.OK && response.StatusCode!=HttpStatusCode.InternalServerError) throw new IOException("Router request failed without following redirects");
                if(response.ContentLength>MaxXml) throw new IOException("Router response is too large");
                using(var input=response.GetResponseStream()) using(var output=new MemoryStream()) {
                    var elapsed=Stopwatch.StartNew();
                    byte[] buffer=new byte[4096];int count;
                    while((count=input.Read(buffer,0,buffer.Length))>0) {if(elapsed.Elapsed>TimeSpan.FromSeconds(5) || output.Length+count>MaxXml) throw new IOException("Router response exceeded its time/size limit");output.Write(buffer,0,count);}
                    return new UTF8Encoding(false,true).GetString(output.ToArray());
                }
            }
        }
        Dictionary<string,string> Action(string name,params string[] pairs) {
            var body=new StringBuilder("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:"+name+" xmlns:u=\""+service+"\">");
            for(int i=0;i<pairs.Length;i+=2) body.Append("<"+pairs[i]+">"+SecurityElement.Escape(pairs[i+1])+"</"+pairs[i]+">");
            body.Append("</u:"+name+"></s:Body></s:Envelope>");
            var document=ParseXml(Request(control,service+"#"+name,Encoding.UTF8.GetBytes(body.ToString())));
            var result=new Dictionary<string,string>();
            XmlNode soapBody=document.SelectSingleNode("/*[local-name()='Envelope' and namespace-uri()='http://schemas.xmlsoap.org/soap/envelope/']/*[local-name()='Body']");
            if(soapBody==null || soapBody.ChildNodes.Count!=1) throw new IOException("Malformed router response");
            XmlNode response=soapBody.FirstChild;
            if(response.LocalName=="Fault") {
                var code=response.SelectSingleNode(".//*[local-name()='errorCode']");
                throw new RouterFault(code==null?0:int.Parse(code.InnerText,CultureInfo.InvariantCulture));
            }
            if(response.LocalName!=name+"Response" || response.NamespaceURI!=service) throw new IOException("Unexpected router action response");
            foreach(XmlNode item in response.ChildNodes) {if(result.ContainsKey(item.LocalName)) throw new IOException("Duplicate router field");result.Add(item.LocalName,item.InnerText);}
            return result;
        }
        static string Value(Dictionary<string,string> values,string name) {string value;if(!values.TryGetValue(name,out value)) throw new IOException("Missing router field: "+name);return value;}
        public RouterMapping Find(int port) {
            try {
                var fields=Action("GetSpecificPortMappingEntry","NewRemoteHost","","NewExternalPort",port.ToString(),"NewProtocol","UDP");
                string enabled=Value(fields,"NewEnabled");
                return new RouterMapping {Port=port,InternalPort=int.Parse(Value(fields,"NewInternalPort")),Client=Value(fields,"NewInternalClient"),Description=Value(fields,"NewPortMappingDescription"),External=external,LeaseSeconds=int.Parse(Value(fields,"NewLeaseDuration")),Enabled=enabled=="1" || enabled.Equals("true",StringComparison.OrdinalIgnoreCase)};
            } catch(RouterFault error) {if(error.Code==714) return null;throw;}
        }
        public RouterMapping Add(int port,string client,string description) {
            external=Value(Action("GetExternalIPAddress"),"NewExternalIPAddress");
            if(!HostSession.PublicIPv4(external)) throw new IOException("The router lost its public IPv4. No lease was renewed.");
            Action("AddPortMapping","NewRemoteHost","","NewExternalPort",port.ToString(),"NewProtocol","UDP","NewInternalPort",port.ToString(),"NewInternalClient",client,"NewEnabled","1","NewPortMappingDescription",description,"NewLeaseDuration",LeaseSeconds.ToString());
            return Find(port);
        }
        public void Remove(int port) {try {Action("DeletePortMapping","NewRemoteHost","","NewExternalPort",port.ToString(),"NewProtocol","UDP");} catch(RouterFault error) {if(error.Code!=714) throw;}}
        public void Dispose() { }
        sealed class RouterFault : IOException {
            public readonly int Code;
            public RouterFault(int code):base("Router rejected the request ("+code+"). Finite port leases are required; use VPN/manual hosting if unsupported.") {Code=code;}
        }
    }
}
