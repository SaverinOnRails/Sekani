namespace Sekani.EditorCore;

public delegate int GetPosAdvanceDelegate(int offset, int count);
public readonly record struct TextSpan(int Start, int End);

public readonly struct Range
{
	public int Anchor { get; }

	public bool TrailVisualLine { get; } = false;

	public int Head { get; }

	public bool HasActiveSelection => Anchor != Head;

	public Range(int anchor, bool trailVisualLine = false)
	{
		Anchor = anchor;
		Head = anchor;
		TrailVisualLine = trailVisualLine;
	}

	public Range(
		int anchor,
		int head,
		bool trailVisualLine = false)
	{
		Anchor = anchor;
		Head = head;
		TrailVisualLine = trailVisualLine;
	}

	public Range MoveHead(int newHead, GetPosAdvanceDelegate advanceFunc, bool trailVisualLine = false)
	{
		int anchor = Anchor;
		if (Head >= Anchor)
		{
			if (newHead < Anchor)
			{
				anchor = advanceFunc(Anchor, 1);
			}
		}
		else
		{
			int origin = advanceFunc(Anchor, -1);
			if (newHead >= origin)
			{
				anchor = origin;
			}
		}
		return new Range(anchor, newHead, trailVisualLine);
	}
	public TextSpan GetTextSpan(GetPosAdvanceDelegate advanceFunc)
	{
		if (!HasActiveSelection || Head == To)
		{
			return new(From, advanceFunc(To, 1));
		}
		return new(From, To);
	}
	public int From => Math.Min(Anchor, Head);
	public int To => Math.Max(Anchor, Head);
	public int Width => To - From;
}

public readonly record struct SelectionSet
{
	public List<Range> Ranges { get; }
	public int PrimaryIndex { get; }

	public SelectionSet(List<Range> ranges, int primaryIndex = 0)
	{
		Ranges = ranges ?? throw new ArgumentNullException(nameof(ranges));

		if (ranges.Count > 0 && (primaryIndex < 0 || primaryIndex >= ranges.Count))
			throw new ArgumentOutOfRangeException(nameof(primaryIndex));

		PrimaryIndex = primaryIndex;
		// Ranges = Normalize(ranges);
	}

	// private List<Range> Normalize(List<Range> range)
	// {

	// }
}
