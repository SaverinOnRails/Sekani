namespace Sekani.EditorCore;

public sealed class Line
{
	public string Text { get; set; } = "";

	public void InsertText(int col, string text)
	{
		if (col < 0 || col > Text.Length)
			return;
		Text = Text.Insert(col, text);
	}
}
