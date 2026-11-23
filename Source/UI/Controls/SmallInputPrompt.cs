using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Sekani.EditorCore;
using ShadUI;

namespace SekaniUI.Controls;

public class SmallInputPrompt : FocusablePromptControl
{

	public SmallInputPromptSearchContext _searchInputContext => (Context.SmallInputPromptContext as SmallInputPromptSearchContext)!;
	public SmallInputPrompt()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
		Height = 40;
		Focusable = true;
	}

	private TextBlock _resultCountTextBlock;
	private void TextBoxTextChanged(object? sender, TextChangedEventArgs e)
	{
		var context = (SmallInputPromptSearchContext)Context.SmallInputPromptContext!;
		context.SearchTerm = ((TextBox)sender!).Text;
		context.RaiseSearchTermUpdated();
	}

	private Control MainContainer()
	{
		var panel = new Panel() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
		var grid = new Grid() { ColumnDefinitions = new("*,*") };
		panel.Bind(Panel.BackgroundProperty,
			this.GetResourceObservable("BackgroundColor", obj => obj is Color c ? new SolidColorBrush(c) : null));
		_textBox.InnerRightContent = new PathIcon
		{
			Data = Icons.Search,
			Opacity = 0.75,
			Width = 16
		};
		Grid.SetColumn(_textBox, 0);
		Grid.SetColumn(_resultCountTextBlock, 1);

		grid.Children.Add(_textBox);
		grid.Children.Add(_resultCountTextBlock);
		panel.Children.Add(grid);
		return panel;
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
				_searchInputContext.Commit();
				CloseControl(this);
				break;
		}
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.Property == IsVisibleProperty)
		{
			var isVisible = (bool)e.NewValue!;
			if (isVisible)
			{
				_resultCountTextBlock.Text = string.Empty;
				if (Context?.SmallInputPromptContext is SmallInputPromptSearchContext searchContext)
				{
					searchContext.SearchHitsUpdated += OnSearchHitsUpdated;
				}
			}
		}
	}
	private void OnSearchHitsUpdated(object? sender, EventArgs e)
	{
		if (sender is not SmallInputPromptSearchContext context) return;
		int count = context.SearchHits.Count;
		if (string.IsNullOrEmpty(context.SearchTerm))
		{
			_resultCountTextBlock.Text = string.Empty;
		}
		else
		{
			_resultCountTextBlock.Text = count switch
			{
				0 => "No results",
				1 => "1 result",
				_ => $"{context.SearchHitOriginalIndex + 1} of {count}"
			};
		}
	}
	protected override void CloseControl(object? sender)
	{
		if (Context.SmallInputPromptContext is SmallInputPromptSearchContext searchContext)
		{
			searchContext.SearchHitsUpdated -= OnSearchHitsUpdated;
		}
		base.CloseControl(Context.SmallInputPromptContext);
		Context.SmallInputPromptContext = null;
	}

	//TODO: this design is dogshit
	protected override void OnActivated()
	{
		_textBox = new();
		_textBox.TextChanged += TextBoxTextChanged;
		_textBox.PlaceholderText = "Search file...";
		_textBox.Margin = new(15);

		_resultCountTextBlock = new()
		{
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
			FontSize = 15,
			Margin = new(0, 0, 16, 0),
			Opacity = 0.6
		};
		Build();
	}

	protected override void OnDeactivated()
	{
	}
}
