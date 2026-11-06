using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
namespace Pulse;
public sealed record SensorReading(string Component,string Name,double Value,string Source)
{
 public string Text=>$"{Value:0.0} °C";
}
public sealed record SensorSnapshot(DateTime Timestamp,List<SensorReading> Temperatures,string GpuName,long? GpuCapacity,double? GpuLoad);
internal sealed class Sensors
{
 private DateTime last=DateTime.MinValue;
 private SensorSnapshot cached=new(DateTime.MinValue,new(),"GPU",null,null);
 public SensorSnapshot Sample()
 {
  if((DateTime.UtcNow-last).TotalSeconds<5)return cached;last=DateTime.UtcNow;
  var temps=new List<SensorReading>();string gpuName="GPU";long? capacity=null;double? load=null;
  string smi=Path.Combine(Environment.SystemDirectory,"nvidia-smi.exe");
  if(File.Exists(smi))try {
   using var process=Process.Start(new ProcessStartInfo(smi,"--query-gpu=name,temperature.gpu,memory.total,utilization.gpu --format=csv,noheader,nounits"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true});
   if(process!=null){var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();if(process.WaitForExit(1800)&&process.ExitCode==0){var names=new List<string>();long total=0;bool found=false;foreach(var line in output.GetAwaiter().GetResult().Split('\n',StringSplitOptions.RemoveEmptyEntries)){var c=line.Trim().Split(',');if(c.Length!=4)continue;names.Add(c[0].Trim());if(double.TryParse(c[1],CultureInfo.InvariantCulture,out var t)&&t is >0 and <150)temps.Add(new("GPU",c[0].Trim(),t,"NVIDIA-Treiber"));if(double.TryParse(c[2],CultureInfo.InvariantCulture,out var mb)){total+=(long)(mb*1048576);found=true;}if(double.TryParse(c[3],CultureInfo.InvariantCulture,out var l))load=Math.Max(load??0,l);}gpuName=string.Join(" + ",names);if(found)capacity=total;}else if(!process.HasExited)process.Kill();}
  }catch { }
  // Read only an already-running sensor provider. No kernel driver is installed or started.
  try {
   using var search=new ManagementObjectSearcher(new ManagementScope(@"\\.\root\LibreHardwareMonitor"),new ObjectQuery("SELECT Name, Parent, Value FROM Sensor WHERE SensorType = 'Temperature'"),new System.Management.EnumerationOptions{Timeout=TimeSpan.FromSeconds(1),ReturnImmediately=true});
   using var values=search.Get();foreach(ManagementObject obj in values)using(obj){string parent=obj["Parent"]?.ToString()??"";string component=parent.Contains("cpu")?"CPU":parent.Contains("gpu")?"GPU":parent.Contains("ram")?"RAM":parent.Contains("hdd")||parent.Contains("nvme")||parent.Contains("ssd")?"Laufwerk":"Hardware";double v=Convert.ToDouble(obj["Value"],CultureInfo.InvariantCulture);if(v is >0 and <150)temps.Add(new(component,obj["Name"]?.ToString()??component,v,"LibreHardwareMonitor"));}
  }catch { }
  cached=new(DateTime.Now,temps,gpuName,capacity,load);return cached;
 }
}
