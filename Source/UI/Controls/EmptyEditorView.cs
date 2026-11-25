using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class EmptyEditorView : ContentControl
{
	public static readonly StyledProperty<Mode> ModeProperty =
		AvaloniaProperty.Register<EmptyEditorView, Mode>(nameof(Mode));

	public Mode Mode
	{
		get => GetValue(ModeProperty);
		set => SetValue(ModeProperty, value);
	}

	private IEditorContext? _editorContext;

	public EmptyEditorView()
	{
		Focusable = true;
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;
		Background = Brush.Parse("#1e1e2e");
		Content = MainContainer();
	}

	private static Control MainContainer()
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

		stack.Children.Add(Hint(":", "Command palette"));
		stack.Children.Add(Hint("space f", "Open project file"));
		stack.Children.Add(Hint("space b", "Open buffers"));

		return stack;
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
					_editorContext.OpenBufferPicker();
					break;
				case "f":
					_editorContext.OpenProjectFilePicker();
					break;
			}
		}
		else if (e.Text == ":")
		{
			_editorContext.OpenCommandPalette();
		}
	}
}
