
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
	public Range(int anchor, int? head = null, int? cursor = null, bool trailVisualLine = false)
	{
		Anchor = anchor;
		if (cursor is null)
		{

			Cursor = anchor;
		}
		else
		{
			Cursor = cursor.Value;
		}
		_head = head;
		TrailVisualLine = trailVisualLine;
	}

	public int Lower => Math.Min(Anchor, Head);
	public int Higher => Math.Max(Anchor, Head);
}
