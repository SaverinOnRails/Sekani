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
	private bool _cacheBlocksSetup = false;
	private List<CacheBlock> _cacheBlocks = [];

	//will see to using a fenwick tree here
	private List<int> _visualLinesIndexes = [];
	public IReadOnlyList<int> VisualLineIndexes => _visualLinesIndexes;
	private CancellationTokenSource _cacheBlocksCancellationTokenSource = new();

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
		if (_cacheBlocksSetup)
		{
			if (e.kind == BufferChangeKind.LineChanged)
			{
				Console.WriteLine("line changed");
				int blockIndex = e.Line / _cacheBlockSize;
				// InvalidateBlock(blockIndex);
			}
		}
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

	public LineLayout? GetOrCreate(Line line, int? index = null)
	{
		if (!_layoutCache.TryGetValue(line, out var lineLayout))
		{
			lineLayout = new LineLayout(
				line,
				_tabSize,
				_wordWrap,
				_maxVisualColsPerLine);

			_layoutCache[line] = lineLayout;
		}
		if (index is not null)
		{
			// Pad to fill up
			while (_visualLinesIndexes.Count <= index)
				_visualLinesIndexes.Add(1);

			if (_buffer.Lines[index.Value] != line)
			{
				_visualLinesIndexes[index.Value] =
					new LineLayout(
						line,
						_tabSize,
						_wordWrap,
						_maxVisualColsPerLine)
					.VisualLines.Count;
			}
			else
			{
				_visualLinesIndexes[index.Value] = lineLayout.VisualLines.Count;
			}
		}
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

	public async Task BuildCacheBlocks()
	{
		_cacheBlocksCancellationTokenSource.Cancel();
		_cacheBlocksCancellationTokenSource = new();
		if (!_wordWrap)
			return;
		var token = _cacheBlocksCancellationTokenSource.Token;
		try
		{
			var blocks = await Task.Run(() =>
			{
				var result = new List<CacheBlock>();

				for (int i = 0; i < _buffer.Lines.Count; i++)
				{
					token.ThrowIfCancellationRequested();

					int blockIndex = i / _cacheBlockSize;

					if (blockIndex == result.Count)
						result.Add(new CacheBlock());

					var layout = new LineLayout(
						_buffer.Lines[i],
						_tabSize,
						_wordWrap,
						_maxVisualColsPerLine);

					result[blockIndex].VisualLinesSum +=
						layout.VisualLines.Count;
				}

				return result;
			}, token);
			_cacheBlocks = blocks;
			_cacheBlocksSetup = true;
		}
		catch
		{
			//do nothing
		}
	}

	public void BuildVisualLinesIndexes()
	{
		_visualLinesIndexes.Clear();
		int n = _buffer.Lines.Count;
		for (int i = 0; i < n; i++)
			_visualLinesIndexes.Add(1);
	}

	public int TotalVisualLinesBeforeLine(Coordinate logicalCoords)
	{
		if (!_wordWrap)
			return logicalCoords.Line;
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

	//thanks claude
	public int LineIndexAtVisualLine(int targetVisualLine, out int visualLineOffsetOfLineStart)
	{
		if (!_wordWrap)
		{
			int index = Math.Clamp(
				targetVisualLine,
				0,
				_buffer.Lines.Count - 1);

			visualLineOffsetOfLineStart = index;
			return index;
		}
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

	public int TotalVisualLines()
	{
		if (!_wordWrap)
			return _buffer.Lines.Count;

		return _visualLinesIndexes.Sum();
	}
}


public class CacheBlock
{
	public int VisualLinesSum { get; set; }

	public bool Dirty { get; set; } = true;

}
