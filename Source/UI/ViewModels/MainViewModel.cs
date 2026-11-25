using System;
using System.Collections.Generic;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Sekani.EditorCore;
namespace SekaniUI.ViewModels;

public partial class MainViewModel : ViewModelBase, IEditorContext
{
    public Sekani.EditorCore.Clipboard Clipboard { get; set; } = null!;
    public event EventHandler? PanelClosed;

    public DocumentManager DocumentManager { get; }
    public ProjectManager ProjectManager { get; }

    public static List<string> Commands = ["write", "buffer-close"];

    public MainViewModel(IClipboard? clipboard)
    {
        CreateClipboard(clipboard);
        DocumentManager = new(Clipboard!, this);
        ProjectManager = new();
        ProjectManager.OpenProject("/home/noble/Projects/Sekani");
    }

    private void CreateClipboard(IClipboard? clipboard)
    {
        if (clipboard is null) throw new InvalidOperationException("No clipboard provider for platform?");
        Clipboard = new(clipboard.TryGetTextAsync, clipboard.SetTextAsync);
    }


    public void HandleCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return;
        switch (command)
        {
            case "write":
                WriteActiveBuffer();
                break;
            case "buffer-close":
                CloseActiveBuffer();
                break;
        }
    }

    private void WriteActiveBuffer()
    {
        if (ActiveDocumentView is null) return;
        var doc = ActiveDocumentView.Document;
        doc.TrySave();
    }

    private void CloseActiveBuffer()
    {
        if (ActiveDocumentView is null) return;
        var path = ActiveDocumentView.Document.FilePath;
        DocumentManager.CloseDocument(path);
    }


    public void RaisePanelClosed(object? sender)
    {
        PanelClosed?.Invoke(sender, new());
    }

    public void OpenBufferPicker()
    {
        var openDocuments = DocumentManager.GetOpenDocuments();
        BigInputPromptContext = new BigInputPrompBufferPickerContext(openDocuments, this);
    }

    public void OpenProjectFilePicker()
    {
        var files = ProjectManager.AllFiles();
        BigInputPromptContext = new BigInputPrompBufferPickerContext(files, this);
    }

    public void OpenCommandPalette()
    {
        BigInputPromptContext = new CommandPaletteContext(Commands, this);
    }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Sekani!";

    [ObservableProperty]
    private DocumentView? _activeDocumentView;

    [ObservableProperty]
    private ISmallInputPromptContext? _smallInputPromptContext = null;

    [ObservableProperty]
    private IBigInputPromptContext? _bigInputPromptContext = null;


    [ObservableProperty]
    private Mode _mode = Mode.Normal;

    [ObservableProperty]
    private string? _motionCountBuffer = null;
}

