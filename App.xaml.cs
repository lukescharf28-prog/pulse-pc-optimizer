using System.Windows;
namespace Pulse;
public partial class App : Application
{
 protected override void OnStartup(StartupEventArgs e)
 {
  base.OnStartup(e);
  if(e.Args.Length==2&&e.Args[0]=="--apply-update"){Shutdown(Updates.Apply(e.Args[1]));return;}
  if(e.Args.Length==2&&e.Args[0]=="--network-test"){Shutdown(Task.Run(()=>NetworkTest.Run(e.Args[1])).GetAwaiter().GetResult());return;}
  if(e.Args.Length==4&&e.Args[0]=="--update-integration-test"){Shutdown(Updates.IntegrationTest(e.Args[1],e.Args[2],e.Args[3]));return;}
  if (e.Args.Contains("--test-worker")) {
   var memory = new byte[48*1024*1024]; for(int i=0;i<memory.Length;i+=4096)memory[i]=1;
   var worker = new Window { Title="Pulse test worker", Width=300, Height=130, Content="Isolierter Funktionstest", Tag=memory, ShowInTaskbar=true };
   var timeout = new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromSeconds(45) };
   timeout.Tick+=(_,_)=>Shutdown();timeout.Start();worker.Show();return;
  }
  if (e.Args.Contains("--self-test")) { var code = SelfTest.Run(e.Args); Shutdown(code); return; }
  DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Pulse · Fehler", MessageBoxButton.OK, MessageBoxImage.Error); args.Handled = true; };
  string? uiOutput=e.Args.Contains("--ui-test")?e.Args.SkipWhile(x=>x!="--ui-test").Skip(1).FirstOrDefault():null;
  var main=new MainWindow(uiOutput);
  if(e.Args.Length==3&&e.Args[0]=="--update-health"&&Updates.WithinStaging(e.Args[1]))main.ContentRendered+=(_,_)=>{try{System.IO.File.WriteAllText(e.Args[1],e.Args[2]);}catch{}};
  main.Show();
 }
}
