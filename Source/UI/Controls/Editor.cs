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
	private readonly double _caretWidth = 2;
	private readonly float _fontSize = 16;
	private readonly float _scrollBarDimension = 7;
	private List<Line> _lines = [new Line()];
	private Coordinate _caretPosition = new(0, 0);

	private Coordinate CaretPosition
	{
		get => _caretPosition;
		set
		{
			_caretPosition = value;
			EnsureCaretVisible();
		}
	}
	private readonly float _editorHorizontalMargin = 10F;
	private bool _caretVisible = true;
	private readonly DispatcherTimer _caretBlinkTimer;
	private readonly int _tabSize = 4;
	private readonly static string TAB_CHAR = "\t";
	private readonly static string NEW_LINE_CHAR = Environment.NewLine;
	private static IBrush _scollBarBrush = new SolidColorBrush(Color.Parse("#BFC9D1"), 0.5);

	//TODO: Optimize this
	private bool _canScrollX => GetDocumentWidth() > EditorAreaWidth;
	private bool _canScrollY => GetDocumentHeight() > EditorAreaHeight;

	//Bounds of the actual text and only text area
	public Rect EditorArea =>
		new(
			new Point(_editorHorizontalMargin, 0),
			new Size(
				Bounds.Width - 2 * _editorHorizontalMargin - _scrollBarDimension,
				Bounds.Height - _scrollBarDimension)); private double _scrollXOffset = 0;

	private double _scrollYOffset = 0;
	private bool _pointerPressedOnHorizontalScrollbar = false;
	private bool _pointerPressedOnVerticalScrollbar;

	private double _scrollbarPointerStartX = 0;
	private double _scrollbarScrollStartX = 0;
	private double _scrollbarPointerStartY;
	private double _scrollbarScrollStartY;
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

	public double EditorAreaWidth => EditorArea.Width;
	public double EditorAreaHeight => EditorArea.Height;

	private void SetAvaloniaProperties()
	{
		Focusable = true;
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
		Cursor = new Cursor(StandardCursorType.Ibeam);
	}

	private void EnsureCaretVisible()
	{
		var pos = GridToUICoord(_caretPosition);
		if (pos.X + _caretWidth > _scrollXOffset + EditorArea.Right)
		{
			_scrollXOffset =
				pos.X + _caretWidth - EditorArea.Right;
		}
		if (pos.X < _scrollXOffset + EditorArea.Left)
		{
			_scrollXOffset =
				pos.X - EditorArea.Left;
		}
		if (pos.Y + _lineHeight > _scrollYOffset + EditorArea.Bottom)
		{
			_scrollYOffset =
				pos.Y + _lineHeight - EditorArea.Bottom;
		}
		if (pos.Y < _scrollYOffset + EditorArea.Top)
		{
			_scrollYOffset =
				pos.Y - EditorArea.Top;
		}
		_scrollXOffset = Math.Clamp(
			_scrollXOffset,
			0,
			Math.Max(0, GetDocumentWidth() - EditorArea.Width));

		_scrollYOffset = Math.Clamp(
			_scrollYOffset,
			0,
			Math.Max(0, GetDocumentHeight() - EditorArea.Height));
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
		TryHittestScrollbars(e);
		base.OnPointerPressed(e);
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);

		if (!_canScrollY)
			return;

		var maxScrollOffset = Math.Max(
			0,
			GetDocumentHeight() - EditorAreaHeight);

		// Scale wheel "ticks" into pixels.
		var scrollAmount = 40.0;

		_scrollYOffset = Math.Clamp(
			_scrollYOffset - (e.Delta.Y * scrollAmount),
			0,
			maxScrollOffset);

		Redraw();

		e.Handled = true;
	}
	private void TryHittestScrollbars(PointerEventArgs e)
	{
		var point = e.GetPosition(this);

		if (_canScrollX &&
			GetHorizontalScrollbarRect().Contains(point))
		{
			e.Handled = true;

			_pointerPressedOnHorizontalScrollbar = true;
			_scrollbarPointerStartX = point.X;
			_scrollbarScrollStartX = _scrollXOffset;

			return;
		}

		if (_canScrollY &&
			GetVerticalScrollbarRect().Contains(point))
		{
			e.Handled = true;

			_pointerPressedOnVerticalScrollbar = true;
			_scrollbarPointerStartY = point.Y;
			_scrollbarScrollStartY = _scrollYOffset;

			return;
		}
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		TryMoveScrollbars(e);
	}

	private void TryMoveScrollbars(PointerEventArgs e)
	{
		var point = e.GetPosition(this);
		if (_pointerPressedOnHorizontalScrollbar)
		{
			var pointerDelta = point.X - _scrollbarPointerStartX;

			var documentWidth = GetDocumentWidth();
			var thumbRect = GetHorizontalScrollbarRect();

			var maxScrollOffset =
				Math.Max(0, documentWidth - EditorAreaWidth);

			var maxThumbOffset =
				EditorAreaWidth - thumbRect.Width;

			if (maxThumbOffset > 0)
			{
				var scrollDelta =
					pointerDelta / maxThumbOffset * maxScrollOffset;

				_scrollXOffset = Math.Clamp(
					_scrollbarScrollStartX + scrollDelta,
					0,
					maxScrollOffset);
			}

			Redraw();
			return;
		}

		if (_pointerPressedOnVerticalScrollbar)
		{
			var pointerDelta = point.Y - _scrollbarPointerStartY;

			var documentHeight = GetDocumentHeight();
			var thumbRect = GetVerticalScrollbarRect();

			var maxScrollOffset =
				Math.Max(0, documentHeight - EditorAreaHeight);

			var maxThumbOffset =
				EditorAreaHeight - thumbRect.Height;

			if (maxThumbOffset > 0)
			{
				var scrollDelta =
					pointerDelta / maxThumbOffset * maxScrollOffset;

				_scrollYOffset = Math.Clamp(
					_scrollbarScrollStartY + scrollDelta,
					0,
					maxScrollOffset);
			}

			Redraw();
		}
	}
	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		ResetPointerPressed();
	}

	private void ResetPointerPressed()
	{
		_pointerPressedOnHorizontalScrollbar = false;
		_pointerPressedOnVerticalScrollbar = false;
		Cursor = new Cursor(StandardCursorType.Ibeam);
	}

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		DrawMainRectangle(context);
		DrawCaret(context);
		DrawText(context);
		if (_canScrollX)
		{
			DrawHorizontalScrollbar(context);
		}
		if (_canScrollY)
		{
			DrawVerticalScrollbar(context);
		}
	}

	private void DrawVerticalScrollbar(DrawingContext context)
	{
		var rect = GetVerticalScrollbarRect();
		context.FillRectangle(_scollBarBrush, rect);
	}

	private void DrawHorizontalScrollbar(DrawingContext context)
	{
		var rect = GetHorizontalScrollbarRect();
		context.FillRectangle(_scollBarBrush, rect);
	}

	private Rect GetHorizontalScrollbarRect()
	{
		var height = _scrollBarDimension;
		var editorHeight = Bounds.Height;

		var documentWidth = GetDocumentWidth();

		var thumbWidth = documentWidth <= EditorAreaWidth
			? EditorAreaWidth
			: (EditorAreaWidth / documentWidth) * EditorAreaWidth;

		var maxScrollOffset = Math.Max(0, documentWidth - EditorAreaWidth);
		var maxThumbX = EditorAreaWidth - thumbWidth;

		var x = maxScrollOffset <= 0
			? 0
			: (_scrollXOffset / maxScrollOffset) * maxThumbX;

		var y = editorHeight - height;

		return new Rect(x, y, thumbWidth, height);
	}

	private double GetDocumentWidth()
	{
		var maxWidth = 0.0;

		for (int i = 0; i < _lines.Count; i++)
		{
			var line = _lines[i];

			if (line.Count == 0)
				continue;

			var x = GridToUICoord(new(line.Count - 1, i)).X
				  + _charAdvance;

			maxWidth = Math.Max(maxWidth, x);
		}

		return maxWidth;
	}

	private double GetDocumentHeight()
	{
		return _lines.Count * _lineHeight;
	}

	private Rect GetVerticalScrollbarRect()
	{
		var width = _scrollBarDimension;

		var documentHeight = GetDocumentHeight();

		var thumbHeight = documentHeight <= EditorAreaHeight
			? EditorAreaHeight
			: (EditorAreaHeight / documentHeight) * EditorAreaHeight;

		var maxScrollOffset = Math.Max(0, documentHeight - EditorAreaHeight);
		var maxThumbY = EditorAreaHeight - thumbHeight;

		var y = maxScrollOffset <= 0
			? 0
			: (_scrollYOffset / maxScrollOffset) * maxThumbY;

		var x = EditorArea.Right + _editorHorizontalMargin;

		return new Rect(x, y, width, thumbHeight);
	}
	//TODO: Optimize this
	private void DrawText(DrawingContext context)
	{
		using var clip = context.PushClip(EditorArea);
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
					var point = GridToUICoord(coord, glyph.S);
					point = point.WithX(point.X - _scrollXOffset);
					point = point.WithY(point.Y - _scrollYOffset);
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
		using var clip = context.PushClip(EditorArea);
		if (!_caretVisible) return;
		var point = GridToUICoord(CaretPosition);
		point = point.WithX(point.X - _scrollXOffset);
		point = point.WithY(point.Y - _scrollYOffset);
		var caretRect = new Rect(
			point,
			new Size(_caretWidth, _lineHeight));
		context.FillRectangle(Brushes.White, caretRect);
	}

	private Point GridToUICoord(Coordinate coord, string? p = null)
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
			visualCol * _charAdvance + EditorArea.X,
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

	private void AdvanceCaretCol(int count)
	{
		CaretPosition = new(
			CaretPosition.Col + count,
			CaretPosition.Line);
	}
	private void Backspace()
	{
		var lineIndex = CaretPosition.Line;
		var col = CaretPosition.Col;

		var line = _lines[lineIndex];

		if (col > 0)
		{
			line.RemoveAt(col - 1);
			AdvanceCaretCol(-1);
		}
		if (col == 0 && lineIndex > 0)
		{
			var previousLine = _lines[lineIndex - 1];
			var previousLength = previousLine.Count;
			previousLine.AddRange(line);
			_lines.RemoveAt(lineIndex);
			CaretPosition = new(previousLength, lineIndex - 1);
		}
		HoldCaretAndRedraw();
	}
	private void CaretLeft()
	{
		if (CaretPosition.Col == 0)
		{
			if (CaretPosition.Line == 0)
				return;

			CaretPosition = new(
				_lines[CaretPosition.Line - 1].Count,
				CaretPosition.Line - 1);

			HoldCaretAndRedraw();
			return;
		}

		AdvanceCaretCol(-1);
		HoldCaretAndRedraw();
	}

	private void CaretRight()
	{
		var line = _lines[CaretPosition.Line];

		if (CaretPosition.Col < line.Count)
		{
			AdvanceCaretCol(1);
			HoldCaretAndRedraw();
			return;
		}

		if (CaretPosition.Line == _lines.Count - 1)
			return;

		CaretPosition = new(0, CaretPosition.Line + 1);
		HoldCaretAndRedraw();
	}

	private void CaretUp()
	{
		if (CaretPosition.Line == 0)
			return;

		var line = _lines[CaretPosition.Line - 1];

		CaretPosition = new(
			Math.Min(CaretPosition.Col, line.Count),
			CaretPosition.Line - 1);

		HoldCaretAndRedraw();
	}

	private void CaretDown()
	{
		if (CaretPosition.Line == _lines.Count - 1)
			return;

		var line = _lines[CaretPosition.Line + 1];

		CaretPosition = new(
			Math.Min(CaretPosition.Col, line.Count),
			CaretPosition.Line + 1);

		HoldCaretAndRedraw();
	}

	private void InsertNewLine()
	{
		var lineIndex = CaretPosition.Line;
		var col = CaretPosition.Col;
		var line = _lines[lineIndex];
		var rightSplit = line.GetRange(col, line.Count - col);
		line.RemoveRange(col, line.Count - col);
		_lines.Insert(lineIndex + 1, rightSplit);
		CaretPosition = new(0, lineIndex + 1);

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
		var line = _lines[CaretPosition.Line];
		line.Insert(CaretPosition.Col, new Glyph(data));
		AdvanceCaretCol(1);
		HoldCaretAndRedraw();
	}
}

readonly record struct Glyph(string S);

struct Coordinate(int Col, int Line)
{
	public int Col { get; private set; } = Col;
	public int Line { get; private set; } = Line;

}

