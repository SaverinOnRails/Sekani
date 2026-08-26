namespace Sekani.EditorCore;

public class Coordinate(int col, int line)
{
	public int Col { get; set; } = col;
	public int Line { get; set; } = line;
}

public class VisualCoordinate(int col, int line) : Coordinate(col, line)
{

};
