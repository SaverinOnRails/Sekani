using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
namespace SekaniUI.Controls;

internal class Editor : Control
{
	private Typeface _editorFontFace = Typeface.Default;
	private readonly double _caretWidth = 2;
	private readonly float _fontSize = 16;
	private readonly float _scrollBarDimension = 7;
	private EditorModel _editorModel = null!;
	private const float _baseLineNumberWidth = 20;
	private float LineNumberSectDisplayWidth =>
		_baseLineNumberWidth +
		Math.Max(0, _editorModel.Lines.Count.ToString().Length - 1) * _editorModel.Metrics.CharAdvance;
	private float LineNumberSectWidth =>
		_drawLineNumbers ? LineNumberSectDisplayWidth : 0;
	private bool _drawLineNumbers = true;
	private bool _wordWrap = false;
	private readonly float _editorHorizontalMargin = 10F;
	private bool _caretVisible = true;
	private readonly DispatcherTimer _caretBlinkTimer;
	private static IBrush _scollBarBrush = new SolidColorBrush(Color.Parse("#BFC9D1"), 0.5);
	private double _scrollXOffset = 0;
	private bool _canScrollX => _wordWrap == false && GetDocumentWidthInPixels() > EditorAreaWidth;
	private bool _canScrollY => GetDocumentHeight() > EditorAreaHeight;

	//Bounds of the actual textarea
	public Rect EditorArea =>
			new(
				new Point(_editorHorizontalMargin + LineNumberSectWidth, 0),
				new Size(
					Bounds.Width - 2 * _editorHorizontalMargin - _scrollBarDimension - LineNumberSectWidth,
					Bounds.Height - _scrollBarDimension));

	private Rect LineNumbersSectRect =>
		new(
			new Point(0, 0),
			new Size(
			LineNumberSectWidth,
			EditorArea.Height
		));

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
		SetEditorMetrics();
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
		var pos = LogicalToUI(_editorModel.CaretPosition);
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
		if (pos.Y + _editorModel.Metrics.LineHeight > _scrollYOffset + EditorArea.Bottom)
		{
			_scrollYOffset =
				pos.Y + _editorModel.Metrics.LineHeight - EditorArea.Bottom;
		}
		if (pos.Y < _scrollYOffset + EditorArea.Top)
		{
			_scrollYOffset =
				pos.Y - EditorArea.Top;
		}
		_scrollXOffset = Math.Clamp(
			_scrollXOffset,
			0,
			Math.Max(0, GetDocumentWidthInPixels() - EditorArea.Width));

