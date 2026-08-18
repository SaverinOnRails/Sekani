using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Line = System.Collections.Generic.List<SekaniUI.Controls.Glyph>;

namespace SekaniUI.Controls;

public class MainTextArea : Control
{
	private Typeface _editorFontFace = Typeface.Default;
	private float _charAdvance;
	private float _lineHeight;
	private readonly float _fontSize = 14;
	private List<Line> _lines = [new Line()];
	private Coordinate _caretPosition = new(0, 0);
	private readonly float _editorHorizontalMargin = 10F;
	private bool _caretVisible = true;
	private readonly DispatcherTimer _caretBlinkTimer;
	private readonly int _tabSize = 4;
	private readonly static string TAB_CHAR = "\t";
	private readonly static string NEW_LINE_CHAR = Environment.NewLine;
	public MainTextArea()
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
		_editorFontFace = new Typeface("JetBrains Mono");
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
					var point = TextGridToUICoord(coord);
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

	private Point TextGridToUICoord(Coordinate coord)
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

		}
	}

	private void InsertNewLine()
	{
		var line = _lines.Last();
		_lines.Add(new());
		_caretPosition = new(0, _lines.Count - 1);
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
		var line = _lines.Last();
		var data = Encoding.UTF8.GetString(text);
		_caretPosition.AdvanceCol();
		line.Add(new Glyph(data));
		ResetCaretBlink();
		Redraw();
	}
}

readonly record struct Glyph(string S);

class Coordinate(int Col, int Line)
{
	public int Col { get; private set; } = Col;
	public int Line { get; private set; } = Line;

	public void AdvanceCol()
	{
		Col++;
	}
}

