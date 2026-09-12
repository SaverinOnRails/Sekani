using Sekani.EditorCore.Utils;

namespace Sekani.EditorCore;

public sealed class LineCache
{
	private readonly SekaniBuffer _buffer;
	private readonly Dictionary<Line, LineLayout> _layoutCache = [];
	public int Width { get; private set; }
	private Line? _longestLine;
	private readonly int _tabSize;
	private readonly bool _wordWrap;
	private int _maxVisualColsPerLine;

	private VisualLineTree _visualLineTree = new();

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
		//do this first as a removed line may no longer be in the buffer
		if (e.kind == BufferChangeKind.LineRemoved)
		{
			_visualLineTree.RemoveAt(e.Line);
		}

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
		if (e.kind == BufferChangeKind.LineAdded)
		{
			if (e.Line <= _visualLineTree.Count)
			{
				_visualLineTree.Insert(e.Line, 1);
			}
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
			while (_visualLineTree.Count <= index)
				_visualLineTree.Insert(_visualLineTree.Count, 1);

			if (_buffer.Lines[index.Value] != line)
			{
				SetVisualLineCount(index.Value,
					new LineLayout(
						line,
						_tabSize,
						_wordWrap,
						_maxVisualColsPerLine)
					.VisualLines.Count
					);
			}
			else
			{
				SetVisualLineCount(index.Value, lineLayout.VisualLines.Count);
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


	//This is the initial build call
	public void BuildVisualLinesIndexes()
	{
		_visualLineTree.Clear();
		var buffer = new int[_buffer.Lines.Count];
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
		return VisualLinesPrefixSum(_buffer.Lines.Count);
	}

	public int VisualLineCountAt(int index)
	{
		return _visualLineTree[index];
	}
}


