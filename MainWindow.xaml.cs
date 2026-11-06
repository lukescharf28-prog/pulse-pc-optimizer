using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Pulse;
public partial class MainWindow : Window
{
 private readonly SystemMonitor monitor=new();
 private readonly DispatcherTimer timer=new();
 private readonly ObservableCollection<string> activity=new();
 private Snapshot? snapshot;
 private bool busy,actionBusy,ready,closed;
 private string page="overview";
 private readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Pulse","settings.json");
 private readonly string? uiTestOutput;
 public MainWindow(string? uiTestOutput=null)
 {
  this.uiTestOutput=uiTestOutput;
  InitializeComponent();
  SourceInitialized+=(_,_)=>{int dark=1;Native.DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,20,ref dark,4);};
  ActivityList.ItemsSource=activity;
  CpuDetail.Text=$"{Environment.ProcessorCount} logische Prozessoren";
  Preferences prefs=new();try{if(File.Exists(settingsPath))prefs=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath))??new();}catch { }
  preferences=prefs;preferences.Normalize();
  timer.Interval=TimeSpan.FromSeconds(preferences.Interval);
  PauseMinimized.IsChecked=prefs.Pause;
  timer.Tick+=async(_,_)=>{if(WindowState!=WindowState.Minimized || PauseMinimized.IsChecked!=true)await Refresh();};
  Closed+=(_,_)=>{closed=true;timer.Stop();if(!busy)monitor.Dispose();};
  InitializeExtras();ready=true;SwitchPage("overview");UpdateLiveLabel();
 }
 private async void OnLoaded(object sender,RoutedEventArgs e){Log("Monitoring gestartet. Alle Aktionen werden von dir ausgelöst.");await Refresh();if(uiTestOutput!=null){await RunUiTest(uiTestOutput);Close();return;}if(!closed){timer.Start();if(preferences.AutoUpdates)_=CheckUpdates();}}
 private async Task Refresh()
 {
  if(busy||closed||actionBusy)return;busy=true;
  try {
   var sample=await Task.Run(monitor.Sample);if(closed)return;snapshot=sample;
   RamValue.Text=$"{sample.RamPercent:0} %";RamBar.Value=sample.RamPercent;
   RamDetail.Text=$"{(sample.Total-sample.Available)/1073741824d:0.0} / {sample.Total/1073741824d:0.0} GB";
   RamFree.Text=$"{sample.Available/1073741824d:0.0} GB verfügbar";
   CpuValue.Text=sample.Cpu.HasValue?$"{sample.Cpu:0} %":"—";CpuBar.Value=sample.Cpu??0;
   GpuValue.Text=sample.Gpu.HasValue?Format.Bytes(sample.Gpu.Value):"Nicht verfügbar";GpuValue.FontSize=sample.Gpu.HasValue?31:19;
   GpuDetail.Text=sample.Gpu.HasValue?"Dedizierter Speicher · alle GPUs":"Treiber liefert keine GPU-Zähler";
   RamChart.Add(sample.RamPercent);UpdatePerformance(sample);FilterRows();
   if(uiTestOutput==null&&preferences.AutoUpdates&&(DateTime.UtcNow-lastUpdateCheck).TotalMinutes>=30)_=CheckUpdates();
   if(activity.Count<=1)StatusText.Text=$"Aktualisiert {DateTime.Now:HH:mm:ss}  ·  {sample.Processes.Count} Prozesse  ·  Ohne Administratorrechte";
  }catch(Exception ex){StatusText.Text="Monitoring: "+ex.Message;}
  finally{busy=false;if(closed)monitor.Dispose();}
 }
 private void Navigate(object sender,RoutedEventArgs e){if(sender is Button b && b.Tag is string tag)SwitchPage(tag);}
 private void SwitchPage(string next)
 {
  page=next;
  foreach(var b in new[]{NavOverview,NavRam,NavGpu,NavProcesses,NavSettings,NavPerformance,NavApps,NavSpeed,NavUpdates}){bool active=(string)b.Tag==page;b.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(active?"#273124":"#00000000"));b.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(active?"#C5F277":"#A1A9B8"));}
  OverviewPanel.Visibility=page=="overview"?Visibility.Visible:Visibility.Collapsed;
  ProcessPanel.Visibility=page is "ram" or "gpu" or "processes"?Visibility.Visible:Visibility.Collapsed;
  SettingsPanel.Visibility=page=="settings"?Visibility.Visible:Visibility.Collapsed;
  MetricsPanel.Visibility=page is "settings" or "performance" or "apps" or "speed" or "updates"?Visibility.Collapsed:Visibility.Visible;
  foreach(var pair in extraPages)pair.Value.Visibility=pair.Key==page?Visibility.Visible:Visibility.Collapsed;
  if(page=="apps"&&installedApps.Count==0)_=LoadApps();
  PageTitle.Text=page switch{"performance"=>"Deine Hardware. In Echtzeit.","apps"=>"Platz für das, was du nutzt.","speed"=>"Wie schnell ist deine Verbindung?","updates"=>"Pulse bleibt aktuell.","ram"=>"Platz für deinen nächsten Move.","gpu"=>"Mehr Überblick. Weniger VRAM-Last.","processes"=>"Was läuft auf deinem PC?","settings"=>"So, wie du es brauchst.",_=>"Mehr Raum für das Wesentliche."};
  PageEyebrow.Text=page switch{"performance"=>"LIVE-LEISTUNG","apps"=>"APP-VERWALTUNG","speed"=>"NETZWERKTEST","updates"=>"SIGNIERTE UPDATES","ram"=>"MEMORY REDUCER","gpu"=>"GPU & VRAM","processes"=>"PROZESSMANAGER","settings"=>"EINSTELLUNGEN",_=>"DEIN PC. IM BLICK."};
  ActionTitle.Text=page=="gpu"?"Grafikspeicher gezielt entlasten":page=="ram"?"RAM reduzieren":"Programme gezielt verwalten";
  ActionDescription.Text=page=="gpu"?"Wähle einen VRAM-Verbraucher und schließe ihn, wenn du ihn nicht brauchst. Windows und der Treiber geben dessen Speicher wieder frei.":page=="ram"?"Wähle einen Prozess aus. Pulse reduziert seinen physischen Arbeitssatz, ohne das Programm zu beenden.":"Wähle einen Prozess aus, um seinen RAM zu reduzieren oder ihn zu schließen. Windows-Prozesse bleiben geschützt.";
  SafetyNote.Text=page=="gpu"?"I/O = Lese-/Schreibtransfer des Prozesses einschließlich Dateien, Netzwerk und Geräten, nicht nur Datenträger. Dedizierter GPU-Speicher laut Windows, über alle GPUs summiert. Gemeinsamer Speicher kann pro Prozess mehrfach auftauchen. Hintergrund bedeutet nicht automatisch unbenutzt. Keine automatische Beendigung.":"RAM-Reduktion ist kurzfristig: benötigte Daten werden erneut geladen. Das kann Ruckler auslösen. I/O umfasst Dateien, Netzwerk und Geräte. Schließe ungenutzte Apps für eine nachhaltige Entlastung. Geschützte Prozesse sind gesperrt.";
  TrimButton.Visibility=page=="gpu"?Visibility.Collapsed:Visibility.Visible;
  SearchBox.Text="";FilterRows();UpdateActions();
  BatchTrimButton.Visibility=page=="ram"?Visibility.Visible:Visibility.Collapsed;
 }
 private void FilterRows()
 {
  if(snapshot==null)return;
  var selected=ProcessGrid.SelectedItem as ProcessRow;
  IEnumerable<ProcessRow> rows=snapshot.Processes;
  if(preferences.HideProtected)rows=rows.Where(x=>x.Allowed);
  if(page=="gpu")rows=rows.Where(x=>x.Gpu>0).OrderByDescending(x=>x.Gpu);
  string search=SearchBox.Text.Trim();if(search.Length>0)rows=rows.Where(x=>x.Name.Contains(search,StringComparison.OrdinalIgnoreCase)||x.Id.ToString().Contains(search));
  var sorts=System.Windows.Data.CollectionViewSource.GetDefaultView(ProcessGrid.ItemsSource)?.SortDescriptions.ToArray();
  var list=rows.ToList();ProcessGrid.ItemsSource=list;
  if(sorts!=null){var view=System.Windows.Data.CollectionViewSource.GetDefaultView(list);foreach(var sort in sorts)view.SortDescriptions.Add(sort);}
  if(selected!=null)ProcessGrid.SelectedItem=list.FirstOrDefault(x=>x.Id==selected.Id&&x.StartTicks==selected.StartTicks);
  ListNote.Text=page=="gpu"&&!snapshot.GpuProcessesAvailable?"Der Grafiktreiber stellt keine Prozesszähler bereit. Die allgemeine Prozessliste steht weiterhin zur Verfügung.":$"{list.Count} Prozesse · Wähle eine Zeile für Aktionen.  — = Messwert nicht verfügbar.";
  if(page=="gpu"&&snapshot.GpuProcessesAvailable&&list.Count==0)ListNote.Text=search.Length>0?"Keine Treffer für diese Suche.":"Zurzeit werden keine Prozesse mit dediziertem GPU-Speicher gemeldet.";
  UpdateActions();
 }
 private void SearchChanged(object sender,TextChangedEventArgs e){if(SearchPlaceholder!=null)SearchPlaceholder.Visibility=string.IsNullOrEmpty(SearchBox.Text)?Visibility.Visible:Visibility.Collapsed;if(ready)FilterRows();}
 private void SelectionChanged(object sender,SelectionChangedEventArgs e)=>UpdateActions();
 private void UpdateActions(){if(TrimButton==null)return;bool allowed=ProcessGrid.SelectedItem is ProcessRow{Allowed:true}&&!actionBusy;TrimButton.IsEnabled=allowed;CloseButton.IsEnabled=allowed;KillButton.IsEnabled=false;if(BatchTrimButton!=null)BatchTrimButton.IsEnabled=!actionBusy&&snapshot!=null&&preferences.AllowBatch;}
 private async void TrimBackground(object sender,RoutedEventArgs e)
 {
  if(snapshot==null||actionBusy||!preferences.AllowBatch)return;
  if(!CanTrim())return;
  var candidates=snapshot.Processes.Where(x=>x.Allowed&&x.Status=="Hintergrund"&&x.Ram>=preferences.MinRamMb*1024L*1024).Take(preferences.MaxBatch).ToArray();
  if(candidates.Length==0){Log("Keine geeigneten Hintergrundprozesse über der eingestellten RAM-Grenze gefunden.");return;}
  string names=string.Join("\n",candidates.GroupBy(x=>x.Name).Select(g=>$"• {g.Key}  ({g.Count()} Prozess{(g.Count()==1?"":"e")})"));
  if(MessageBox.Show(this,$"Arbeitssätze dieser {candidates.Length} Hintergrundprozesse reduzieren?\n\n{names}\n\nEs werden keine Programme beendet. Benötigte Daten werden erneut geladen; dabei können Ruckler auftreten. Hintergrundprozesse können weiterhin aktiv genutzt werden. Die Aktion berücksichtigt deine eingestellten Grenzen.","Hintergrund-RAM reduzieren",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
  await Act(async()=>{
   lastTrim=DateTime.UtcNow;var result=await Task.Run(()=>{int done=0,skipped=0;long reduced=0;foreach(var row in candidates){try{using var p=Process.GetProcessById(row.Id);Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out var front);if(p.MainWindowHandle!=IntPtr.Zero||front==row.Id){skipped++;continue;}reduced+=ProcessActions.Trim(row);done++;}catch{skipped++;}}return(done,skipped,reduced);});
   Log($"{result.done} Arbeitssätze reduziert: {Format.Bytes(result.reduced)} summierte Prozess-Differenz; {result.skipped} übersprungen. Kein Maß für global freien RAM.");
  });
 }
 private async void RefreshClick(object sender,RoutedEventArgs e)=>await Refresh();
 private async void TrimSelected(object sender,RoutedEventArgs e)
 {
  if(ProcessGrid.SelectedItem is not ProcessRow row||!row.Allowed)return;
  if(!CanTrim())return;lastTrim=DateTime.UtcNow;
  await Act(async()=>{long reduced=await Task.Run(()=>ProcessActions.Trim(row));Log($"{row.Name}: Arbeitssatz um {Format.Bytes(reduced)} reduziert (Momentaufnahme).");});
 }
 private async void CloseSelected(object sender,RoutedEventArgs e)
 {
  if(ProcessGrid.SelectedItem is not ProcessRow row||!row.Allowed)return;
  if(MessageBox.Show(this,$"{row.Name} (PID {row.Id}) schließen?\n\nSpeichere vorher deine Arbeit. Pulse sendet eine normale Schließen-Anfrage. Das Programm kann einen Speicherdialog anzeigen oder im Hintergrund weiterlaufen.","App schließen",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
  await Act(()=>{bool sent=ProcessActions.Close(row);Log(sent?$"Schließen-Anfrage an {row.Name} gesendet. Prüfe eventuelle Speicherdialoge.":$"{row.Name} hat kein schließbares Hauptfenster. Pulse erzwingt keine Beendigung.");return Task.CompletedTask;});
 }
 private bool CanTrim(){double remaining=preferences.Cooldown-(DateTime.UtcNow-lastTrim).TotalSeconds;if(remaining>0){Log($"Nächste RAM-Aktion in {Math.Ceiling(remaining)} Sekunden. Einstellbar unter Einstellungen.");return false;}return true;}
 private async Task Act(Func<Task> operation)
 {
  if(actionBusy)return;actionBusy=true;UpdateActions();
  try{await operation();}catch(Exception ex){Log("Aktion nicht ausgeführt: "+ex.Message);}finally{actionBusy=false;UpdateActions();}
  await Refresh();
 }
 private void Log(string message){activity.Insert(0,$"{DateTime.Now:HH:mm}   {message}");while(activity.Count>4)activity.RemoveAt(activity.Count-1);StatusText.Text=message;}
 private void SetInterval(object sender,RoutedEventArgs e){timer.Interval=TimeSpan.FromSeconds(int.Parse((string)((Button)sender).Tag));UpdateLiveLabel();Persist();Log($"Monitoring-Intervall: {timer.Interval.TotalSeconds:0} Sekunden.");}
 private void UpdateLiveLabel()=>LiveLabel.Text=$"●  LIVE  ·  {timer.Interval.TotalSeconds:0} s";
 private void SaveSettings(object sender,RoutedEventArgs e){if(ready)Persist();}
 private void Persist(){preferences.Interval=(int)timer.Interval.TotalSeconds;preferences.Pause=PauseMinimized.IsChecked==true;try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,JsonSerializer.Serialize(preferences));}catch(Exception ex){Log("Einstellungen konnten nicht gespeichert werden: "+ex.Message);}}
 private void OpenSettings(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo((string)((Button)sender).Tag){UseShellExecute=true});}catch(Exception ex){Log("Windows-Einstellungen konnten nicht geöffnet werden: "+ex.Message);}}
 private async Task RunUiTest(string directory)
 {
  Directory.CreateDirectory(directory);var checks=new List<object>();bool pass=true;
  void Check(string name,bool ok){checks.Add(new{name,ok});if(!ok)pass=false;}
  async Task Capture(string name){await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();var root=(FrameworkElement)Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);}
  try {
   await Task.Delay(1100);await Refresh();await Capture("01-dashboard");Check("Live RAM visible",RamValue.Text.Contains('%'));Check("Dashboard visible",OverviewPanel.Visibility==Visibility.Visible);
   NavRam.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("RAM navigation",page=="ram"&&ProcessPanel.Visibility==Visibility.Visible);Check("No selection disables actions",!TrimButton.IsEnabled&&!KillButton.IsEnabled);
   SearchBox.Text="Pulse";Check("Search filters process names",ProcessGrid.Items.Cast<ProcessRow>().All(x=>x.Name.Contains("Pulse",StringComparison.OrdinalIgnoreCase))&&ProcessGrid.Items.Count>0);
   ProcessGrid.SelectedItem=ProcessGrid.Items.Cast<ProcessRow>().FirstOrDefault(x=>x.Id==Environment.ProcessId);Check("Own process UI actions disabled",!TrimButton.IsEnabled&&!KillButton.IsEnabled&&!CloseButton.IsEnabled);
   SearchBox.Text="does-not-exist-pulse-test";Check("Search empty state",ProcessGrid.Items.Count==0);
   SearchBox.Text="";ProcessGrid.SelectedItem=ProcessGrid.Items.Cast<ProcessRow>().FirstOrDefault(x=>x.Allowed);Check("Eligible process enables actions",TrimButton.IsEnabled&&!KillButton.IsEnabled&&CloseButton.IsEnabled);await Capture("02-memory");
   NavGpu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("GPU navigation",page=="gpu"&&TrimButton.Visibility==Visibility.Collapsed);Check("Only GPU consumers listed",ProcessGrid.Items.Cast<ProcessRow>().All(x=>x.Gpu>0));await Capture("03-gpu");
   NavProcesses.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Processes navigation",page=="processes"&&TrimButton.Visibility==Visibility.Visible);await Capture("04-processes");
   NavSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Settings navigation",SettingsPanel.Visibility==Visibility.Visible&&MetricsPanel.Visibility==Visibility.Collapsed);await Capture("05-settings");
   NavPerformance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Performance navigation",page=="performance");await Capture("07-performance");
   NavApps.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await LoadApps();Check("Apps navigation",page=="apps");Check("No Microsoft uninstall entries",installedApps.All(x=>!x.Publisher.Contains("Microsoft",StringComparison.OrdinalIgnoreCase)));await Capture("08-apps");
   NavSpeed.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Speed test requires click",speedCancellation==null);await Capture("09-speed");
   NavUpdates.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("Install blocked without release",!installUpdate.IsEnabled);await Capture("10-updates");
   NavOverview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Width=1000;Height=730;await Capture("06-minimum-window");Check("Minimum width layout",ActualWidth>=MinWidth);
  }catch(Exception ex){pass=false;checks.Add(new{name="Unexpected UI error",ok=false,error=ex.ToString()});}
  File.WriteAllText(Path.Combine(directory,"ui-results.json"),JsonSerializer.Serialize(new{pass,checks},new JsonSerializerOptions{WriteIndented=true}));
 }
}
