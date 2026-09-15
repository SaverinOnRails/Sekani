using CommunityToolkit.Mvvm.ComponentModel;
using Sekani.EditorCore;

namespace SekaniUI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public static MainViewModel Instance = null!;

    [ObservableProperty]
    private SekaniDocument _document;
    public MainViewModel()
    {
        Instance = this;
        CreateDocument();
    }

    private void CreateDocument()
    {
        // var file = "/home/noble/jquery.min.js.js";
        // var file = "/home/noble/Projects/Sekani/Source/UI/Controls/Editor.cs";
        var file = "/home/noble/Documents/sekanitestfiles/xdisp.c";
        Document = new(file);
        // Document = new();
    }

    public void HandleCommand(string? command)
    {
        if(string.IsNullOrEmpty(command)) return;
        if (command == "write" || command == "w")
        {
            Document.TrySave();
        }
    }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Sekani!";


    [ObservableProperty]
    private Mode _mode = Mode.Normal;
}
