using CommunityToolkit.Mvvm.ComponentModel;
using Sekani.EditorCore;

namespace SekaniUI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Sekani!";


    [ObservableProperty]
    private Mode _mode = Mode.Normal;
}
