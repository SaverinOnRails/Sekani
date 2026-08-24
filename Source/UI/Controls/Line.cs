using System.Collections.Generic;
using System.Text;

namespace SekaniUI.Controls;

public sealed class Line(int tabSize)
{
	private readonly List<Grapheme> _graphemes = [];
	private readonly List<int> _visualToLogical = [];

	public IReadOnlyList<Grapheme> Graphemes => _graphemes;
	// public IReadOnlyList<int> VisualToLogical => _visualToLogical;

	public int VisualLength { get; private set; }

	public string LineString { get; private set; } = "";

	public int GraphemeCount => Graphemes.Count;

	public int TabSize { get; init; } = tabSize;

	public void DoLayout()
	{
		var builder = new StringBuilder();
		_visualToLogical.Clear();
		int visualCol = 0;
		for (int i = 0; i < GraphemeCount; i++)
		{
			var grapheme = Graphemes[i];
			grapheme.VisualColumn = visualCol;

			var tabWidth = grapheme.Data == EditorModel.TAB_CHAR
				? VisualTabWidth(visualCol, TabSize)
				: 1;

			//so we don't skip visual cols inside tabs
			for (int v = 0; v < tabWidth; v++)
			{
				_visualToLogical.Add(i);
			}
			if (grapheme.Data == EditorModel.TAB_CHAR)
			{
				builder.Append(' ', tabWidth);
			}
			else
			{
				builder.Append(grapheme.Data);
			}
			visualCol += tabWidth;
		}
		VisualLength = visualCol;
		LineString = builder.ToString();
	}

	//will not work for wrapping
	public Coordinate VisualToLogical(Coordinate coord)
	{
		if (coord.Col >= VisualLength)
			return new Coordinate(GraphemeCount, coord.Line);
		return new Coordinate(
				   _visualToLogical[coord.Col],
				   coord.Line);
	}

	public static int VisualTabWidth(int visualCol, int tabSize)
		=> tabSize - (visualCol % tabSize);

	public int VisualColumnAt(int logicalCol)
	{
		return logicalCol == GraphemeCount //this can also try to get the caret position which can appear after any characters
			? VisualLength
			: Graphemes[logicalCol].VisualColumn;
	}

	public void Insert(int index, Grapheme grapheme)
	{
		_graphemes.Insert(index, grapheme);
		LineChanged();
	}

	public void Add(Grapheme grapheme)
	{
		_graphemes.Add(grapheme);
		LineChanged();
	}

	public void RemoveAt(int index)
	{
		_graphemes.RemoveAt(index);
		LineChanged();
	}

	public void RemoveRange(int index, int count)
	{
		_graphemes.RemoveRange(index, count);
		LineChanged();
	}

	public List<Grapheme> GetRange(int index, int count)
	{
		return _graphemes.GetRange(index, count);
	}
	public void AddRange(IEnumerable<Grapheme> data)
	{
		_graphemes.AddRange(data);
		LineChanged();
	}

	private void LineChanged()
	{
		DoLayout();
	}
}
