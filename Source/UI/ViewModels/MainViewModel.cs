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
        // var file = "/home/noble/Documents/sekanitestfiles/longfile.text";
       var file = "/home/noble/Documents/sekanitestfiles/xdisp.c";
        // var file = "/home/noble/Documents/sekanitestfiles/PdfViewer.Viewport.cs";
        // Document = new(file);
        Document = new();
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
