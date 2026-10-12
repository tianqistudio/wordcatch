using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WordLens;
internal static class SelectionFixture
{
    public static void Show(bool empty)
    {
        var text = new TextBox { Text="Hello world selected text", FontSize=24, Margin=new Thickness(30) };
        var window = new Window { Title="WordLens selection test fixture", Width=600, Height=200, Content=text, Background=Brushes.White };
        window.Loaded += (_,_) => { window.Activate(); text.Focus(); if(empty) text.Select(0,0); else text.Select(0,11); };
        var timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(8) };
        timer.Tick += (_,_) => { timer.Stop(); Application.Current.Shutdown(); };
        window.Show(); timer.Start();
    }
}
