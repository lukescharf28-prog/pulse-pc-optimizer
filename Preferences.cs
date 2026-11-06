namespace Pulse;
internal sealed class Preferences
{
 public int Interval{get;set;}=3;
 public bool Pause{get;set;}=true;
 public int History{get;set;}=60;
 public int MinRamMb{get;set;}=64;
 public int MaxBatch{get;set;}=10;
 public int Cooldown{get;set;}=60;
 public bool AllowBatch{get;set;}=false;
 public bool ReadSensors{get;set;}=true;
 public bool Fahrenheit{get;set;}=false;
 public bool AutoUpdates{get;set;}=true;
 public bool HideProtected{get;set;}=false;
 public string Exclusions{get;set;}="";
 public int SpeedBudget{get;set;}=40;
 public int SpeedSeconds{get;set;}=8;
 public int SpeedParallel{get;set;}=2;
 public bool WifiOnly{get;set;}=false;
 public void Normalize(){Interval=new[]{1,3,5,10}.Contains(Interval)?Interval:3;History=Math.Clamp(History,30,180);MinRamMb=Math.Clamp(MinRamMb,32,1024);MaxBatch=Math.Clamp(MaxBatch,1,30);Cooldown=Math.Clamp(Cooldown,30,600);SpeedBudget=Math.Clamp(SpeedBudget,10,300);SpeedSeconds=Math.Clamp(SpeedSeconds,3,20);SpeedParallel=Math.Clamp(SpeedParallel,1,4);Exclusions??="";}
}
