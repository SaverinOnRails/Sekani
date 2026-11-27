using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

internal sealed partial class EmptyEditorView : ContentControl
{
	[GeneratedStyledProperty]
	public partial Mode Mode { get; set; }

	private IEditorContext? _editorContext;

	public EmptyEditorView()
	{
		Focusable = true;
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;
		Background = Brush.Parse("#0c1017");
		Content = MainContainer();
	}

	private Control MainContainer()
	{
		var stack = new StackPanel
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 10
		};

		stack.Children.Add(new TextBlock
		{
			Text = "SEKANI",
			FontSize = 96,
			FontWeight = FontWeight.Bold,
			LetterSpacing = 16,
			Opacity = 0.85,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0, 0, 0, 30)
		});
		var openFolderButton = new Button
		{
			Content = "Open folder",
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0, 0, 0, 20),
		};
		openFolderButton.Classes.Add("Primary");
		openFolderButton.Click += OpenFolderClicked;

		stack.Children.Add(Hint(":", "Command palette"));
		stack.Children.Add(Hint("space f", "Open project file"));
		stack.Children.Add(Hint("space b", "Open buffers"));
		stack.Children.Add(openFolderButton);

		return stack;
	}

	private async void OpenFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (_editorContext is null) return;
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel is null || !topLevel.StorageProvider.CanPickFolder) return;

		var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = "Open folder",
			AllowMultiple = false
		});
		if (folders.Count == 0 || !folders[0].Path.IsFile) return;

		_editorContext.ProjectManager.OpenProject(folders[0].Path.LocalPath);
		Focus();
	}

	private static Control Hint(string keys, string description)
	{
		var grid = new Grid { ColumnDefinitions = new("140,220") };

		var keyText = new TextBlock
		{
			Text = keys,
			FontSize = 16,
			FontWeight = FontWeight.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 0, 24, 0)
		};
		var descriptionText = new TextBlock
		{
			Text = description,
			FontSize = 16,
			Opacity = 0.6
		};

		Grid.SetColumn(keyText, 0);
		Grid.SetColumn(descriptionText, 1);
		grid.Children.Add(keyText);
		grid.Children.Add(descriptionText);
		return grid;
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);
		if (_editorContext is not null) _editorContext.PanelClosed -= OnPanelClosed;
		_editorContext = DataContext as IEditorContext;
		if (_editorContext is not null) _editorContext.PanelClosed += OnPanelClosed;

		if (_editorContext!.ActiveDocumentView is null)
			FocusWhenReady();
	}

	private void OnPanelClosed(object? sender, EventArgs e)
	{
		if (IsVisible) FocusWhenReady();
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.Property == IsVisibleProperty && e.GetNewValue<bool>())
		{
			FocusWhenReady();
		}
	}

	private void FocusWhenReady() =>
		Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		Focus();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		switch (e.Key)
		{
			case Key.Space:
				Mode = Mode.Space;
				break;
			case Key.Escape:
				Mode = Mode.Normal;
				break;
		}
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		base.OnTextInput(e);
		if (e.Text is null || _editorContext is null) return;

		if (Mode == Mode.Space)
		{
			switch (e.Text)
			{
				case "b":
					_editorContext.HandleCommand(CommandNames.BufferPicker);
					break;
				case "f":
					_editorContext.HandleCommand(CommandNames.FilePicker);
					break;
			}
		}
		else if (e.Text == ":")
		{
			_editorContext.HandleCommand(CommandNames.CommandPalette);
		}
	}
}
