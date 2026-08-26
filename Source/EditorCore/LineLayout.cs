namespace Sekani.EditorCore;

public class LineLayout
{
	public string LineText { get; set; }

	public LineLayout(Line line)
	{
		LineText = line.Text;
	}
}


