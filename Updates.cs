using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
namespace Pulse;
public sealed record UpdatePayload(string Product,string Version,string Url,string Sha256,long Size,string Notes);
public sealed record UpdateEnvelope(string Payload,string Signature);
internal sealed record UpdateJob(string Target,string Package,string Manifest,int ParentId,long ParentStart,string Ack,string Nonce);
internal static class Updates
{
 public const string Feed="https://github.com/lukescharf28-prog/pulse-pc-optimizer/releases/latest/download/pulse-update.json";
 public static readonly string Staging=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pulse","Updates");
 public static Version Current=>Assembly.GetExecutingAssembly().GetName().Version!;
 public static string CurrentText=>$"{Current.Major}.{Current.Minor}.{Current.Build}";
 public static UpdatePayload Verify(string json)
 {
  if(json.Length>65536)throw new InvalidDataException("Update-Metadaten sind zu groß.");
  var envelope=JsonSerializer.Deserialize<UpdateEnvelope>(json)??throw new InvalidDataException("Update-Metadaten fehlen.");
  byte[] payload=Convert.FromBase64String(envelope.Payload);using var rsa=RSA.Create();
  using var key=Assembly.GetExecutingAssembly().GetManifestResourceStream("Pulse.Assets.update-public.pem")!;using var reader=new StreamReader(key);rsa.ImportFromPem(reader.ReadToEnd());
  if(!rsa.VerifyData(payload,Convert.FromBase64String(envelope.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new InvalidDataException("Update-Signatur ungültig. Installation blockiert.");
  var data=JsonSerializer.Deserialize<UpdatePayload>(payload)??throw new InvalidDataException("Ungültiges Update.");
  if(data.Product!="Pulse"||!Version.TryParse(data.Version,out _)||data.Size is <1000000 or >500000000||data.Sha256.Length!=64||!data.Sha256.All(Uri.IsHexDigit))throw new InvalidDataException("Unpassende Update-Metadaten.");
  var uri=new Uri(data.Url);if(uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/lukescharf28-prog/pulse-pc-optimizer/releases/download/",StringComparison.Ordinal)||uri.UserInfo.Length>0)throw new InvalidDataException("Update-Adresse ist nicht zugelassen.");return data;
 }
 private static async Task<HttpResponseMessage> Get(HttpClient http,string url,CancellationToken token)
 {
  var uri=new Uri(url);for(int i=0;i<6;i++){
   if(uri.Scheme!="https"||uri.UserInfo.Length>0||!(uri.Host=="github.com"||uri.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Nicht zugelassene Update-Weiterleitung.");
   var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token);
   if((int)response.StatusCode is >=300 and <400&&response.Headers.Location!=null){uri=new Uri(uri,response.Headers.Location);response.Dispose();continue;}
   response.EnsureSuccessStatusCode();return response;
  }throw new InvalidDataException("Zu viele Update-Weiterleitungen.");
 }
 private static HttpClient Client(){var c=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromMinutes(8)};c.DefaultRequestHeaders.UserAgent.ParseAdd("Pulse/"+CurrentText);return c;}
 public static async Task<(UpdatePayload Data,string Manifest)> Check(CancellationToken token)
 {
  using var http=Client();using var response=await Get(http,Feed,token);await using var stream=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();byte[] buffer=new byte[4096];int count;while((count=await stream.ReadAsync(buffer,token))>0){if(memory.Length+count>65536)throw new InvalidDataException("Update-Metadaten überschreiten das Limit.");memory.Write(buffer,0,count);}string json=System.Text.Encoding.UTF8.GetString(memory.ToArray());return(Verify(json),json);
 }
 public static async Task<string> Download(UpdatePayload payload,string manifest,IProgress<double> progress,CancellationToken token)
 {
  if(Verify(manifest)!=payload||Version.Parse(payload.Version)<=Current)throw new InvalidDataException("Kein neueres, passendes Update.");
  var directory=Path.Combine(Staging,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var package=Path.Combine(directory,"Pulse.package.exe");
  using var http=Client();using var response=await Get(http,payload.Url,token);await using(var input=await response.Content.ReadAsStreamAsync(token))await using(var output=File.Create(package)){byte[] buffer=new byte[131072];long total=0;int count;while((count=await input.ReadAsync(buffer,token))>0){total+=count;if(total>payload.Size)throw new InvalidDataException("Update-Datei überschreitet die signierte Größe.");await output.WriteAsync(buffer.AsMemory(0,count),token);progress.Report(total*100d/payload.Size);}if(total!=payload.Size)throw new InvalidDataException("Update-Datei ist unvollständig.");}
  VerifyFile(package,payload);await File.WriteAllTextAsync(Path.Combine(directory,"manifest.json"),manifest,token);return package;
 }
 internal static void VerifyFile(string package,UpdatePayload data)
 {
  using var file=File.OpenRead(package);if(file.Length!=data.Size||!Convert.ToHexString(SHA256.HashData(file)).Equals(data.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update-Prüfsumme stimmt nicht überein.");
  var version=FileVersionInfo.GetVersionInfo(package);if(version.ProductName!="Pulse"||Version.Parse(version.FileVersion!)!=Version.Parse(data.Version))throw new InvalidDataException("Dateiversion passt nicht zum Update.");
 }
 public static void BeginInstall(string package)
 {
  string folder=Path.GetDirectoryName(package)!;var target=Environment.ProcessPath!;if(!Path.GetFileName(target).Equals("Pulse.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Für Updates muss die App als Pulse.exe gestartet sein.");
  var helper=Path.Combine(folder,"PulseUpdater.exe");File.Copy(target,helper,true);
  using var process=Process.GetCurrentProcess();var job=new UpdateJob(target,package,Path.Combine(folder,"manifest.json"),process.Id,process.StartTime.ToUniversalTime().Ticks,Path.Combine(folder,"healthy.txt"),Guid.NewGuid().ToString("N"));
  string jobFile=Path.Combine(folder,"job.json");File.WriteAllText(jobFile,JsonSerializer.Serialize(job));var start=new ProcessStartInfo(helper){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(jobFile);Process.Start(start);
 }
 internal static int IntegrationTest(string package,string manifest,string result)
 {
  var json=File.ReadAllText(manifest);var data=Verify(json);VerifyFile(package,data);
  if(Version.Parse(data.Version)<=Current)throw new InvalidOperationException("Test benötigt eine neuere Version.");
  var folder=Path.Combine(Staging,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var copied=Path.Combine(folder,"Pulse.package.exe");File.Copy(package,copied);File.WriteAllText(Path.Combine(folder,"manifest.json"),json);File.WriteAllText(result,JsonSerializer.Serialize(new{folder,target=Environment.ProcessPath,version=data.Version}));BeginInstall(copied);return 0;
 }
 internal static bool WithinStaging(string path)=>Path.GetFullPath(path).StartsWith(Path.GetFullPath(Staging).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);
 internal static void NoReparse(string path)
 {
  var item=new FileInfo(path);if(item.Exists&&(item.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Verknüpfte Update-Pfade sind nicht zulässig.");
  for(var dir=item.Directory;dir!=null;dir=dir.Parent)if(dir.Exists&&(dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Verknüpfte Update-Ordner sind nicht zulässig.");
 }
 public static int Apply(string jobFile)
 {
  string? target=null,backup=null;bool replaced=false;Process? launched=null;
  try {
   if(!WithinStaging(jobFile))throw new InvalidDataException("Ungültiger Update-Auftrag.");NoReparse(jobFile);
   var job=JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(jobFile))??throw new InvalidDataException("Update-Auftrag fehlt.");
   foreach(var p in new[]{job.Package,job.Manifest,job.Ack}){if(!WithinStaging(p)||Path.GetDirectoryName(Path.GetFullPath(p))!=Path.GetDirectoryName(Path.GetFullPath(jobFile)))throw new InvalidDataException("Ungültiger Update-Pfad.");NoReparse(p);}
   target=Path.GetFullPath(job.Target);NoReparse(target);if(!Path.GetFileName(target).Equals("Pulse.exe",StringComparison.OrdinalIgnoreCase)||FileVersionInfo.GetVersionInfo(target).ProductName!="Pulse")throw new InvalidDataException("Ziel ist keine Pulse-Installation.");
   var data=Verify(File.ReadAllText(job.Manifest));if(Version.Parse(data.Version)<=Current)throw new InvalidDataException("Update ist nicht neuer.");VerifyFile(job.Package,data);
   try{using var parent=Process.GetProcessById(job.ParentId);if(parent.StartTime.ToUniversalTime().Ticks!=job.ParentStart||!string.Equals(parent.MainModule?.FileName,target,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update-Prozess stimmt nicht überein.");if(!parent.WaitForExit(30000))throw new IOException("Pulse wurde nicht geschlossen.");}catch(ArgumentException){}
   backup=target+".previous";NoReparse(backup);var replacement=target+"."+Guid.NewGuid().ToString("N")+".new";File.Copy(job.Package,replacement);VerifyFile(replacement,data);File.Replace(replacement,target,backup);replaced=true;
   var start=new ProcessStartInfo(target){UseShellExecute=false};start.ArgumentList.Add("--update-health");start.ArgumentList.Add(job.Ack);start.ArgumentList.Add(job.Nonce);launched=Process.Start(start);
   var watch=Stopwatch.StartNew();while(watch.Elapsed<TimeSpan.FromSeconds(30)){if(File.Exists(job.Ack)&&File.ReadAllText(job.Ack)==job.Nonce){File.WriteAllText(Path.Combine(Staging,"last-result.txt"),"Update auf "+data.Version+" erfolgreich. Vorversion bleibt als .previous erhalten.");return 0;}if(launched==null||launched.HasExited)break;Thread.Sleep(250);}throw new IOException("Neue Version hat den Start nicht bestätigt.");
  }catch(Exception ex){if(replaced&&target!=null&&backup!=null){try{if(launched!=null&&!launched.HasExited){launched.Kill();launched.WaitForExit(5000);}File.Copy(backup,target,true);Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch{}}
   Directory.CreateDirectory(Staging);File.WriteAllText(Path.Combine(Staging,"last-result.txt"),"Update fehlgeschlagen: "+ex.Message+(replaced?" Wiederherstellung der Vorversion wurde versucht.":" Die installierte Version wurde nicht ersetzt."));return 1;
  }finally{launched?.Dispose();}
 }
}
