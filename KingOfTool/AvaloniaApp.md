```csharp
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AvaloniaDemo
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }

    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }

    public class MainWindow : Window
    {
        private TextBlock? _messageText;

        public MainWindow()
        {
            Title = "Avalonia SharpPad Demo";
            Width = 800;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // 创建 UI 布局
            _messageText = new TextBlock
            {
                FontSize = 24,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(20)
            };

            Content = new StackPanel
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Children =
                {
                    _messageText
                }
            };

            // 窗体加载后显示动画文字
            Opened += async (_, __) => await ShowMessageAsync();
        }

        private async Task ShowMessageAsync()
        {
            string message = "Hello, SharpPad! 关注我: https://github.com/gaoconggit/SharpPad";
            string displayed = "";

            foreach (char c in message)
            {
                displayed += c;
                var currentText = displayed; // 捕获当前文本

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_messageText != null)
                    {
                        _messageText.Text = currentText;
                    }
                });

                await Task.Delay(100);
            }
        }
    }
}
```
