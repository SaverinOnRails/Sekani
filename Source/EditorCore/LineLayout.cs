using System.Globalization;
using Wcwidth;
using System.Text;
using System.Diagnostics;
namespace Sekani.EditorCore;

public record LineLayout
{
	public string VisualText { get; private set; } = "";
	private string _logicalText = "";
	public string LogicalText => _logicalText;
	private readonly int _tabSize;
	private List<VisualLine> _visualLines = [];
	public IReadOnlyList<VisualLine> VisualLines => _visualLines;
	private bool _wrap;
	private int _maxVisualColsPerLine;
	private List<int> _graphemeBoundaries = new();
	public IReadOnlyList<int> GraphemeBoundaries => _graphemeBoundaries;
	private List<int> _logicalOffsetToVisualOffsetArena = [];
	public IReadOnlyList<int> LogicalOffsetToVisualOffsetArena => _logicalOffsetToVisualOffsetArena;

	public LineLayout(string text, int tabSize, bool wrap, int maxVisualColsPerLine)
	{
		_tabSize = tabSize;
		_wrap = wrap;
		_maxVisualColsPerLine = maxVisualColsPerLine;
		DoLayout(text);
	}

	public int GraphemeWidth(ReadOnlySpan<char> grapheme, int visualCol)
	{
		if (grapheme.SequenceEqual("\t")) return VisualTabWidth(visualCol, _tabSize);
		if (grapheme.SequenceEqual("\n")) return 1;
		var width = UnicodeCalculator.GetWidth(grapheme.ToString());
		return width;
	}

	private void DoLayout(string text)
	{
		_logicalText = text;
		int visualCol = 0;
		int logicalLineStartOffset = 0;
		int visualLineStartOffset = 0;
		TextElementEnumerator enumerator =
				StringInfo.GetTextElementEnumerator(_logicalText);

		var builder = new StringBuilder();
		var index = 0;
		var visualOffset = 0;
		_logicalOffsetToVisualOffsetArena.Add(0);
		int chunkStartIndex = 0;
		while (enumerator.MoveNext())
		{
			var grapheme = enumerator.GetTextElement();
			index = enumerator.ElementIndex;
			_graphemeBoundaries.Add(index);
			int width = GraphemeWidth(grapheme, visualCol);
			if (_wrap && visualCol + width > _maxVisualColsPerLine && index > logicalLineStartOffset)
			{
				int chunkLength = _logicalOffsetToVisualOffsetArena.Count - chunkStartIndex;
				_visualLines.Add(
					new VisualLine(
						LogicalOffset: logicalLineStartOffset,
						LogicalLength: index - logicalLineStartOffset,
						VisualOffset: visualLineStartOffset,
						VisualLength: builder.Length - visualLineStartOffset,
						ArenaStartIndex: chunkStartIndex,
						ArenaLength: chunkLength
					));
				chunkStartIndex = _logicalOffsetToVisualOffsetArena.Count;
				_logicalOffsetToVisualOffsetArena.Add(0);
				logicalLineStartOffset = index;
				visualLineStartOffset = builder.Length;
				visualCol = 0;
				visualOffset = 0;
				width = GraphemeWidth(grapheme, visualCol);
			}

			if (grapheme == "\t")
			{
				builder.Append(' ', width);
				visualOffset += width;
				_logicalOffsetToVisualOffsetArena.Add(visualOffset);
			}
			else
			{
				builder.Append(grapheme);
				for (int i = 0; i < grapheme.Length; i++)
				{
					visualOffset += 1;
					_logicalOffsetToVisualOffsetArena.Add(visualOffset);
				}
			}
			visualCol += width;
		}
		//if this line does not end with a line break(last line in buffer), still add an offset for it at the next utf18 offset to grapheme boundaries so the cursor can visit it
		if (_logicalText.LastOrDefault() != '\n')
		{
			_graphemeBoundaries.Add(index + 1);
		}
		_visualLines.Add(
			new VisualLine(
				LogicalOffset: logicalLineStartOffset,
				LogicalLength: text.Length - logicalLineStartOffset,
				VisualOffset: visualLineStartOffset,
				VisualLength: builder.Length - visualLineStartOffset,
				ArenaStartIndex: chunkStartIndex,
				ArenaLength: _logicalOffsetToVisualOffsetArena.Count - chunkStartIndex
			));

		VisualText = builder.ToString();
	}

