namespace Sekani.EditorCore;

public class Coordinate
{
	public int Col { get; private set; }
	public int Line { get; private set; }
	public bool TrailVisualLine { get; set; } = false;
	public Coordinate? Anchor = null;

	public override string ToString()
	{
		return $"({Col},{Line}). TrailVisualLine: {TrailVisualLine}";
	}

	public bool HasRange() => Anchor != null;
	public Coordinate? GreaterRangeEnd()
	{
		return GreaterRandeEndInner(out bool thisWasGreaterRangeEnd);
	}
	public Coordinate(int col, int line)
	{
		Col = col;
		Line = line;
	}

	public Coordinate(Coordinate c)
	{
		Col = c.Col;
		Line = c.Line;
	}

	public void SetCoord(int col, int line)
	{
		Col = col;
		Line = line;
	}

	public void SetCoord(Coordinate c)
	{
		Col = c.Col;
		Line = c.Line;
	}

	private Coordinate? GreaterRandeEndInner(out bool thisWasGreaterRangeEnd)
	{
		thisWasGreaterRangeEnd = false;
		if (Anchor is null) return null;
		if (Line > Anchor.Line)
		{
			thisWasGreaterRangeEnd = true;
			return this;
		}
		if (Line == Anchor.Line && Col > Anchor.Col)
		{
			thisWasGreaterRangeEnd = true;
			return this;
		}
		return Anchor;
	}

	public Coordinate? LesserRangeEnd()
	{
		var greaterRangeEnd = GreaterRandeEndInner(out bool thisWasGreaterRangeEnd);
		if (thisWasGreaterRangeEnd) return Anchor;
		return this;
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
