using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
namespace SekaniUI.Controls;

public sealed class EditorModel(EditorMetrics metrics)
{
	public EditorMetrics Metrics { get; init; } = metrics;
	public List<Line> Lines { get; } = [new Line(metrics.TabSize)];

	public readonly static string TAB_CHAR = "\t";
	public readonly static string NEW_LINE_CHAR = Environment.NewLine;

	private int _maxLastSetCaretVisualCol = 0;
	private Coordinate _caretPosition = new(0, 0);
	public Coordinate CaretPosition
	{
		get => _caretPosition;
		set
		{
			_caretPosition = value;
			// EnsureCaretVisible();
		}
	}

	public Coordinate LogicalToVisual(Coordinate coord)
	{
		var line = Lines[coord.Line];
		int visualCol = line.VisualColumnAt(coord.Col);
		return new Coordinate(
			visualCol,
			coord.Line);
	}

	//will not work for wrapping
	public Coordinate VisualToLogical(Coordinate coord, Line line)
	{
		if (coord.Col >= line.VisualLength)
			return new Coordinate(line.GraphemeCount, coord.Line);
		Console.WriteLine(coord.Col);
		Console.WriteLine(line.VisualToLogical[coord.Col]);
		for (int i = 0; i < line.VisualToLogical.Count; i++)
		{
			Console.WriteLine($"visual :{i} logical {line.VisualToLogical[i]}");
		}
		Console.WriteLine();
		return new Coordinate(
				   line.VisualToLogical[coord.Col],
				   coord.Line);
	}

	public void HandleTextInput(string? text)
	{
		if (string.IsNullOrEmpty(text)) return;
		Span<byte> buffer = stackalloc byte[4];
		//does not properly handle grapheme clusters yet
		foreach (var rune in text.EnumerateRunes())
		{
			int length = rune.EncodeToUtf8(buffer);
			InsertUTF8(buffer[..length]);
		}
	}

	private void InsertUTF8(ReadOnlySpan<byte> text)
	{
		var data = Encoding.UTF8.GetString(text);
		if (data == NEW_LINE_CHAR)
		{
			InsertNewLine();
			return;
		}
		var line = Lines[CaretPosition.Line];
		line.Insert(CaretPosition.Col, new Grapheme(data));
		AdvanceCaretCol(1);
	}

	public void InsertNewLine()
	{
		var lineIndex = CaretPosition.Line;
		var col = CaretPosition.Col;
		var line = Lines[lineIndex];
		var rightSplit = new Line(Metrics.TabSize);
		rightSplit.AddRange(
			line.GetRange(col, line.GraphemeCount - col));
		line.RemoveRange(col, line.GraphemeCount - col);
		Lines.Insert(lineIndex + 1, rightSplit);
		CaretPosition = new(0, lineIndex + 1);
	}

	public void AdvanceCaretCol(int count)
	{
		CaretPosition = new(
			CaretPosition.Col + count,
			CaretPosition.Line);
		_maxLastSetCaretVisualCol = LogicalToVisual(CaretPosition).Col;
	}

	public void InsertTab()
	{
		HandleTextInput(EditorModel.TAB_CHAR);
	}

	public void CaretLeft()
	{
		if (CaretPosition.Col == 0)
		{
			if (CaretPosition.Line == 0)
				return;

			CaretPosition = new(
				Lines[CaretPosition.Line - 1].GraphemeCount,
				CaretPosition.Line - 1);
			_maxLastSetCaretVisualCol = LogicalToVisual(CaretPosition).Col;

			return;
		}
		AdvanceCaretCol(-1);
	}

	public void CaretRight()
	{
		var line = Lines[CaretPosition.Line];

		if (CaretPosition.Col < line.GraphemeCount)
		{
			AdvanceCaretCol(1);
			return;
		}

		if (CaretPosition.Line == Lines.Count - 1)
			return;
		CaretPosition = new(0, CaretPosition.Line + 1);
		_maxLastSetCaretVisualCol = LogicalToVisual(CaretPosition).Col;
	}

	public void CaretUp()
	{
		if (CaretPosition.Line == 0)
			return;

		var line = Lines[CaretPosition.Line - 1];

		var logicalPos = VisualToLogical(new(_maxLastSetCaretVisualCol, 1), line);
		CaretPosition = new(
			logicalPos.Col,
			CaretPosition.Line - 1);
	}

	public void CaretDown()
	{
		if (CaretPosition.Line == Lines.Count - 1)
			return;

		var line = Lines[CaretPosition.Line + 1];

		var logicalPos = VisualToLogical(new(_maxLastSetCaretVisualCol, 1), line);
		CaretPosition = new(
			logicalPos.Col,
			CaretPosition.Line + 1);
	}

	public void Backspace()
	{
		var lineIndex = CaretPosition.Line;
		var col = CaretPosition.Col;

		var line = Lines[lineIndex];

		if (col > 0)
		{
			line.RemoveAt(col - 1);
			AdvanceCaretCol(-1);
		}
		if (col == 0 && lineIndex > 0)
		{
			var previousLine = Lines[lineIndex - 1];
			var previousLength = previousLine.GraphemeCount;
			previousLine.AddRange(line.Graphemes);
			Lines.RemoveAt(lineIndex);
			CaretPosition = new(previousLength, lineIndex - 1);
		}
	}
}

public readonly record struct EditorMetrics(
	float CharAdvance,
	float LineHeight,
	int TabSize);


public struct Coordinate(int Col, int Line)
{
	public int Col { get; private set; } = Col;
	public int Line { get; private set; } = Line;

}

public sealed class Grapheme
{
	public string Data { get; }

	public int VisualColumn { get; internal set; }

	public int VisualLine { get; internal set; } = 1;

	public Grapheme(string data)
	{
		Data = data;
	}
}

public sealed class Line(int tabSize)
{
	private readonly List<Grapheme> _graphemes = [];
	private readonly List<int> _visualToLogical = [];

	public IReadOnlyList<Grapheme> Graphemes => _graphemes;
	public IReadOnlyList<int> VisualToLogical => _visualToLogical;

	public int VisualLength { get; private set; }

	public string LineString { get; private set; } = "";

	public int GraphemeCount => Graphemes.Count;

	public int TabSize { get; init; } = tabSize;

	public void DoLayout()
	{
		var builder = new StringBuilder();
		_visualToLogical.Clear();
		int visualCol = 0;
		// Console.WriteLine(GraphemeCount);
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
				// Console.WriteLine(i);
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
		// Console.WriteLine();
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
