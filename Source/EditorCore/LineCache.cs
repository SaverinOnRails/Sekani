using Sekani.EditorCore.Utils;
namespace Sekani.EditorCore;

public sealed class LineCache
{
	private readonly SekaniBuffer _buffer;
	private readonly PieceTable _pieceTable;
	private readonly List<LineLayout?> _layoutCache = [];
	public int Width { get; private set; } = 0;
	private Line? _longestLine;
	private readonly int _tabSize;
	private readonly bool _wordWrap;
	private int _maxVisualColsPerLine;
	public event EventHandler<VisualLineTreeUpdatedParams> VisualLineTreeUpdated;

	private VisualLineTree _visualLineTree = new();

	internal LineCache(SekaniBuffer buffer, PieceTable pieceTable, int tabSize, bool wordWrap = false, int maxVisualColsPerLine = 0)
	{
		_buffer = buffer;
		_tabSize = tabSize;
		_pieceTable = pieceTable;
		_wordWrap = wordWrap;
		_maxVisualColsPerLine = maxVisualColsPerLine;
		// _buffer.BufferChangedEvent += BufferChanged;
		_pieceTable.BufferChangedEvent += PieceTableLineChanged;
		// RecalculateWidth();
	}

	private void PieceTableLineChanged(object? sender, BufferChangeData e)
	{
		if (e.kind == BufferChangeKind.LineRemoved)
		{
			_visualLineTree.RemoveAt(e.Line);
			_layoutCache.RemoveAt(e.Line);
		}

		if (e.kind == BufferChangeKind.LineAdded)
		{
			if (e.Line <= _visualLineTree.Count)
			{
				_visualLineTree.Insert(e.Line, 1);
				_layoutCache.Insert(e.Line, null);
			}
		}
		InvalidateLayout(e.Line);
	}

	// private void BufferChanged(object? sender, BufferChangeData e)
	// {
	// 	//do this first as a removed line may no longer be in the buffer
	// 	if (e.kind == BufferChangeKind.LineRemoved)
	// 	{
	// 		_visualLineTree.RemoveAt(e.Line);
	// 		_layoutCache.RemoveAt(e.Line);
	// 	}

	// 	var line = _buffer.GetLine(e.Line);
	// 	if (line is null)
	// 		return;

	// 	if (_longestLine is null ||
	// 		line.Text.Length >= Width)
	// 	{
	// 		_longestLine = line;
	// 		Width = line.Text.Length;
	// 	}
	// 	else if (line == _longestLine)
	// 	{
	// 		RecalculateWidth();
	// 	}
	// 	if (e.kind == BufferChangeKind.LineAdded)
	// 	{
	// 		if (e.Line <= _visualLineTree.Count)
	// 		{
	// 			_visualLineTree.Insert(e.Line, 1);
	// 			_layoutCache.Insert(e.Line, null);
	// 		}
	// 	}
	// 	InvalidateLayout(e.Line);
	// }


	// private void RecalculateWidth()
	// {
	// 	_longestLine = null;
	// 	Width = 0;
	// 	foreach (var line in _buffer.Lines)
	// 	{
	// 		if (line.Text.Length > Width)
	// 		{
	// 			_longestLine = line;
	// 			Width = line.Text.Length;
	// 		}
	// 	}
	// }

	//Source from the piece table
	public LineLayout? GetOrCreate(int index)
	{
		if (index > _pieceTable.LineFeedCount) return null;
		//pad to fill up
		while (_layoutCache.Count <= index)
			_layoutCache.Insert(_layoutCache.Count, null);
		while (_visualLineTree.Count <= index)
			_visualLineTree.Insert(_visualLineTree.Count, 1);

		var line = new Line() { Text = _pieceTable.PrintCleanLine(index) };
		var lineLayout = new LineLayout(
			line,
			_tabSize,
			_wordWrap,
			_maxVisualColsPerLine);
		_layoutCache[index] = lineLayout;
		var oldCount = VisualLineCountAt(index);
		var newCount = lineLayout.VisualLines.Count;
		var delta = newCount - oldCount;
		if (delta != 0)
			VisualLineTreeUpdated?.Invoke(
				this,
				new(index, delta));
		SetVisualLineCount(index, newCount);
		return lineLayout;

	}
	// public LineLayout? GetOrCreate(int index, bool updateVisualLineCount = true)
	// {
	// 	if (index > _buffer.Lines.Count) return null;
	// 	//pad to fill up
	// 	while (_layoutCache.Count <= index)
	// 		_layoutCache.Insert(_layoutCache.Count, null);
	// 	while (_visualLineTree.Count <= index)
	// 		_visualLineTree.Insert(_visualLineTree.Count, 1);
	// 	if (_layoutCache[index] != null) return _layoutCache[index];
	// 	var line = _buffer.Lines[index];
	// 	var lineLayout = new LineLayout(
	// 		line,
	// 		_tabSize,
	// 		_wordWrap,
	// 		_maxVisualColsPerLine);
	// 	_layoutCache[index] = lineLayout;
	// 	if (updateVisualLineCount)
	// 	{
	// 		var oldCount = VisualLineCountAt(index);
	// 		var newCount = lineLayout.VisualLines.Count;
	// 		var delta = newCount - oldCount;
	// 		if (delta != 0)
	// 			VisualLineTreeUpdated?.Invoke(
	// 				this,
	// 				new(index, delta));
	// 		SetVisualLineCount(index, newCount);
	// 	}
	// 	return lineLayout;

	// }

	private void InvalidateLayout(int line)
	{
		// _layoutCache.Remove(line);
		if (line < _layoutCache.Count)
		{
			_layoutCache[line] = null;
		}
	}

	private void InvalidateAll()
	{
		// _layoutCache.Clear();
		_layoutCache.Clear();
	}

	public void SetMaxVisualColsForWrap(int maxVisualColsPerLine)
	{
		_maxVisualColsPerLine = maxVisualColsPerLine;
		InvalidateAll();
	}


	public void BuildVisualLinesIndexes()
	{
		_visualLineTree.Clear();
		var buffer = new int[_pieceTable.LineFeedCount];
		Array.Fill(buffer, 1);
		_visualLineTree.Build(buffer);
	}


	public int VisualLinesPrefixSum(int index)
	{
		return _visualLineTree.PrefixSum(index);
	}
	public void SetVisualLineCount(int index, int newValue)
	{
		_visualLineTree.SetVisualLineCount(index, newValue);
	}

	public int FindByPrefixSum(int target, out int prefixSum)
	{
		return _visualLineTree.FindByPrefixSum(target, out prefixSum);
	}


	public int TotalVisualLines()
	{
		return VisualLinesPrefixSum(_pieceTable.LineFeedCount);
	}

	public int VisualLineCountAt(int index)
	{
		return _visualLineTree[index];
	}
}


public readonly record struct VisualLineTreeUpdatedParams(int lineIndex, int delta);
