using System.IO;
using System.Text.Json;
namespace Pulse;
internal static class NetworkTest
{
 public static async Task<int> Run(string output)
 {
  var rows=new List<object>();bool pass=true;
  try{using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(60));var result=await SpeedTest.Run(10,3,1,new Progress<SpeedProgress>(),limit.Token);bool ok=result.Download>0&&result.Upload>0&&result.Ping>0&&result.Bytes<=10000000;rows.Add(new{name="Real Cloudflare ping/download/upload, 10 MB cap",ok,result});pass&=ok;
   using var cancel=new CancellationTokenSource();cancel.Cancel();try{await SpeedTest.Run(10,3,1,new Progress<SpeedProgress>(),cancel.Token);rows.Add(new{name="Cancellation",ok=false});pass=false;}catch(OperationCanceledException){rows.Add(new{name="Cancellation",ok=true});}
  }catch(Exception ex){rows.Add(new{name="Network test",ok=false,error=ex.Message});pass=false;}
  await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{pass,rows},new JsonSerializerOptions{WriteIndented=true}));return pass?0:1;
 }
}
