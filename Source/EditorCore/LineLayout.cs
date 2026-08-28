using System.Text;
namespace Sekani.EditorCore;

public class LineLayout
{
	public string LineText { get; set; } = "";
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
		int visualCol = 0;
		int visualLineStart = 0;
		var builder = new StringBuilder();
		for (int i = 0; i < line.Text.Length; i++)
		{
			var width = line.Text[i] == '\t' ? VisualTabWidth(visualCol, _tabSize) : 1;

			if (_wrap && visualCol + width > _maxVisualColsPerLine && i > visualLineStart)
			{
				_visualLines.Add(new VisualLine(visualLineStart, i - visualLineStart));
				visualLineStart = i;
				visualCol = 0;
			}

			if (line.Text[i] == '\t')
				builder.Append(' ', width);
			else
				builder.Append(line.Text[i]);

			visualCol += width;
		}
		_visualLines.Add(new VisualLine(visualLineStart, line.Text.Length - visualLineStart));

		LineText = builder.ToString();
	}

	public VisualCoordinate GetVisualCoordinate(int logicalCol)
	{
        var targetVisualLine = GetVisualLine(logicalCol, out int targetVlineIndex);

        if (targetVisualLine is null)
			return new(0, 0);

		var visualLine = targetVisualLine.Value;

		int visualCol = 0;

		for (int i = visualLine.Offset;
			 i < visualLine.Offset + visualLine.Length;
			 i++)
		{
			if (i == logicalCol)
				return new(visualCol, targetVlineIndex);

			var width = LineText[i] == '\t'
				? VisualTabWidth(visualCol, _tabSize)
				: 1;

			visualCol += width;
		}

		return new(visualCol, _visualLines.Count - 1);
	}

	private VisualLine? GetVisualLine(int logicalCol, out int index)
	{
		for (int i = 0; i < _visualLines.Count; i++)
		{
			var vl = _visualLines[i];

			if (logicalCol >= vl.Offset &&
				logicalCol <= vl.Offset + vl.Length)
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
public readonly record struct VisualLine(int Offset, int Length);


