namespace Sekani.EditorCore;

public sealed class LineCache
{
	private readonly SekaniBuffer _buffer;
	private readonly Dictionary<Line, LineLayout> _layoutCache = [];
	public IReadOnlyList<CacheBlock> CacheBlocks => _cacheBlocks;
	public int Width { get; private set; }
	private Line? _longestLine;
	private readonly int _tabSize;
	private readonly bool _wordWrap;
	private int _maxVisualColsPerLine;
	private readonly int _cacheBlockSize = 10_000;
	public int CacheBlockSize => _cacheBlockSize;
	private List<CacheBlock> _cacheBlocks = [];

	public LineCache(SekaniBuffer buffer, int tabSize, bool wordWrap = false, int maxVisualColsPerLine = 0)
	{
		_buffer = buffer;
		_tabSize = tabSize;
		_wordWrap = wordWrap;
		_maxVisualColsPerLine = maxVisualColsPerLine;
		_buffer.BufferChangedEvent += BufferChanged;
	}

	private void BufferChanged(object? sender, BufferChangeData e)
	{
		var line = _buffer.GetLine(e.Line);
		if (line is null)
			return;

		if (_longestLine is null ||
			line.Text.Length >= Width)
		{
			_longestLine = line;
			Width = line.Text.Length;
		}
		else if (line == _longestLine)
		{
			RecalculateWidth();
		}
		InvalidateLayout(line);
	}

	private void RecalculateWidth()
	{
		_longestLine = null;
		Width = 0;
		foreach (var line in _buffer.Lines)
		{
			if (line.Text.Length > Width)
			{
				_longestLine = line;
				Width = line.Text.Length;
			}
		}
	}

	public LineLayout? GetOrCreate(Line line)
	{
		if (_layoutCache.TryGetValue(line, out var layout))
			return layout;

		var lineLayout = new LineLayout(line, _tabSize, _wordWrap, _maxVisualColsPerLine);
		_layoutCache[line] = lineLayout;

		return lineLayout;
	}
	private void InvalidateLayout(Line line)
	{
		_layoutCache.Remove(line);
	}

	private void InvalidateAll()
	{
		_layoutCache.Clear();
	}

	public void SetMaxVisualColsForWrap(int maxVisualColsPerLine)
	{
		_maxVisualColsPerLine = maxVisualColsPerLine;
		InvalidateAll();
	}

	//yes this is very slow, I Know. Actively build every cache block
	public void BuildCacheBlocks()
	{
		_cacheBlocks.Clear();
		for (int i = 0; i < _buffer.Lines.Count; i++)
		{
			int blockIndex = i / _cacheBlockSize;

			var lineLayout = new LineLayout(
				_buffer.Lines[i],
				_tabSize,
				_wordWrap,
				_maxVisualColsPerLine);

			if (blockIndex == _cacheBlocks.Count)
				_cacheBlocks.Add(new CacheBlock());

			_cacheBlocks[blockIndex].VisualLinesSum +=
				lineLayout.VisualLines.Count;
		}
	}

	public int TotalVisualLinesBeforeLine(Coordinate logicalCoords)
	{
		int lineIndex = logicalCoords.Line;

		if (lineIndex <= 0)
			return 0;

		int blockIndex = lineIndex / _cacheBlockSize;

		int total = 0;

		//every block before containing block
		for (int i = 0; i < blockIndex; i++)
		{
			total += _cacheBlocks[i].VisualLinesSum;
		}

		int blockStartIndex = blockIndex * _cacheBlockSize;
		for (int i = blockStartIndex; i < lineIndex; i++)
		{
			var layout = GetOrCreate(_buffer.Lines[i]);

			if (layout is not null)
				total += layout.VisualLines.Count;
		}

		return total;
	}

	public int LineIndexAtVisualLine(int targetVisualLine, out int visualLineOffsetOfLineStart)
	{
		if (targetVisualLine <= 0)
		{
			visualLineOffsetOfLineStart = 0;
			return 0;
		}
		int blockIndex = 0;
		int visualLineCount = 0;
		for (; blockIndex < _cacheBlocks.Count; blockIndex++)
		{
			int blockSum = _cacheBlocks[blockIndex].VisualLinesSum;
			if (visualLineCount + blockSum > targetVisualLine)
				break;
			visualLineCount += blockSum;
		}
		int lineIndex = blockIndex * _cacheBlockSize;
		int lineLimit = Math.Min(lineIndex + _cacheBlockSize, _buffer.Lines.Count);
		for (; lineIndex < lineLimit; lineIndex++)
		{
			var layout = GetOrCreate(_buffer.Lines[lineIndex]);
			int count = layout?.VisualLines.Count ?? 0;
			if (visualLineCount + count > targetVisualLine)
				break;
			visualLineCount += count;
		}

		visualLineOffsetOfLineStart = visualLineCount;
		return Math.Min(lineIndex, _buffer.Lines.Count - 1);
	}
}


public class CacheBlock
{
	public int VisualLinesSum { get; set; }

}
