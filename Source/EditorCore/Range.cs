
namespace Sekani.EditorCore;

public readonly struct Range
{
	public int Anchor { get; }
	private readonly int? _head;

	public int Cursor { get; }
	public bool TrailVisualLine { get; } = false;

	public int Head
	{
		get => _head ?? Anchor + 1;
	}

	public bool HasActiveSelection => _head is not null;

	public Range(int cursor, bool trailVisualLine = false)
	{
		Anchor = cursor;
		Cursor = cursor;
		_head = null;
		TrailVisualLine = trailVisualLine;
	}

	public Range(
		int anchor,
		int head,
		int cursor,
		bool trailVisualLine = false)
	{
		Anchor = anchor;
		_head = head;
		Cursor = cursor;
		TrailVisualLine = trailVisualLine;
	}

	public int Lower => Math.Min(Anchor, Head);
	public int Higher => Math.Max(Anchor, Head);
	public int Width => Higher - Lower;
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
