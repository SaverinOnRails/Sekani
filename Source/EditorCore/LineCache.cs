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
	// private int[] _fenwickTree = [];

	//will see to using a fenwick tree here
	// private List<int> _visualLinesIndexes = [];
	private VisualLineTree _visualLineTree = new();
	// public IReadOnlyList<int> VisualLineIndexes => _visualLinesIndexes;

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
			Console.WriteLine("removed");
			// _visualLinesIndexes.RemoveAt(e.Line);
			// BuildFenwickTree();
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
			// if (e.Line <= _visualLinesIndexes.Count)
			// {
			// 	_visualLinesIndexes.Insert(e.Line, 1);
			// 	// BuildFenwickTree();
			// }
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

		// _visualLinesIndexes.Clear();
		// _visualLineTree.Clear();
		// int n = _buffer.Lines.Count;
		// for (int i = 0; i < n; i++)
		// {
		// 	_visualLinesIndexes.Add(1);
		// 	_visualLineTree.Insert(i, 1);
		// }
		// BuildFenwickTree();
	}

	//binary index tree stuff. Courtesy of chatgpt
	// public void BuildFenwickTree()
	// {
	// 	_fenwickTree = new int[_visualLinesIndexes.Count + 1];

	// 	for (int i = 0; i < _visualLinesIndexes.Count; i++)
	// 	{
	// 		AddFenwickTreeIndex(i, _visualLinesIndexes[i]);
	// 	}
	// }

	// private void AddFenwickTreeIndex(int index, int value)
	// {
	// 	// Fenwick tree uses 1-based indexing.
	// 	index++;
	// 	while (index < _fenwickTree.Length)
	// 	{
	// 		_fenwickTree[index] += value;
	// 		index += index & -index;
	// 	}
	// }

	public int VisualLinesPrefixSum(int index)
	{
		return _visualLineTree.PrefixSum(index);
		// int sum = 0;

		// while (index > 0)
		// {
		// 	sum += _fenwickTree[index];
		// 	index -= index & -index;
		// }

		// return sum;
	}
	public void SetVisualLineCount(int index, int newValue)
	{
		// int oldValue = _visualLinesIndexes[index];
		// _visualLinesIndexes[index] = newValue;
		// int difference = newValue - oldValue;
		// UpdateFenwickTree(index, difference);
		_visualLineTree.SetVisualLineCount(index, newValue);
	}

	// private void UpdateFenwickTree(int index, int difference)
	// {
	// 	index++;

	// 	while (index < _fenwickTree.Length)
	// 	{
	// 		_fenwickTree[index] += difference;
	// 		index += index & -index;
	// 	}
	// }

	public int FindByPrefixSum(int target, out int prefixSum)
	{
		return _visualLineTree.FindByPrefixSum(target, out prefixSum);
		// int index = 0;
		// prefixSum = 0;

		// int bit = HighestPowerOfTwoAtMost(_fenwickTree.Length - 1);

		// while (bit != 0)
		// {
		// 	int next = index + bit;

		// 	if (next < _fenwickTree.Length &&
		// 		prefixSum + _fenwickTree[next] <= target)
		// 	{
		// 		prefixSum += _fenwickTree[next];
		// 		index = next;
		// 	}

		// 	bit >>= 1;
		// }

		// return index;
	}

	private static int HighestPowerOfTwoAtMost(int value)
	{
		int result = 1;

		while (result << 1 <= value)
			result <<= 1;

		return result;
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


