using System.Diagnostics;
using Sekani.EditorCore;
using Range = Sekani.EditorCore.Range;

namespace Tests;

[TestClass]
public sealed class SekaniDocumentTests
{
	private sealed class MemoryClipboard
	{
		public string? Text { get; set; }

		public Clipboard CreateClipboard() => new(
			() => Task.FromResult(Text),
			text =>
			{
				Text = text;
				return Task.CompletedTask;
			});
	}

	private sealed class DocumentFixture
	{
		private readonly MemoryClipboard _memoryClipboard;

		public SekaniDocument Document { get; }
		public string? ClipboardText
		{
			get => _memoryClipboard.Text;
			set => _memoryClipboard.Text = value;
		}

		public DocumentFixture(string initialText = "", bool wrap = false, int maxVisualColsPerLine = 0)
		{
			_memoryClipboard = new MemoryClipboard();
			Document = new SekaniDocument(_memoryClipboard.CreateClipboard());
			Document.CreateLineCache(tabSize: 4, wordWrap: wrap, maxVisualColsPerLine: maxVisualColsPerLine);
			if (initialText.Length > 0)
				Document.TypeChars(initialText);
		}

		public string ReadText()
		{
			if (Document.BufferLength == 0)
				return "";

			var previousRange = Document.PrimaryCursorRange;
			Document.SelectAll();
			Document.YankSelection();
			Document.SetCursor(previousRange);
			return ClipboardText ?? "";
		}
	}

	private static DocumentFixture CreateDocument(string initialText = "", bool wrap = false, int maxVisualColsPerLine = 0)
		=> new(initialText, wrap, maxVisualColsPerLine);

	[TestMethod]
	public void Constructor_StartsWithEmptyDocumentAndCaretAtZero()
	{
		var fixture = CreateDocument();
		var document = fixture.Document;

		Assert.AreEqual(0, document.BufferLength);
		Assert.AreEqual(1, document.LineCount);
		Assert.AreEqual(new Range(0), document.PrimaryCursorRange);
		Assert.AreEqual(new Coordinate(0, 0), document.PrimaryCursorPosition);
	}

