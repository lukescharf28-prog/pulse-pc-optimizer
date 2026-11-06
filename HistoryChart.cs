using System.Windows;
using System.Windows.Media;
namespace Pulse;
public sealed class HistoryChart:FrameworkElement
{
 private readonly Queue<double> values=new();
 public int Capacity{get;set;}=60;
 public void Clear(){values.Clear();InvalidateVisual();}
 public void Add(double value){values.Enqueue(value);while(values.Count>Capacity)values.Dequeue();InvalidateVisual();}
 protected override void OnRender(DrawingContext dc)
 {
  double w=ActualWidth,h=ActualHeight;if(w<=0||h<=0)return;
  var grid=new Pen(new SolidColorBrush(Color.FromRgb(43,53,56)),1);
  for(int i=0;i<=4;i++)dc.DrawLine(grid,new Point(0,h*i/4),new Point(w,h*i/4));
  for(int i=1;i<6;i++)dc.DrawLine(grid,new Point(w*i/6,0),new Point(w*i/6,h));
  if(values.Count==0)return;
  var data=values.ToArray();var points=data.Select((v,i)=>new Point(w-(data.Length-1-i)*w/Math.Max(1,Capacity-1),h-5-Math.Clamp(v,0,100)/100*(h-10))).ToArray();
  if(points.Length>1){var fill=new StreamGeometry();using(var c=fill.Open()){c.BeginFigure(new Point(points[0].X,h),true,true);c.PolyLineTo(points,true,false);c.LineTo(new Point(w,h),true,false);}dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(70,197,242,119),Color.FromArgb(0,197,242,119),90),null,fill);
  var line=new StreamGeometry();using(var c=line.Open()){c.BeginFigure(points[0],false,false);c.PolyLineTo(points.Skip(1).ToArray(),true,false);}dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromRgb(197,242,119)),2),line);}
  dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(197,242,119)),null,points[^1],4,4);
 }
}
