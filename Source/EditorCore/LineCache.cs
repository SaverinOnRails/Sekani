namespace Sekani.EditorCore;

public sealed class LineCache
{
	private readonly SekaniBuffer _buffer;
	private readonly Dictionary<Line, LineLayout> _layoutCache = [];
	public int Width { get; private set; }
	private Line? _longestLine;
	private readonly int _tabSize;

	public LineCache(SekaniBuffer buffer, int tabSize)
	{
		_buffer = buffer;
		_tabSize = tabSize;
		_buffer.BufferChangedEvent += BufferChanged;
	}

	private void BufferChanged(object? sender, BufferChangeData e)
	{
		var line = _buffer.GetLine(e.Line);

		if (line is null)
			return;

		if (_longestLine is null ||
			line.Text.Length >= Width)
		{
			_longestLine = line;
			Width = line.Text.Length;
		}
		else if (line == _longestLine)
		{
			RecalculateWidth();
		}
		InvalidateLayout(line);
	}

	private void RecalculateWidth()
	{
		_longestLine = null;
		Width = 0;

		foreach (var line in _buffer.Lines)
		{
			if (line.Text.Length > Width)
			{
				_longestLine = line;
				Width = line.Text.Length;
			}
		}
	}

	public LineLayout? GetOrCreate(Line line)
	{
		if (_layoutCache.TryGetValue(line, out var layout))
			return layout;

		var lineLayout = new LineLayout(line, _tabSize);
		_layoutCache[line] = lineLayout;

		return lineLayout;
	}
	public void InvalidateLayout(Line line)
	{
		_layoutCache.Remove(line);
	}
}
