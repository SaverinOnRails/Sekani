using System;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class Editor : Control
{
	private SekaniDocument _document;
	public EditorMetrics _editorMetrics;
	private Typeface _editorFontFace = Typeface.Default;
	private readonly float _fontSize = 15;
	private LineCache _lineCache;
	private const float _baseLineNumberWidth = 20;

	//TODO: This can change during normal editing operations like adding a new line that increasing this count, which indirectly invalidates the max width of wrapped characters
	private double LineNumberSectDisplayWidth =>
		_baseLineNumberWidth +
		Math.Max(0, _document.Lines.Count.ToString().Length - 1) * _editorMetrics.CharAdvance;
	private bool _drawLineNumbers = true;
	private double LineNumberSectWidth =>
		_drawLineNumbers ? LineNumberSectDisplayWidth : 0;
	private double _scrollXOffset = 0;
	private double _scrollYOffset = 0;
	private bool _useThickCursor => Mode == Mode.Normal;
	private double _caretWidth = 2;
	private readonly double _defaultCaretWidth = 2;
	private bool _pointerPressedOnHorizontalScrollbar = false;
	private bool _pointerPressedOnVerticalScrollbar;
	private bool _pointerPressedOnCoordinate = false;
	private double _scrollbarPointerStartX = 0;
	private double _scrollbarScrollStartX = 0;
	private bool _caretVisible = true;
	private double _scrollbarPointerStartY;
	private double _scrollbarScrollStartY;
	private bool _softWordWrap = true;
	private bool _canScrollX => !_softWordWrap && GetDocumentWidthInPixels() > EditorArea.Width;
	private bool _canScrollY => GetDocumentHeightInPixels() > EditorArea.Height;
	private static IBrush _scollBarBrush = new SolidColorBrush(Color.Parse("#BFC9D1"), 0.5);

	public static readonly StyledProperty<Mode> ModeProperty = AvaloniaProperty.Register<Editor, Mode>(nameof(Mode));
	public Mode Mode
	{
		get => GetValue(ModeProperty);
		set => SetValue(ModeProperty, value);
	}
	private readonly DispatcherTimer _caretBlinkTimer;
	private Rect LineNumbersSectRect =>
		new(
			new Point(0, 0),
			new Size(
			LineNumberSectWidth,
			EditorArea.Height
		));

	public Editor()
	{
		SetAvaloniaProperties();
		SetEditorMetrics();
		_document = new();
		// var file = File.ReadAllText("/home/noble/Projects/Sekani/Source/UI/Controls/Editor.cs");
		// var file = File.ReadAllText("/home/noble/Projects/ktexteditor/src/document/katedocument.cpp");
		// var file = File.ReadAllText("/home/noble/Documents/emacs/src/xdisp.c");
		var file = File.ReadAllText("/home/noble/longfile.text");
		_lineCache = _document.CreateLineCache(_editorMetrics.TabSize, _softWordWrap, 0);
		_document.TypeChars(file);
		_document.CaretPosition = new(0, 0);
		_caretBlinkTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
		TimeCaret();
	}

	private void TimeCaret()
	{
		_caretBlinkTimer.Tick += (_, _) =>
		{
			if (_useThickCursor) return;
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
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		HandleKeyInput(e);
	}

	private bool IsCaretVisibleVertical(
		out bool caretAboveViewport,
		out double correctiveDistance)
	{
		caretAboveViewport = false;
		correctiveDistance = 0;

		var coord = _document.CaretPosition;

		var line = _document.Lines[coord.Line];
		var lineLayout = _lineCache.GetOrCreate(line);
		if (lineLayout is null)
			return false;

		int caretVisualLine = lineLayout.GetVisualCoordinate(coord).Line;

		// Number of visual lines belonging to logical lines before the caret's line.
		int visualLinesBeforeCaret = _lineCache.VisualLinesPrefixSum(coord.Line);

		// Absolute visual-line position of the caret.
		int absoluteCaretVisualLine =
			visualLinesBeforeCaret + caretVisualLine;

		double caretY =
			absoluteCaretVisualLine * _editorMetrics.LineHeight;

		double caretBottom =
			caretY + _editorMetrics.LineHeight;

		double viewportTop = _scrollYOffset;
		double viewportBottom = _scrollYOffset + EditorArea.Height;

		// Caret is completely above the viewport.
		if (caretY < viewportTop)
		{
			caretAboveViewport = true;
			correctiveDistance = viewportTop - caretY;
			return false;
		}

		// Caret is completely below the viewport.
		if (caretBottom > viewportBottom)
		{
			caretAboveViewport = false;
			correctiveDistance = caretBottom - viewportBottom;
			return false;
		}

		// Entire visual line containing the caret is visible.
		return true;
	}
	private bool IsCaretVisibleHorizontal(
		out bool caretLeftOfViewport,
		out double correctiveDistance)
	{
		caretLeftOfViewport = false;
		correctiveDistance = 0;

		var coord = _document.CaretPosition;
		var layout = _lineCache.GetOrCreate(_document.Lines[coord.Line]);
		if (layout is null) return false;
		var visualCol = layout.GetVisualCoordinate(coord, _useThickCursor).Col;
		double caretX =
			visualCol * _editorMetrics.CharAdvance;

		double caretRight =
			caretX + _caretWidth;

		double viewportLeft = _scrollXOffset;
		double viewportRight = _scrollXOffset + EditorArea.Width;

		// Caret is completely to the left of the viewport.
		if (caretX < viewportLeft)
		{
			caretLeftOfViewport = true;
			correctiveDistance = viewportLeft - caretX;
			return false;
		}

		// Caret is completely to the right of the viewport.
		if (caretRight > viewportRight)
		{
			caretLeftOfViewport = false;
			correctiveDistance = caretRight - viewportRight;
			return false;
		}

		return true;
	}
	private void EnsureCaretVisible()
	{
		if (!IsCaretVisibleVertical(
			out bool caretAboveViewport,
			out double correctiveDistance))
		{
			if (caretAboveViewport)
				_scrollYOffset -= correctiveDistance;
			else
				_scrollYOffset += correctiveDistance;
		}

		if (!IsCaretVisibleHorizontal(
			  out bool caretLeftOfViewport,
			  out double xCorrectiveDistance))

		{
			if (caretLeftOfViewport)
				_scrollXOffset -= xCorrectiveDistance;
			else
				_scrollXOffset += xCorrectiveDistance;
		}
		CorrectScrollBarOffsetOnResize();
	}

	private void CorrectScrollBarOffsetOnResize()
	{
		_scrollXOffset = Math.Clamp(
			_scrollXOffset,
			0,
			Math.Max(0, GetDocumentWidthInPixels() - EditorArea.Width));

		_scrollYOffset = Math.Clamp(
			_scrollYOffset,
			0,
			Math.Max(0, GetDocumentHeightInPixels() - EditorArea.Height));
	}
	private void HandleKeyInput(KeyEventArgs e)
	{
		if (e.KeyModifiers != KeyModifiers.None)
		{
			return;
		}

		//these guys work in all modes
		switch (e.Key)
		{
			case Key.Up:
				_document.CaretUp();
				break;
			case Key.Left:
				_document.CaretLeft();
				break;
			case Key.Right:
				_document.CaretRight();
				break;
			case Key.Down:
				_document.CaretDown();
				break;
		}
		if (Mode == Mode.Insert)
		{
			switch (e.Key)
			{
				case Key.Tab:
					_document.TypeChars("\t");
					break;
				case Key.Return:
					_document.TypeChars(Environment.NewLine);
					break;
				case Key.Back:
					_document.Backspace(_document.CaretPosition);
					break;
				case Key.Escape:
					EnterNormalMode();
					break;
			}
		}
		if (Mode == Mode.Normal)
		{
			switch (e.Key)
			{
				case Key.I:
					EnterInsertMode();
					break;
				case Key.D:
					_document.DeleteSelection();
					break;
				case Key.K:
					_document.CaretUp();
					break;
				case Key.H:
					_document.CaretLeft();
					break;
				case Key.L:
					_document.CaretRight();
					break;
				case Key.J:
					_document.CaretDown();
					break;
				case Key.O:
					_document.AddNewLineUnderSelection();
					EnterInsertMode();
					break;
			}
			e.Handled = true;
		}
		HoldCaretAndRedraw();
		EnsureCaretVisible();
	}

	private void EnterNormalMode()
	{
		Mode = Mode.Normal;
	}

	private void EnterInsertMode()
	{
		Mode = Mode.Insert;
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		Focus();
	}

	private int MaxVisualColsPerLine => (int)(EditorArea.Width / _editorMetrics.CharAdvance);

	protected override async void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == BoundsProperty)
		{
			if (_softWordWrap)
			{
				_lineCache.SetMaxVisualColsForWrap(MaxVisualColsPerLine);
			}
			_lineCache.BuildVisualLinesIndexes();
			CorrectScrollBarOffsetOnResize();
		}
		base.OnPropertyChanged(e);
	}

	private void HoldCaretAndRedraw()
	{
		ResetCaretBlink();
		Redraw();
	}

	private void ResetCaretBlink()
	{
		_caretVisible = true;
		_caretBlinkTimer.Stop();
		_caretBlinkTimer.Start();
	}
	private void SetEditorMetrics()
	{
		_editorFontFace = new Typeface("Cascadia Code");
		var text = new FormattedText(
			"#",
			CultureInfo.InvariantCulture,
			FlowDirection.LeftToRight,
			_editorFontFace,
			_fontSize,
			Brushes.White);

		var charAdvance = (float)text.WidthIncludingTrailingWhitespace;
		var lineHeight = text.Height;
		var tabSize = 6;
		_editorMetrics = new(charAdvance, 17, tabSize);
	}

	private double GetDocumentWidthInPixels()
	{
		return VisualToUI(new(_lineCache.Width, 0)).X;
	}

	private double GetDocumentHeightInPixels()
	{
		return _lineCache.TotalVisualLines()
			* _editorMetrics.LineHeight;
	}
	private readonly float _editorHorizontalMargin = 10F;
	private readonly float _scrollBarDimension = 7;

	//Bounds of the actual textarea
	public Rect EditorArea =>
			new(
				new Point(_editorHorizontalMargin + LineNumberSectWidth, 0),
				new Size(
					Bounds.Width - 2 * _editorHorizontalMargin - _scrollBarDimension - LineNumberSectWidth,
					Bounds.Height - _scrollBarDimension));

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		DrawMainRectangle(context);
		DrawText(context);
		DrawLineNumbers(context);
		DrawSelection(context);
		DrawCaret(context);
		DrawHorizontalScrollbar(context);
		DrawVerticalScrollbar(context);
	}

	private void DrawSelection(DrawingContext context)
	{
		if (!_document.CaretPosition.HasRange()) return;
		//TODO: this will only work if range end is greater than the caret position
		using var clip = context.PushClip(EditorArea);
		var firstLine = _document.CaretPosition.Line;
		var lastLine = _document.CaretPosition.RangeEnd!.Line;
		var visualLinesBefore = _lineCache.VisualLinesPrefixSum(firstLine);
		var visualLineSum = visualLinesBefore;
		for (int i = firstLine; i <= lastLine; i++)
		{
			var lineLayout = _lineCache.GetOrCreate(_document.Lines[i]);
			if (lineLayout is null) continue;
			for (int j = 0; j < _lineCache.VisualLineCountAt(i); j++)
			{
				var startAtPixels = EditorArea.Left;
				var endOfVisualLine = lineLayout.VisualLines[j].VisualLength;
				var endAtPixels = endOfVisualLine * _editorMetrics.CharAdvance + EditorArea.Left;
				if (i == firstLine)
				{
					var startVisualCoord = lineLayout.GetVisualCoordinate(_document.CaretPosition, _useThickCursor);
					if (j < startVisualCoord.Line)
					{
						visualLineSum++;
						continue;
					}
					;
					if (j == startVisualCoord.Line)
					{
						startAtPixels = EditorArea.Left + startVisualCoord.Col * _editorMetrics.CharAdvance;
					}
				}
				if (i == lastLine)
				{
					var endVisualCoord = lineLayout.GetVisualCoordinate(_document.CaretPosition.RangeEnd, _useThickCursor);
					if (j > endVisualCoord.Line)
					{
						break;
					}
					if (j == endVisualCoord.Line)
					{
						endAtPixels = endVisualCoord.Col * _editorMetrics.CharAdvance + EditorArea.Left;
					}
				}
				var point = new Point(startAtPixels, visualLineSum * _editorMetrics.LineHeight - _scrollYOffset);
				var size = new Size(endAtPixels - startAtPixels, _editorMetrics.LineHeight);
				context.FillRectangle(new SolidColorBrush(Colors.Blue, 0.5), new Rect(point, size));
				visualLineSum ++;
			}
		}

	}

	private void DrawLineNumbers(DrawingContext context)
	{
		if (!_drawLineNumbers || LineNumberSectWidth == 0)
			return;
		using var clip = context.PushClip(LineNumbersSectRect);

		// gutter
		context.DrawLine(
			new Pen(new SolidColorBrush(Colors.White, 0.4)),
			new Point(LineNumbersSectRect.Right, 0),
			new Point(LineNumbersSectRect.Right, LineNumbersSectRect.Height));

		(int firstLogicalLine, int lastLogicalLine, double pixelOffset) = ComputeVisibleText();

		int visualLine = 0;
		lastLogicalLine += 5;
		for (int i = firstLogicalLine; i < lastLogicalLine; i++)
		{
			if (i >= _document.Lines.Count) return;
			var line = _document.Lines[i];
			var lineLayout = _lineCache.GetOrCreate(line);
			if (lineLayout is null)
				continue;

			var number = (i + 1).ToString();
			var ft = new FormattedText(
				number,
				CultureInfo.InvariantCulture,
				FlowDirection.LeftToRight,
				_editorFontFace,
				_fontSize,
				new SolidColorBrush(Colors.White, 0.4));
			var x = LineNumberSectWidth - ft.Width - 5;
			var point = new Point(
				x,
				visualLine * _editorMetrics.LineHeight - pixelOffset);
			context.DrawText(ft, point);
			visualLine += lineLayout.VisualLines.Count;
		}
	}
	private void DrawHorizontalScrollbar(DrawingContext context)
	{
		if (!_canScrollX) return;
		var rect = GetHorizontalScrollbarRect();
		context.FillRectangle(_scollBarBrush, rect);
	}

	private void DrawVerticalScrollbar(DrawingContext context)
	{
		if (!_canScrollY) return;
		var rect = GetVerticalScrollbarRect();
		context.FillRectangle(_scollBarBrush, rect);
	}

	private void DrawMainRectangle(DrawingContext context)
	{
		context.DrawRectangle(Brush.Parse("#092328"), new Pen(Brushes.Black, 1), Bounds);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		Focus();
		base.OnPointerPressed(e);
		TryHittestScrollbars(e, out bool didHitTestScrollBars);
		if (didHitTestScrollBars) return;
		SetCursorToMousePos(e);
	}
	private Coordinate? MousePosToCursor(Point mousePoint)
	{

		if (_lineCache is null)
			return null;
		int targetVisualLine = Math.Max(
			0,
			(int)((mousePoint.Y - EditorArea.Top + _scrollYOffset)
				/ _editorMetrics.LineHeight));

		int visualCol = Math.Max(
			0,
			(int)((mousePoint.X - EditorArea.Left + _scrollXOffset)
				/ _editorMetrics.CharAdvance));

		// Find the logical line containing targetVisualLine.
		int logicalLineIndex = _lineCache.FindByPrefixSum(targetVisualLine, out int visualLineSum);
		if (logicalLineIndex >= _document.Lines.Count) return null;
		var layout = _lineCache.GetOrCreate(
			_document.Lines[logicalLineIndex]);

		if (layout is null)
			return null;

		int localVisualLine =
			targetVisualLine - visualLineSum;

		var vpos = new VisualCoordinate(
			visualCol,
			localVisualLine);

		int logicalColumn =
			layout.GetLogicalColumn(vpos);
		return new Coordinate(logicalColumn, logicalLineIndex);
	}
	private void SetCursorToMousePos(PointerPressedEventArgs e)
	{
		var pos = MousePosToCursor(e.GetPosition(this));
		if (pos is null) return;
		_document.CaretPosition =
			pos;
		_caretVisible = true;
		_pointerPressedOnCoordinate = true;
		Redraw();
	}

	private void TryHittestScrollbars(PointerEventArgs e, out bool didHitTestScrollBars)
	{
		var point = e.GetPosition(this);

		if (_canScrollX &&
			GetHorizontalScrollbarRect().Contains(point))
		{
			e.Handled = true;

			_pointerPressedOnHorizontalScrollbar = true;
			_scrollbarPointerStartX = point.X;
			_scrollbarScrollStartX = _scrollXOffset;
			didHitTestScrollBars = true;
			return;
		}

		if (_canScrollY &&
			GetVerticalScrollbarRect().Contains(point))
		{
			e.Handled = true;

			_pointerPressedOnVerticalScrollbar = true;
			_scrollbarPointerStartY = point.Y;
			_scrollbarScrollStartY = _scrollYOffset;
			didHitTestScrollBars = true;
			return;
		}
		didHitTestScrollBars = false;
	}

	protected override void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		TryMoveScrollbars(e, out bool didMoveScrollBars);
		if (didMoveScrollBars) return;
		TryDoDragSelection(e);
	}

	private void TryDoDragSelection(PointerEventArgs e)
	{
		if (!_pointerPressedOnCoordinate) return;
		var pos = MousePosToCursor(e.GetPosition(this));
		if (pos is null) return;
		_document.CaretPosition.RangeEnd = pos;
		Redraw();
	}

	private void TryMoveScrollbars(PointerEventArgs e, out bool didMoveScrollBars)
	{
		var point = e.GetPosition(this);
		didMoveScrollBars = false;
		if (_pointerPressedOnHorizontalScrollbar)
		{
			var pointerDelta = point.X - _scrollbarPointerStartX;

			var documentWidth = GetDocumentWidthInPixels();
			var thumbRect = GetHorizontalScrollbarRect();

			var maxScrollOffset =
				Math.Max(0, documentWidth - EditorArea.Width);

			var maxThumbOffset =
				EditorArea.Width - thumbRect.Width;

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
			didMoveScrollBars = true;
			return;
		}

		if (_pointerPressedOnVerticalScrollbar)
		{
			var pointerDelta = point.Y - _scrollbarPointerStartY;

			var documentHeight = GetDocumentHeightInPixels();
			var thumbRect = GetVerticalScrollbarRect();

			var maxScrollOffset =
				Math.Max(0, documentHeight - EditorArea.Height);

			var maxThumbOffset =
				EditorArea.Height - thumbRect.Height;

			if (maxThumbOffset > 0)
			{
				var scrollDelta =
					pointerDelta / maxThumbOffset * maxScrollOffset;

				_scrollYOffset = Math.Clamp(
					_scrollbarScrollStartY + scrollDelta,
					0,
					maxScrollOffset);
			}
			didMoveScrollBars = true;
			Redraw();
		}
	}

	private void Redraw() => InvalidateVisual();


	protected override void OnPointerReleased(PointerReleasedEventArgs e)
	{
		ResetPointerPressed();
	}

	private void ResetPointerPressed()
	{
		_pointerPressedOnHorizontalScrollbar = false;
		_pointerPressedOnVerticalScrollbar = false;
		_pointerPressedOnCoordinate = false;
		Cursor = new Cursor(StandardCursorType.Ibeam);
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);

		if (!_canScrollY)
			return;

		var maxScrollOffset = Math.Max(
			0,
			GetDocumentHeightInPixels() - EditorArea.Height);

		var scrollAmount = 60.0;

		_scrollYOffset = Math.Clamp(
			_scrollYOffset - (e.Delta.Y * scrollAmount),
			0,
			maxScrollOffset);

		Redraw();

		e.Handled = true;
	}

	private (int firstLogicalLine, int lastLogicalLine, double pixelOffset) ComputeVisibleText()
	{
		double startAt = Math.Max(
			0,
			(_scrollYOffset / _editorMetrics.LineHeight));

		int targetVisualLine = (int)startAt;

		int firstLogicalLine =
			_lineCache.FindByPrefixSum(
				targetVisualLine,
				out int visualSum);
		double offset =
				(startAt - visualSum) * _editorMetrics.LineHeight;
		//worst case, no matter how it wraps we don't ever need to draw more than this amount so this isnt really the last visible logical line
		int lastLogicalLine = (int)(EditorArea.Height / _editorMetrics.LineHeight) + firstLogicalLine;
		return (firstLogicalLine, lastLogicalLine, offset);
	}
	private void DrawText(DrawingContext context)
	{
		using var clip = context.PushClip(EditorArea);
		(int firstLogicalLine, int lastLogicalLine, double pixelOffset) = ComputeVisibleText();
		int currentVisualLine = 0;

		//pre measure some lines above the viewport.
		int logicalLineToBeginCount = firstLogicalLine - 10;
		lastLogicalLine += 5;
		for (int i = logicalLineToBeginCount; i <= lastLogicalLine; i++)
		{
			if (i >= _document.Lines.Count) return;
			if (i < 0) continue;
			var line = _document.Lines[i];
			int oldCount = _lineCache.VisualLineCountAt(i);
			var lineLayout = _lineCache.GetOrCreate(line, i);
			if (lineLayout is null) return;
			//correct scroll behind
			if (i < firstLogicalLine)
			{
				int newCount = lineLayout.VisualLines.Count;
				if (newCount != oldCount)
				{
					if (i < firstLogicalLine)
						_scrollYOffset += (newCount - oldCount) * _editorMetrics.LineHeight;
				}
				continue;
			}
			for (int j = 0; j < lineLayout.VisualLines.Count; j++)
			{
				var visualLine = lineLayout.VisualLines[j];
				var text = lineLayout.VisualText.AsSpan(
					visualLine.VisualOffset,
					visualLine.VisualLength);
				var ft = new FormattedText(
					text.ToString(),
					CultureInfo.InvariantCulture,
					FlowDirection.LeftToRight,
					_editorFontFace,
					_fontSize,
					Brushes.White);
				Point point = VisualToUI(
					new VisualCoordinate(
						0,
						currentVisualLine));
				point = point.WithY(point.Y - pixelOffset);
				point = point.WithX(point.X - _scrollXOffset);
				context.DrawText(ft, point);
				currentVisualLine++;
			}
		}
	}
	private void DrawCaret(DrawingContext context)
	{
		using var clip = context.PushClip(EditorArea);
		if (!_caretVisible) return;

		var coord = _document.CaretPosition;
		(int firstLogicalLine, int lastLogicalLine, double pixelOffset) = ComputeVisibleText();
		if (coord.Line < firstLogicalLine || coord.Line > lastLogicalLine)
			return;
		int visualLine = 0;
		for (int i = firstLogicalLine; i < coord.Line; i++)
		{
			var lineLayout = _lineCache.GetOrCreate(_document.Lines[i], i);
			if (lineLayout is null) return;
			visualLine += lineLayout.VisualLines.Count;
		}

		var caretLineLayout = _lineCache.GetOrCreate(_document.Lines[coord.Line], coord.Line);
		if (caretLineLayout is null) return;

		//force trail visual line when drawing a thick cursor
		var localVisualCoord = caretLineLayout.GetVisualCoordinate(coord, _useThickCursor);
		visualLine += localVisualCoord.Line;

		var point = VisualToUI(new VisualCoordinate(localVisualCoord.Col, visualLine));
		point = point.WithY(point.Y - pixelOffset);
		point = point.WithX(point.X - _scrollXOffset);

		_caretWidth = Mode == Mode.Normal ? _editorMetrics.CharAdvance : _defaultCaretWidth;
		var caretRect = new Rect(point, new Size(_caretWidth, _editorMetrics.LineHeight));
		context.FillRectangle(Brushes.White, caretRect);
	}

	private Point VisualToUI(VisualCoordinate visualCoord)
	{
		return new(visualCoord.Col * _editorMetrics.CharAdvance + EditorArea.Left,
			visualCoord.Line * _editorMetrics.LineHeight
		);
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		base.OnTextInput(e);
		if (e.Text is null) return;
		if (Mode == Mode.Insert)
		{
			HandleInsertModeTextInput(e);
		}
		EnsureCaretVisible();
	}

	private void HandleInsertModeTextInput(TextInputEventArgs e)
	{
		if (e.Text is null) return;
		_document.TypeChars(e.Text);
		HoldCaretAndRedraw();
	}
	private Rect GetHorizontalScrollbarRect()
	{
		var height = _scrollBarDimension;
		var editorHeight = Bounds.Height;

		var documentWidth = GetDocumentWidthInPixels();

		var thumbWidth = documentWidth <= EditorArea.Width
			? EditorArea.Width
			: (EditorArea.Width / documentWidth) * EditorArea.Width;

		var maxScrollOffset = Math.Max(0, documentWidth - EditorArea.Width);
		var maxThumbX = EditorArea.Width - thumbWidth;

		var x = maxScrollOffset <= 0
			? 0
			: (_scrollXOffset / maxScrollOffset) * maxThumbX;

		var y = editorHeight - height;

		return new Rect(x + EditorArea.Left - _editorHorizontalMargin, y, thumbWidth + _editorHorizontalMargin, height);
	}

	private const double _minVerticalScrollbarHeight = 20;


	private Rect GetVerticalScrollbarRect()
	{
		var width = _scrollBarDimension;
		var documentHeight = GetDocumentHeightInPixels();

		var thumbHeight = documentHeight <= EditorArea.Height
			? EditorArea.Height
			: Math.Max(
				_minVerticalScrollbarHeight,
				(EditorArea.Height / documentHeight) * EditorArea.Height);

		thumbHeight = Math.Min(thumbHeight, EditorArea.Height);
		var maxScrollOffset = Math.Max(0, documentHeight - EditorArea.Height);
		var maxThumbY = EditorArea.Height - thumbHeight;

		var y = maxScrollOffset <= 0
			? 0
			: (_scrollYOffset / maxScrollOffset) * maxThumbY;

		var x = EditorArea.Right + _editorHorizontalMargin;

		return new Rect(x, y, width, thumbHeight);
	}
}


public readonly record struct EditorMetrics(
	double CharAdvance,
	double LineHeight,
	int TabSize);

