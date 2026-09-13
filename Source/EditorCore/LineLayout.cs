using System.Text;
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
	public LineLayout(Line line, int tabSize, bool wrap, int maxVisualColsPerLine)
	{
		_tabSize = tabSize;
		_wrap = wrap;
		_maxVisualColsPerLine = maxVisualColsPerLine;
		DoLayout(line);
	}
	private void DoLayout(Line line)
	{
		_logicalText = line.Text;
		int visualCol = 0;
		int logicalLineStart = 0;
		int visualLineStart = 0;

		var builder = new StringBuilder();

		for (int i = 0; i < line.Text.Length; i++)
		{
			char c = line.Text[i];

			int width = c == '\t'
				? VisualTabWidth(visualCol, _tabSize)
				: 1;

			if (_wrap &&
				visualCol + width > _maxVisualColsPerLine &&
				i > logicalLineStart)
			{
				_visualLines.Add(
					new VisualLine(
						LogicalOffset: logicalLineStart,
						LogicalLength: i - logicalLineStart,
						VisualOffset: visualLineStart,
						VisualLength: builder.Length - visualLineStart));

				logicalLineStart = i;
				visualLineStart = builder.Length;
				visualCol = 0;

				width = c == '\t'
					? VisualTabWidth(visualCol, _tabSize)
					: 1;
			}

			if (c == '\t')
				builder.Append(' ', width);
			else
				builder.Append(c);

			visualCol += width;
		}

		_visualLines.Add(
			new VisualLine(
				LogicalOffset: logicalLineStart,
				LogicalLength: line.Text.Length - logicalLineStart,
				VisualOffset: visualLineStart,
				VisualLength: builder.Length - visualLineStart));

		VisualText = builder.ToString();
	}
	public VisualCoordinate GetVisualCoordinate(Coordinate coord , bool forceTrailVisualLine = false)
	{
		var logicalCol = coord.Col;
		var targetVisualLine =
			GetVisualLine(logicalCol, out int targetVlineIndex);

		if (targetVisualLine is null)
			return new(0, 0);

		var visualLine = targetVisualLine.Value;

		int visualCol = 0;

		for (int i = visualLine.LogicalOffset;
			 i < visualLine.LogicalOffset + visualLine.LogicalLength;
			 i++)
		{
			if (i == logicalCol)
			{
				return new(visualCol, targetVlineIndex);
			}

			visualCol += _logicalText[i] == '\t'
				? VisualTabWidth(visualCol, _tabSize)
				: 1;
		}
		if ((coord.TrailVisualLine || forceTrailVisualLine) && visualCol == targetVisualLine.Value.VisualLength && targetVlineIndex + 1 < _visualLines.Count)
		{
			return new(0, targetVlineIndex + 1);
		}
		return new(visualCol, targetVlineIndex);
	}

	public int GetLogicalColumn(VisualCoordinate coord)
	{
		if (coord.VisualLine < 0 || coord.VisualLine >= _visualLines.Count)
			return 0;
		var visualLine = _visualLines[coord.VisualLine];
		int currentVisualCol = 0;
		for (int i = visualLine.LogicalOffset;
			 i < visualLine.LogicalOffset + visualLine.LogicalLength;
			 i++)
		{
			int width = _logicalText[i] == '\t'
				? VisualTabWidth(currentVisualCol, _tabSize)
				: 1;

			if (coord.VisualCol < currentVisualCol + width)
				return i;

			currentVisualCol += width;
		}
		return visualLine.LogicalOffset + visualLine.LogicalLength;
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

