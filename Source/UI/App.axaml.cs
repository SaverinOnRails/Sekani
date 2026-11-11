using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SekaniUI.ViewModels;
using SekaniUI.Views;

namespace SekaniUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();

            window.DataContext = new MainViewModel(window.Clipboard);

            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
