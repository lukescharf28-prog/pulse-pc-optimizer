using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Pulse;
public sealed record InstalledApp(string Key,string Name,string Publisher,string Version,long? Size,string Command,string Kind,RegistryHive Hive,RegistryView View)
{
 public string SizeText=>Size is >0?Format.Bytes(Size.Value):"Nicht gemeldet";
}
internal static class AppCatalog
{
 private const string Root=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
 private static readonly Regex Blocked=new(@"microsoft|windows|webview|runtime|redistributable|visual c\+\+|\.net|driver|treiber|sdk\b|framework|services?\b|dienst|update\b|hotfix|firmware|chipset|anti.cheat|easyanticheat|battleye|vanguard|security|antivirus|vpn|virtual|python|java|node\.js|support component",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 private static readonly string[] Publishers={"Google LLC","Mozilla","VideoLAN","Discord Inc.","Discord Inc","Spotify AB","Valve Corporation","Epic Games, Inc.","Epic Games","GOG.com","Ubisoft","Electronic Arts","OBS Project","Blender Foundation","Notepad++ Team","7-Zip","Igor Pavlov","Brave Software Inc","Brave Software, Inc."};
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr CommandLineToArgvW(string command,out int count);
 [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr pointer);
 internal static bool Candidate(string name,string publisher,bool component,bool noRemove,string parent,string release,bool steam)
 {
  if(string.IsNullOrWhiteSpace(name)||component||noRemove||parent.Length>0||release.Length>0||Blocked.IsMatch(name+" "+publisher))return false;
  return steam||Publishers.Contains(publisher.Trim(),StringComparer.OrdinalIgnoreCase);
 }
 public static List<InstalledApp> Read()
 {
  var apps=new List<InstalledApp>();
  foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
   using var baseKey=RegistryKey.OpenBaseKey(hive,view);using var root=baseKey.OpenSubKey(Root);if(root==null)continue;
   foreach(var name in root.GetSubKeyNames())try {
    using var key=root.OpenSubKey(name);if(key==null)continue;
    string S(string k)=>key.GetValue(k)?.ToString()??"";
    bool steam=Regex.IsMatch(name,@"^Steam App [0-9]+$");
    if(!Candidate(S("DisplayName"),S("Publisher"),S("SystemComponent")=="1",S("NoRemove")=="1",S("ParentKeyName"),S("ReleaseType"),steam))continue;
    var app=new InstalledApp(name,S("DisplayName"),S("Publisher"),S("DisplayVersion"),long.TryParse(S("EstimatedSize"),out var size)&&size>0?size*1024:null,S("UninstallString"),steam?"Steam-Spiel":"App",hive,view);
    if(TryCommand(app,out _,out _))apps.Add(app);
   }catch(System.Security.SecurityException){}catch(IOException){}catch(UnauthorizedAccessException){}
  }
  return apps.DistinctBy(x=>(x.Name,x.Publisher,x.Command)).OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
 }
 internal static bool TryCommand(InstalledApp app,out string file,out string[] args)
 {
  file="";args=Array.Empty<string>();
  if(Guid.TryParse(app.Key,out var product)) {
   // MSI products use the product ID, never arbitrary registry-provided switches.
   if(!Regex.IsMatch(app.Command,@"(?i)\bmsiexec(?:\.exe)?\b"))return false;
   file=Path.Combine(Environment.SystemDirectory,"msiexec.exe");args=new[]{"/x",product.ToString("B"),"/norestart"};return true;
  }
  var ptr=CommandLineToArgvW(app.Command,out var count);if(ptr==IntPtr.Zero)return false;
  string[] parts;try{parts=Enumerable.Range(0,count).Select(i=>Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr,i*IntPtr.Size))??"").ToArray();}finally{LocalFree(ptr);}
  if(parts.Length==0||!Path.IsPathFullyQualified(parts[0])||!parts[0].EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(parts[0]))return false;
  file=Path.GetFullPath(parts[0]);args=parts.Skip(1).ToArray();
  if(file.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+"\\",StringComparison.OrdinalIgnoreCase))return false;
  string exe=Path.GetFileName(file);
  if(new[]{"cmd.exe","powershell.exe","pwsh.exe","rundll32.exe","regsvr32.exe","wscript.exe","cscript.exe","mshta.exe"}.Contains(exe,StringComparer.OrdinalIgnoreCase))return false;
  if(app.Kind=="Steam-Spiel")return exe.Equals("steam.exe",StringComparison.OrdinalIgnoreCase)&&args.Length==1&&args[0]=="steam://uninstall/"+app.Key[10..]&&(FileVersionInfo.GetVersionInfo(file).CompanyName??"").Contains("Valve",StringComparison.OrdinalIgnoreCase);
  if(args.Any(x=>Regex.IsMatch(x,@"^(?i)(/s|/q[nbr]?|/quiet|/silent|/verysilent|--silent|--force|/forcerestart)$")))return false;
  return true;
 }
 public static void Uninstall(InstalledApp selected)
 {
  var current=Read().SingleOrDefault(x=>x.Key==selected.Key&&x.Hive==selected.Hive&&x.View==selected.View);
  if(current==null||current.Command!=selected.Command||current.Name!=selected.Name||current.Publisher!=selected.Publisher)throw new InvalidOperationException("Der Installationseintrag hat sich geändert. Bitte neu einlesen.");
  if(!TryCommand(current,out var file,out var args))throw new InvalidOperationException("Kein unterstützter Deinstallationsweg.");
  var start=new ProcessStartInfo(file){UseShellExecute=true};foreach(var arg in args)start.ArgumentList.Add(arg);Process.Start(start);
 }
}
