using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public abstract class FocusableTextInputControl : ContentControl
{
	protected TextBox _textBox = new();

	protected IEditorContext Context => (DataContext as IEditorContext)!;

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		HandleKeyInput(e);
	}

	protected virtual void HandleKeyInput(KeyEventArgs e)
	{
		switch (e.Key)
		{
			case Key.Escape:
				CloseControl(this);
				break;
		}
	}

	protected virtual void CloseControl(object? sender)
	{
		Context.RaisePanelClosed(sender);
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
