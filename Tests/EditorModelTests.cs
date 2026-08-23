namespace SekaniTests;

using SekaniUI.Controls;

[TestClass]
public class EditorModelTests
{
	public EditorModel CreateEditorModel(int charAdvance, int lineHeight, int tabSize)
	{
		EditorModel model = new(new EditorMetrics(charAdvance, lineHeight, tabSize));
		 
		return model;
	}

	[TestMethod]
	public void GridToUICoord_AccountsForTabExpansion()
	{
		var model = CreateEditorModel(8, 15, 4);
		model.HandleTextInput("ab\tcd");
		Assert.AreEqual(5, model.CaretPosition.Col);
		Assert.AreEqual(0, model.CaretPosition.Line);
	}

	[TestMethod]
	public void CaretDown_OnLastLine_DoesNotMoveCaret()
	{
		var model = CreateEditorModel(8, 15, 4);
		model.HandleTextInput("ab\tcd");
		var initialPosition = model.CaretPosition;
		model.CaretDown();
		Assert.AreEqual(initialPosition, model.CaretPosition);
	}

	[TestMethod]
	public void InsertNewLine_MovesCaretToBeginningOfNextLine()
	{
		var model = CreateEditorModel(8, 15, 4);
		model.HandleTextInput("ab\tcd");
		model.InsertNewLine();
		Assert.AreEqual(0, model.CaretPosition.Col);
		Assert.AreEqual(1, model.CaretPosition.Line);
	}

	[TestMethod]
	public void Backspace_DeletesGlyphBeforeCaret()
	{
		var model = CreateEditorModel(8, 15, 4);
		model.HandleTextInput("abc");

		model.Backspace();

		Assert.AreEqual(2, model.Lines[0].GraphemeCount);
		Assert.AreEqual("a", model.Lines[0].Graphemes[0].Data);
		Assert.AreEqual("b", model.Lines[0].Graphemes[1].Data);

		Assert.AreEqual(2, model.CaretPosition.Col);
		Assert.AreEqual(0, model.CaretPosition.Line);
	}

	[TestMethod]
	public void Backspace_AtStartOfLine_MergesWithPreviousLine()
	{
		var model = CreateEditorModel(8, 15, 4);
		model.HandleTextInput("abc");
		model.InsertNewLine();
		model.HandleTextInput("def");
		model.CaretLeft();
		model.CaretLeft();
		model.CaretLeft();
		model.Backspace();
		Assert.AreEqual(1, model.Lines.Count);

		Assert.AreEqual(6, model.Lines[0].GraphemeCount);
		Assert.AreEqual("a", model.Lines[0].Graphemes[0].Data);
		Assert.AreEqual("b", model.Lines[0].Graphemes[1].Data);
		Assert.AreEqual("c", model.Lines[0].Graphemes[2].Data);
		Assert.AreEqual("d", model.Lines[0].Graphemes[3].Data);
		Assert.AreEqual("e", model.Lines[0].Graphemes[4].Data);
		Assert.AreEqual("f", model.Lines[0].Graphemes[5].Data);

		Assert.AreEqual(3, model.CaretPosition.Col);
		Assert.AreEqual(0, model.CaretPosition.Line);
	}

	[TestMethod]
	public void Return_AtMiddleOfLine_SplitsToTwoLines()
	{
		var model = CreateEditorModel(8, 15, 4);

		model.HandleTextInput("abcd");
		model.InsertNewLine();
		model.HandleTextInput("efgh");

		model.CaretUp();
		model.CaretLeft();
		model.CaretLeft();

		model.InsertNewLine();

		Assert.AreEqual(3, model.Lines.Count);
		Assert.AreEqual(2, model.Lines[0].GraphemeCount);
		Assert.AreEqual("a", model.Lines[0].Graphemes[0].Data);
		Assert.AreEqual("b", model.Lines[0].Graphemes[1].Data);

		Assert.AreEqual(2, model.Lines[1].GraphemeCount);
		Assert.AreEqual("c", model.Lines[1].Graphemes[0].Data);
		Assert.AreEqual("d", model.Lines[1].Graphemes[1].Data);

		Assert.AreEqual(4, model.Lines[2].GraphemeCount);
		Assert.AreEqual("e", model.Lines[2].Graphemes[0].Data);
		Assert.AreEqual("f", model.Lines[2].Graphemes[1].Data);
		Assert.AreEqual("g", model.Lines[2].Graphemes[2].Data);
		Assert.AreEqual("h", model.Lines[2].Graphemes[3].Data);

		Assert.AreEqual(new Coordinate(0, 1), model.CaretPosition);
	}
	[TestMethod]
	public void Backspace_AtStartOfDocument_DoesNothing()
	{
		var model = CreateEditorModel(8, 15, 4);

		model.Backspace();

		Assert.AreEqual(1, model.Lines.Count);
		Assert.AreEqual(0, model.Lines[0].GraphemeCount);
		Assert.AreEqual(new Coordinate(0, 0), model.CaretPosition);
	}
}