	[TestMethod]
	public void TypeChars_InsertsAtCaretAndUpdatesPositionAndLineCount()
	{
		var fixture = CreateDocument("one\ntwo");
		var document = fixture.Document;

		Assert.AreEqual("one\ntwo", fixture.ReadText());
		Assert.AreEqual(7, document.BufferLength);
		Assert.AreEqual(2, document.LineCount);
		Assert.AreEqual(new Range(7), document.PrimaryCursorRange);
		Assert.AreEqual(new Coordinate(3, 1), document.PrimaryCursorPosition);

		document.SetCursor(1, 0);
		document.TypeChars("X");

		Assert.AreEqual("oXne\ntwo", fixture.ReadText());
		Assert.AreEqual(8, document.BufferLength);
		Assert.AreEqual(new Range(2), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void CoordinateConversion_RoundTripsDocumentOffsets()
	{
		var fixture = CreateDocument("ab\ncd\n😀");
		var document = fixture.Document;

		for (var offset = 0; offset <= document.BufferLength; offset++)
		{
			var coordinate = document.GetCoordinate(offset);
			Assert.AreEqual(offset, document.GetOffset(coordinate), $"Offset {offset} mapped through {coordinate}");
		}
	}

	[TestMethod]
	public void CaretRightAndLeft_MoveByGraphemeBoundaries()
	{
		var document = CreateDocument("A😀B").Document;
		document.SetCursor(0, 0);

		document.CaretRight(1, extendSelection: false);
		Assert.AreEqual(new Range(1), document.PrimaryCursorRange);

		document.CaretRight(1, extendSelection: false);
		Assert.AreEqual(new Range(3), document.PrimaryCursorRange);

		document.CaretLeft(1, extendSelection: false);
		Assert.AreEqual(new Range(1), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void CaretNavigation_MovesByUnicodeGraphemeBoundaries()
	{
		var cases = new[]
		{
			(text: "e\u0301x", boundaries: new[] { 0, 2, 3 }), // e + combining acute accent
			(text: "👩‍💻x", boundaries: new[] { 0, 5, 6 }), // emoji joined with ZWJ
			(text: "🇳🇬x", boundaries: new[] { 0, 4, 5 }), // regional indicator pair
			(text: "✈️x", boundaries: new[] { 0, 2, 3 }), // variation selector
			(text: "👍🏽x", boundaries: new[] { 0, 4, 5 }), // emoji skin tone modifier
		};

		foreach (var (text, boundaries) in cases)
		{
			var document = CreateDocument(text).Document;
			document.SetCursor(0, 0);
			Assert.AreEqual(boundaries[0], document.PrimaryCursorRange.Head);

			for (var i = 1; i < boundaries.Length; i++)
			{
				document.CaretRight(1, extendSelection: false);
				Assert.AreEqual(boundaries[i], document.PrimaryCursorRange.Head,
					$"Right movement stopped at the wrong UTF-16 offset in {text}");
			}

			for (var i = boundaries.Length - 2; i >= 0; i--)
			{
				document.CaretLeft(1, extendSelection: false);
				Assert.AreEqual(boundaries[i], document.PrimaryCursorRange.Head,
					$"Left movement stopped at the wrong UTF-16 offset in {text}");
			}
		}
	}

	[TestMethod]
	public void CaretRight_WithSelectionExtension_SelectsWholeCombiningGrapheme()
	{
		var document = CreateDocument("e\u0301x").Document;
		document.SetCursor(0, 0);

		document.CaretRight(1, extendSelection: true);

		Assert.AreEqual(new Range(0, 2), document.PrimaryCursorRange);
		Assert.AreEqual(2, document.PrimaryCursorRange.Head);
	}

	[TestMethod]
	public void CaretMovement_CanExtendSelectionInBothDirections()
	{
		var document = CreateDocument("abcd").Document;
		document.SetCursor(1, 0);

		document.CaretRight(2, extendSelection: true);
		Assert.AreEqual(new Range(1, 3), document.PrimaryCursorRange);

		document.CaretLeft(3, extendSelection: true);
		Assert.AreEqual(new Range(2, 0), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void CaretVerticalMovement_PreservesPreferredVisualColumnAcrossShortLine()
	{
		var document = CreateDocument("abcd\nx\nabcd").Document;
		document.SetCursor(0, 0);
		document.CaretRight(3, extendSelection: false);

		document.CaretDown(1, extendSelection: false);
		Assert.AreEqual(new Coordinate(1, 1), document.PrimaryCursorPosition);

		document.CaretDown(1, extendSelection: false);
		Assert.AreEqual(new Coordinate(3, 2), document.PrimaryCursorPosition);

		document.CaretUp(1, extendSelection: false);
		Assert.AreEqual(new Coordinate(1, 1), document.PrimaryCursorPosition);
	}

	[TestMethod]
	public void BackSpace_RemovesPreviousGraphemeAsOneEdit()
	{
		var fixture = CreateDocument("A😀B");
		var document = fixture.Document;
		document.SetCursor(3, 0);

		document.BackSpace(new Coordinate(0, 0));

		Assert.AreEqual("AB", fixture.ReadText());
		Assert.AreEqual(new Range(1), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void BackSpace_AtStartOfLineRemovesLineBreak()
	{
		var fixture = CreateDocument("a\nb");
		var document = fixture.Document;
		document.SetCursor(2, 0);

		document.BackSpace(new Coordinate(0, 1));

		Assert.AreEqual("ab", fixture.ReadText());
		Assert.AreEqual(new Range(1), document.PrimaryCursorRange);
		Assert.AreEqual(1, document.LineCount);
	}

	[TestMethod]
	public void DeleteSelection_RemovesSelectedTextAndCollapsesCaretToStart()
	{
		var fixture = CreateDocument("abcdef");
		var document = fixture.Document;
		document.SetAnchorAndHead(1, 3);

		document.DeleteSelection();

		Assert.AreEqual("aef", fixture.ReadText());
		Assert.AreEqual(new Range(1), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void SelectAll_SelectsAndYanksEntireNonEmptyDocument()
	{
		var fixture = CreateDocument("alpha\nbeta");
		var document = fixture.Document;

		document.SelectAll();

		Assert.AreEqual(new Range(0, document.BufferLength ), document.PrimaryCursorRange);
		document.YankSelection();
		Assert.AreEqual("alpha\nbeta", fixture.ClipboardText);
	}

	[TestMethod]
	public void PlaceCursorAtLineStartAndEnd_UsesLogicalLineBoundaries()
	{
		var document = CreateDocument("  alpha\nbeta").Document;
		document.SetCursor(5, 0);

		document.PlaceCursorAtLineStart(extendSelection: false);
		Assert.AreEqual(2, document.PrimaryCursorRange.Head);

		document.PlaceCursorAtLineEnd(extendSelection: false);
		Assert.AreEqual(7, document.PrimaryCursorRange.Head);
	}

	[TestMethod]
	public void PlaceCursorBeforeSelection_ReversesSelectionDirection()
	{
		var document = CreateDocument("abcdef").Document;
		document.SetAnchorAndHead(1, 4);

		document.PlaceCursorBeforeSelection();

		Assert.AreEqual(new Range(4, 1), document.PrimaryCursorRange);
		Assert.AreEqual(1, document.PrimaryCursorRange.From);
		Assert.AreEqual(4, document.PrimaryCursorRange.To);
	}

	[TestMethod]
	public void SelectLine_SelectsCurrentLineAndCanExtendAcrossNextLine()
	{
		var document = CreateDocument("abc\ndef").Document;
		document.SetCursor(1, 0);

		document.SelectLine(1);
		Assert.AreEqual(new Range(0, 3), document.PrimaryCursorRange);

		document.SelectLine(1);
		Assert.AreEqual(new Range(0, 7), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void AddNewLineAboveSelection_InsertsBlankLineAboveCaretLine()
	{
		var fixture = CreateDocument($"one{Environment.NewLine}two");
		var document = fixture.Document;
		document.SetCursor(document.GetOffset(new Coordinate(1, 1)), 0);

		document.AddNewLineAboveSelection();

		Assert.AreEqual($"one{Environment.NewLine}{Environment.NewLine}two", fixture.ReadText());
		Assert.AreEqual(new Coordinate(0, 1), document.PrimaryCursorPosition);
	}

	[TestMethod]
	public void AddNewLineUnderSelection_InsertsBlankLineBelowCaretLine()
	{
		var fixture = CreateDocument($"one{Environment.NewLine}two");
		var document = fixture.Document;
		document.SetCursor(document.GetOffset(new Coordinate(1, 0)), 0);

		document.AddNewLineUnderSelection();

		Assert.AreEqual($"one{Environment.NewLine}{Environment.NewLine}two", fixture.ReadText());
		Assert.AreEqual(new Coordinate(0, 1), document.PrimaryCursorPosition);
	}

	[TestMethod]
	public void UndoAndRedo_RestoreTextAndCaretPositions()
	{
		var fixture = CreateDocument("abc");
		var document = fixture.Document;

		document.CommitTransaction();

		document.Undo(1);
		Assert.AreEqual(0, document.BufferLength);
		Assert.AreEqual(new Range(0), document.PrimaryCursorRange);

		document.Redo(1);
		Assert.AreEqual("abc", fixture.ReadText());
		Assert.AreEqual(new Range(3), document.PrimaryCursorRange);
	}

	[TestMethod]
	public async Task FindInDocumentAsync_ReturnsHitsAndSelectsNextHitFromOffset()
	{
		var document = CreateDocument("one two one").Document;

		var (hits, targetIndex) = await document.FindInDocumentAsync("ONE", offset: 1);

		CollectionAssert.AreEqual(new[] { new SearchHit(0, 3), new SearchHit(8, 3) }, hits);
		Assert.AreEqual(1, targetIndex);
		Assert.AreEqual(new Range(8, 10), document.PrimaryCursorRange);
	}

	[TestMethod]
	public async Task FindInDocumentAsync_WrapsToFirstHitWhenOffsetIsAfterAllHits()
	{
		var document = CreateDocument("cat dog cat").Document;

		var (hits, targetIndex) = await document.FindInDocumentAsync("cat", offset: 11);

		Assert.AreEqual(2, hits.Count);
		Assert.AreEqual(0, targetIndex);
		Assert.AreEqual(new Range(0, 2), document.PrimaryCursorRange);
	}

	[TestMethod]
	public async Task FindInDocumentAsync_WithNoMatchReturnsEmptyResultsAndLeavesSelectionAlone()
	{
		var document = CreateDocument("alpha beta").Document;
		document.SetAnchorAndHead(1, 4);

		var (hits, targetIndex) = await document.FindInDocumentAsync("missing", offset: 0);

		Assert.AreEqual(0, hits.Count);
		Assert.AreEqual(0, targetIndex);
		Assert.AreEqual(new Range(1, 4), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void YankSelection_WritesSelectedTextToInjectedClipboard()
	{
		var fixture = CreateDocument("abcde");
		fixture.Document.SetAnchorAndHead(1, 2);

		fixture.Document.YankSelection();

		Assert.AreEqual("bc", fixture.ClipboardText);
	}

	[TestMethod]
	public void PasteAfterCursor_InsertsClipboardTextAndMovesCaretAfterIt()
	{
		var fixture = CreateDocument("abc");
		var document = fixture.Document;
		fixture.ClipboardText = "XY";
		document.SetCursor(1, 0);

		document.PasteAfterCursor();

		Assert.AreEqual("aXYbc", fixture.ReadText());
		Assert.AreEqual(new Range(3), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void PasteAfterCursor_NullClipboardTextLeavesDocumentUnchanged()
	{
		var fixture = CreateDocument("abc");
		var document = fixture.Document;
		fixture.ClipboardText = null;
		document.SetCursor(1, 0);

		document.PasteAfterCursor();

		Assert.AreEqual("abc", fixture.ReadText());
		Assert.AreEqual(new Range(1), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void DuplicateRanges_CreatesAlignedCursorsOnFollowingLines()
	{
		var document = CreateDocument("abc\nabc\nabc").Document;
		document.SetCursor(1, 0);

		document.DuplicateRanges(count: 2, direction: CaretVerticalDirection.Down);

		Assert.AreEqual(3, document.Selections.Count);
		Assert.AreEqual(new Range(1), document.Selections[0]);
		Assert.AreEqual(new Range(5), document.Selections[1]);
		Assert.AreEqual(new Range(9), document.Selections[2]);
		Assert.AreEqual(new Range(9), document.PrimaryCursorRange);
	}

	[TestMethod]
	public void HorizontalCaretMovement_MovesEveryCursorByTheSameGraphemeCount()
	{
		var document = CreateDocument("abcd\nabcd\nabcd").Document;
		document.SetCursor(1, 0);
		document.DuplicateRanges(count: 1, direction: CaretVerticalDirection.Down);
		CollectionAssert.AreEqual(new[] { new Range(1), new Range(6) }, document.Selections.Ranges);

		document.CaretRight(2, extendSelection: false);
		CollectionAssert.AreEqual(new[] { new Range(3), new Range(8) }, document.Selections.Ranges);

		document.CaretLeft(1, extendSelection: false);
		CollectionAssert.AreEqual(new[] { new Range(2), new Range(7) }, document.Selections.Ranges);
	}

	[TestMethod]
	public void VerticalCaretMovement_MovesEveryCursorAndPreservesColumn()
	{
		var document = CreateDocument("abcd\nabcd\nabcd\nabcd").Document;
		document.SetCursor(0, 0);
		document.CaretRight(1, extendSelection: false);
		document.DuplicateRanges(count: 1, direction: CaretVerticalDirection.Down);
		CollectionAssert.AreEqual(new[] { new Range(1), new Range(6) }, document.Selections.Ranges);

		document.CaretDown(1, extendSelection: false);
		CollectionAssert.AreEqual(new[] { new Range(6), new Range(11) }, document.Selections.Ranges);

		document.CaretUp(1, extendSelection: false);
		CollectionAssert.AreEqual(new[] { new Range(1), new Range(6) }, document.Selections.Ranges);
	}

	[TestMethod]
	public void HorizontalMovementWithExtension_ExtendsEveryCursorSelection()
	{
		var document = CreateDocument("abcd\nabcd\nabcd").Document;
		document.SetCursor(1, 0);
		document.DuplicateRanges(count: 1, direction: CaretVerticalDirection.Down);

		document.CaretRight(2, extendSelection: true);

		CollectionAssert.AreEqual(
			new[] { new Range(1, 3), new Range(6, 8) },
			document.Selections.Ranges);

		document.CaretLeft(1, extendSelection: true);
		CollectionAssert.AreEqual(
			new[] { new Range(1, 2), new Range(6, 7) },
			document.Selections.Ranges);
	}

	[TestMethod]
	public void CollapseCursors_KeepsOnlyPrimaryCursor()
	{
		var document = CreateDocument("abc\nabc\nabc").Document;
		document.SetCursor(1, 0);
		document.DuplicateRanges(count: 2, direction: CaretVerticalDirection.Down);
		var primary = document.PrimaryCursorRange;

		document.CollapseCursors();

		Assert.AreEqual(1, document.Selections.Count);
		Assert.AreEqual(primary, document.PrimaryCursorRange);
	}
}
