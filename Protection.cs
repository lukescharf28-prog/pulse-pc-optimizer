using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
namespace Pulse;
internal static class Protection
{
 private static readonly ConcurrentDictionary<string,(DateTime stamp,bool blocked)> cache=new(StringComparer.OrdinalIgnoreCase);
 private static readonly Regex Restricted=new(@"microsoft|windows|nvidia|advanced micro devices|intel|realtek|qualcomm|synaptics|antivirus|malwarebytes|kaspersky|eset|bitdefender|crowdstrike|sentinel|defender",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 public static string[] UserExclusions{get;set;}=Array.Empty<string>();
 public static bool Blocked(string path)
 {
  if(UserExclusions.Any(x=>Path.GetFileNameWithoutExtension(path).Equals(x.Trim(),StringComparison.OrdinalIgnoreCase)||path.Equals(x.Trim(),StringComparison.OrdinalIgnoreCase)))return true;
  if(path.Contains(@"\WindowsApps\",StringComparison.OrdinalIgnoreCase)||path.Contains(@"\Common Files\",StringComparison.OrdinalIgnoreCase))return true;
  try {
   var stamp=File.GetLastWriteTimeUtc(path);if(cache.TryGetValue(path,out var entry)&&entry.stamp==stamp)return entry.blocked;
   var v=FileVersionInfo.GetVersionInfo(path);bool blocked=Restricted.IsMatch((v.CompanyName??"")+" "+(v.ProductName??""))||Regex.IsMatch(Path.GetFileName(path),@"(?i)service|helper.*service|anti.?cheat|battleye|vanguard|installer|setup|update|vpn|backup|sync|OneDrive|MsMp|Security|lsass|csrss|winlogon|smss|svchost");
   cache[path]=(stamp,blocked);return blocked;
  }catch{return true;}
 }
}
