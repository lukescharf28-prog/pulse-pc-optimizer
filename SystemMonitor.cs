using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Net.NetworkInformation;

namespace Pulse;
public sealed record ProcessRow(int Id,long StartTicks,string Name,string Path,long Ram,long? Gpu,bool Allowed,string Status)
{
 public string RamText=>Format.Bytes(Ram);
 public string GpuText=>Gpu.HasValue?Format.Bytes(Gpu.Value):"—";
 public double? CpuPercent{get;init;}
 public double? GpuPercent{get;init;}
 public double? IoBytes{get;init;}
 public string CpuText=>CpuPercent.HasValue?$"{CpuPercent:0.0} %":"—";
 public string LoadText=>GpuPercent.HasValue?$"{GpuPercent:0.0} %":"—";
 public string IoText=>IoBytes.HasValue?Format.Bytes((long)IoBytes.Value)+"/s":"—";
}
public static class Format { public static string Bytes(long bytes)=>bytes>=1073741824?$"{bytes/1073741824d:0.00} GB":$"{bytes/1048576d:0.0} MB"; }
public sealed record Snapshot(ulong Total,ulong Available,double? Cpu,long? Gpu,List<ProcessRow> Processes,bool GpuProcessesAvailable)
{
 public double RamPercent=>Total==0?0:100d*(Total-Available)/Total;
 public double? GpuLoad{get;init;}
 public double? DiskRead{get;init;}
 public double? DiskWrite{get;init;}
 public double? DiskActive{get;init;}
 public ulong Committed{get;init;}
 public ulong CommitLimit{get;init;}
 public SensorSnapshot? Sensors{get;init;}
 public List<DriveRow> Drives{get;init;}=new();
 public List<NetworkRow> Networks{get;init;}=new();
}
public sealed record DriveRow(string Name,long Total,long Free){public string Capacity=>$"{Format.Bytes(Total-Free)} belegt / {Format.Bytes(Total)} · {Format.Bytes(Free)} frei";public double Used=>Total==0?0:100d*(Total-Free)/Total;}
public sealed record NetworkRow(string Name,string Kind,long Link,double? Receive,double? Send){public string Text=>$"{Name} · {Kind} · Link {Link/1000000d:0} Mbit/s\n↓ {(Receive.HasValue?Format.Bytes((long)Receive.Value)+"/s":"—")}   ↑ {(Send.HasValue?Format.Bytes((long)Send.Value)+"/s":"—")}";}
internal sealed class SystemMonitor : IDisposable
{
 private readonly GpuCounters gpu=new();
 private ulong lastIdle,lastTotal;
 private readonly Sensors sensors=new();
 private Dictionary<(int,long),(double Cpu,ulong Io,long Tick)> previous=new();
 private Dictionary<string,(long Rx,long Tx,long Tick)> lastNetwork=new();
 public bool ReadSensors{get;set;}=true;
 public Snapshot Sample()
 {
  var memory=new Native.MemoryStatus{Length=(uint)Marshal.SizeOf<Native.MemoryStatus>()};
  if(!Native.GlobalMemoryStatusEx(ref memory))throw new Win32Exception(Marshal.GetLastWin32Error());
  double? cpu=null;
  if(Native.GetSystemTimes(out var idle,out var kernel,out var user)) {
   var total=kernel.Value+user.Value;
   if(lastTotal>0 && total>lastTotal)cpu=Math.Clamp(100d*(1d-(idle.Value-lastIdle)/(double)(total-lastTotal)),0,100);
   lastIdle=idle.Value;lastTotal=total;
  }
  var graphics=gpu.Sample(); var rows=new List<ProcessRow>();var next=new Dictionary<(int,long),(double,ulong,long)>();long now=Stopwatch.GetTimestamp();
  Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out var foreground);
  foreach(var p in Process.GetProcesses())using(p) {
   try {
    var ram=p.WorkingSet64; if(ram==0)continue;
    var handle=Native.OpenProcess(0x1000,false,p.Id);
    string path="";long start=0; bool allowed=false;double? cpuTime=null,ioRate=null,processCpu=null;ulong? io=null;
    if(handle!=IntPtr.Zero)try {
     path=Native.ImagePath(handle)??"";
     if(Native.GetProcessTimes(handle,out var created,out _,out _,out _))start=unchecked((long)created.Value);
     allowed=ProcessActions.IsAllowed(handle,p.Id,path);
     if(Native.GetProcessTimes(handle,out _,out _,out var kt,out var ut))cpuTime=(kt.Value+ut.Value)/10000000d;
     if(Native.GetProcessIoCounters(handle,out var counters))io=counters.ReadBytes+counters.WriteBytes;
    }finally{Native.CloseHandle(handle);}
    long? vram=graphics.Processes?.GetValueOrDefault(p.Id);
    string status=!allowed?"Geschützt":foreground==p.Id?"Im Vordergrund":p.MainWindowHandle!=IntPtr.Zero?"Fenster geöffnet":"Hintergrund";
    if(cpuTime.HasValue&&io.HasValue){if(previous.TryGetValue((p.Id,start),out var old)){double dt=(now-old.Tick)/(double)Stopwatch.Frequency;if(dt>0){processCpu=Math.Clamp(100*(cpuTime.Value-old.Cpu)/dt/Environment.ProcessorCount,0,100);if(io>=old.Io)ioRate=(io.Value-old.Io)/dt;}}next[(p.Id,start)]=(cpuTime.Value,io.Value,now);}
    rows.Add(new(p.Id,start,p.ProcessName,path,ram,vram,allowed,status){CpuPercent=processCpu,GpuPercent=graphics.GpuLoads?.GetValueOrDefault(p.Id),IoBytes=ioRate});
   }catch(Exception ex) when(ex is InvalidOperationException or Win32Exception or NotSupportedException){ }
  }
  previous=next;var drives=new List<DriveRow>();foreach(var d in DriveInfo.GetDrives())try{if(d.IsReady&&d.DriveType==DriveType.Fixed)drives.Add(new(d.Name,d.TotalSize,d.AvailableFreeSpace));}catch(IOException){}catch(UnauthorizedAccessException){}
  var network=new List<NetworkRow>();var fresh=new Dictionary<string,(long,long,long)>();foreach(var n in NetworkInterface.GetAllNetworkInterfaces())try{if(n.OperationalStatus!=OperationalStatus.Up||n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)continue;var s=n.GetIPStatistics();double? rx=null,tx=null;if(lastNetwork.TryGetValue(n.Id,out var old)){double dt=(now-old.Tick)/(double)Stopwatch.Frequency;if(dt>0&&s.BytesReceived>=old.Rx&&s.BytesSent>=old.Tx){rx=(s.BytesReceived-old.Rx)/dt;tx=(s.BytesSent-old.Tx)/dt;}}fresh[n.Id]=(s.BytesReceived,s.BytesSent,now);network.Add(new(n.Name,n.NetworkInterfaceType==NetworkInterfaceType.Wireless80211?"WLAN":"Netzwerk",n.Speed,rx,tx));}catch(NetworkInformationException){}
  lastNetwork=fresh;
  return new(memory.TotalPhysical,memory.AvailablePhysical,cpu,graphics.Total,rows.OrderByDescending(x=>x.Ram).ToList(),graphics.Processes!=null){GpuLoad=graphics.GpuLoad,DiskRead=graphics.DiskRead,DiskWrite=graphics.DiskWrite,DiskActive=graphics.DiskActive,Committed=memory.TotalPageFile-memory.AvailablePageFile,CommitLimit=memory.TotalPageFile,Sensors=ReadSensors?sensors.Sample():null,Drives=drives,Networks=network};
 }
 public void Dispose()=>gpu.Dispose();
}
internal static class ProcessActions
{
 private static readonly string UserSid=WindowsIdentity.GetCurrent().User?.Value??"";
 private static readonly string WindowsRoot=Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\')+"\\";
 private static readonly uint Session=(uint)Process.GetCurrentProcess().SessionId;
 private static readonly HashSet<string> DeniedNames=new(StringComparer.OrdinalIgnoreCase){"System","Registry","Idle","Memory Compression","explorer","dwm","csrss","wininit","winlogon","lsass","services","svchost","smss","sihost","fontdrvhost","spoolsv","audiodg","MsMpEng","NisSrv","SecurityHealthService","SecurityHealthSystray","SearchHost","StartMenuExperienceHost","ShellExperienceHost","ShellHost","RuntimeBroker"};
 internal static bool IsAllowed(IntPtr handle,int pid,string path)
 {
  if(pid<=4 || pid==Environment.ProcessId || string.IsNullOrWhiteSpace(path) || UserSid.Length==0)return false;
  if(path.StartsWith(WindowsRoot,StringComparison.OrdinalIgnoreCase) || DeniedNames.Contains(Path.GetFileNameWithoutExtension(path)))return false;
  if(Protection.Blocked(path))return false;
  if(!Native.ProcessIdToSessionId(pid,out var session)||session!=Session)return false;
  if(!Native.IsProcessCritical(handle,out var critical)||critical)return false;
  return Native.Owner(handle)==UserSid;
 }
 private static IntPtr Validate(ProcessRow row,uint rights)
 {
  if(!row.Allowed)throw new InvalidOperationException("Dieser Prozess ist geschützt.");
  var handle=Native.OpenProcess(rights|0x1000,false,row.Id);
  if(handle==IntPtr.Zero)throw new InvalidOperationException("Der Prozess wurde beendet oder Windows verweigert den Zugriff.");
  bool valid=false;
  try {
   var path=Native.ImagePath(handle)??"";
   if(!Native.GetProcessTimes(handle,out var created,out _,out _,out _) || unchecked((long)created.Value)!=row.StartTicks || !string.Equals(path,row.Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Der Prozess hat sich geändert. Bitte die Liste aktualisieren.");
   if(!IsAllowed(handle,row.Id,path))throw new InvalidOperationException("Dieser Prozess ist geschützt oder nicht mehr zugänglich.");
   valid=true;return handle;
  }finally{if(!valid)Native.CloseHandle(handle);}
 }
 public static long Trim(ProcessRow row)
 {
  var handle=Validate(row,0x100);
  try {
   long before=0,after=0;using var p=Process.GetProcessById(row.Id);before=p.WorkingSet64;
   if(!Native.EmptyWorkingSet(handle))throw new Win32Exception(Marshal.GetLastWin32Error());
   p.Refresh();after=p.WorkingSet64;return Math.Max(0,before-after);
  }finally{Native.CloseHandle(handle);}
 }
 public static bool Close(ProcessRow row)
 {
  var handle=Validate(row,0);
  try {using var p=Process.GetProcessById(row.Id);return p.CloseMainWindow();}finally{Native.CloseHandle(handle);}
 }
 public static void Kill(ProcessRow row)
 {
  var handle=Validate(row,1);
  try{if(!Native.TerminateProcess(handle,1))throw new Win32Exception(Marshal.GetLastWin32Error());}finally{Native.CloseHandle(handle);}
 }
}
