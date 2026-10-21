namespace Sekani.EditorCore;

public class Coordinate
{
	public int Col { get; private set; } //zero indexed utf-16 code point offset
	public int Line { get; private set; }
	public bool TrailVisualLine { get; set; } = false;
	public Coordinate? Anchor = null;

	public override string ToString()
	{
		return $"({Col},{Line}). TrailVisualLine: {TrailVisualLine}";
	}

	public Coordinate(int col, int line)
	{
		Col = col;
		Line = line;
	}
}


public readonly record struct VisualCoordinate(int VisualCol, int VisualLine);