	public VisualCoordinate GetVisualCoordinate(Coordinate coord, bool forceTrailVisualLine = false)
	{
		var logicalCol = AlignColumn(coord.Col);
		var targetVisualLine =
			GetVisualLine(logicalCol, out int targetVlineIndex);

		if (targetVisualLine is null)
			return new(0, 0);
		var logicalOffset = targetVisualLine.Value.LogicalOffset;
		int visualCol = 0;
		var graphemeBounaryIndexStart = GraphemeBoundaryIndexOf(logicalOffset);
		var logicalEnd = logicalOffset + targetVisualLine.Value.LogicalLength;
		var graphemeBounaryIndexEnd = GraphemeBoundaryIndexOf(AlignColumn(logicalEnd));

		if (graphemeBounaryIndexStart == -1) return new(0, targetVlineIndex);
		for (int i = graphemeBounaryIndexStart; i < graphemeBounaryIndexEnd; i++)
		{
			int offset = _graphemeBoundaries[i];
			if (offset == logicalCol)
				return new(visualCol, targetVlineIndex);

			int next = _graphemeBoundaries[i + 1];
			var g = GraphemeAt(offset);
			visualCol += GraphemeWidth(g, visualCol);
		}
		if ((coord.TrailVisualLine || forceTrailVisualLine) && visualCol == targetVisualLine.Value.VisualLength && targetVlineIndex + 1 < _visualLines.Count)
		{
			return new(0, targetVlineIndex + 1);
		}
		return new(visualCol, targetVlineIndex);

	}

	private int AlignColumn(int col)
	{
		int index = _graphemeBoundaries.BinarySearch(col);
		if (index >= 0)
			return _graphemeBoundaries[index];
		index = ~index - 1;
		return index >= 0 ? _graphemeBoundaries[index] : 0;

	}
	public int? NextCol(int col)
	{
		int index = _graphemeBoundaries.BinarySearch(col);
		if (index >= 0)
			index++;
		else
			index = ~index;
		return index < _graphemeBoundaries.Count
			? _graphemeBoundaries[index]
			: null;
	}

	public int? PrevCol(int col)
	{
		col = AlignColumn(col);
		int index = _graphemeBoundaries.BinarySearch(col) - 1;
		return index >= 0
			? _graphemeBoundaries[index]
			: null;
	}
	public int GetLogicalColumn(VisualCoordinate coord)
	{
		if (coord.VisualLine < 0 || coord.VisualLine >= _visualLines.Count)
			return 0;
		var visualLine = _visualLines[coord.VisualLine];
		var logicalOffset = visualLine.LogicalOffset;
		int visualCol = 0;
		var graphemeBounaryIndexStart = GraphemeBoundaryIndexOf(logicalOffset);
		var logicalEnd = logicalOffset + visualLine.LogicalLength;
		var graphemeBounaryIndexEnd = GraphemeBoundaryIndexOf(AlignColumn(logicalEnd));

		if (graphemeBounaryIndexStart == -1) return 0;
		for (int i = graphemeBounaryIndexStart; i < graphemeBounaryIndexEnd; i++)
		{
			int offset = _graphemeBoundaries[i];
			int next = _graphemeBoundaries[i + 1];
			var width = GraphemeWidth(GraphemeAt(offset), visualCol);
			if (coord.VisualCol < visualCol + width)
				return offset;
			visualCol += width;
		}
		return _graphemeBoundaries[graphemeBounaryIndexEnd];
	}
	private VisualLine? GetVisualLine(int logicalCol, out int index)
	{
		for (int i = 0; i < _visualLines.Count; i++)
		{
			var vl = _visualLines[i];

			if (logicalCol >= vl.LogicalOffset &&
				logicalCol <= vl.LogicalOffset + vl.LogicalLength)
			{
				index = i;
				return vl;
			}
		}

		index = 0;
		return null;
	}

	public int GraphemeBoundaryIndexOf(int col)
	{
		return _graphemeBoundaries.BinarySearch(col);
	}

	public static int VisualTabWidth(int visualCol, int tabSize)
			=> tabSize - (visualCol % tabSize);

	public ReadOnlySpan<char> GraphemeAt(int column)
	{
		if (_logicalText.Length == 0) return "\n";
		//this is stupid, doing a binary search twice here
		column = AlignColumn(column);
		int index = _graphemeBoundaries.BinarySearch(column);
		int start = column;
		int end = index + 1 < _graphemeBoundaries.Count
			? _graphemeBoundaries[index + 1]
			: _logicalText.Length;
		return _logicalText.AsSpan(start, end - start);
	}
}
public readonly record struct VisualLine(
	int LogicalOffset,
	int LogicalLength,
	int VisualOffset,
	int VisualLength,
	int ArenaStartIndex,
	int ArenaLength
);

