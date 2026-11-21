namespace Sekani.EditorCore;

public delegate int GetPosAdvanceDelegate(int offset, int count);
public readonly record struct TextSpan(int Start, int End);

public readonly struct Range
{
	public int Anchor { get; }

	//This is entirely for rendering and should not be here tbh. Does not affect any buffer or cursor operations
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

	//Not sure if this required the actual textspan
	public bool Intersects(Range other) => From <= other.To && other.From <= To;

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

	public Range Merge(Range other)
	{
		var newFrom = Math.Min(From, other.From);
		var newTo = Math.Max(To, other.To);

		int newAnchor, newHead;
		if (Head >= other.Head)
		{
			newAnchor = Anchor <= Head ? newFrom : newTo;
			newHead = Anchor <= Head ? newTo : newFrom;
		}
		else
		{
			newAnchor = other.Anchor <= other.Head ? newFrom : newTo;
			newHead = other.Anchor <= other.Head ? newTo : newFrom;
		}
		bool trailVisualLine = TrailVisualLine || other.TrailVisualLine;
		return new Range(newAnchor, newHead, trailVisualLine);
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
	public override string ToString()
	{
		return $"Anchor: {Anchor} , Head : {Head}";
	}
}
public readonly record struct SelectionSet
{
	public IReadOnlyList<Range> Ranges { get; }
	public int PrimaryIndex { get; }

	public SelectionSet(List<Range> ranges, int primaryIndex = 0)
	{
		ArgumentNullException.ThrowIfNull(ranges);
		if (ranges.Count > 0 && (primaryIndex < 0 || primaryIndex >= ranges.Count))
			throw new ArgumentOutOfRangeException(nameof(primaryIndex));
		if (ranges.Count == 0)
		{
			Ranges = [];
			PrimaryIndex = 0;
			return;
		}
		(Ranges, PrimaryIndex) = Normalize(ranges, primaryIndex);
	}

	public SelectionSet(SelectionSet selectionSet)
	{
		(Ranges, PrimaryIndex) = Normalize(selectionSet.Ranges.ToList(), selectionSet.PrimaryIndex);
	}

	public SelectionSet WithRange(int index, Range newRange)
	{
		var copy = Ranges.ToList();
		copy[index] = newRange;
		return new SelectionSet(copy, PrimaryIndex);
	}
	public SelectionSet WithPrimaryRange(Range newRange) => WithRange(PrimaryIndex, newRange);
	public SelectionSet WithHead(int index, int newHead, GetPosAdvanceDelegate advanceFunc, bool trailVisualLine = false)
	{
		var updated = Ranges[index].MoveHead(newHead, advanceFunc, trailVisualLine);
		return WithRange(index, updated);
	}

	public Range this[int index] => Ranges[index];
	public int Count => Ranges.Count;

	private static (List<Range> Ranges, int PrimaryIndex) Normalize(List<Range> ranges, int primaryIndex)
	{
		var sorted = ranges
			.Select((r, i) => (Range: r, IsPrimary: i == primaryIndex))
			.OrderBy(p => p.Range.From)
			.ToList();
		var finalRanges = new List<(Range Range, bool IsPrimary)> { sorted[0] };
		for (int i = 1; i < sorted.Count; i++)
		{
			var lastAccepted = finalRanges[^1];
			var current = sorted[i];
			if (lastAccepted.Range.Intersects(current.Range))
			{
				var merged = lastAccepted.Range.Merge(current.Range);
				finalRanges[^1] = (merged, lastAccepted.IsPrimary || current.IsPrimary);
			}
			else
			{
				finalRanges.Add(current);
			}
		}
		int newPrimaryIndex = finalRanges.FindIndex(p => p.IsPrimary);
		return (
			finalRanges.Select(p => p.Range).ToList(),
			newPrimaryIndex >= 0 ? newPrimaryIndex : 0
		);
	}
}
