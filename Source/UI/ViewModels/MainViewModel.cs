using CommunityToolkit.Mvvm.ComponentModel;
using Sekani.EditorCore;

namespace SekaniUI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public static MainViewModel Instance = null!;
    public MainViewModel()
    {
        Instance = this;
    }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Sekani!";


    [ObservableProperty]
    private Mode _mode = Mode.Normal;
}
