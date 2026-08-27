using System.Text;
namespace Sekani.EditorCore;

public class LineLayout
{
	public string LineText { get; set; } = "";
	private readonly int _tabSize;
	private List<int> _logicalToVisualList = [];
	public LineLayout(Line line, int tabSize)
	{
		_tabSize = tabSize;
		SetText(line);
	}

	private void SetText(Line line)
	{
		int visualCol = 0;
		var builder = new StringBuilder();
		for (int i = 0; i < line.Text.Length; i++)
		{
			var width = line.Text[i] == '\t' ? VisualTabWidth(visualCol, _tabSize) : 1;
			_logicalToVisualList.Add(visualCol);
			if (line.Text[i] == '\t')
			{
				builder.Append(' ', width);
			}
			else
			{
				builder.Append(line.Text[i]);
			}
			visualCol += width;
		}
		_logicalToVisualList.Add(visualCol); //for caret at the end of line
		LineText = builder.ToString();
	}

	public int GetVisualCol(int logicalCol)
	{
		if (logicalCol >= _logicalToVisualList.Count) return _logicalToVisualList.Last();
		return _logicalToVisualList[logicalCol];
	}

	public static int VisualTabWidth(int visualCol, int tabSize)
		=> tabSize - (visualCol % tabSize);
}


