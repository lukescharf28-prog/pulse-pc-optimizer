using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
namespace Pulse;
public partial class MainWindow
{
 private Preferences preferences=new();
 private readonly CancellationTokenSource lifetime=new();
 private CancellationTokenSource? speedCancellation;
 private DateTime lastTrim=DateTime.MinValue,lastUpdateCheck=DateTime.MinValue;
 private List<InstalledApp> installedApps=new();
 private bool appLoading,updateBusy,installBusy;
 private UpdatePayload? availableUpdate;private string? updateManifest;
 private readonly Dictionary<string,StackPanel> extraPages=new();
 private readonly Dictionary<string,TextBlock> performanceValues=new();
 private readonly Dictionary<string,HistoryChart> performanceCharts=new();
 private StackPanel diskList=new(),networkList=new(),sensorList=new();
 private TextBlock performanceDetail=new(),appCount=new(),appDetails=new(),speedStatus=new(),updateStatus=new();
 private TextBlock speedPing=new(),speedDownload=new(),speedUpload=new();
 private TextBox appSearch=new();private DataGrid appsGrid=new();private Button uninstallButton=new(),speedStart=new(),speedStop=new(),installUpdate=new();
 private ProgressBar speedBar=new();private bool gamesOnly;
 private static Brush Ink(string hex)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
 private static TextBlock Label(string text,int size=13,string color="#A1ACBA")=>new(){Text=text,FontSize=size,Foreground=Ink(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,9)};
 private Border Section(string title,out StackPanel content){content=new StackPanel();content.Children.Add(Label(title,19,"#EDF0F5"));return new Border{Style=(Style)FindResource("Card"),Child=content,Margin=new Thickness(0,0,0,16)};}
 private Button Action(string text,Action action,bool primary=false){var b=new Button{Content=text,Margin=new Thickness(0,4,10,4),HorizontalAlignment=HorizontalAlignment.Left};if(primary)b.Style=(Style)FindResource("Primary");b.Click+=(_,_)=>action();return b;}
 private void InitializeExtras()
 {
  foreach(var name in new[]{"performance","apps","speed","updates"}){var p=new StackPanel{Visibility=Visibility.Collapsed};extraPages[name]=p;ExtraPanels.Children.Add(p);}
  BuildPerformance();BuildApps();BuildSpeed();BuildUpdates();BuildSettings();ApplyPreferences();
  Closed+=(_,_)=>{lifetime.Cancel();speedCancellation?.Cancel();};
 }
 private void ApplyPreferences(){preferences.Normalize();Protection.UserExclusions=preferences.Exclusions.Split(new[]{'\n',';'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);monitor.ReadSensors=preferences.ReadSensors;RamChart.Capacity=preferences.History;foreach(var chart in performanceCharts.Values)chart.Capacity=preferences.History;}
 private void BuildPerformance()
 {
  var p=extraPages["performance"];var grid=new UniformGrid{Columns=2};
  foreach(var (id,title) in new[]{("cpu","CPU · GESAMTAUSLASTUNG"),("ram","RAM · PHYSISCH BELEGT"),("gpu","GPU · STÄRKSTE ENGINE"),("disk","DATENTRÄGER · HÖCHSTE AKTIVITÄT")}){
   var card=Section(title,out var box);card.Margin=new Thickness(0,0,12,12);var value=Label("Wird gemessen …",27,"#C5F277");var chart=new HistoryChart{Height=110,Margin=new Thickness(0,8,0,0)};box.Children.Add(value);box.Children.Add(chart);performanceValues[id]=value;performanceCharts[id]=chart;grid.Children.Add(card);
  }p.Children.Add(grid);performanceDetail=Label("Live-Daten werden geladen …");p.Children.Add(performanceDetail);
  p.Children.Add(Section("Temperaturen",out var sensors));sensorList=new StackPanel();sensors.Children.Add(sensorList);sensors.Children.Add(Label("CPU-, RAM- und Laufwerkssensoren werden aus einem bereits laufenden LibreHardwareMonitor gelesen. GPU-Temperaturen kommen zusätzlich vom NVIDIA-Treiber. Fehlende Sensoren werden nicht geschätzt.",11));
  p.Children.Add(Section("Speicherplatz & Laufwerke",out var disks));diskList=new();disks.Children.Add(diskList);
  p.Children.Add(Section("Netzwerk · aktueller Durchsatz",out var net));networkList=new();net.Children.Add(networkList);net.Children.Add(Label("Linkrate und laufender Datenverkehr sind keine Messung deiner maximalen Internetgeschwindigkeit. Der Speedtest hat eine eigene Seite.",11));
 }
 private void UpdatePerformance(Snapshot s)
 {
  foreach(var pair in new[]{("cpu",s.Cpu),("ram",(double?)s.RamPercent),("gpu",s.GpuLoad),("disk",s.DiskActive)}){performanceValues[pair.Item1].Text=pair.Item2.HasValue?$"{pair.Item2:0.0} %":"Nicht verfügbar";if(pair.Item2.HasValue)performanceCharts[pair.Item1].Add(pair.Item2.Value);}
  string Rate(double? x)=>x.HasValue?Format.Bytes((long)x.Value)+"/s":"—";
  performanceDetail.Text=$"RAM verfügbar: {Format.Bytes((long)s.Available)} · Zugesichert: {Format.Bytes((long)s.Committed)} / {Format.Bytes((long)s.CommitLimit)}\nDatenträger: Lesen {Rate(s.DiskRead)} · Schreiben {Rate(s.DiskWrite)} · Verlauf: {preferences.History} Messungen\nGPU: {s.Sensors?.GpuName??"Treiberzähler"} · VRAM-Kapazität: {(s.Sensors?.GpuCapacity is long size?Format.Bytes(size):"nicht gemeldet")}";
  diskList.Children.Clear();foreach(var d in s.Drives){diskList.Children.Add(Label(d.Name+"   "+d.Capacity));diskList.Children.Add(new ProgressBar{Value=d.Used,Maximum=100,Height=5,Foreground=Ink("#81B6FF"),Background=Ink("#293340"),BorderThickness=new(0),Margin=new(0,0,0,14)});}
  networkList.Children.Clear();foreach(var n in s.Networks)networkList.Children.Add(Label(n.Text));if(s.Networks.Count==0)networkList.Children.Add(Label("Kein aktiver Netzwerkadapter erkannt."));
  sensorList.Children.Clear();foreach(var component in new[]{"CPU","GPU","RAM","Laufwerk"}){var list=s.Sensors?.Temperatures.Where(x=>x.Component==component).ToArray()??Array.Empty<SensorReading>();if(list.Length==0)sensorList.Children.Add(Label(component+"   —   Sensor nicht verfügbar",13,"#8591A2"));else foreach(var t in list){double v=preferences.Fahrenheit?t.Value*9/5+32:t.Value;sensorList.Children.Add(Label($"{component} · {t.Name}   {v:0.0} °{(preferences.Fahrenheit?"F":"C")}   · {t.Source}",13,"#D6E5C6"));}}
  if(s.Sensors!=null)sensorList.Children.Add(Label($"Sensorabfrage: {s.Sensors.Timestamp:HH:mm:ss} · maximal alle 5 Sekunden",11));
 }
 private void BuildApps()
 {
  var p=extraPages["apps"];p.Children.Add(Section("Apps & Spiele deinstallieren",out var intro));intro.Children.Add(Label("Eine konservative Auswahl erkannter Drittanbieter-Apps und Steam-Spiele. Windows-/Microsoft-Komponenten, Treiber, Laufzeitbibliotheken und unbekannte Einträge werden ausgeblendet. Das ist keine Garantie gegen Verlust von App-Daten."));var actions=new WrapPanel();actions.Children.Add(Action("Neu einlesen",()=>_=LoadApps()));actions.Children.Add(Action("Alle",()=>{gamesOnly=false;FilterApps();}));actions.Children.Add(Action("Nur Steam-Spiele",()=>{gamesOnly=true;FilterApps();}));intro.Children.Add(actions);
  appSearch=new TextBox{Margin=new(0,0,0,12),ToolTip="Installierte App suchen"};appSearch.TextChanged+=(_,_)=>FilterApps();System.Windows.Automation.AutomationProperties.SetName(appSearch,"Installierte App suchen");p.Children.Add(appSearch);
  appsGrid=new DataGrid{Height=300,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,IsReadOnly=true,SelectionMode=DataGridSelectionMode.Single,HeadersVisibility=DataGridHeadersVisibility.Column,GridLinesVisibility=DataGridGridLinesVisibility.None,Background=Ink("#171C25"),BorderThickness=new(0),RowHeight=44};
  foreach(var (header,path,width) in new[]{("APP / SPIEL","Name",240),("HERSTELLER","Publisher",170),("VERSION","Version",110),("GRÖSSE¹","SizeText",110),("TYP","Kind",100)})appsGrid.Columns.Add(new DataGridTextColumn{Header=header,Binding=new System.Windows.Data.Binding(path),Width=width});
  appsGrid.SelectionChanged+=(_,_)=>{uninstallButton.IsEnabled=appsGrid.SelectedItem is InstalledApp&&!appLoading;appDetails.Text=appsGrid.SelectedItem is InstalledApp app?$"{app.Name} · {app.Publisher}\nBeim Deinstallieren können Einstellungen, Spielstände oder lokale App-Daten betroffen sein. Pulse öffnet den normalen Deinstallationsdialog und löscht keine Dateien selbst.":"Wähle eine App aus. Es gibt keine Sammeldeinstallation.";};p.Children.Add(appsGrid);appCount=Label("Noch nicht eingelesen.",11);appCount.Margin=new(0,12,0,12);p.Children.Add(appCount);
  p.Children.Add(Section("Ausgewählte Anwendung",out var details));appDetails=Label("Wähle eine App aus. Es gibt keine Sammeldeinstallation.");details.Children.Add(appDetails);uninstallButton=Action("Deinstallation öffnen …",UninstallSelected);uninstallButton.IsEnabled=false;details.Children.Add(uninstallButton);details.Children.Add(Label("¹ Größe laut Installer. Fehlende Größen werden nicht durch Scannen privater Ordner ergänzt. Manche Installer benötigen eine Windows-Administratorbestätigung.",11));
 }
 private async Task LoadApps(){if(appLoading)return;appLoading=true;uninstallButton.IsEnabled=false;appCount.Text="Installationseinträge werden geprüft …";try{installedApps=await Task.Run(AppCatalog.Read);FilterApps();}catch(Exception ex){appCount.Text="Einlesen fehlgeschlagen: "+ex.Message;}finally{appLoading=false;uninstallButton.IsEnabled=appsGrid.SelectedItem is InstalledApp;}}
 private void FilterApps(){if(appsGrid==null)return;var list=installedApps.Where(x=>(!gamesOnly||x.Kind=="Steam-Spiel")&&(x.Name.Contains(appSearch.Text,StringComparison.OrdinalIgnoreCase)||x.Publisher.Contains(appSearch.Text,StringComparison.OrdinalIgnoreCase))).ToList();appsGrid.ItemsSource=list;appCount.Text=$"{list.Count} angezeigte Einträge · Sicherheitsfilter dauerhaft aktiv · keine Windows-/Microsoft-Produkte";}
 private void UninstallSelected(){if(appsGrid.SelectedItem is not InstalledApp app)return;if(MessageBox.Show(this,$"Deinstallationsdialog für „{app.Name}“ öffnen?\nHersteller: {app.Publisher}\n\nDie Anwendung kann eigene Daten oder Spielstände entfernen. Sichere wichtige Daten vorher. Im folgenden Herstellerdialog kannst du abbrechen.","App deinstallieren",MessageBoxButton.OKCancel,MessageBoxImage.Warning,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;try{AppCatalog.Uninstall(app);Log("Deinstallationsdialog geöffnet: "+app.Name+". Nach Abschluss die Liste neu einlesen.");}catch(Exception ex){Log("Deinstallation nicht gestartet: "+ex.Message);}}
 private void BuildSpeed()
 {
  var p=extraPages["speed"];p.Children.Add(Section("WLAN & Internet · Speedtest",out var intro));intro.Children.Add(Label("Misst HTTP-Ping, Download und Upload zu Cloudflare über die aktive Windows-Route. Bei LAN oder VPN wird dieser Weg gemessen; der Test kann keine WLAN-Verbindung erzwingen. Cloudflare erhält deine IP-Adresse und synthetische Testdaten. Es werden keine persönlichen Dateien hochgeladen."));
  var cards=new UniformGrid{Columns=3};foreach(var (title,id) in new[]{("HTTP-PING","ping"),("DOWNLOAD","down"),("UPLOAD","up")}){var card=Section(title,out var box);card.Margin=new(0,0,12,12);var val=Label("—",31,"#C5F277");box.Children.Add(val);if(id=="ping")speedPing=val;else if(id=="down")speedDownload=val;else speedUpload=val;cards.Children.Add(card);}p.Children.Add(cards);
  p.Children.Add(Section("Messung",out var run));speedStatus=Label("Bereit. Start erfolgt ausschließlich per Klick.");run.Children.Add(speedStatus);speedBar=new ProgressBar{Maximum=100,Height=6,Foreground=Ink("#C5F277"),Background=Ink("#293340"),BorderThickness=new(0),Margin=new(0,10,0,18)};run.Children.Add(speedBar);var actions=new WrapPanel();speedStart=Action("Speedtest starten",()=>_=RunSpeed(),true);speedStop=Action("Abbrechen",()=>speedCancellation?.Cancel());speedStop.IsEnabled=false;actions.Children.Add(speedStart);actions.Children.Add(speedStop);run.Children.Add(actions);run.Children.Add(Label("HTTP-Ping ist keine ICMP-Spielserver-Latenz. Ergebnisse hängen von Server, WLAN, VPN und parallel laufenden Downloads ab. Datenlimit und Dauer findest du in den Einstellungen.",11));
 }
 private async Task RunSpeed()
 {
  if(speedCancellation!=null)return;if(preferences.WifiOnly&&!SpeedTest.HasWifi()){speedStatus.Text="Kein aktiver WLAN-Adapter. WLAN aktivieren oder die WLAN-Voraussetzung in den Einstellungen ausschalten.";return;}
  speedCancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);speedStart.IsEnabled=false;speedStop.IsEnabled=true;speedPing.Text=speedDownload.Text=speedUpload.Text="—";speedBar.Value=0;
  speedStatus.Text=$"Start · Nutzdatenlimit {preferences.SpeedBudget} MB (+ Protokoll-Overhead)";
  try{await SpeedTest.Run(preferences.SpeedBudget,preferences.SpeedSeconds,preferences.SpeedParallel,new Progress<SpeedProgress>(x=>{speedPing.Text=x.Ping.HasValue?$"{x.Ping:0} ms":"—";speedDownload.Text=x.Download.HasValue?$"{x.Download:0.0} Mbit/s":"—";speedUpload.Text=x.Upload.HasValue?$"{x.Upload:0.0} Mbit/s":"—";speedBar.Value=x.Progress;speedStatus.Text=$"{x.Phase} · {x.Bytes/1000000d:0.0} MB gemessene Nutzdaten · Cloudflare";}),speedCancellation.Token);Log("Speedtest abgeschlossen.");}catch(OperationCanceledException){speedStatus.Text="Test abgebrochen. Angezeigte Zwischenwerte sind unvollständig.";}catch(Exception ex){speedStatus.Text="Test fehlgeschlagen: "+ex.Message;}finally{speedCancellation.Dispose();speedCancellation=null;speedStart.IsEnabled=true;speedStop.IsEnabled=false;}
 }
 private void BuildUpdates()
 {
  var p=extraPages["updates"];p.Children.Add(Section("Pulse aktuell halten",out var box));box.Children.Add(Label("Installierte Version: "+Updates.CurrentText,22,"#C5F277"));box.Children.Add(Label("Neue Versionen werden aus dem Pulse-Repository geladen. Signatur, Dateigröße, Prüfsumme und Version werden geprüft. Die bisherige EXE bleibt als Rückfallkopie erhalten. Deine Einstellungen bleiben erhalten."));updateStatus=Label("Noch nicht geprüft.");box.Children.Add(updateStatus);var actions=new WrapPanel();actions.Children.Add(Action("Nach Updates suchen",()=>_=CheckUpdates(true)));installUpdate=Action("Aktualisieren & neu starten",()=>_=InstallUpdate(),true);installUpdate.IsEnabled=false;actions.Children.Add(installUpdate);box.Children.Add(actions);box.Children.Add(Label(Updates.Feed,11));box.Children.Add(Label("Die portable App benötigt Schreibrechte in ihrem eigenen Ordner. Update-Prüfungen verbinden sich mit GitHub. Automatische Prüfungen können in den Einstellungen ausgeschaltet werden.",11));var receipt=Path.Combine(Updates.Staging,"last-result.txt");if(File.Exists(receipt))box.Children.Add(Label(File.ReadAllText(receipt),12));
 }
 private async Task CheckUpdates(bool requested=false)
 {
  if(updateBusy||installBusy||closed)return;updateBusy=true;lastUpdateCheck=DateTime.UtcNow;updateStatus.Text="Update-Signatur wird geprüft …";
  try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromSeconds(20));var result=await Updates.Check(timeout.Token);availableUpdate=Version.Parse(result.Data.Version)>Updates.Current?result.Data:null;updateManifest=result.Manifest;installUpdate.IsEnabled=availableUpdate!=null;UpdateBadge.Visibility=availableUpdate!=null?Visibility.Visible:Visibility.Collapsed;updateStatus.Text=availableUpdate!=null?$"Version {result.Data.Version} verfügbar.\n{result.Data.Notes}":"Du verwendest die aktuelle Version. Geprüft: "+DateTime.Now.ToString("HH:mm");}catch(Exception ex){updateStatus.Text="Update-Prüfung nicht möglich: "+ex.Message;if(requested)Log("Update-Prüfung fehlgeschlagen. Die installierte App bleibt unverändert.");}finally{updateBusy=false;}
 }
 private async Task InstallUpdate()
 {
  if(availableUpdate==null||updateManifest==null||installBusy)return;
  if(speedCancellation!=null){updateStatus.Text="Bitte den Speedtest zuerst beenden.";return;}
  if(MessageBox.Show(this,$"Pulse auf {availableUpdate.Version} aktualisieren?\n\nDie geprüfte Datei wird heruntergeladen. Danach wird Pulse geschlossen, ersetzt und neu gestartet. Andere Programme bleiben geöffnet.","Pulse aktualisieren",MessageBoxButton.OKCancel,MessageBoxImage.Question,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
  installBusy=true;installUpdate.IsEnabled=false;try{var package=await Updates.Download(availableUpdate,updateManifest,new Progress<double>(x=>updateStatus.Text=$"Update wird geladen: {x:0} %"),lifetime.Token);Updates.BeginInstall(package);Close();}catch(Exception ex){updateStatus.Text="Update nicht installiert: "+ex.Message;installUpdate.IsEnabled=true;}finally{installBusy=false;}
 }
 private void BuildSettings()
 {
  void Toggle(Panel box,string text,bool value,Action<bool> set){var c=new CheckBox{Content=text,IsChecked=value,Margin=new(0,9,0,9)};c.Click+=(_,_)=>{set(c.IsChecked==true);ApplyPreferences();Persist();};box.Children.Add(c);}
  void Range(Panel box,string text,int value,int min,int max,Action<int> set,string unit=""){var label=Label($"{text}: {value} {unit}");var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=1,IsSnapToTickEnabled=true,Margin=new(0,0,0,16),MaxWidth=500,HorizontalAlignment=HorizontalAlignment.Left,Width=420};slider.ValueChanged+=(_,e)=>{label.Text=$"{text}: {(int)e.NewValue} {unit}";set((int)e.NewValue);ApplyPreferences();if(ready)Persist();};box.Children.Add(label);box.Children.Add(slider);}
  var tuning=Section("Monitoring & Anzeige",out var monitorSettings);Range(monitorSettings,"Verlauf",preferences.History,30,180,v=>preferences.History=v,"Messungen");Toggle(monitorSettings,"Temperatursensoren abfragen (max. alle 5 Sekunden)",preferences.ReadSensors,v=>preferences.ReadSensors=v);Toggle(monitorSettings,"Temperaturen in Fahrenheit anzeigen",preferences.Fahrenheit,v=>preferences.Fahrenheit=v);Toggle(monitorSettings,"Geschützte Prozesse in Prozesslisten ausblenden",preferences.HideProtected,v=>{preferences.HideProtected=v;FilterRows();});SettingsPanel.Children.Insert(1,tuning);
  var memory=Section("Memory Reducer · manuell",out var ramSettings);ramSettings.Children.Add(Label("Keine automatische RAM-Leerung und keine automatische Prozessbeendigung. Die folgenden Grenzen gelten für die manuelle Sammelaktion."));Toggle(ramSettings,"Sammelaktion für Hintergrund-RAM freigeben",preferences.AllowBatch,v=>{preferences.AllowBatch=v;UpdateActions();});Range(ramSettings,"Mindest-RAM pro Hintergrundprozess",preferences.MinRamMb,32,1024,v=>preferences.MinRamMb=v,"MB");Range(ramSettings,"Maximale Prozesse je Sammelaktion",preferences.MaxBatch,1,30,v=>preferences.MaxBatch=v);Range(ramSettings,"Pause zwischen RAM-Aktionen",preferences.Cooldown,30,600,v=>preferences.Cooldown=v,"Sekunden");SettingsPanel.Children.Insert(2,memory);
  var protect=Section("Zusätzlich geschützte Programme",out var protectedSettings);protectedSettings.Children.Add(Label("Ein Prozessname ohne .exe oder ein vollständiger EXE-Pfad pro Zeile. Diese Liste kann den festen Windows-/Treiber-/Microsoft-Schutz nur erweitern."));var exclusions=new TextBox{Text=preferences.Exclusions,AcceptsReturn=true,Height=85,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};protectedSettings.Children.Add(exclusions);protectedSettings.Children.Add(Action("Schutzliste speichern",()=>{preferences.Exclusions=exclusions.Text;ApplyPreferences();Persist();Log("Zusätzliche Schutzliste gespeichert. Die nächste Messung übernimmt sie.");}));SettingsPanel.Children.Insert(3,protect);
  var speed=Section("Speedtest · Datenverbrauch",out var speedSettings);Range(speedSettings,"Nutzdatenlimit insgesamt",preferences.SpeedBudget,10,300,v=>preferences.SpeedBudget=v,"MB");Range(speedSettings,"Dauer je Download-/Upload-Phase",preferences.SpeedSeconds,3,20,v=>preferences.SpeedSeconds=v,"Sekunden");Range(speedSettings,"Parallele Verbindungen",preferences.SpeedParallel,1,4,v=>preferences.SpeedParallel=v);Toggle(speedSettings,"Test nur erlauben, wenn ein WLAN-Adapter aktiv ist",preferences.WifiOnly,v=>preferences.WifiOnly=v);speedSettings.Children.Add(Label("Es gilt das zuerst erreichte Zeit- oder Datenlimit. Der HTTP-Ping benötigt zusätzliche Zeit. Die Windows-Routingtabelle bestimmt den tatsächlichen Netzwerkweg.",11));SettingsPanel.Children.Insert(4,speed);
  var update=Section("Updates",out var updateSettings);Toggle(updateSettings,"Bei Start und alle 30 Minuten auf GitHub nach Updates suchen",preferences.AutoUpdates,v=>preferences.AutoUpdates=v);updateSettings.Children.Add(Label("Downloads und Installation starten immer erst durch deinen Klick.",11));SettingsPanel.Children.Insert(5,update);
 }
}
