using System;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Sekani.EditorCore;
using SekaniUI.ViewModels;

namespace SekaniUI.Controls;

public class CommandDialog : FocusableTextInputControl
{

	public CommandDialog()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
		Background = Brushes.Transparent;
		_textBox = new TextBox() { Height = 40, Width = 250 };
		Focusable = true;
		Build();
	}

	private void Build()
	{
		Content = MainContainer();
	}

	private Control MainContainer()
	{
		return _textBox;
	}

	protected override void HandleKeyInput(KeyEventArgs e)
	{
		base.HandleKeyInput(e);
		switch (e.Key)
		{
			case Key.Enter:
				Context.HandleCommand(_textBox.Text);
				CloseControl(this);
				break;
		}
	}

	protected override void CloseControl(object sender)
	{
		Context.Mode = Mode.Normal;
		base.CloseControl(this);
	}
}
