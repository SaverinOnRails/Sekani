using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SekaniUI.ViewModels;
using SekaniUI.Views;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
namespace SekaniUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

		//Prevent ShadUI's Toasts from stealing escape key from editorview via it's hotkey
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TemplatedControl>((control, e) =>
        {
            if (control.GetType().FullName != "ShadUI.Toast") return;
            if (e.NameScope.Find<Button>("PART_CloseButton") is { } close)
                close.HotKey = null;
        });
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
