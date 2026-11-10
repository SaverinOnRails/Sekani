using Sekani.EditorCore.Utils;
namespace Sekani.EditorCore;

public sealed class LineCache
{
	private readonly PieceTable _pieceTable;
	private readonly List<LineLayout?> _layoutCache = [];
	public int Width { get; private set; } = 0;
	private readonly int _tabSize;
	private readonly bool _wordWrap;
	private int _maxVisualColsPerLine;
	public event EventHandler<VisualLineTreeUpdatedParams> VisualLineTreeUpdated;

	private VisualLineTree _visualLineTree = new();

	internal LineCache(PieceTable pieceTable, int tabSize, bool wordWrap = false, int maxVisualColsPerLine = 0)
	{
		_tabSize = tabSize;
		_pieceTable = pieceTable;
		_wordWrap = wordWrap;
		_maxVisualColsPerLine = maxVisualColsPerLine;
		// _buffer.BufferChangedEvent += BufferChanged;
		_pieceTable.BufferLineChangedEvent += PieceTableLineChanged;
		// RecalculateWidth();
	}

	private void PieceTableLineChanged(object? _, BufferLineChangeData e)
	{
		if (e.Kind == BufferLineChangeKind.LineRemoved)
		{
			int startIndex = e.Range.First;
			int count = e.Range.Last.HasValue
				? (e.Range.Last.Value - e.Range.First + 1)
				: 1;

			if (count > 0 && startIndex < _visualLineTree.Count)
			{
				int removeCount = Math.Min(count, _visualLineTree.Count - startIndex);
				_visualLineTree.RemoveRange(startIndex, removeCount);
				if (startIndex < _layoutCache.Count)
				{
					int layoutRemoveCount = Math.Min(removeCount, _layoutCache.Count - startIndex);
					_layoutCache.RemoveRange(startIndex, layoutRemoveCount);
				}
			}
		}
		else if (e.Kind == BufferLineChangeKind.LineAdded)
		{
			int startIndex = e.Range.First;
			int count = e.Range.Last.HasValue
				? (e.Range.Last.Value - e.Range.First + 1)
				: 1;

			if (count > 0)
			{
				_visualLineTree.InsertRange(startIndex, count, defaultValue: 1);
				if (startIndex <= _layoutCache.Count)
				{
					_layoutCache.InsertRange(startIndex, Enumerable.Repeat<LineLayout?>(null, count));
				}
			}
		}
		else if (e.Kind == BufferLineChangeKind.LineChanged)
		{
			int end = e.Range.Last ?? e.Range.First;
			for (int i = e.Range.First; i <= end; i++)
			{
				InvalidateLayout(i);
			}
		}
	}
	private void UpdateDocumentWidth(int width) => Width = Math.Max(Width, width);


	public LineLayout? GetOrCreate(int index)
	{
		if (index > _pieceTable.LineFeedCount) return null;
		//pad to fill up
		while (_layoutCache.Count <= index)
			_layoutCache.Insert(_layoutCache.Count, null);
		while (_visualLineTree.Count <= index)
			_visualLineTree.Insert(_visualLineTree.Count, 1);
		if (_layoutCache[index] != null) return _layoutCache[index];
		var lineLayout = new LineLayout(
			_pieceTable.PrintCleanLine(index),
			_tabSize,
			_wordWrap,
			_maxVisualColsPerLine);
		_layoutCache[index] = lineLayout;
		UpdateDocumentWidth(lineLayout.VisualText.Length);
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
	// 	while (_lyoutCache.Count <= index)
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
		Width = 0;
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

