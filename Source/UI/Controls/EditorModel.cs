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

	//Bounds of the actual textarea
	public Rect EditorArea { get; set; }

	public Coordinate LogicalToVisual(Coordinate coord)
	{
		var line = Lines[coord.Line];
		int visualCol = line.VisualColumnAt(coord.Col);
		return new Coordinate(
			visualCol,
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
	}

	public void CaretUp()
	{
		if (CaretPosition.Line == 0)
			return;

		var line = Lines[CaretPosition.Line - 1];

		CaretPosition = new(
			Math.Min(CaretPosition.Col, line.GraphemeCount),
			CaretPosition.Line - 1);
	}

	public void CaretDown()
	{
		if (CaretPosition.Line == Lines.Count - 1)
			return;

		var line = Lines[CaretPosition.Line + 1];

		CaretPosition = new(
			Math.Min(CaretPosition.Col, line.GraphemeCount),
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

	public Grapheme(string data)
	{
		Data = data;
	}
}

public sealed class Line(int tabSize)
{
	private readonly List<Grapheme> _graphemes = [];

	public IReadOnlyList<Grapheme> Graphemes => _graphemes;

	public int VisualLength { get; private set; }

	public int GraphemeCount => Graphemes.Count;

	public int TabSize { get; init; } = tabSize;


	public void SetVisualLength()
	{
		int visualCol = 0;

		for (int i = 0; i < GraphemeCount; i++)
		{
			var grapheme = Graphemes[i];

			grapheme.VisualColumn = visualCol;

			visualCol += grapheme.Data == EditorModel.TAB_CHAR
				? VisualTabWidth(visualCol, TabSize)
				: 1;
		}
		VisualLength = visualCol;
	}

	public static int VisualTabWidth(int visualCol, int tabSize)
		=> tabSize - (visualCol % tabSize);


	public int VisualColumnAt(int logicalCol)
	{
		return logicalCol == GraphemeCount //this can also try to get the caret position
			? VisualLength
			: Graphemes[logicalCol].VisualColumn;
	}

	public void Insert(int index, Grapheme grapheme)
	{
		_graphemes.Insert(index, grapheme);
		SetVisualLength();
	}

	public void Add(Grapheme grapheme)
	{
		_graphemes.Add(grapheme);
		SetVisualLength();
	}

	public void RemoveAt(int index)
	{
		_graphemes.RemoveAt(index);
		SetVisualLength();
	}

	public void RemoveRange(int index, int count)
	{
		_graphemes.RemoveRange(index, count);
		SetVisualLength();
	}

	public List<Grapheme> GetRange(int index, int count)
	{
		return _graphemes.GetRange(index, count);
	}
	public void AddRange(IEnumerable<Grapheme> data)
	{
		_graphemes.AddRange(data);
	}
}
