using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class BigInputPrompt : FocusablePromptControl
{
	private ListBox? _fileList;

	public BigInputPrompt()
	{
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;

		AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
	}

	private BigInputPromptListContext? _listContext => Context.BigInputPromptContext as BigInputPromptListContext;

	public Control MainContainer()
	{
		var wrapperGrid = new Grid() { ColumnDefinitions = new("25*,50*,25*") };

		var mainGrid = new Grid() { Height = 700, RowDefinitions = new("70, *") };
		mainGrid.Bind(Grid.BackgroundProperty,
			this.GetResourceObservable("BackgroundColor", obj => obj is Color c ? new SolidColorBrush(c, 0.8) : null));
		mainGrid.BackdropEffect = new BlurEffect() { Radius = 20 };
		Grid.SetColumn(mainGrid, 1);
		wrapperGrid.Children.Add(mainGrid);

		Grid.SetRow(_textBox!, 0);
		mainGrid.Children.Add(_textBox!);

		_fileList = new ListBox
		{
			ItemsSource = _listContext?.Items,
			Margin = new Thickness(15, 0, 15, 15),
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Stretch,
			Focusable = false,
			ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock
			{
				Text = name,
				Padding = new Thickness(8, 6),
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.LeadingCharacterEllipsis,
			})
		};

		ScrollViewer.SetHorizontalScrollBarVisibility(_fileList, ScrollBarVisibility.Disabled);
		_fileList.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
		{
			Setters = {
				new Setter(FocusableProperty, false),
			}
		});
		if (_listContext?.Items.Count > 0)
			_fileList.SelectedIndex = 0;

		Grid.SetRow(_fileList, 1);
		mainGrid.Children.Add(_fileList);

		return wrapperGrid;
	}

	private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
			case Key.Down:
				MoveSelection(1);
				e.Handled = true;
				break;
			case Key.Up:
				MoveSelection(-1);
				e.Handled = true;
				break;
			case Key.Enter:
				CommitSelection();
				e.Handled = true;
				break;
		}
	}

	private void MoveSelection(int delta)
	{
		if (_fileList is null || _fileList.ItemCount == 0) return;
		int count = _fileList.ItemCount;
		int index = _fileList.SelectedIndex < 0 ? 0 : (_fileList.SelectedIndex + delta + count) % count;
		_fileList.SelectedIndex = index;
		_fileList.ScrollIntoView(index);
	}

	private void CommitSelection()
	{
		if (_fileList?.SelectedItem is string selected)
		{
			_listContext?.Selected = selected;
			_listContext?.Commit();
		}
		CloseControl(this);
	}

	private void Build()
	{
		Content = MainContainer();
	}

	protected override void CloseControl(object? sender)
	{
		base.CloseControl(Context.BigInputPromptContext);
		Context.BigInputPromptContext = null;
	}

	protected override void OnActivated()
	{
		_textBox = new();
		_textBox.Margin = new(15);
		_textBox.TextChanged += TextBoxTextChanged;
		Build();
	}

	private void TextBoxTextChanged(object? sender, TextChangedEventArgs e)
	{
		var source = (TextBox)e.Source!;
		FilterList(source.Text ?? string.Empty);
	}

	private void FilterList(string? query)
	{
		if (_fileList is null || _listContext?.Items is null)
			return;
		var items = _listContext.Items;
		var filtered = items
			.Where(file => file.Contains(query, StringComparison.OrdinalIgnoreCase))
			.ToList();
		_fileList.ItemsSource = filtered;
		_fileList.SelectedIndex = filtered.Count > 0 ? 0 : -1;
		if (_fileList.SelectedIndex >= 0)
		{
			_fileList.ScrollIntoView(_fileList.SelectedIndex);
		}
	}
	protected override void OnDeactivated()
	{
		_fileList = null;
	}
}
