using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace Pulse;
internal static class Native
{
 [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
 [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
 [StructLayout(LayoutKind.Sequential)] internal struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
 [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetProcessIoCounters(IntPtr handle,out IoCounters io);
 [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
 [DllImport("kernel32.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemTimes(out FileTime idle,out FileTime kernel,out FileTime user);
 [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
 [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetProcessTimes(IntPtr handle,out FileTime creation,out FileTime exit,out FileTime kernel,out FileTime user);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool QueryFullProcessImageName(IntPtr handle,uint flags,StringBuilder name,ref uint length);
 [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool IsProcessCritical(IntPtr handle, out bool critical);
 [DllImport("kernel32.dll")] internal static extern bool ProcessIdToSessionId(int pid,out uint session);
 [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool TerminateProcess(IntPtr handle,uint exitCode);
 [DllImport("psapi.dll", SetLastError=true)] internal static extern bool EmptyWorkingSet(IntPtr process);
 [DllImport("advapi32.dll", SetLastError=true)] internal static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll", SetLastError=true)] internal static extern bool GetTokenInformation(IntPtr token,int type,IntPtr buffer,uint length,out uint needed);
 [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);

 internal static string? ImagePath(IntPtr handle) { var sb=new StringBuilder(32768);uint size=32768; return QueryFullProcessImageName(handle,0,sb,ref size)?sb.ToString():null; }
 internal static string? Owner(IntPtr handle)
 {
  if(!OpenProcessToken(handle,8,out var token))return null;
  try { GetTokenInformation(token,1,IntPtr.Zero,0,out var size); if(size==0)return null; var buffer=Marshal.AllocHGlobal((int)size); try { return GetTokenInformation(token,1,buffer,size,out _)?new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value:null; }finally{Marshal.FreeHGlobal(buffer);} }finally{CloseHandle(token);}
 }
}

internal sealed class GpuCounters : IDisposable
{
 [StructLayout(LayoutKind.Explicit, Size=16)] private struct Value { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
 [StructLayout(LayoutKind.Sequential)] private struct Item { public IntPtr Name; public Value Value; }
 [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source,IntPtr data,out IntPtr query);
 [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr data,out IntPtr counter);
 [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
 [DllImport("pdh.dll",CharSet=CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format,ref uint bytes,out uint count,IntPtr buffer);
 [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
 private IntPtr query, processCounter, adapterCounter, engineCounter, readCounter, writeCounter, idleCounter;
 public GpuCounters()
 {
  if(PdhOpenQuery(null,IntPtr.Zero,out query)!=0)return;
  PdhAddEnglishCounter(query,@"\GPU Process Memory(*)\Dedicated Usage",IntPtr.Zero,out processCounter);
  PdhAddEnglishCounter(query,@"\GPU Adapter Memory(*)\Dedicated Usage",IntPtr.Zero,out adapterCounter);
  PdhAddEnglishCounter(query,@"\GPU Engine(*)\Utilization Percentage",IntPtr.Zero,out engineCounter);
  PdhAddEnglishCounter(query,@"\PhysicalDisk(*)\Disk Read Bytes/sec",IntPtr.Zero,out readCounter);
  PdhAddEnglishCounter(query,@"\PhysicalDisk(*)\Disk Write Bytes/sec",IntPtr.Zero,out writeCounter);
  PdhAddEnglishCounter(query,@"\PhysicalDisk(*)\% Idle Time",IntPtr.Zero,out idleCounter);
  PdhCollectQueryData(query);
 }
 private static Dictionary<string,double>? Read(IntPtr counter)
 {
  if(counter==IntPtr.Zero)return null;
  // GPU instances can appear between size lookup and read. Retry with a fresh size.
  for(int attempt=0;attempt<3;attempt++) {
   uint bytes=0; var status=PdhGetFormattedCounterArray(counter,0x200|0x8000,ref bytes,out var count,IntPtr.Zero);
   if(status!=0x800007D2 || bytes==0)return null;
   var buffer=Marshal.AllocHGlobal(checked((int)bytes));
   try {
    status=PdhGetFormattedCounterArray(counter,0x200|0x8000,ref bytes,out count,buffer);
    if(status==0x800007D2)continue;
    if(status!=0)return null;
    var result=new Dictionary<string,double>(); int stride=Marshal.SizeOf<Item>();
    for(int i=0;i<count;i++){var item=Marshal.PtrToStructure<Item>(buffer+i*stride);if(item.Value.Status<=1 && item.Value.Number>=0)result[Marshal.PtrToStringUni(item.Name)??""]=item.Value.Number;}
    return result.Count>0?result:null;
   }finally{Marshal.FreeHGlobal(buffer);}
  }
  return null;
 }
 public sealed record SampleData(long? Total,Dictionary<int,long>? Processes,Dictionary<int,double>? GpuLoads,double? GpuLoad,double? DiskRead,double? DiskWrite,double? DiskActive);
 public SampleData Sample()
 {
  if(query==IntPtr.Zero || PdhCollectQueryData(query)!=0)return new(null,null,null,null,null,null,null);
  var adapters=Read(adapterCounter); var processes=Read(processCounter);
  Dictionary<int,long>? byPid=processes==null?null:new();
  if(processes!=null)foreach(var pair in processes){var parts=pair.Key.Split('_');if(parts.Length>1 && parts[0]=="pid" && int.TryParse(parts[1],out var pid))byPid![pid]=byPid.GetValueOrDefault(pid)+(long)pair.Value;}
  var engines=Read(engineCounter);Dictionary<int,double>? loads=engines==null?null:new();var totals=new Dictionary<string,double>();
  if(engines!=null)foreach(var pair in engines){var parts=pair.Key.Split('_');if(parts.Length>2&&int.TryParse(parts[1],out int pid)){loads![pid]=Math.Max(loads.GetValueOrDefault(pid),Math.Clamp(pair.Value,0,100));var engine=string.Join('_',parts.Skip(2));totals[engine]=totals.GetValueOrDefault(engine)+pair.Value;}}
  var read=Read(readCounter);var write=Read(writeCounter);var idle=Read(idleCounter);
  double? Sum(Dictionary<string,double>? d)=>d==null?null:d.Where(x=>x.Key!="_Total").Sum(x=>x.Value);
  return new(adapters==null?null:(long)adapters.Values.Sum(),byPid,loads,totals.Count>0?Math.Clamp(totals.Values.Max(),0,100):null,Sum(read),Sum(write),idle?.Where(x=>x.Key!="_Total").Select(x=>(double?)Math.Clamp(100-x.Value,0,100)).Max());
 }
 public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
}
