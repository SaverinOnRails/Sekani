using System;
using Avalonia.Controls;
using Avalonia.Input;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class SmallInputPrompt : FocusableTextInputControl
{

	public SmallInputPromptSearchContext _searchInputContext => (Context.SmallInputPromptContext as SmallInputPromptSearchContext)!;
	public SmallInputPrompt()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
		Height = 40;
		Focusable = true;
		_textBox.TextChanged += TextBoxTextChanged;
		Build();
	}

	private void TextBoxTextChanged(object? sender, TextChangedEventArgs e)
	{
		var context = (SmallInputPromptSearchContext)Context.SmallInputPromptContext!;
		context.SearchTerm = ((TextBox)sender!).Text;
		context.RaiseSearchTermUpdated();
	}

	private Control MainContainer()
	{
		return _textBox;
	}

	private void Build()
	{
		Content = MainContainer();
	}

	protected override void HandleKeyInput(KeyEventArgs e)
	{
		base.HandleKeyInput(e);
		switch (e.Key)
		{
			case Key.Enter:
				_searchInputContext.Committed = true;
				CloseControl(this);
				break;
		}
	}
	protected override void CloseControl(object? sender)
	{
		base.CloseControl(Context.SmallInputPromptContext);
		Context.SmallInputPromptContext = null;
	}
}
