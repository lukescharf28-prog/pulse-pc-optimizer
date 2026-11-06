using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
namespace Pulse;
public sealed record SpeedProgress(string Phase,double? Ping,double? Download,double? Upload,long Bytes,double Progress);
internal static class SpeedTest
{
 public static async Task<SpeedProgress> Run(int budgetMb,int seconds,int parallel,IProgress<SpeedProgress> progress,CancellationToken token)
 {
  if(budgetMb is <10 or >300||seconds is <3 or >20||parallel is <1 or >4)throw new ArgumentOutOfRangeException(nameof(budgetMb));
  using var http=new HttpClient(new SocketsHttpHandler{AutomaticDecompression=DecompressionMethods.None,MaxConnectionsPerServer=parallel,ConnectTimeout=TimeSpan.FromSeconds(8)}){Timeout=Timeout.InfiniteTimeSpan};
  http.DefaultRequestHeaders.UserAgent.ParseAdd("Pulse/1.1");http.DefaultRequestHeaders.CacheControl=new(){NoCache=true};
  var rtts=new List<double>();
  for(int i=0;i<6;i++){using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));var watch=Stopwatch.StartNew();using var result=await http.GetAsync("https://speed.cloudflare.com/__down?bytes=0&pulse="+Guid.NewGuid().ToString("N"),timeout.Token);result.EnsureSuccessStatusCode();await result.Content.ReadAsByteArrayAsync(timeout.Token);if(i>0)rtts.Add(watch.Elapsed.TotalMilliseconds);progress.Report(new("HTTP-Ping",rtts.Count>0?rtts.Order().ElementAt(rtts.Count/2):null,null,null,0,i/6d*15));}
  double ping=rtts.Order().ElementAt(rtts.Count/2);long used=0;double lastDownload=0;
  async Task<double> Stage(bool upload)
  {
   long completed=0,live=0;long quota=budgetMb*1000000L/2;var watch=Stopwatch.StartNew();
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
   async Task Worker(int index)
   {
    long remaining=quota/parallel;
    while(remaining>0&&!deadline.IsCancellationRequested){int amount=(int)Math.Min(remaining,1000000);remaining-=amount;
     try {
      if(upload){byte[] body=RandomNumberGenerator.GetBytes(amount);using var content=new ByteArrayContent(body);content.Headers.ContentType=new("application/octet-stream");using var response=await http.PostAsync("https://speed.cloudflare.com/__up",content,deadline.Token);response.EnsureSuccessStatusCode();await response.Content.ReadAsByteArrayAsync(deadline.Token);Interlocked.Add(ref completed,amount);Interlocked.Add(ref live,amount);}
      else{using var response=await http.GetAsync("https://speed.cloudflare.com/__down?bytes="+amount+"&pulse="+Guid.NewGuid().ToString("N"),HttpCompletionOption.ResponseHeadersRead,deadline.Token);response.EnsureSuccessStatusCode();await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);byte[] buffer=new byte[65536];int read;long got=0;while((read=await stream.ReadAsync(buffer,deadline.Token))>0){got+=read;if(got>amount)throw new IOException("Speedtest-Antwort Ã¼berschreitet das Datenlimit.");Interlocked.Add(ref live,read);Interlocked.Add(ref completed,read);}}
     }catch(OperationCanceledException) when(!token.IsCancellationRequested){break;}
    }
   }
   var workers=Task.WhenAll(Enumerable.Range(0,parallel).Select(Worker));
   while(!workers.IsCompleted){double rate=Interlocked.Read(ref live)*8/Math.Max(.001,watch.Elapsed.TotalSeconds)/1000000;progress.Report(new(upload?"Upload":"Download",ping,upload?lastDownload:rate,upload?rate:null,used+Interlocked.Read(ref live),(upload?57.5:15)+42.5*Math.Min(1,watch.Elapsed.TotalSeconds/seconds)));await Task.WhenAny(workers,Task.Delay(200,token));token.ThrowIfCancellationRequested();}
   await workers;token.ThrowIfCancellationRequested();used+=completed;
   if(completed==0)throw new IOException("Keine Nutzdaten gemessen. Verbindung prÃ¼fen oder einen lÃ¤ngeren Test wÃ¤hlen.");
   return completed*8/Math.Max(.001,watch.Elapsed.TotalSeconds)/1000000;
  }
  lastDownload=await Stage(false);double upload=await Stage(true);
  var final=new SpeedProgress("Abgeschlossen",ping,lastDownload,upload,used,100);progress.Report(final);return final;
 }
 public static bool HasWifi()=>NetworkInterface.GetAllNetworkInterfaces().Any(x=>x.OperationalStatus==OperationalStatus.Up&&x.NetworkInterfaceType==NetworkInterfaceType.Wireless80211);
}
