namespace Sekani.EditorCore;

public sealed class LineCache
{
	private SekaniBuffer _buffer;

	public int Width { get; private set; }
	public int _longestLineIndex = 0;

	public LineCache(SekaniBuffer buffer)
	{
		_buffer = buffer;
		_buffer.BufferChangedEvent += BufferChanged;
	}

	private void BufferChanged(object? sender, BufferChangeData e)
	{
		var line = _buffer.GetLine(e.Line);
		if (line is null) return;
		_longestLineIndex = e.Line;
		Width = Math.Max(Width, line.Text.Length);
	}


	private Dictionary<int, LineLayout> _layoutCache = new();

	public LineLayout? GetOrCreate(int index)
	{
		if (_layoutCache.TryGetValue(index, out LineLayout? layout))
		{
			return layout;
		}
		var line = _buffer.GetLine(index);
		if (line is null) return null;
		var lineLayout = new LineLayout(line);
		_layoutCache[index] = lineLayout;
		return lineLayout;
	}

	public void InvalidateLayout(int index)
	{

	}
}
