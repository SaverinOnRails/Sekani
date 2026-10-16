using System.Globalization;
using Wcwidth;
using System.Text;
using System.Diagnostics;
namespace Sekani.EditorCore;

public class LineLayout
{
	public string VisualText { get; set; } = "";
	private string _logicalText = "";
	private readonly int _tabSize;
	private List<VisualLine> _visualLines = [];
	public IReadOnlyList<VisualLine> VisualLines => _visualLines;
	private bool _wrap;
	private int _maxVisualColsPerLine;
	private List<int> _graphemeBoundaries = new();
	public IReadOnlyList<int> GraphemeBoundaries => _graphemeBoundaries;

	public LineLayout(Line line, int tabSize, bool wrap, int maxVisualColsPerLine)
	{
		_tabSize = tabSize;
		_wrap = wrap;
		_maxVisualColsPerLine = maxVisualColsPerLine;
		DoLayout(line);
	}

	private int GraphemeWidth(string grapheme, int visualCol)
	{
		if (grapheme == "\t") return VisualTabWidth(visualCol, _tabSize);
		if (grapheme == "\n") return 1;
		var width = UnicodeCalculator.GetWidth(grapheme);
		return width;
	}

	private void DoLayout(Line line)
	{
		_logicalText = line.Text;
		int visualCol = 0;
		int logicalLineStartOffset = 0;
		int visualLineStartOffset = 0;
		TextElementEnumerator enumerator =
				StringInfo.GetTextElementEnumerator(_logicalText);

		var builder = new StringBuilder();
		var index = 0;
		while (enumerator.MoveNext())
		{
			var grapheme = enumerator.GetTextElement();
			index = enumerator.ElementIndex;
			logicalLineStartOffset = index;
			_graphemeBoundaries.Add(index);

			int width = GraphemeWidth(grapheme, visualCol);
			if (_wrap && visualCol + width > _maxVisualColsPerLine && index > logicalLineStartOffset)
			{
				_visualLines.Add(
					new VisualLine(
						LogicalOffset: logicalLineStartOffset,
						LogicalLength: index - logicalLineStartOffset,
						VisualOffset: visualLineStartOffset,
						VisualLength: builder.Length - visualLineStartOffset));
				logicalLineStartOffset = index;
				visualLineStartOffset = builder.Length;
				visualCol = 0;
				width = GraphemeWidth(grapheme, visualCol);
			}

			if (grapheme == "\t")
				builder.Append(' ', width);
			else
				builder.Append(grapheme);
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
				LogicalLength: line.Text.Length - logicalLineStartOffset,
				VisualOffset: visualLineStartOffset,
				VisualLength: builder.Length - visualLineStartOffset));

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
		var graphemeBounaryIndexStart = _graphemeBoundaries.IndexOf(logicalOffset);
		var logicalEnd = logicalOffset + targetVisualLine.Value.LogicalLength;
		var graphemeBounaryIndexEnd = AlignColumn(logicalEnd);

		if (graphemeBounaryIndexStart == -1) return new(0, targetVlineIndex);
		for (int i = graphemeBounaryIndexStart; i < graphemeBounaryIndexEnd; i++)
		{
			if (i == logicalCol)
			{
				return new(visualCol, targetVlineIndex);
			}
			visualCol += GraphemeWidth(_logicalText[i].ToString(), visualCol);
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
	public int NextCol(int col)
	{
		int index = _graphemeBoundaries.BinarySearch(col);
		if (index >= 0)
			index++;
		else
			index = ~index;
		return index < _graphemeBoundaries.Count
			? _graphemeBoundaries[index]
			: col;
	}
	// public int GetLogicalColumn(VisualCoordinate coord)
	// {
	// 	if (coord.VisualLine < 0 || coord.VisualLine >= _visualLines.Count)
	// 		return 0;
	// 	var visualLine = _visualLines[coord.VisualLine];
	// 	var logicalOffset = visualLine.LogicalOffset;
	// 	int visualCol = 0;
	// 	var graphemeBounaryIndexStart = _graphemeBoundaries.IndexOf(logicalOffset);
	// 	var logicalEnd = logicalOffset + visualLine.LogicalLength;
	// 	var graphemeBounaryIndexEnd = AlignColumn(logicalEnd);
	// 	int currentVisualCol = 0;

	// 	// if (graphemeBounaryIndexStart == -1) return new(0, targetVlineIndex);
	// 	for (int i = graphemeBounaryIndexStart; i < graphemeBounaryIndexEnd; i++)
	// 	{
	// 		if (visualCol == coord.VisualCol) return i;
	// 		currentVisualCol += GraphemeWidth(_logicalText[i].ToString(), visualCol);
	// 	}
	// 	return graphemeBounaryIndexEnd;
	// }
	public int GetLogicalColumn(VisualCoordinate coord)
	{
		if (coord.VisualLine < 0 || coord.VisualLine >= _visualLines.Count)
			return 0;

		var visualLine = _visualLines[coord.VisualLine];
		int start = visualLine.LogicalOffset;
		int end = start + visualLine.LogicalLength;

		// Exclude the line break from the visible range.
		if (end > start && _logicalText[end - 1] == '\n') end--;

		int currentVisualCol = 0;
		for (int i = start; i < end; i++)
		{
			int width = _logicalText[i] == '\t'
				? VisualTabWidth(currentVisualCol, _tabSize)
				: 1;

			if (coord.VisualCol < currentVisualCol + width)
				return i;

			currentVisualCol += width;
		}
		return end;
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

	public static int VisualTabWidth(int visualCol, int tabSize)
			=> tabSize - (visualCol % tabSize);
}
public readonly record struct VisualLine(
	int LogicalOffset,
	int LogicalLength,
	int VisualOffset,
	int VisualLength);