		_scrollYOffset = Math.Clamp(
			_scrollYOffset,
			0,
			Math.Max(0, GetDocumentHeight() - EditorArea.Height));
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
		var lineHeight = (float)text.Height;
		var tabSize = 4;
		_editorModel = new(new EditorMetrics(charAdvance, lineHeight, tabSize));
	}

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		Focus();
		LoadDummyText();
		_editorModel.CaretPosition = new(0, 0);
	}

	private void LoadDummyText()
	{
		// var text = File.ReadAllText("/home/noble/Projects/focus/src/draw.jai");
		var text = File.ReadAllText("/home/noble/Projects/Sekani/Source/UI/Controls/Editor.cs");
		_editorModel.HandleTextInput(text);
		// _editorModel.HandleTextInput("é");
		// _editorModel.HandleTextInput("ddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddrffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
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

		var scrollAmount = 60.0;

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

			var documentWidth = GetDocumentWidthInPixels();
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
		DrawLineNumbers(context);
		DrawText(context);
		DrawHorizontalScrollbar(context);
		DrawVerticalScrollbar(context);
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

		int firstLine =
				Math.Max(0, (int)(_scrollYOffset / _editorModel.Metrics.LineHeight));

		int lastLine =
			Math.Min(
				_editorModel.Lines.Count,
				(int)((_scrollYOffset + EditorArea.Height) / _editorModel.Metrics.LineHeight) + 1);

		for (int l = firstLine; l < lastLine; l++)
		{
			var number = (l + 1).ToString();

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
				l * _editorModel.Metrics.LineHeight - _scrollYOffset);

			context.DrawText(ft, point);
		}
	}

	private FormattedText GetFormattedTextWhole(Line line)
	{
		return new FormattedText(
					line.LineString,
					CultureInfo.InvariantCulture,
					FlowDirection.LeftToRight,
					_editorFontFace,
					_fontSize,
					Brushes.White);
	}
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == BoundsProperty)
		{
			EnsureCaretVisible();
		}
		base.OnPropertyChanged(e);
	}

	private void DrawVerticalScrollbar(DrawingContext context)
	{
		if (!_canScrollY) return;
		var rect = GetVerticalScrollbarRect();
		context.FillRectangle(_scollBarBrush, rect);
	}

	private void DrawHorizontalScrollbar(DrawingContext context)
	{
		if (!_canScrollX) return;
		var rect = GetHorizontalScrollbarRect();
		context.FillRectangle(_scollBarBrush, rect);
	}

	private Rect GetHorizontalScrollbarRect()
	{
		var height = _scrollBarDimension;
		var editorHeight = Bounds.Height;

		var documentWidth = GetDocumentWidthInPixels();

		var thumbWidth = documentWidth <= EditorAreaWidth
			? EditorAreaWidth
			: (EditorAreaWidth / documentWidth) * EditorAreaWidth;

		var maxScrollOffset = Math.Max(0, documentWidth - EditorAreaWidth);
		var maxThumbX = EditorAreaWidth - thumbWidth;

		var x = maxScrollOffset <= 0
			? 0
			: (_scrollXOffset / maxScrollOffset) * maxThumbX;

		var y = editorHeight - height;

		return new Rect(x + EditorArea.Left - _editorHorizontalMargin, y, thumbWidth + _editorHorizontalMargin, height);
	}


	private double GetDocumentWidthInPixels()
	{
		var longestLine = _editorModel.Lines.MaxBy(p => p.VisualLength);
		if (longestLine is null) return EditorAreaWidth;
		return VisualToUI(new(longestLine.VisualLength, 0)).X;
	}

	private double GetDocumentHeight()
	{
		return _editorModel.Lines.Count * _editorModel.Metrics.LineHeight;
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

	private void DrawText(DrawingContext context)
	{
		using var clip = context.PushClip(EditorArea);

		int firstLine =
			Math.Max(0, (int)(_scrollYOffset / _editorModel.Metrics.LineHeight));
		int lastLine =
			Math.Min(
				_editorModel.Lines.Count,
				(int)((_scrollYOffset + EditorArea.Height) / _editorModel.Metrics.LineHeight) + 1);


		for (int l = firstLine; l < lastLine; l++)
		{
			var ft = GetFormattedTextWhole(_editorModel.Lines[l]);
			Coordinate coord = new(0, l);
			var point = LogicalToUI(coord);
			point = point.WithX(point.X - _scrollXOffset);
			point = point.WithY(point.Y - _scrollYOffset);
			context.DrawText(ft, point);
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
		context.DrawRectangle(Brush.Parse("#0F3040"), new Pen(Brushes.Black, 1), Bounds);
	}

	private void Redraw() => InvalidateVisual();

	private void DrawCaret(DrawingContext context)
	{
		using var clip = context.PushClip(EditorArea);
		if (!_caretVisible) return;
		var point = LogicalToUI(_editorModel.CaretPosition);
		point = point.WithX(point.X - _scrollXOffset);
		point = point.WithY(point.Y - _scrollYOffset);
		var caretRect = new Rect(
			point,
			new Size(_caretWidth, _editorModel.Metrics.LineHeight));
		context.FillRectangle(Brushes.White, caretRect);
	}

	private Point VisualToUI(Coordinate visualCoord)
	{
		return new(visualCoord.Col * _editorModel.Metrics.CharAdvance + EditorArea.Left,
			visualCoord.Line * _editorModel.Metrics.LineHeight
		);
	}

	private Point LogicalToUI(Coordinate logicalCoord)
	{
		return VisualToUI(_editorModel.LogicalToVisual(logicalCoord));
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
				_editorModel.InsertTab();
				break;
			case Key.Return:
				_editorModel.InsertNewLine();
				break;
			case Key.Up:
				_editorModel.CaretUp();
				break;
			case Key.Left:
				_editorModel.CaretLeft();
				break;
			case Key.Right:
				_editorModel.CaretRight();
				break;
			case Key.Down:
				_editorModel.CaretDown();
				break;
			case Key.Back:
				_editorModel.Backspace();
				break;

		}
		HoldCaretAndRedraw();
		EnsureCaretVisible();
	}

	private void HoldCaretAndRedraw()
	{
		ResetCaretBlink();
		Redraw();
	}


	private void HandleKeyWithModifier()
	{
		// throw new NotImplementedException();
	}

	protected override void OnTextInput(TextInputEventArgs e)
	{
		base.OnTextInput(e);
		_editorModel.HandleTextInput(e.Text);
		HoldCaretAndRedraw();
		EnsureCaretVisible();
	}

}
