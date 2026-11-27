using System;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Sekani.EditorCore;
using SekaniUI.Models;
namespace SekaniUI.ViewModels;

public partial class MainViewModel : ViewModelBase, IEditorContext
{
    public Sekani.EditorCore.Clipboard Clipboard { get; set; } = null!;
    public event EventHandler? PanelClosed;

    public DocumentManager DocumentManager { get; }
    public ProjectManager ProjectManager { get; }

    private readonly EditorCommands _commands;

    public MainViewModel(IClipboard? clipboard)
    {
        CreateClipboard(clipboard);
        DocumentManager = new(Clipboard!, this);
        ProjectManager = new();
        ToastManager = new ToastManager(ShadToastManager);
        _commands = new(this);
        // ProjectManager.OpenProject("/home/noble/Projects/Sekani");
    }

    private void CreateClipboard(IClipboard? clipboard)
    {
        if (clipboard is null) throw new InvalidOperationException("No clipboard provider for platform?");
        Clipboard = new(clipboard.TryGetTextAsync, clipboard.SetTextAsync);
    }


    public void HandleCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return;
        _commands.Execute(command);
    }

    public void RaisePanelClosed(object? sender)
    {
        PanelClosed?.Invoke(sender, new());
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

    public ShadUI.ToastManager ShadToastManager { get; } = new();

    public IToastManager ToastManager { get; }

}

