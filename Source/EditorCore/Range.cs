
namespace Sekani.EditorCore;

public class Range
{
	public int Anchor { get; }
	private int? _head;

	public int Cursor { get; set; }

	public int Head
	{
		get => _head ?? Anchor + 1;
		set => _head = value;
	}
	public Range(int anchor, int? head = null)
	{
		Anchor = anchor;
		Cursor = anchor;
		_head = head;
	}

	public int Lower => Math.Min(Anchor, Head);
	public int Higher => Math.Max(Anchor, Head);
}
