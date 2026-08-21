namespace SekaniTests;
using SekaniUI.Controls;

[TestClass]
public class EditorModelTests
{
	public EditorModel CreateEditorModel(int charAdvance, int lineHeight, int tabSize)
	{
		EditorModel model = new(new EditorMetrics(charAdvance, lineHeight, tabSize)); 
		//800x600 editor area, no gutters
		model.EditorArea = new(new Avalonia.Point(0, 0), new Avalonia.Size(800, 600));
		return model;
	}

	[TestMethod]
	public void GridToUICoord_AccountsForTabExpansion()
	{
		var model = CreateEditorModel(8,15,4);
		model.HandleTextInput("ab\tcd");
		var coord = model.GridToUICoord(model.CaretPosition);
		Assert.AreEqual(48, coord.X);
		Assert.AreEqual(0, coord.Y);

		Assert.AreEqual(5,model.CaretPosition.Col);
		Assert.AreEqual(0,model.CaretPosition.Line);
	}

	[TestMethod]
	public void CaretDown_OnLastLine_DoesNotMoveCaret()
	{
		var model = CreateEditorModel(8,15,4);
		model.HandleTextInput("ab\tcd");
		var initialPosition = model.CaretPosition;
		model.CaretDown();
		Assert.AreEqual(initialPosition, model.CaretPosition);
	}

	[TestMethod]
	public void InsertNewLine_MovesCaretToBeginningOfNextLine()
	{
		var model = CreateEditorModel(8,15,4);
		model.HandleTextInput("ab\tcd");
		model.InsertNewLine();
		var coord = model.GridToUICoord(model.CaretPosition);

		Assert.AreEqual(0, coord.X);
		Assert.AreEqual(15, coord.Y);

		Assert.AreEqual(0,model.CaretPosition.Col);
		Assert.AreEqual(1,model.CaretPosition.Line);
	}
}
