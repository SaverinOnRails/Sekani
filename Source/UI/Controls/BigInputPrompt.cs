using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class BigInputPrompt : FocusablePromptControl
{
	public BigInputPrompt()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
	}

	private BigInputPrompBufferPickerContext _bufferPickerContext => (Context.BigInputPromptContext as BigInputPrompBufferPickerContext)!;

	public Control MainContainer()
	{
		var wrapperGrid = new Grid() { ColumnDefinitions = new("25*,50*,25*") };

		var mainGrid = new Grid() { Height = 700, RowDefinitions = new("70, *") };
		mainGrid.Bind(Grid.BackgroundProperty,
			this.GetResourceObservable("BackgroundColor", obj => obj is Color c ? new SolidColorBrush(c, 0.5) : null));
		mainGrid.BackdropEffect = new BlurEffect() { Radius = 20 };
		Grid.SetColumn(mainGrid, 1);
		wrapperGrid.Children.Add(mainGrid);

		Grid.SetRow(_textBox, 0);
		mainGrid.Children.Add(_textBox);

		string[] dummyFiles =
		[
			"PieceTree.cs",
			"RedBlackTree.cs",
			"LineLayout.cs",
			"VisualLineTree.cs",
			"SyntaxHighlighter.cs",
			"SmoothScrollController.cs",
			"ThemeDictionaryLoader.cs",
			"BigInputPrompt.cs",
			"SmallInputPrompt.cs",
			"Program.cs"
		];

		var fileList = new ListBox
		{
			ItemsSource = dummyFiles,
			Margin = new Thickness(15, 0, 15, 15),
			Background = Brushes.Transparent,
			VerticalAlignment = VerticalAlignment.Stretch,
			ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock
			{
				Text = name,
				Padding = new Thickness(8, 6),
				VerticalAlignment = VerticalAlignment.Center
			})
		};

		fileList.SelectionChanged += (_, e) =>
		{
			if (fileList.SelectedItem is string selected)
			{
				// TODO: open the selected file
			}
		};

		Grid.SetRow(fileList, 1);
		mainGrid.Children.Add(fileList);

		return wrapperGrid;
	}
	private void Build()
	{
		Content = MainContainer();
	}

	protected override void CloseControl(object? sender)
	{
		base.CloseControl(Context.SmallInputPromptContext);
		Context.BigInputPromptContext = null;
	}

	protected override void OnActivated()
	{
		_textBox = new();
		_textBox.Margin = new(15);
		Build();
	}

	protected override void OnDeactivated()
	{
	}
}


