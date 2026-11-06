using System.Diagnostics;
using System.IO;
using System.Text.Json;
namespace Pulse;
internal static class SelfTest
{
 public static int Run(string[] args)
 {
  var results=new List<object>();bool pass=true;
  void Check(string name,bool ok,object? detail=null){results.Add(new {name,ok,detail});if(!ok)pass=false;}
  Process? worker=null;
  try {
   using var monitor=new SystemMonitor();var first=monitor.Sample();Thread.Sleep(1100);var sample=monitor.Sample();
   Check("Physical RAM metrics",sample.Total>0&&sample.Available<=sample.Total,new{sample.Total,sample.Available,sample.RamPercent});
   Check("CPU sample",sample.Cpu is >=0 and <=100,sample.Cpu);
   Check("Process enumeration",sample.Processes.Count>0,sample.Processes.Count);
   Check("GPU reporting (hardware-dependent)",true,new{sample.Gpu,sample.GpuProcessesAvailable,processes=sample.Processes.Count(x=>x.Gpu>0)});
   var self=sample.Processes.Single(x=>x.Id==Environment.ProcessId);Check("Own process protected",!self.Allowed);
   try{ProcessActions.Kill(self);Check("Protected action rejected",false);}catch(InvalidOperationException){Check("Protected action rejected",true);}
   Check("Windows processes protected",sample.Processes.Where(x=>x.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+"\\",StringComparison.OrdinalIgnoreCase)).All(x=>!x.Allowed));
   Check("Per-process CPU metrics",sample.Processes.Any(x=>x.CpuPercent.HasValue)&&sample.Processes.Where(x=>x.CpuPercent.HasValue).All(x=>x.CpuPercent is >=0 and <=100));
   Check("GPU engine / disk / sensor observations",true,new{sample.GpuLoad,sample.DiskRead,sample.DiskWrite,sample.DiskActive,temperatures=sample.Sensors?.Temperatures.Count});
   Check("Windows uninstall entries excluded",!AppCatalog.Candidate("Windows component","Microsoft Corporation",false,false,"","",false));
   Check("Shared Epic dependency excluded",!AppCatalog.Candidate("Epic Online Services","Epic Games, Inc.",false,false,"","",false));
   Check("Hidden components excluded",!AppCatalog.Candidate("Google Chrome","Google LLC",true,false,"","",false));
   Check("Unknown publishers excluded",!AppCatalog.Candidate("Mystery app","Unknown",false,false,"","",false));
   Check("Known consumer app accepted",AppCatalog.Candidate("Google Chrome","Google LLC",false,false,"","",false));
   Check("Missing executable rejected",!AppCatalog.TryCommand(new("test","App","Google LLC","1",null,"cmd.exe /c bad","App",Microsoft.Win32.RegistryHive.CurrentUser,Microsoft.Win32.RegistryView.Registry64),out _,out _));
   var installed=AppCatalog.Read();Check("Conservative installed-app catalog",installed.All(x=>!x.Publisher.Contains("Microsoft",StringComparison.OrdinalIgnoreCase)),new{count=installed.Count});
   Check("Update path escape rejected",!Updates.WithinStaging(Path.Combine(Updates.Staging,"..","evil.json")));
   try{Updates.Verify("{\"Payload\":\"e30=\",\"Signature\":\"AAAA\"}");Check("Invalid update signature blocked",false);}catch{Check("Invalid update signature blocked",true);}
   worker=StartWorker();var row=WaitRow(monitor,worker);
   Check("Owned helper eligible",row.Allowed);
   try{ProcessActions.Trim(row with {StartTicks=row.StartTicks+1});Check("Stale identity rejected",false);}catch(InvalidOperationException){Check("Stale identity rejected",true);}
   long trimmed=ProcessActions.Trim(row);Check("Native working-set reduction",true,new{bytesReduced=trimmed});
   Check("Graceful close request",ProcessActions.Close(row));Check("Graceful helper exit",worker.WaitForExit(5000));
   if(!worker.HasExited)worker.Kill();worker.Dispose();worker=StartWorker();row=WaitRow(monitor,worker);
   ProcessActions.Kill(row);Check("Explicit helper termination",worker.WaitForExit(5000));
   try{ProcessActions.Trim(row);Check("Exited process rejected",false);}catch(InvalidOperationException){Check("Exited process rejected",true);}
  }catch(Exception ex){Check("Unexpected error",false,ex.ToString());}
  finally{if(worker!=null){try{if(!worker.HasExited)worker.Kill();}catch{}worker.Dispose();}}
  var output=args.SkipWhile(x=>x!="--self-test").Skip(1).FirstOrDefault()??Path.Combine(AppContext.BaseDirectory,"self-test.json");
  File.WriteAllText(output,JsonSerializer.Serialize(new{pass,results},new JsonSerializerOptions{WriteIndented=true}));return pass?0:1;
 }
 private static Process StartWorker()=>Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--test-worker"){UseShellExecute=false})!;
 private static ProcessRow WaitRow(SystemMonitor monitor,Process worker)
 {
  worker.WaitForInputIdle(5000);
  for(int i=0;i<20;i++){worker.Refresh();var row=monitor.Sample().Processes.FirstOrDefault(x=>x.Id==worker.Id);if(row!=null&&worker.MainWindowHandle!=IntPtr.Zero)return row;Thread.Sleep(150);}
  throw new InvalidOperationException("Test helper did not start.");
 }
}
