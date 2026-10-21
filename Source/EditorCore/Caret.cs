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

	public override bool Equals(object? obj)
	{
		return obj is Coordinate other && this == other;
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Col, Line);
	}

	public static bool operator ==(Coordinate? a, Coordinate? b)
	{
		if (ReferenceEquals(a, b))
			return true;

		if (a is null || b is null)
			return false;

		return a.Col == b.Col && a.Line == b.Line;
	}

	public static bool operator !=(Coordinate? a, Coordinate? b)
	{
		return !(a == b);
	}
}


public readonly record struct VisualCoordinate(int VisualCol, int VisualLine);
