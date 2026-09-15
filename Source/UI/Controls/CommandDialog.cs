using System;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Sekani.EditorCore;
using SekaniUI.ViewModels;

namespace SekaniUI.Controls;

public class CommandDialog : ContentControl
{
	private TextBox _textBox;
	public MainViewModel _mainViewModelInstance => MainViewModel.Instance;
	public CommandDialog()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
		Background = Brushes.Transparent;
		Focusable = true;
		Build();
	}

	private void Build()
	{
		Content = MainContainer();
	}

	public Control MainContainer()
	{
		_textBox = new TextBox() { Height = 40, Width = 250 };
		return _textBox;
	}

	private void ExitMode()
	{
		_mainViewModelInstance.Mode = Mode.Normal;
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		HandleKeyInput(e);
	}

	private void HandleKeyInput(KeyEventArgs e)
	{
		switch (e.Key)
		{
			case Key.Escape:
				ExitMode();
				break;
			case Key.Enter:
				Console.WriteLine(_textBox.Text);
				ExitMode();
				break;
		}
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.Property == IsVisibleProperty)
		{
			var newValue = (bool)e.NewValue!;
			if (newValue)
			{
				_textBox.Clear();
				Dispatcher.UIThread.Post(() =>
				{
					_textBox.Focus();
				});
			}
		}
	}

}
