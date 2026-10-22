using CommunityToolkit.Mvvm.ComponentModel;
using Sekani.EditorCore;

namespace SekaniUI.ViewModels;

public partial class MainViewModel : ViewModelBase, IEditorContext
{
    public static IEditorContext EditorContext = null!;

    [ObservableProperty]
    private SekaniDocument _document;

    public MainViewModel()
    {
        EditorContext = this;
        CreateDocument();
    }

    private void CreateDocument()
    {
        // var file = "/home/noble/jquery.min.js.js";
        // var file = "/home/noble/Documents/sekanitestfiles/longfile.text";
        // var file = "/home/noble/Documents/sekanitestfiles/piecetabletestfile.txt";
        // var file = "/home/noble/Documents/sekanitestfiles/xdisp.c";
        var file = "/home/noble/Projects/yin/daemon/Monitor.cpp";
        // var file = "/home/noble/Documents/sekanitestfiles/unicodetest.txt";
        // var file = "/home/noble/Documents/sekanitestfiles/chinesetest.txt";
        // var file = "/home/noble/Documents/sekanitestfiles/PdfViewer.Viewport.cs";
        Document = new(file);
        // Document = new();
    }

    public void HandleCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return;
        if (command == "write" || command == "w")
        {
            Document.TrySave();
        }
    }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Sekani!";


    [ObservableProperty]
    private Mode _mode = Mode.Normal;

    [ObservableProperty]
    private int _motionCountBuffer = 0;
}

public interface IEditorContext
{
    public int MotionCountBuffer { get; }

    public void HandleCommand(string command);

    public Mode Mode { get; set; }
}
