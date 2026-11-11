using System;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Sekani.EditorCore;
namespace SekaniUI.ViewModels;

public partial class MainViewModel : ViewModelBase, IEditorContext
{
    public Sekani.EditorCore.Clipboard Clipboard { get; set; }
    public event EventHandler? PanelClosed;

    [ObservableProperty]
    private SekaniDocument _document;

    public MainViewModel(Avalonia.Input.Platform.IClipboard? clipboard)
    {
        CreateClipboard(clipboard);
        CreateDocument();
    }

    private void CreateClipboard(IClipboard? clipboard)
    {
        if (clipboard is null) throw new InvalidOperationException("No clipboard provider for platform?");
        Clipboard = new(clipboard.TryGetTextAsync, clipboard.SetTextAsync);
    }

    private void CreateDocument()
    {
        // var file = "/home/noble/jquery.min.js.js";
        // var file = "/home/noble/longfile.text";
        // var file = "/home/noble/Documents/sekanitestfiles/piecetabletestfile.txt";
        // var file = "/home/noble/Documents/sekanitestfiles/xdisp.c";
        // var file = "/home/noble/Documents/sekanitestfiles/sqlite3.c";
        // var file = "/home/noble/Projects/noctalia/third_party/luau/Compiler/src/CostModel.cpp";
        var file = "/home/noble/Projects/yin/daemon/Monitor.cpp";
        // var file = "/home/noble/Documents/Hyprland/src/render/OpenGL.cpp";
        // var file = "/home/noble/Documents/sekanitestfiles/unicodetest.txt";
        // var file = "/home/noble/Documents/sekanitestfiles/chinesetest.txt";
        // var file = "/home/noble/Documents/sekanitestfiles/PdfViewer.Viewport.cs";
        Document = new(file, Clipboard);
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

    public void RaisePanelClosed(object? sender)
    {
        PanelClosed?.Invoke(sender, new());
    }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Sekani!";

    [ObservableProperty]
    private ISmallInputPromptContext? _smallInputPromptContext = null;


    [ObservableProperty]
    private Mode _mode = Mode.Normal;

    [ObservableProperty]
    private string? _motionCountBuffer = null;
}

