using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class Editor : Control
{
	public EditorMetrics _editorMetrics;
	private Typeface _editorFontFace = Typeface.Default;
	private Typeface _normalLineNumberFontFace = Typeface.Default;
	private Typeface _focusedLineNumberTypeFace = Typeface.Default;
	private readonly float _fontSize = 15;
	private LineCache _lineCache = null!;
	private
	const float _baseLineNumberWidth = 20;

	private double LineNumberSectDisplayWidth =>
	  _baseLineNumberWidth +
	  Math.Max(0, Math.Max(Document.LineCount.ToString().Length - 1, 7)) * _editorMetrics.CharAdvance;

	private bool _drawLineNumbers = true;
	private bool _smoothScroll = true;
	private bool _useRelativeLineNumber = false;
	private double LineNumberSectWidth =>
	  _drawLineNumbers ? LineNumberSectDisplayWidth : 0;
	private double _scrollXOffset = 0;
	private double _scrollYOffset = 0;
	private double _targetScrollYOffset = 0;
	private CursorStyle _cursorStyle
	{
		get
		{
			switch (Mode)
			{
				case Mode.Insert:
					return CursorStyle.Bar;
				case Mode.Normal:
					return CursorStyle.Block;
				case Mode.Visual:
					return CursorStyle.Underline;
				default:
					return CursorStyle.Block;
			}
		}
	}
	private bool _extendSelection => Mode == Mode.Visual;
	private bool _useWideCursor => _cursorStyle != CursorStyle.Bar;
	private double _caretWidth = 2;
	private readonly double _defaultCaretWidth = 2;
	private bool _pointerPressedOnHorizontalScrollbar = false;
	private bool _pointerPressedOnVerticalScrollbar;
	private bool _shouldDrawCaretFocusBubble = false;
	private readonly IBrush _mainTextBrush = Brush.Parse("#F3E6D5");
	private readonly IBrush _lineNumberBrush = new SolidColorBrush(Colors.White, 0.4);
	private readonly IBrush _lineNumberGutterBrush = new SolidColorBrush(Colors.White, 0.1);
	private readonly IBrush _focusedLineNumberBrush = new SolidColorBrush(Colors.White, 0.8);
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
	public static readonly StyledProperty<string?> MotionCountBufferProperty = AvaloniaProperty.Register<Editor, string?>(nameof(Document));

	public string? MotionCountBuffer
	{
		get => GetValue(MotionCountBufferProperty);
		set => SetValue(MotionCountBufferProperty, value);
	}

	public SekaniDocument Document
	{
		get => GetValue(DocumentProperty);
		set => SetValue(DocumentProperty, value);
	}
	private readonly DispatcherTimer _caretBlinkTimer;
	private DispatcherTimer? _autoDragScrollTimer;
	private DispatcherTimer? _caretFocusBubbleTimer;
	private readonly Stopwatch _caretFocusBubbleStopwatch = new();
	private const double CaretFocusBubbleDurationMs = 360;
	private bool _isSmoothScrolling;
	private TimeSpan _lastSmoothScrollFrameTime;
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

	private void ClearMotionCountBuffer()
	{
		MotionCountBuffer = null;
	}

	//Correct scroll behind when measured size changes
	private void LineCacheVisualLineTreeUpdated(
		 object? _,
		 VisualLineTreeUpdatedParams e)
	{
		var (firstLogicalLine, _, _) = ComputeVisibleText();
		if (e.lineIndex >= firstLogicalLine)
			return;

		var deltaPixels = e.delta * _editorMetrics.LineHeight;

		_scrollYOffset += deltaPixels;

		_targetScrollYOffset += deltaPixels;
	}

	private void SetLineCache()
	{
		_lineCache = Document.CreateLineCache(_editorMetrics.TabSize, _softWordWrap, 0);
		_lineCache.VisualLineTreeUpdated += LineCacheVisualLineTreeUpdated;
		Document.SetCaret(0);
	}

	private Dictionary<string, IBrush> _highlightBrushes = new()
	{
		["attribute"] = Brush.Parse("#c69f2a"),
		["type"] = Brush.Parse("#c69f2a"),
		["type.enum.variant"] = Brush.Parse("#48a8a0"),
		["constructor"] = Brush.Parse("#c03054"),
		["constant"] = Brush.Parse("#48a8a0"),
		["constant.character"] = Brush.Parse("#143936"),
		["constant.character.escape"] = Brush.Parse("#3f0d1a"),
		["string"] = Brush.Parse("#c03054"),
		["string.regexp"] = Brush.Parse("#3f0d1a"),
		["string.special"] = Brush.Parse("#c03054"),
		["string.special.symbol"] = Brush.Parse("#c03054"),
		["string.special.path"] = Brush.Parse("#48a8a0"),
		["path"] = Brush.Parse("#48a8a0"),
		["comment"] = Brush.Parse("#79685a"),
		["variable"] = Brush.Parse("#d8cebc"),
		["variable.builtin"] = Brush.Parse("#48a8a0"),
		["variable.parameter"] = Brush.Parse("#f5edd8"),
		["variable.other.member"] = Brush.Parse("#c69f2a"),
		["label"] = Brush.Parse("#48a8a0"),
		["punctuation"] = Brush.Parse("#79685a"),
		["keyword"] = Brush.Parse("#c69f2a"),
		["keyword.control.conditional"] = Brush.Parse("#c03054"),
		["operator"] = Brush.Parse("#48a8a0"),
		["function"] = Brush.Parse("#c03054"),
		["function.macro"] = Brush.Parse("#c03054"),
		["tag"] = Brush.Parse("#48a8a0"),
		["namespace"] = Brush.Parse("#48a8a0"),
		["special"] = Brush.Parse("#48a8a0"),
		["markup.heading"] = Brush.Parse("#c69f2a"),
		["markup.list"] = Brush.Parse("#d8cebc"),
		["markup.bold"] = Brush.Parse("#48a8a0"),
		["markup.italic"] = Brush.Parse("#48a8a0"),
		["markup.link.url"] = Brush.Parse("#48a8a0"),
		["markup.link.label"] = Brush.Parse("#c69f2a"),
		["markup.raw"] = Brush.Parse("#d8cebc"),
		["markup.quote"] = Brush.Parse("#48a8a0"),
		["diff.plus"] = Brush.Parse("#a6e3a1"),
		["diff.minus"] = Brush.Parse("#c6372a"),
		["diff.delta"] = Brush.Parse("#89b4fa")
	};

	private IBrush? TryGetHighlight(string capture)
	{
		var lookup = _highlightBrushes.GetAlternateLookup<ReadOnlySpan<char>>();
		ReadOnlySpan<char> current = capture;
		while (!current.IsEmpty)
		{
			if (lookup.TryGetValue(current, out var brush))
				return brush;

			int lastDot = current.LastIndexOf('.');
			if (lastDot < 0)
				break;
			current = current[..lastDot];
		}
		return null;
	}

	private void TimeCaret()
	{
		_caretBlinkTimer.Tick += (_, _) =>
		{
			if (_useWideCursor) return;
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
	  double offset,
	  out bool caretAboveViewport,
	  out double correctiveDistance)
	{
		caretAboveViewport = false;
		correctiveDistance = 0;

		var coord = Document.CaretPosition;
		var lineLayout = _lineCache.GetOrCreate(coord.Line);
		if (lineLayout is null)
			return false;

		int caretVisualLine = lineLayout.GetVisualCoordinate(coord, _useWideCursor).VisualLine;

		// Number of visual lines belonging to logical lines before the caret's line.
		int visualLinesBeforeCaret = _lineCache.VisualLinesPrefixSum(coord.Line);

		// Absolute visual-line position of the caret.
		int absoluteCaretVisualLine =
		  visualLinesBeforeCaret + caretVisualLine;

		double caretY =
		  absoluteCaretVisualLine * _editorMetrics.LineHeight;

		double caretBottom =
		  caretY + _editorMetrics.LineHeight;

		double viewportTop = offset;
		double viewportBottom = offset + EditorArea.Height;

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

		return true;
	}
	private bool IsCaretVisibleHorizontal(
	  out bool caretLeftOfViewport,
	  out double correctiveDistance)
	{
		caretLeftOfViewport = false;
		correctiveDistance = 0;

		var coord = Document.CaretPosition;
		var layout = _lineCache.GetOrCreate(coord.Line);
		if (layout is null) return false;
		var visualCol = layout.GetVisualCoordinate(coord, _useWideCursor).VisualCol;
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
		if (!_isSmoothScrolling)
		{
			_targetScrollYOffset = _scrollYOffset;
		}
		if (!IsCaretVisibleVertical(
			_targetScrollYOffset,
			out bool caretAboveViewport,
			out double correctiveDistance) && ensureVertical)
		{
			if (caretAboveViewport)
				_targetScrollYOffset -= correctiveDistance;
			else
				_targetScrollYOffset += correctiveDistance;

			//only do this when we are not drag selecting
			if (correctiveDistance > _editorMetrics.LineHeight * 5 && _autoDragScrollTimer is null) StartDrawCaretFocusBubble();
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
		// CorrectScrollBarOffset();
		if (_smoothScroll)
			TryStartSmoothScroll();
		else
			_scrollYOffset = _targetScrollYOffset;
	}

	private void TryStartSmoothScroll()
	{
		if (_scrollYOffset == _targetScrollYOffset)
			return;
		if (_isSmoothScrolling)
			return;

		_isSmoothScrolling = true;
		_lastSmoothScrollFrameTime = TimeSpan.Zero;
		var diff = Math.Abs(_targetScrollYOffset - _scrollYOffset);
		if (diff >= EditorArea.Height / 2)
		{
			_scrollYOffset = _targetScrollYOffset;
			CorrectScrollBarOffset();
			StopSmoothScroll();
			Redraw();
			return;
		}
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
		topLevel.RequestAnimationFrame((t) => DoSmoothScroll(t));
	}

	private void DoSmoothScroll(TimeSpan frameTime)
	{
		if (!_isSmoothScrolling)
			return;

		var distance = _targetScrollYOffset - _scrollYOffset;
		double dt = (frameTime - _lastSmoothScrollFrameTime).TotalSeconds;
		_lastSmoothScrollFrameTime = frameTime;
		dt = Math.Min(dt, 0.1);

		if (Math.Abs(distance) < 0.5)
		{
			_scrollYOffset = _targetScrollYOffset;
			CorrectScrollBarOffset();
			StopSmoothScroll();
			Redraw();
			return;
		}
		const double smoothing = 14.0;
		double t = 1.0 - Math.Exp(-smoothing * dt);
		_scrollYOffset += distance * t;
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
		_caretFocusBubbleStopwatch.Restart();
		_caretFocusBubbleTimer = new()
		{
			Interval = TimeSpan.FromMilliseconds(16),
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
	private int MotionCount
	{
		get
		{
			if (MotionCountBuffer is null) return 1;
			return int.Parse(MotionCountBuffer);
		}
	}

	private void HandleKeyInput(KeyEventArgs e)
	{
		bool handled = false;
		// These work in all modes
		switch (e.Key)
		{
			case Key.Up:
				Document.CaretUp(MotionCount, _extendSelection);
				handled = true;
				break;

			case Key.Left:
				Document.CaretLeft(MotionCount, _extendSelection);
				handled = true;
				break;

			case Key.Right:
				Document.CaretRight(MotionCount, _extendSelection);
				handled = true;
				break;

			case Key.Down:
				Document.CaretDown(MotionCount, _extendSelection);
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
			}
		}
		if (e.Key == Key.Escape)
		{
			EnterNormalMode();
			HoldCaretAndRedraw();
			ClearMotionCountBuffer();
			return;
		}

		if (handled)
		{
			ClearMotionCountBuffer();
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

	private void UpdateMotionCountBuffer(char buf)
	{
		if (MotionCountBuffer?.Length == 5) return;
		StringBuilder builder = new(MotionCountBuffer);
		builder.Append(buf);
		MotionCountBuffer = builder.ToString();
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
			ClearMotionCountBuffer();
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
		_editorFontFace = new Typeface("Jetbrains Mono");
		_normalLineNumberFontFace = new Typeface(
		_editorFontFace.FontFamily,
		_editorFontFace.Style,
		_editorFontFace.Weight);

		_focusedLineNumberTypeFace = new Typeface(
		_editorFontFace.FontFamily,
		_editorFontFace.Style,
		FontWeight.Bold);

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
		_editorMetrics = new(charAdvance, lineHeight, tabSize);
	}

	private double GetDocumentWidthInPixels()
	{
		return VisualToUI(new(_lineCache.Width, 0)).X;
	}

	private double GetDocumentHeightInPixels()
	{
		return _lineCache.TotalVisualLines() *
		  _editorMetrics.LineHeight + (EditorArea.Height / 2);
	}
	private readonly float _editorHorizontalMargin = 10F;
	private readonly float _scrollBarDimension = 15;

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
		var visibleTextBounds = ComputeVisibleText();
		DrawMainRectangle(context);
		DrawText(context, visibleTextBounds);
		DrawLineNumbers(context, visibleTextBounds);
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

		_caretFocusBubbleProgress = Math.Clamp(
			_caretFocusBubbleStopwatch.Elapsed.TotalMilliseconds
				/ CaretFocusBubbleDurationMs,
			0,
			1);

		if (_caretFocusBubbleProgress >= 1)
		{
			_caretFocusBubbleTimer?.Stop();
			_caretFocusBubbleTimer = null;
			_caretFocusBubbleStopwatch.Stop();
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
		using
		var clip = context.PushClip(EditorArea);
		var range = Document.CaretRange;
		if (Mode == Mode.Insert && !range.HasActiveSelection) return;
		var higher = Document.GetCoordinate(Document.GetHigherEnd(range));
		var lower = Document.GetCoordinate(range.Lower);

		var firstLine = lower.Line;
		var lastLine = higher.Line;
		var VisibleTextBounds = ComputeVisibleText();
		firstLine = Math.Max(firstLine, VisibleTextBounds.firstLogicalLine);
		lastLine = Math.Min(lastLine, VisibleTextBounds.lastPossibleLogicalLine);
		if (firstLine > lastLine) return;
		var visualLineSum = _lineCache.VisualLinesPrefixSum(firstLine);
		for (int i = firstLine; i <= lastLine; i++)
		{
			var lineLayout = _lineCache.GetOrCreate(i);
			if (lineLayout is null) continue;
			if (i > firstLine)
				visualLineSum = _lineCache.VisualLinesPrefixSum(i);
			for (int j = 0; j < _lineCache.VisualLineCountAt(i); j++)
			{
				var startAtPixels = EditorArea.Left;
				var endOfVisualLine = lineLayout.VisualLines[j].VisualLength;
				var endAtPixels = endOfVisualLine * _editorMetrics.CharAdvance + EditorArea.Left;
				if (i == lower.Line)
				{
					var startVisualCoord = lineLayout.GetVisualCoordinate(lower, _useWideCursor);
					if (j < startVisualCoord.VisualLine)
					{
						visualLineSum++;
						continue;
					}
					if (j == startVisualCoord.VisualLine)
					{
						startAtPixels = EditorArea.Left + startVisualCoord.VisualCol * _editorMetrics.CharAdvance;
					}
				}
				if (i == higher.Line)
				{
					var endVisualCoord = lineLayout.GetVisualCoordinate(higher, _useWideCursor);
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
				context.FillRectangle(new SolidColorBrush(Color.Parse("#FFCB3D"), 0.25), new Rect(point, size));
				visualLineSum++;
			}
		}
	}
	private void DrawLineNumbers(DrawingContext context, VisibleTextBounds visibleTextBounds)
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
		  new Pen(_lineNumberGutterBrush),
		  new Point(LineNumbersSectRect.Right, 0),
		  new Point(LineNumbersSectRect.Right, LineNumbersSectRect.Height));

		int visualLine = 0;
		var lastLogicalLine = visibleTextBounds.lastPossibleLogicalLine + 5;
		lastLogicalLine += 5;
		for (int i = visibleTextBounds.firstLogicalLine; i < lastLogicalLine; i++)
		{
			if (i >= Document.LineCount) return;
			var lineLayout = _lineCache.GetOrCreate(i);
			if (lineLayout is null)
				continue;
			var caretLine = Document.CaretPosition.Line;
			var number = _useRelativeLineNumber
				? (i == caretLine ? i + 1 : Math.Abs(i - caretLine))
				: i + 1;
			var ft = new FormattedText(
			  number.ToString(),
			  CultureInfo.InvariantCulture,
			  FlowDirection.LeftToRight,
			  i == Document.CaretPosition.Line ? _focusedLineNumberTypeFace : _normalLineNumberFontFace,
			  _fontSize,
			  i == Document.CaretPosition.Line ? _focusedLineNumberBrush : _lineNumberBrush
			);
			var x = LineNumberSectWidth - ft.Width - 5;
			var point = new Point(
			  x,
			  visualLine * _editorMetrics.LineHeight - visibleTextBounds.pixelOffset);
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
		context.DrawRectangle(Brush.Parse("#100e0c"), new Pen(Brushes.Black, 1), Bounds);
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
		int logicalLineIndex = Math.Min(Document.LineCount, _lineCache.FindByPrefixSum(targetVisualLine, out int visualLineSum));
		var layout = _lineCache.GetOrCreate(
		  logicalLineIndex);

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
		Document.SetCaretRange(pos);
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
				var layout = _lineCache.GetOrCreate(Document.CaretPosition.Line);
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
						var offset = Document.GetOffset(new(newLogicalCol, Document.CaretPosition.Line));
						Document.MoveHead(offset);
						EnsureCaretVisible();
						Redraw();
						return;
					}
				}

				if (Document.CaretPosition.Line == 0) return;

				var layoutOfPrevLine = _lineCache.GetOrCreate(
				  Document.CaretPosition.Line - 1);

				if (layoutOfPrevLine is null) return;

				var lastVisualLineOfPrevLine =
				  layoutOfPrevLine.VisualLines.Count - 1;

				var newVisualCoordOfLastLine = new VisualCoordinate(
				  _preferredDragScrollVisualColumn,
				  lastVisualLineOfPrevLine);

				var newLogicalColOfLastLine =
				  layoutOfPrevLine.GetLogicalColumn(
					newVisualCoordOfLastLine);

				Document.MoveHead(Document.GetOffset(new(newLogicalColOfLastLine, Document.CaretPosition.Line - 1)));
				EnsureCaretVisible();
				Redraw();
			}

			if (point.Y > EditorArea.Bottom - _autoScrollMargin)
			{
				var layout = _lineCache.GetOrCreate(Document.CaretPosition.Line);
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
						Document.MoveHead(Document.GetOffset(new(newLogicalCol, Document.CaretPosition.Line)));
						EnsureCaretVisible();
						Redraw();
						return;
					}
				}

				if (Document.CaretPosition.Line >= Document.LineCount - 1)
					return;

				var layoutOfNextLine = _lineCache.GetOrCreate(
				  Document.CaretPosition.Line + 1);

				if (layoutOfNextLine is null) return;

				var newVisualCoordOfFirstLine = new VisualCoordinate(
				  _preferredDragScrollVisualColumn,
				  0);

				var newLogicalColOfFirstLine =
				  layoutOfNextLine.GetLogicalColumn(
					newVisualCoordOfFirstLine);
				Document.MoveHead(Document.GetOffset(new(newLogicalColOfFirstLine, Document.CaretPosition.Line + 1)));
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

		Document.MoveHead(Document.GetOffset(pos));
		var layout = _lineCache.GetOrCreate(pos.Line);
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

		var scrollAmount = 20.0;

		_scrollYOffset = Math.Clamp(
		  _scrollYOffset - (e.Delta.Y * scrollAmount),
		  0,
		  maxScrollOffset);

		Redraw();

		e.Handled = true;
	}

	private VisibleTextBounds ComputeVisibleText()
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
		return new(firstLogicalLine, lastLogicalLine, offset);
	}
	private void DrawText(DrawingContext context, VisibleTextBounds visibleTextBounds)
	{
		using var textOptions = context.PushTextOptions(new TextOptions
		{
			BaselinePixelAlignment = BaselinePixelAlignment.Unaligned,
			TextHintingMode = TextHintingMode.None
		});

		int currentVisualLine = 0;
		int logicalLineToBeginCount = visibleTextBounds.firstLogicalLine;
		var lastLogicalLine = visibleTextBounds.lastPossibleLogicalLine + 5;
		var caretPos = Document.CaretPosition;
		var relativeCaretVisualLine = _lineCache.GetOrCreate(caretPos.Line)?.GetVisualCoordinate(caretPos).VisualLine;
		Rect? hintRectangle = null;
		using (var clip = context.PushClip(EditorArea))
		{
			for (int i = logicalLineToBeginCount; i <= visibleTextBounds.lastPossibleLogicalLine; i++)
			{
				if (i >= Document.LineCount) break;
				if (i < 0) continue;
				var logicalLineAbsoluteOffset = Document.GetOffset(new(0, i));
				var lineLayout = _lineCache.GetOrCreate(i);

				if (lineLayout is null) return;
				for (int j = 0; j < lineLayout.VisualLines.Count; j++)
				{
					double x, y = 0;
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
					  _mainTextBrush);

					var expectedPixelWidth = _editorMetrics.CharAdvance * visualLine.VisualLength;
					var actualPixelWidth = ft.WidthIncludingTrailingWhitespace;
					if (text.Contains('\n'))
					{
						expectedPixelWidth -= _editorMetrics.CharAdvance; //linebreaks are not measured by avalonia
					}
					const double epsilon = 0.01;
					if (Math.Abs(expectedPixelWidth - actualPixelWidth) < epsilon)
					{
						Point point = VisualToUI(
						  new VisualCoordinate(
							0,
							currentVisualLine));
						y = point.Y - visibleTextBounds.pixelOffset;
						x = point.X - _scrollXOffset;
						point = point.WithY(y);
						point = point.WithX(x);
						//apply highlighting
						var logicalOffset = logicalLineAbsoluteOffset + visualLine.LogicalOffset;
						var logicalEnd = logicalOffset + visualLine.LogicalLength;
						if (Document.TreeSitterClientActive)
						{
							var highlights = Document.GetHighlights(logicalOffset, logicalEnd);
							foreach (var span in highlights)
							{
								var hightlightBrush = TryGetHighlight(span.Capture);
								if (hightlightBrush is not null)
								{
									var logicalToVisualOffsetArena = lineLayout.LogicalOffsetToVisualOffsetArena;
									//all this bullshit becuase i decided to convert tabs to spaces
									var map = CollectionsMarshal.AsSpan((List<int>)logicalToVisualOffsetArena).Slice(visualLine.ArenaStartIndex, visualLine.ArenaLength);
									int s = map[Math.Clamp(span.Start - logicalOffset, 0, map.Length - 1)];
									int e = map[Math.Clamp(span.End - logicalOffset, 0, map.Length - 1)];
									if (e <= s) continue;
									ft.SetForegroundBrush(hightlightBrush, s, e - s);
								}
							}
						}
						context.DrawText(ft, point);
					}
					else
					{
						//TODO: Do highlighting for this
						var graphemeBoundaries = lineLayout.GraphemeBoundaries;
						var visualLineOffset = visualLine.LogicalOffset;
						var startIndex = lineLayout.GraphemeBoundaryIndexOf(visualLineOffset);
						var end = visualLine.LogicalOffset + visualLine.LogicalLength;
						int visualCol = 0;
						Point basePoint = VisualToUI(new VisualCoordinate(0, currentVisualLine));
						double baseY = basePoint.Y - visibleTextBounds.pixelOffset;
						double baseX = basePoint.X - _scrollXOffset;
						for (int k = startIndex; k < graphemeBoundaries.Count - 1; k++)
						{
							var offset = graphemeBoundaries[k];
							if (offset >= end) break;
							var next = graphemeBoundaries[k + 1];
							var grapheme = lineLayout.LogicalText.AsSpan(offset, next - offset);
							var width = lineLayout.GraphemeWidth(grapheme, visualCol);
							var ftglyph = new FormattedText(
							  grapheme.ToString(),
							  CultureInfo.InvariantCulture,
							  FlowDirection.LeftToRight,
							  _editorFontFace,
							  _fontSize,
							  _mainTextBrush);

							var glyphPoint = new Point(
								baseX + visualCol * _editorMetrics.CharAdvance,
								baseY);
							context.DrawText(ftglyph, glyphPoint);
							visualCol += width;
							context.DrawText(ftglyph, glyphPoint);
						}
					}
					currentVisualLine++;
					//true check whether we should draw this line
					if (y + _editorMetrics.LineHeight < EditorArea.Top) continue; //is bottom in viewport?
					if (y > EditorArea.Bottom) break;
					if (i == caretPos.Line && j == relativeCaretVisualLine)
					{
						hintRectangle = new Rect(new Point(0, y), new Size(Bounds.Width, _editorMetrics.LineHeight));
					}
				}
			}
		}
		if (hintRectangle is not null)
		{
			//draw a hint rectangle
			context.FillRectangle(new SolidColorBrush(Colors.Gray, 0.1), hintRectangle.Value);
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
			var lineLayout = _lineCache.GetOrCreate(i);
			if (lineLayout is null) return null;
			visualLine += lineLayout.VisualLines.Count;
		}

		var caretLineLayout = _lineCache.GetOrCreate(coord.Line);
		if (caretLineLayout is null) return null;

		//force trail visual line when drawing a thick cursor
		var localVisualCoord = caretLineLayout.GetVisualCoordinate(coord, _useWideCursor);
		visualLine += localVisualCoord.VisualLine;

		var point = VisualToUI(new VisualCoordinate(localVisualCoord.VisualCol, visualLine));
		point = point.WithY(point.Y - pixelOffset);
		point = point.WithX(point.X - _scrollXOffset);

		var advanceWidth = _editorMetrics.CharAdvance;
		var nextLogicalCol = caretLineLayout.NextCol(coord.Col);

		if (nextLogicalCol is int nextCol)
		{
			var nextVisualCoord = caretLineLayout.GetVisualCoordinate(
				new(nextCol, coord.Line));

			if (nextVisualCoord.VisualLine == localVisualCoord.VisualLine)
				advanceWidth = (nextVisualCoord.VisualCol - localVisualCoord.VisualCol)
					* _editorMetrics.CharAdvance;
			advanceWidth = Math.Max(advanceWidth, _editorMetrics.CharAdvance);
		}

		var rect = GetCaretRectGeometry(point, advanceWidth);
		return rect;
	}


	private Rect GetCaretRectGeometry(Point point, double advanceWidth)
	{
		var caretHeight = _cursorStyle == CursorStyle.Underline ? _editorMetrics.LineHeight * 0.1 : _editorMetrics.LineHeight;
		var caretWidth = _useWideCursor ? advanceWidth : _defaultCaretWidth;
		if (_cursorStyle == CursorStyle.Underline)
		{
			var y = point.Y + (0.9 * _editorMetrics.LineHeight);
			point = point.WithY(y);
		}
		return new Rect(point, new Size(caretWidth, caretHeight));
	}
	private void DrawCaret(DrawingContext context)
	{
		using
		var clip = context.PushClip(EditorArea);
		var caretRect = GetCaretRect();
		if (caretRect is null) return;
		context.FillRectangle(new SolidColorBrush(Colors.White, _useWideCursor ? 0.5 : 1), caretRect.Value);
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
		if (Mode == Mode.Normal || Mode == Mode.Visual)
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
		if (e.Text.Length == 1 && char.IsNumber(e.Text[0]))
		{
			UpdateMotionCountBuffer(e.Text[0]);
			return;
		}
		switch (e.Text)
		{
			case "i":
				Document.PlaceCursorBeforeSelection();
				EnterInsertMode();
				break;
			case "v":
				EnterVisualMode();
				break;
			case "I":
				Document.PlaceCursorAtLineStart(_extendSelection);
				EnterInsertMode();
				break;
			case "a":
				Document.PlaceCursorAfterSelection();
				EnterInsertMode();
				break;
			case "A":
				Document.PlaceCursorAtLineEnd(_extendSelection);
				EnterInsertMode();
				break;
			case "d":
				Document.DeleteSelection();
				break;
			case "c":
				Document.DeleteSelection();
				EnterInsertMode();
				break;
			case "k":
				Document.CaretUp(MotionCount, _extendSelection);
				break;
			case "h":
				Document.CaretLeft(MotionCount, _extendSelection);
				break;
			case "l":
				Document.CaretRight(MotionCount, _extendSelection);
				break;
			case "j":
				Document.CaretDown(MotionCount, _extendSelection);
				break;
			case "o":
				Document.AddNewLineUnderSelection();
				EnterInsertMode();
				break;
			case "O":
				Document.AddNewLineAboveSelection();
				EnterInsertMode();
				break;
			case "w":
				Document.SelectToWordMotion(new(MotionKind.NextWordStart, MotionSubKind.Word), MotionCount, _extendSelection);
				break;
			case "e":
				Document.SelectToWordMotion(new(MotionKind.NextWordEnd, MotionSubKind.Word), MotionCount, _extendSelection);
				break;
			case "b":
				Document.SelectToWordMotion(new(MotionKind.CurrentWordStart, MotionSubKind.Word), MotionCount, _extendSelection);
				break;
			case "W":
				Document.SelectToWordMotion(new(MotionKind.NextWordStart, MotionSubKind.WORD), MotionCount, _extendSelection);
				break;
			case "E":
				Document.SelectToWordMotion(new(MotionKind.NextWordEnd, MotionSubKind.WORD), MotionCount, _extendSelection);
				break;
			case "B":
				Document.SelectToWordMotion(new(MotionKind.CurrentWordStart, MotionSubKind.WORD), MotionCount, _extendSelection);
				break;
			case "p":
				Document.PrintPieceTable();
				break;
			case "%":
				Document.SelectAll();
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
		ClearMotionCountBuffer();
	}

	private void EnterVisualMode()
	{
		Mode = Mode.Visual;
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

public readonly record struct VisibleTextBounds(int firstLogicalLine, int lastPossibleLogicalLine, double pixelOffset);

internal enum CursorStyle
{
	Bar,
	Block,
	Underline,
}
