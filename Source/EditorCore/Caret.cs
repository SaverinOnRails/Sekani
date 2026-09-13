namespace Sekani.EditorCore;

public class Coordinate(int col, int line)
{
	public int Col { get; } = col;
	public int Line { get; } = line;
	public bool TrailVisualLine { get; set; } = false;
	public Coordinate? RangeEnd = null;

	public override string ToString()
	{
		return $"({Col},{Line}). TrailVisualLine: {TrailVisualLine}";
	}

	public bool HasRange() => RangeEnd != null;
}


public class VisualCoordinate(int col, int line) : Coordinate(col, line)
{

}
