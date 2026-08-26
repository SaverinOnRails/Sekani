using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Sekani.EditorCore;

namespace SekaniUI.Controls;

public class Editor : Control
{
	private SekaniDocument _document;
	public EditorMetrics _editorMetrics;
	private Typeface _editorFontFace = Typeface.Default;
	private readonly float _fontSize = 20;
	private LineCache _lineCache;
	private double _scrollXOffset = 0;
	private double _scrollYOffset = 0;
	private bool _pointerPressedOnHorizontalScrollbar = false;
	private bool _pointerPressedOnVerticalScrollbar;
	private double _scrollbarPointerStartX = 0;
	private double _scrollbarScrollStartX = 0;
	private double _scrollbarPointerStartY;
	private double _scrollbarScrollStartY;

	private bool _canScrollX => GetDocumentWidthInPixels() > EditorArea.Width;
	private bool _canScrollY => GetDocumentHeightInPixels() > EditorArea.Height;

	private static IBrush _scollBarBrush = new SolidColorBrush(Color.Parse("#BFC9D1"), 0.5);

	public Editor()
	{
		SetAvaloniaProperties();
		SetEditorMetrics();
		_document = new();
		// var file = File.ReadAllText("/home/noble/Projects/focus/src/draw.jai");
		var file = File.ReadAllText("/home/noble/longfile.text");
		_lineCache = _document.LineCache;
		_document.TypeChars(file, new(0, 0));
	}

	private void SetAvaloniaProperties()
	{
		HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
		VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
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
		_editorMetrics = new(charAdvance, lineHeight, tabSize);
	}

	private double GetDocumentWidthInPixels()
	{
		return VisualToUI(new(_lineCache.Width, 0)).X;
	}

	private double GetDocumentHeightInPixels()
	{
		return _document.Lines.Count * _editorMetrics.LineHeight;
	}
	private readonly float _editorHorizontalMargin = 10F;
	private readonly float _scrollBarDimension = 7;
	private VisualCoordinate _caretPosition = new(0, 0);

	//Bounds of the actual textarea
	public Rect EditorArea =>
			new(
				new Point(_editorHorizontalMargin, 0),
				new Size(
					Bounds.Width - 2 * _editorHorizontalMargin - _scrollBarDimension,
					Bounds.Height - _scrollBarDimension));

	public override void Render(DrawingContext context)
	{
		base.Render(context);
		DrawMainRectangle(context);
		DrawText(context);
		DrawHorizontalScrollbar(context);
		DrawVerticalScrollbar(context);
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
		context.DrawRectangle(Brush.Parse("#0F3040"), new Pen(Brushes.Black, 1), Bounds);
	}

	protected override void OnPointerPressed(PointerPressedEventArgs e)
	{
		Focus();
		TryHittestScrollbars(e);
		base.OnPointerPressed(e);
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
	private void DrawText(DrawingContext context)
	{
		var height = EditorArea.Height;
		int firstLine =
			Math.Max(0, (int)(_scrollYOffset / _editorMetrics.LineHeight));
		int lastLine =
			Math.Min(
				_document.Lines.Count,
				(int)((_scrollYOffset + EditorArea.Height) / _editorMetrics.LineHeight) + 1);
		for (int i = firstLine; i < lastLine; i++)
		{
			var linelayout = _lineCache.GetOrCreate(i);
			if (linelayout is null) continue;
			Point point = VisualToUI(new VisualCoordinate(0, i));
			point = point.WithY(point.Y - _scrollYOffset);
			point = point.WithX(point.X - _scrollXOffset);
			var ft = new FormattedText(
					linelayout.LineText,
					CultureInfo.InvariantCulture,
					FlowDirection.LeftToRight,
					_editorFontFace,
					_fontSize,
					Brushes.White);
			context.DrawText(ft, point);
		}
	}

	private Point VisualToUI(VisualCoordinate visualCoord)
	{
		return new(visualCoord.Col * _editorMetrics.CharAdvance + EditorArea.Left,
			visualCoord.Line * _editorMetrics.LineHeight
		);
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

	private Rect GetVerticalScrollbarRect()
	{
		var width = _scrollBarDimension;

		var documentHeight = GetDocumentHeightInPixels();

		var thumbHeight = documentHeight <= EditorArea.Height
			? EditorArea.Height
			: (EditorArea.Height / documentHeight) * EditorArea.Height;

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
	float CharAdvance,
	float LineHeight,
	int TabSize);
