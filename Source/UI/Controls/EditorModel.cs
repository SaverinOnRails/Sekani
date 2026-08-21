global using Line = System.Collections.Generic.List<SekaniUI.Controls.Glyph>;
using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
namespace SekaniUI.Controls;

public class EditorModel(EditorMetrics metrics)
{
	public EditorMetrics Metrics { get; init; } = metrics;
	public List<Line> Lines = [[]];
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

	public Point GridToUICoord(Coordinate coord)
	{
		var line = Lines[coord.Line];
		int visualCol = 0;
		for (int i = 0; i < coord.Col; i++)
		{
			var glyph = line[i];

			if (glyph.S == TAB_CHAR)
				visualCol += Metrics.TabSize - (visualCol % Metrics.TabSize);
			else
				visualCol++;
		}
		return new(
			visualCol * Metrics.CharAdvance + EditorArea.Left,
			coord.Line * Metrics.LineHeight);
	}

	public void HandleTextInput(string? text)
	{
		if (string.IsNullOrEmpty(text)) return;
		Span<byte> buffer = stackalloc byte[4];
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
		line.Insert(CaretPosition.Col, new Glyph(data));
		AdvanceCaretCol(1);
	}

	public void InsertNewLine()
	{
		var lineIndex = CaretPosition.Line;
		var col = CaretPosition.Col;
		var line = Lines[lineIndex];
		var rightSplit = line.GetRange(col, line.Count - col);
		line.RemoveRange(col, line.Count - col);
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
				Lines[CaretPosition.Line - 1].Count,
				CaretPosition.Line - 1);

			return;
		}
		AdvanceCaretCol(-1);
	}

	public void CaretRight()
	{
		var line = Lines[CaretPosition.Line];

		if (CaretPosition.Col < line.Count)
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
			Math.Min(CaretPosition.Col, line.Count),
			CaretPosition.Line - 1);
	}

	public void CaretDown()
	{
		if (CaretPosition.Line == Lines.Count - 1)
			return;

		var line = Lines[CaretPosition.Line + 1];

		CaretPosition = new(
			Math.Min(CaretPosition.Col, line.Count),
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
			var previousLength = previousLine.Count;
			previousLine.AddRange(line);
			Lines.RemoveAt(lineIndex);
			CaretPosition = new(previousLength, lineIndex - 1);
		}
	}
}

public readonly record struct EditorMetrics(
	float CharAdvance,
	float LineHeight,
	int TabSize);

public readonly record struct Glyph(string S);

public struct Coordinate(int Col, int Line)
{
	public int Col { get; private set; } = Col;
	public int Line { get; private set; } = Line;

}
