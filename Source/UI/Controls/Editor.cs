using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class Editor : Control
{
	public EditorMetrics _editorMetrics;
	private Typeface _editorFontFace = Typeface.Default;
	private readonly float _fontSize = 15;
	private LineCache _lineCache;
	private
	const float _baseLineNumberWidth = 20;

	//TODO: This can change during normal editing operations like adding a new line that increasing this count, which indirectly invalidates the max width of wrapped characters
	private double LineNumberSectDisplayWidth =>
	  _baseLineNumberWidth +
	  Math.Max(0, Math.Max(Document.Lines.Count.ToString().Length - 1, 7)) * _editorMetrics.CharAdvance;

	private bool _drawLineNumbers = true;
	private double LineNumberSectWidth =>
	  _drawLineNumbers ? LineNumberSectDisplayWidth : 0;
	private double _scrollXOffset = 0;
	private double _scrollYOffset = 0;
	private double _targetScrollYOffset = 0;
	private bool _useThickCursor => Mode == Mode.Normal;
	private double _caretWidth = 2;
	private readonly double _defaultCaretWidth = 2;
	private bool _pointerPressedOnHorizontalScrollbar = false;
	private bool _pointerPressedOnVerticalScrollbar;
	private bool _shouldDrawCaretFocusBubble = false;
	private int _caretFocusBubbleRadius = 0;
	private double _caretFocusBubbleProgress = 0;
	private bool _pointerPressedOnCoordinate = false;
	private double _autoScrollMargin = 5;
	private double _scrollbarPointerStartX = 0;
	private double _scrollbarScrollStartX = 0;
	private bool _caretVisible = true;
	private double _scrollbarPointerStartY;
	private int _preferredDragScrollVisualColumn = 0;
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
	public static readonly StyledProperty<SekaniDocument> DocumentProperty = AvaloniaProperty.Register<Editor, SekaniDocument>(nameof(Document));
	public SekaniDocument Document
	{
		get => GetValue(DocumentProperty);
		set => SetValue(DocumentProperty, value);
	}
	private readonly DispatcherTimer _caretBlinkTimer;
	private DispatcherTimer? _autoDragScrollTimer;
	private DispatcherTimer? _caretFocusBubbleTimer;
	private bool _isSmoothScrolling;
	private int _firstVisibleLogicalLineForResizeRestore;

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
		_caretBlinkTimer = new()
		{
			Interval = TimeSpan.FromMilliseconds(700)
		};
		TimeCaret();
	}

	private void SetLineCache()
	{
		_lineCache = Document.CreateLineCache(_editorMetrics.TabSize, _softWordWrap, 0);
		Document.CaretPosition = new(0, 0);
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

		var coord = Document.CaretPosition;

		var line = Document.Lines[coord.Line];
		var lineLayout = _lineCache.GetOrCreate(line);
		if (lineLayout is null)
			return false;

		int caretVisualLine = lineLayout.GetVisualCoordinate(coord).VisualLine;

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

		var coord = Document.CaretPosition;
		var layout = _lineCache.GetOrCreate(Document.Lines[coord.Line]);
		if (layout is null) return false;
		var visualCol = layout.GetVisualCoordinate(coord, _useThickCursor).VisualCol;
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
	private void EnsureCaretVisible(bool ensureVertical = true, bool ensureHorizontal = true)
	{
		//reset the target
		_targetScrollYOffset = _scrollYOffset;
		var target = _targetScrollYOffset;
		if (!IsCaretVisibleVertical(
			out bool caretAboveViewport,
			out double correctiveDistance) && ensureVertical)
		{
			if (caretAboveViewport)
				_targetScrollYOffset -= correctiveDistance;
			else
				_targetScrollYOffset += correctiveDistance;
			//only do this when we are not drag selecting
			if (correctiveDistance > _editorMetrics.LineHeight && _autoDragScrollTimer is null) StartDrawCaretFocusBubble();
		}

		if (!IsCaretVisibleHorizontal(
			out bool caretLeftOfViewport,
			out double xCorrectiveDistance) && ensureHorizontal)

		{
			if (caretLeftOfViewport)
				_scrollXOffset -= xCorrectiveDistance;
			else
				_scrollXOffset += xCorrectiveDistance;
			if (xCorrectiveDistance > _editorMetrics.CharAdvance && _autoDragScrollTimer is null) StartDrawCaretFocusBubble();
		}
		TryStartSmoothScroll();
	}

	private void TryStartSmoothScroll()
	{
		if (_scrollYOffset == _targetScrollYOffset)
			return;
		if (_isSmoothScrolling)
			return;

		_isSmoothScrolling = true;
		RequestSmoothScrollFrame();
	}

	private void RequestSmoothScrollFrame()
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel is null)
		{
			_isSmoothScrolling = false;
			return;
		}
		topLevel.RequestAnimationFrame(_ => DoSmoothScroll());
	}

	private void DoSmoothScroll()
	{
		if (!_isSmoothScrolling)
			return;

		var distance = _targetScrollYOffset - _scrollYOffset;
		if (Math.Abs(distance) < 0.5)
		{
			_scrollYOffset = _targetScrollYOffset;
			CorrectScrollBarOffset();
			StopSmoothScroll();
			Redraw();
			return;
		}
		_scrollYOffset += distance * 0.2;
		Redraw();
		RequestSmoothScrollFrame();
	}

	private void StopSmoothScroll()
	{
		_isSmoothScrolling = false;
	}

	private void StartDrawCaretFocusBubble()
	{
		_caretFocusBubbleTimer?.Stop();
		_shouldDrawCaretFocusBubble = true;
		_caretFocusBubbleProgress = 0;
		_caretFocusBubbleTimer = new()
		{
			Interval = TimeSpan.FromMilliseconds(15),
		};
		_caretFocusBubbleTimer.Tick += (s, e) => Redraw();
		_caretFocusBubbleTimer.Start();
	}

	private void CorrectScrollBarOffset()
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
		bool handled = false;

		// These work in all modes
		switch (e.Key)
		{
			case Key.Up:
				Document.CaretUp();
				handled = true;
				break;

			case Key.Left:
				Document.CaretLeft();
				handled = true;
				break;

			case Key.Right:
				Document.CaretRight();
				handled = true;
				break;

			case Key.Down:
				Document.CaretDown();
				handled = true;
				break;
		}

		if (Mode == Mode.Insert)
		{
			switch (e.Key)
			{
				case Key.Tab:
					Document.TypeChars("\t");
					handled = true;
					break;

				case Key.Return:
					Document.TypeChars(Environment.NewLine);
					handled = true;
					break;

				case Key.Back:
					Document.BackSpace(Document.CaretPosition);
					handled = true;
					break;

				case Key.Escape:
					EnterNormalMode();
					HoldCaretAndRedraw();
					return;
			}
		}

		if (handled)
		{
			HoldCaretAndRedraw();
			EnsureCaretVisible();
		}
	}
	private void EnterCommandMode()
	{
		Mode = Mode.Command;
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

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == BoundsProperty)
		{
			SaveScrollAnchor();
			if (_softWordWrap)
			{
				_lineCache.SetMaxVisualColsForWrap(MaxVisualColsPerLine);
			}
			_lineCache.BuildVisualLinesIndexes();
			RestoreScrollAnchor();
			CorrectScrollBarOffset();
		}

		//TODO: When we have multiple editor windows, focus should return to the right one
		if (e.Property == ModeProperty)
		{
			var oldValue = (Mode)e.OldValue!;
			var newValue = (Mode)e.NewValue!;
			if (oldValue == Mode.Command && newValue == Mode.Normal)
			{
				Focus();
				_caretVisible = true;
				Redraw();
			}
		}
		if (e.Property == DocumentProperty)
		{
			SetLineCache();
		}
		base.OnPropertyChanged(e);
	}

	private void RestoreScrollAnchor()
	{
		var visualLinesBefore = _lineCache.VisualLinesPrefixSum(_firstVisibleLogicalLineForResizeRestore);
		_scrollYOffset = visualLinesBefore * _editorMetrics.LineHeight;
	}

	private void SaveScrollAnchor()
	{
		//Since visual lines will change, we need to retain the first visible logical line. 
		(int firstLogicalLine, int lastLogicalLine, double pixelOffset) = ComputeVisibleText();
		_firstVisibleLogicalLineForResizeRestore = firstLogicalLine;
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
		var lineHeight = text.Height * 1.25;
		var tabSize = 6;
		_editorMetrics = new(charAdvance, 17, tabSize);
	}

	private double GetDocumentWidthInPixels()
	{
		return VisualToUI(new(_lineCache.Width, 0)).X;
	}

	private double GetDocumentHeightInPixels()
	{
		return _lineCache.TotalVisualLines() *
		  _editorMetrics.LineHeight;
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
		DrawCaretFocusBubble(context);
		DrawHorizontalScrollbar(context);
		DrawVerticalScrollbar(context);
	}

	private void DrawCaretFocusBubble(DrawingContext context)
	{
		if (!_shouldDrawCaretFocusBubble)
			return;
		var caretRect = GetCaretRect();
		if (caretRect is null)
			return;
		_caretFocusBubbleProgress += 0.045;

		if (_caretFocusBubbleProgress >= 1)
		{
			_caretFocusBubbleTimer?.Stop();
			_caretFocusBubbleTimer = null;
			_shouldDrawCaretFocusBubble = false;
			return;
		}
		var t = _caretFocusBubbleProgress;
		// Ease out.
		var eased = 1 - Math.Pow(1 - t, 3);

		var radius = 6 + eased * 74;
		var opacity = 1 - t;

		var color = Color.FromArgb(
		  (byte)(255 * opacity),
		  0,
		  220,
		  255);
		context.DrawEllipse(
		  Brushes.Transparent,
		  new Pen(new SolidColorBrush(color), 4),
		  caretRect.Value.Position,
		  radius,
		  radius);
	}
	private void DrawSelection(DrawingContext context)
	{
		if (!Document.CaretPosition.HasRange()) return;
		using
		var clip = context.PushClip(EditorArea);
		var greaterRangeEnd = Document.CaretPosition.GreaterRangeEnd();
		var lesserRangeEnd = Document.CaretPosition.LesserRangeEnd();
		var firstLine = lesserRangeEnd!.Line;
		var lastLine = greaterRangeEnd!.Line;
		var visualLinesBefore = _lineCache.VisualLinesPrefixSum(firstLine);
		var visualLineSum = visualLinesBefore;
		for (int i = firstLine; i <= lastLine; i++)
		{
			var lineLayout = _lineCache.GetOrCreate(Document.Lines[i]);
			if (lineLayout is null) continue;
			for (int j = 0; j < _lineCache.VisualLineCountAt(i); j++)
			{
				var startAtPixels = EditorArea.Left;
				var endOfVisualLine = Math.Max(1, lineLayout.VisualLines[j].VisualLength);
				var endAtPixels = endOfVisualLine * _editorMetrics.CharAdvance + EditorArea.Left;
				if (i == firstLine)
				{
					var startVisualCoord = lineLayout.GetVisualCoordinate(lesserRangeEnd, _useThickCursor);
					if (j < startVisualCoord.VisualLine)
					{
						visualLineSum++;
						continue;
					}
					;
					if (j == startVisualCoord.VisualLine)
					{
						startAtPixels = EditorArea.Left + startVisualCoord.VisualCol * _editorMetrics.CharAdvance;
					}
				}
				if (i == lastLine)
				{
					var endVisualCoord = lineLayout.GetVisualCoordinate(greaterRangeEnd, _useThickCursor);
					if (j > endVisualCoord.VisualLine)
					{
						break;
					}
					if (j == endVisualCoord.VisualLine)
					{
						endAtPixels = endVisualCoord.VisualCol * _editorMetrics.CharAdvance + EditorArea.Left;
					}
				}
				var point = new Point(startAtPixels - _scrollXOffset, visualLineSum * _editorMetrics.LineHeight - _scrollYOffset);
				var size = new Size(endAtPixels - startAtPixels, _editorMetrics.LineHeight);
				context.FillRectangle(new SolidColorBrush(Colors.Blue, 0.5), new Rect(point, size));
				visualLineSum++;
			}
		}

	}

	private void DrawLineNumbers(DrawingContext context)
	{
		if (!_drawLineNumbers || LineNumberSectWidth == 0)
			return;
		using
		var clip = context.PushClip(LineNumbersSectRect);
		using var textOptions = context.PushTextOptions(new TextOptions
		{
			BaselinePixelAlignment = BaselinePixelAlignment.Unaligned, 
			TextHintingMode = TextHintingMode.None                     
		});

		// gutter
		context.DrawLine(
		  new Pen(new SolidColorBrush(Colors.White, 0.1)),
		  new Point(LineNumbersSectRect.Right, 0),
		  new Point(LineNumbersSectRect.Right, LineNumbersSectRect.Height));

		(int firstLogicalLine, int lastLogicalLine, double pixelOffset) = ComputeVisibleText();

		int visualLine = 0;
		lastLogicalLine += 5;
		for (int i = firstLogicalLine; i < lastLogicalLine; i++)
		{
			if (i >= Document.Lines.Count) return;
			var line = Document.Lines[i];
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
		context.DrawRectangle(Brush.Parse("#121212"), new Pen(Brushes.Black, 1), Bounds);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		Focus();
		StopSmoothScroll();
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
		  (int)((mousePoint.Y - EditorArea.Top + _scrollYOffset) /
			_editorMetrics.LineHeight));

		int visualCol = Math.Max(
		  0,
		  (int)((mousePoint.X - EditorArea.Left + _scrollXOffset) /
			_editorMetrics.CharAdvance));

		// Find the logical line containing targetVisualLine.
		int logicalLineIndex = _lineCache.FindByPrefixSum(targetVisualLine, out int visualLineSum);
		if (logicalLineIndex >= Document.Lines.Count) return null;
		var layout = _lineCache.GetOrCreate(
		  Document.Lines[logicalLineIndex]);

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
		Document.CaretPosition =
		  pos;
		Document.UpdatePreferredVisualColumn();
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
		TryDoAutoScrollForDragSelection(e);
	}

	private void TryDoAutoScrollForDragSelection(PointerEventArgs e)
	{
		if (!_pointerPressedOnCoordinate) return;
		var point = e.GetPosition(this);
		if (point.Y < EditorArea.Top + _autoScrollMargin || point.Y > EditorArea.Bottom - _autoScrollMargin)
		{
			StartAutoDragScrollTimer(e);
			return;
		}
		_autoDragScrollTimer?.Stop();
		_autoDragScrollTimer = null;
	}

	private void StartAutoDragScrollTimer(PointerEventArgs e)
	{
		//if already dragging do nothing
		if (_autoDragScrollTimer is not null) return;

		var point = e.GetPosition(this);
		_autoDragScrollTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(16)
		};

		_autoDragScrollTimer.Tick += (_, _) =>
		{
			if (point.Y < EditorArea.Top + _autoScrollMargin)
			{
				//this is rough but just check if we can move the caret up vertically
				var currentLine = Document.Lines[Document.CaretPosition.Line];
				var layout = _lineCache.GetOrCreate(currentLine);
				if (layout is null) return;

				var posInLine = layout.GetVisualCoordinate(Document.CaretPosition);

				if (layout.VisualLines.Count > 1)
				{
					if (posInLine.VisualLine != 0)
					{
						var newVisualCoord = new VisualCoordinate(
						  _preferredDragScrollVisualColumn,
						  posInLine.VisualLine - 1);

						var newLogicalCol = layout.GetLogicalColumn(newVisualCoord);

						Document.CaretPosition.SetCoord(
						  newLogicalCol,
						  Document.CaretPosition.Line);

						EnsureCaretVisible();
						Redraw();
						return;
					}
				}

				if (Document.CaretPosition.Line == 0) return;

				var layoutOfPrevLine = _lineCache.GetOrCreate(
				  Document.Lines[Document.CaretPosition.Line - 1]);

				if (layoutOfPrevLine is null) return;

				var lastVisualLineOfPrevLine =
				  layoutOfPrevLine.VisualLines.Count - 1;

				var newVisualCoordOfLastLine = new VisualCoordinate(
				  _preferredDragScrollVisualColumn,
				  lastVisualLineOfPrevLine);

				var newLogicalColOfLastLine =
				  layoutOfPrevLine.GetLogicalColumn(
					newVisualCoordOfLastLine);

				Document.CaretPosition.SetCoord(
				  newLogicalColOfLastLine,
				  Document.CaretPosition.Line - 1);

				EnsureCaretVisible();
				Redraw();
			}

			if (point.Y > EditorArea.Bottom - _autoScrollMargin)
			{
				var currentLine = Document.Lines[Document.CaretPosition.Line];
				var layout = _lineCache.GetOrCreate(currentLine);
				if (layout is null) return;

				var posInLine = layout.GetVisualCoordinate(Document.CaretPosition);

				if (layout.VisualLines.Count > 1)
				{
					if (posInLine.VisualLine < layout.VisualLines.Count - 1)
					{
						var newVisualCoord = new VisualCoordinate(
						  _preferredDragScrollVisualColumn,
						  posInLine.VisualLine + 1);

						var newLogicalCol = layout.GetLogicalColumn(
						  newVisualCoord);

						Document.CaretPosition.SetCoord(
						  newLogicalCol,
						  Document.CaretPosition.Line);

						EnsureCaretVisible();
						Redraw();
						return;
					}
				}

				if (Document.CaretPosition.Line >= Document.Lines.Count - 1)
					return;

				var layoutOfNextLine = _lineCache.GetOrCreate(
				  Document.Lines[Document.CaretPosition.Line + 1]);

				if (layoutOfNextLine is null) return;

				var newVisualCoordOfFirstLine = new VisualCoordinate(
				  _preferredDragScrollVisualColumn,
				  0);

				var newLogicalColOfFirstLine =
				  layoutOfNextLine.GetLogicalColumn(
					newVisualCoordOfFirstLine);

				Document.CaretPosition.SetCoord(
				  newLogicalColOfFirstLine,
				  Document.CaretPosition.Line + 1);

				EnsureCaretVisible();
				Redraw();
			}
		};

		_autoDragScrollTimer.Start();
	}
	private void TryDoDragSelection(PointerEventArgs e)
	{
		if (!_pointerPressedOnCoordinate) return;
		EnsureCaretVisible(ensureHorizontal: true, ensureVertical: false);
		var pos = MousePosToCursor(e.GetPosition(this));
		if (pos is null) return;
		if (Document.CaretPosition.RangeEnd is null)
		{
			Document.CaretPosition.RangeEnd = new(Document.CaretPosition.Col, Document.CaretPosition.Line);
		}

		Document.CaretPosition.SetCoord(pos.Col, pos.Line);
		var line = Document.Lines[Document.CaretPosition.Line];
		var layout = _lineCache.GetOrCreate(line);
		if (layout is null) return;
		_preferredDragScrollVisualColumn = layout.GetVisualCoordinate(Document.CaretPosition).VisualCol;
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
		_autoDragScrollTimer?.Stop();
		_autoDragScrollTimer = null;
		Cursor = new Cursor(StandardCursorType.Ibeam);
	}

	protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		StopSmoothScroll();
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
		using
		var clip = context.PushClip(EditorArea);
		using var textOptions = context.PushTextOptions(new TextOptions
		{
			BaselinePixelAlignment = BaselinePixelAlignment.Unaligned, 
			TextHintingMode = TextHintingMode.None                     
		});
		(int firstLogicalLine, int lastPossibleLogicalLine, double pixelOffset) = ComputeVisibleText();
		int currentVisualLine = 0;

		//pre measure some lines above the viewport.
		int logicalLineToBeginCount = firstLogicalLine - 2;
		lastPossibleLogicalLine += 5;
		for (int i = logicalLineToBeginCount; i <= lastPossibleLogicalLine; i++)
		{
			if (i >= Document.Lines.Count) return;
			if (i < 0) continue;
			var line = Document.Lines[i];
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
				  Brush.Parse("#F3E6D5"));
				Point point = VisualToUI(
				  new VisualCoordinate(
					0,
					currentVisualLine));
				var y = point.Y - pixelOffset;
				var x = point.X - _scrollXOffset;
				point = point.WithY(y);
				point = point.WithX(x);
				currentVisualLine++;
				//true check whether we should draw this line
				if (y + _editorMetrics.LineHeight < EditorArea.Top) continue; //is bottom in viewport?
				if (y > EditorArea.Bottom) return;
				context.DrawText(ft, point);
			}
		}
	}

	private Rect? GetCaretRect()
	{
		if (!_caretVisible || !IsFocused) return null;

		var coord = Document.CaretPosition;
		(int firstLogicalLine, int lastLogicalLine, double pixelOffset) = ComputeVisibleText();
		if (coord.Line < firstLogicalLine || coord.Line > lastLogicalLine)
			return null;
		int visualLine = 0;
		for (int i = firstLogicalLine; i < coord.Line; i++)
		{
			var lineLayout = _lineCache.GetOrCreate(Document.Lines[i], i);
			if (lineLayout is null) return null;
			visualLine += lineLayout.VisualLines.Count;
		}

		var caretLineLayout = _lineCache.GetOrCreate(Document.Lines[coord.Line], coord.Line);
		if (caretLineLayout is null) return null;

		//force trail visual line when drawing a thick cursor
		var localVisualCoord = caretLineLayout.GetVisualCoordinate(coord, _useThickCursor);
		visualLine += localVisualCoord.VisualLine;

		var point = VisualToUI(new VisualCoordinate(localVisualCoord.VisualCol, visualLine));
		point = point.WithY(point.Y - pixelOffset);
		point = point.WithX(point.X - _scrollXOffset);

		_caretWidth = Mode == Mode.Normal ? _editorMetrics.CharAdvance : _defaultCaretWidth;
		var caretRect = new Rect(point, new Size(_caretWidth, _editorMetrics.LineHeight));
		return caretRect;
	}
	private void DrawCaret(DrawingContext context)
	{
		using
		var clip = context.PushClip(EditorArea);
		var caretRect = GetCaretRect();
		if (caretRect is null) return;
		context.FillRectangle(new SolidColorBrush(Colors.White, _useThickCursor ? 0.5 : 1), caretRect.Value);
	}

	private Point VisualToUI(VisualCoordinate visualCoord)
	{
		return new(visualCoord.VisualCol * _editorMetrics.CharAdvance + EditorArea.Left,
		  visualCoord.VisualLine * _editorMetrics.LineHeight
		);
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		base.OnTextInput(e);
		if (e.Text is null) return;
		if (Mode == Mode.Normal)
		{
			HandleNormalModeTextInput(e);
		}
		else if (Mode == Mode.Insert)
		{
			HandleInsertModeTextInput(e);
		}
	}

	private void HandleNormalModeTextInput(TextInputEventArgs e)
	{
		if (e.Text is null) return;
		switch (e.Text)
		{
			case "i":
				Document.PlaceCursorBeforeSelection();
				EnterInsertMode();
				break;
			case "I":
				Document.PlaceCursorAtLineStart();
				EnterInsertMode();
				break;
			case "a":
				Document.PlaceCursorAfterSelection();
				EnterInsertMode();
				break;
			case "A":
				Document.PlaceCursorAtLineEnd();
				EnterInsertMode();
				break;
			case "d":
				Document.DeleteSelection();
				break;
			case "k":
				Document.CaretUp();
				break;
			case "h":
				Document.CaretLeft();
				break;
			case "l":
				Document.CaretRight();
				break;
			case "j":
				Document.CaretDown();
				break;
			case "o":
				Document.AddNewLineUnderSelection();
				EnterInsertMode();
				break;
			case "O":
				Document.AddNewLineAboveSelection();
				EnterInsertMode();
				break;
			case ":":
				{
					EnterCommandMode();
				}
				break;
			default:
				return;
		}
		e.Handled = true;
		HoldCaretAndRedraw();
		EnsureCaretVisible();
	}

	private void HandleInsertModeTextInput(TextInputEventArgs e)
	{
		if (e.Text is null) return;
		Document.TypeChars(e.Text);
		HoldCaretAndRedraw();
		EnsureCaretVisible();
	}
	private Rect GetHorizontalScrollbarRect()
	{
		var height = _scrollBarDimension;
		var editorHeight = Bounds.Height;

		var documentWidth = GetDocumentWidthInPixels();

		var thumbWidth = documentWidth <= EditorArea.Width ?
		  EditorArea.Width :
		  (EditorArea.Width / documentWidth) * EditorArea.Width;

		var maxScrollOffset = Math.Max(0, documentWidth - EditorArea.Width);
		var maxThumbX = EditorArea.Width - thumbWidth;

		var x = maxScrollOffset <= 0 ?
		  0 :
		  (_scrollXOffset / maxScrollOffset) * maxThumbX;

		var y = editorHeight - height;

		return new Rect(x + EditorArea.Left - _editorHorizontalMargin, y, thumbWidth + _editorHorizontalMargin, height);
	}

	private
	const double _minVerticalScrollbarHeight = 20;

	private Rect GetVerticalScrollbarRect()
	{
		var width = _scrollBarDimension;
		var documentHeight = GetDocumentHeightInPixels();

		var thumbHeight = documentHeight <= EditorArea.Height ?
		  EditorArea.Height :
		  Math.Max(
			_minVerticalScrollbarHeight,
			(EditorArea.Height / documentHeight) * EditorArea.Height);

		thumbHeight = Math.Min(thumbHeight, EditorArea.Height);
		var maxScrollOffset = Math.Max(0, documentHeight - EditorArea.Height);
		var maxThumbY = EditorArea.Height - thumbHeight;

		var y = maxScrollOffset <= 0 ?
		  0 :
		  (_scrollYOffset / maxScrollOffset) * maxThumbY;

		var x = EditorArea.Right + _editorHorizontalMargin;

		return new Rect(x, y, width, thumbHeight);
	}
}

public readonly record struct EditorMetrics(
  double CharAdvance,
  double LineHeight,
  int TabSize);
