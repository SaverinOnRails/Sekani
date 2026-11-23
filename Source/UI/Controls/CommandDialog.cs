using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class CommandDialog : FocusablePromptControl
{
	public CommandDialog()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
		Background = Brushes.Transparent;
		Focusable = true;
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
				if (_textBox?.Text is not null)
				{
					Context.HandleCommand(_textBox.Text);
				}
				CloseControl(this);
				break;
		}
	}

	protected override void CloseControl(object sender)
	{
		Context.Mode = Mode.Normal;
		base.CloseControl(this);
	}

	protected override void OnActivated()
	{
		_textBox = new TextBox() { Height = 40, Width = 250 };
		Build();
	}

	protected override void OnDeactivated()
	{
	}
}
