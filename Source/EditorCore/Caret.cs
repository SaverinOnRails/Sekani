namespace Sekani.EditorCore;

public class Coordinate(int col, int line)
{
	public int Col { get; private set; } = col;
	public int Line { get; private set; } = line;
	public bool TrailVisualLine { get; set; } = false;
	public Coordinate? RangeEnd = null;

	public override string ToString()
	{
		return $"({Col},{Line}). TrailVisualLine: {TrailVisualLine}";
	}

	public bool HasRange() => RangeEnd != null;
	public Coordinate? GreaterRangeEnd()
	{
		return GreaterRandeEndInner(out bool thisWasGreaterRangeEnd);
	}

	public void SetCoord(int col, int line)
	{
		Col = col;
		Line = line;
	}

	private Coordinate? GreaterRandeEndInner(out bool thisWasGreaterRangeEnd)
	{
		thisWasGreaterRangeEnd = false;
		if (RangeEnd is null) return null;
		if (Line > RangeEnd.Line)
		{
			thisWasGreaterRangeEnd = true;
			return this;
		}
		if (Line == RangeEnd.Line && Col > RangeEnd.Col)
		{
			thisWasGreaterRangeEnd = true;
			return this;
		}
		return RangeEnd;
	}

	public Coordinate? LesserRangeEnd()
	{
		var greaterRangeEnd = GreaterRandeEndInner(out bool thisWasGreaterRangeEnd);
		if (thisWasGreaterRangeEnd) return RangeEnd;
		return this;
	}
}


public readonly record struct VisualCoordinate(int VisualCol, int VisualLine);
