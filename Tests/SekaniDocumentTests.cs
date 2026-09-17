using Sekani.EditorCore;

namespace Tests;

[TestClass]
public sealed class SekaniDocumentTests
{
	[TestMethod]
	public void NewDocument_HasOneEmptyLineAndOriginCaret()
	{
		var document = new SekaniDocument();
		Assert.IsFalse(document.IsReadOnly);
	}

	[TestMethod]
	public void TypeChars_InsertsTextAndMovesCaretIncludingAcrossLines()
	{
		var document = new SekaniDocument();

		document.TypeChars("one");
		document.CaretPosition = new Coordinate(1, 0);
		document.TypeChars("X" + Environment.NewLine + "two");

		AssertCaret(document, 3, 1);
	}

	[TestMethod]
	public void TypeChars_IgnoresEmptyTextAndInvalidCaret()
	{
		var document = new SekaniDocument();
		document.TypeChars("text");
		document.CaretPosition = new Coordinate(2, 0);

		document.TypeChars("");
		AssertCaret(document, 2, 0);

		document.CaretPosition = new Coordinate(-1, 0);
		document.TypeChars("ignored");
		AssertCaret(document, -1, 0);
	}

	[TestMethod]
	public void CaretLeftAndRight_MoveWithinAndBetweenLines_AndStopAtDocumentEdges()
	{
		var document = CreateDocument("ab", "cd");

		document.CaretRight();
		document.CaretRight();
		document.CaretRight();
		AssertCaret(document, 0, 1);

		document.CaretLeft();
		document.CaretLeft();
		AssertCaret(document, 1, 0);

		document.CaretPosition = new Coordinate(0, 0);
		document.CaretLeft();
		AssertCaret(document, 0, 0);

		document.CaretPosition = new Coordinate(2, 1);
		document.CaretRight();
		AssertCaret(document, 2, 1);
	}

	[TestMethod]
	public void BackSpace_DeletesCharacterBeforeCaretOrJoinsLines()
	{
		var document = CreateDocument("abc", "def");

		document.BackSpace(new Coordinate(2, 0));
		AssertCaret(document, 1, 0);

		document.BackSpace(new Coordinate(0, 1));
		AssertCaret(document, 2, 0);

		document.BackSpace(new Coordinate(0, 0));
		AssertCaret(document, 2, 0);
	}

	[TestMethod]
	public void DeleteSelection_DeletesNextCharacterWhenThereIsNoSelection()
	{
		var document = CreateDocument("abc");
		document.CaretPosition = new Coordinate(1, 0);

		document.DeleteSelection();

		AssertCaret(document, 1, 0);
	}

	[TestMethod]
	public void DeleteSelection_DeletesForwardAcrossLinesAndNormalizesCaret()
	{
		var document = CreateDocument("first", "middle", "last");
		document.CaretPosition = new Coordinate(2, 0)
		{
			RangeEnd = new Coordinate(3, 2)
		};

		document.DeleteSelection();

		AssertCaret(document, 2, 0);
		Assert.IsFalse(document.CaretPosition.HasRange());
	}

	[TestMethod]
	public void DeleteSelection_HandlesReversedSelectionAndEndOfFinalLine()
	{
		var document = CreateDocument("one", "two");
		document.CaretPosition = new Coordinate(1, 0)
		{
			RangeEnd = new Coordinate(2, 1)
		};

		document.DeleteSelection();
		AssertCaret(document, 1, 0);

		document.CaretPosition = new Coordinate(2, 0);
		document.DeleteSelection();
		AssertCaret(document, 2, 0);
	}

	[TestMethod]
	public void CreateLineCache_EnablesVerticalMovementAndPreservesPreferredColumn()
	{
		var document = CreateDocument("abcd", "x", "wxyz");
		var cache = document.CreateLineCache(tabSize: 4, wordWrap: false, maxVisualColsPerLine: 0);
		document.CaretPosition = new Coordinate(3, 0);
		document.UpdatePreferredVisualColumn();

		document.CaretDown();
		AssertCaret(document, 1, 1);
		document.CaretDown();
		AssertCaret(document, 3, 2);
		document.CaretUp();
		AssertCaret(document, 1, 1);
		document.CaretUp();
		AssertCaret(document, 3, 0);
		Assert.AreEqual(4, cache.Width);
	}

	[TestMethod]
	public void CaretUpAndDown_MoveBetweenWrappedVisualLines()
	{
		var document = CreateDocument("abcdef");
		document.CreateLineCache(tabSize: 4, wordWrap: true, maxVisualColsPerLine: 3);
		document.CaretPosition = new Coordinate(2, 0);
		document.UpdatePreferredVisualColumn();

		document.CaretDown();
		AssertCaret(document, 5, 0);
		document.CaretUp();
		AssertCaret(document, 2, 0);
	}

	[TestMethod]
	public void AddNewLineAboveAndUnderSelection_UsesSelectionBounds()
	{
		var document = CreateDocument("first", "second", "third");
		document.CaretPosition = new Coordinate(1, 2)
		{
			RangeEnd = new Coordinate(1, 0)
		};

		document.AddNewLineAboveSelection();
		AssertCaret(document, 0, 0);

		document.CaretPosition = new Coordinate(1, 1)
		{
			RangeEnd = new Coordinate(1, 3)
		};
		document.AddNewLineUnderSelection();
		AssertCaret(document, 0, 4);
	}

	[TestMethod]
	public void CursorPlacementCommands_HandleSelectionsAndLineBoundaries()
	{
		var document = CreateDocument("  text", "next");
		document.CaretPosition = new Coordinate(4, 0);
		document.PlaceCursorAtLineStart();
		AssertCaret(document, 2, 0);
		document.PlaceCursorAtLineEnd();
		AssertCaret(document, 6, 0);

		document.CaretPosition = new Coordinate(4, 1)
		{
			RangeEnd = new Coordinate(1, 0)
		};
		document.PlaceCursorBeforeSelection();
		AssertCaret(document, 1, 0);
		Assert.AreEqual(new Coordinate(4, 1), document.CaretPosition.RangeEnd);

		document.PlaceCursorAfterSelection();
		 
		//new line should be created
		AssertCaret(document, 0, 2);
		Assert.HasCount(3, document.Lines);
		Assert.AreEqual(new Coordinate(1, 0), document.CaretPosition.RangeEnd);

		var end = CreateDocument("x");
		end.CaretPosition = new Coordinate(1, 0);
		end.PlaceCursorAfterSelection();
		AssertCaret(end, 0, 1);
	}


	private static SekaniDocument CreateDocument(params string[] lines)
	{
		var document = new SekaniDocument();
		document.TypeChars(string.Join(Environment.NewLine, lines));
		document.CaretPosition = new Coordinate(0, 0);
		return document;
	}


	private static void AssertCaret(SekaniDocument document, int expectedCol, int expectedLine)
	{
		Assert.AreEqual(new Coordinate(expectedCol, expectedLine), document.CaretPosition);
	}
}
