using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Line = System.Collections.Generic.List<SekaniUI.Controls.Glyph>;

namespace SekaniUI.Controls;

public class Editor : Control
{
	private Typeface _editorFontFace = Typeface.Default;
	private float _charAdvance;
	private float _lineHeight;
	private readonly float _fontSize = 16;
	private List<Line> _lines = [new Line()];
	private Coordinate _caretPosition = new(0, 0);
	private readonly float _editorHorizontalMargin = 10F;
	private bool _caretVisible = true;
	private readonly DispatcherTimer _caretBlinkTimer;
	private readonly int _tabSize = 4;
	private readonly static string TAB_CHAR = "\t";
	private readonly static string NEW_LINE_CHAR = Environment.NewLine;
	public Editor()
	{
		SetAvaloniaProperties();
		MeasureFont();
		_caretBlinkTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
		TimeCaret();
	}

	private void TimeCaret()
	{
		_caretBlinkTimer.Tick += (_, _) =>
		{
			_caretVisible = !_caretVisible;
			Redraw();
		};
		_caretBlinkTimer.Start();
	}


	private void SetAvaloniaProperties()
	{
		Focusable = true;
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
		Cursor = new Cursor(StandardCursorType.Ibeam);
	}

	private void MeasureFont()
	{
		_editorFontFace = new Typeface("Cascadia Code");
		var text = new FormattedText(
			"#",
			CultureInfo.InvariantCulture,
			FlowDirection.LeftToRight,
			_editorFontFace,
			_fontSize,
			Brushes.White);

		_charAdvance = (float)text.WidthIncludingTrailingWhitespace;
		_lineHeight = (float)text.Height;
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		Focus();
	}
	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		Focus();
		base.OnPointerPressed(e);
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		DrawMainRectangle(context);
		DrawCaret(context);
		DrawText(context);
	}

	private void DrawText(DrawingContext context)
	{
		for (int l = 0; l < _lines.Count; l++)
		{
			for (int c = 0; c < _lines[l].Count; c++)
			{
				var glyph = _lines[l][c];
				if (glyph.S == TAB_CHAR) continue;
				else
				{
					var text = new FormattedText(
						glyph.S,
						CultureInfo.InvariantCulture,
						FlowDirection.LeftToRight,
						_editorFontFace,
						_fontSize,
						Brushes.White);
					Coordinate coord = new(c, l);
					var point = TextGridToUICoord(coord, glyph.S);
					context.DrawText(text, point);
				}
			}
		}
	}

	private void ResetCaretBlink()
	{
		_caretVisible = true;
		_caretBlinkTimer.Stop();
		_caretBlinkTimer.Start();
	}
	private void DrawMainRectangle(DrawingContext context)
	{
		context.DrawRectangle(Brush.Parse("#1D2128"), new Pen(Brushes.Black, 1), Bounds);
	}

	private void Redraw() => InvalidateVisual();

	private void DrawCaret(DrawingContext context)
	{
		if (!_caretVisible) return;
		var point = TextGridToUICoord(_caretPosition);
		var caretRect = new Rect(
			point,
			new Size(2, _lineHeight));
		context.FillRectangle(Brushes.White, caretRect);
	}

	private Point TextGridToUICoord(Coordinate coord, string? p = null)
	{
		var line = _lines[coord.Line];
		int visualCol = 0;
		for (int i = 0; i < coord.Col; i++)
		{
			var glyph = line[i];

			if (glyph.S == TAB_CHAR)
				visualCol += _tabSize - (visualCol % _tabSize);
			else
				visualCol++;
		}
		return new(
			visualCol * _charAdvance + _editorHorizontalMargin,
			coord.Line * _lineHeight);
	}
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		HandleKeyInput(e);
	}

	private void HandleKeyInput(KeyEventArgs e)
	{
		if (e.KeyModifiers != KeyModifiers.None)
		{
			HandleKeyWithModifier(); return;
		}
		switch (e.Key)
		{
			case Key.Tab:
				InsertTab();
				break;
			case Key.Return:
				InsertNewLine();
				break;
			case Key.Up:
				CaretUp();
				break;
			case Key.Left:
				CaretLeft();
				break;
			case Key.Right:
				CaretRight();
				break;
			case Key.Down:
				CaretDown();
				break;
			case Key.Back:
				Backspace();
				break;

		}
	}

	private void Backspace()
	{
		var lineIndex = _caretPosition.Line;
		var col = _caretPosition.Col;

		var line = _lines[lineIndex];

		if (col > 0)
		{
			line.RemoveAt(col - 1);
			_caretPosition.AdvanceCol(-1);
		}
		if (col == 0 && lineIndex > 0)
		{
			var previousLine = _lines[lineIndex - 1];
			var previousLength = previousLine.Count;
			previousLine.AddRange(line);
			_lines.RemoveAt(lineIndex);
			_caretPosition = new(previousLength, lineIndex - 1);
		}
		HoldCaretAndRedraw();
	}
	private void CaretLeft()
	{
		if (_caretPosition.Col == 0)
		{
			if (_caretPosition.Line == 0)
				return;

			_caretPosition = new(
				_lines[_caretPosition.Line - 1].Count,
				_caretPosition.Line - 1);

			HoldCaretAndRedraw();
			return;
		}

		_caretPosition.AdvanceCol(-1);
		HoldCaretAndRedraw();
	}

	private void CaretRight()
	{
		var line = _lines[_caretPosition.Line];

		if (_caretPosition.Col < line.Count)
		{
			_caretPosition.AdvanceCol(1);
			HoldCaretAndRedraw();
			return;
		}

		if (_caretPosition.Line == _lines.Count - 1)
			return;

		_caretPosition = new(0, _caretPosition.Line + 1);
		HoldCaretAndRedraw();
	}

	private void CaretUp()
	{
		if (_caretPosition.Line == 0)
			return;

		var line = _lines[_caretPosition.Line - 1];

		_caretPosition = new(
			Math.Min(_caretPosition.Col, line.Count),
			_caretPosition.Line - 1);

		HoldCaretAndRedraw();
	}

	private void CaretDown()
	{
		if (_caretPosition.Line == _lines.Count - 1)
			return;

		var line = _lines[_caretPosition.Line + 1];

		_caretPosition = new(
			Math.Min(_caretPosition.Col, line.Count),
			_caretPosition.Line + 1);

		HoldCaretAndRedraw();
	}

	private void InsertNewLine()
	{
		var lineIndex = _caretPosition.Line;
		var col = _caretPosition.Col;
		var line = _lines[lineIndex];
		var rightSplit = line.GetRange(col, line.Count - col);
		line.RemoveRange(col, line.Count - col);
		_lines.Insert(lineIndex + 1, rightSplit);
		_caretPosition = new(0, lineIndex + 1);

		HoldCaretAndRedraw();
	}
	private void HoldCaretAndRedraw()
	{
		ResetCaretBlink();
		Redraw();
	}

	private void InsertTab()
	{
		HandleTextInput(TAB_CHAR);
	}

	private void HandleKeyWithModifier()
	{
		// throw new NotImplementedException();
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		base.OnTextInput(e);
		HandleTextInput(e.Text);
	}

	private void HandleTextInput(string? text)
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
		var line = _lines[_caretPosition.Line];
		line.Insert(_caretPosition.Col, new Glyph(data));
		_caretPosition.AdvanceCol();
		HoldCaretAndRedraw();
	}
}

readonly record struct Glyph(string S);

struct Coordinate(int Col, int Line)
{
	public int Col { get; private set; } = Col;
	public int Line { get; private set; } = Line;

	public void AdvanceCol(int count = 1)
	{
		Col += count;
	}
}

