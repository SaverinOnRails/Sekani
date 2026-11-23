using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public abstract class FocusablePromptControl : ContentControl
{
	protected TextBox? _textBox = null;

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
		Context.Mode = Mode.Normal;
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.Property == IsVisibleProperty)
		{
			var newValue = (bool)e.NewValue!;
			if (newValue)
			{
				OnActivated();
				_textBox!.Clear();
				Dispatcher.UIThread.Post(() => _textBox?.Focus());
			}
			else
			{
				OnDeactivated();
			}
		}
	}
	protected abstract void OnActivated();
	protected abstract void OnDeactivated();
}
