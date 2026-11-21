using System.Text;
using Sekani.EditorCore;
using Range = Sekani.EditorCore.Range;
namespace Tests;

[TestClass]
public sealed class BufferTests
{
	private static PieceTable CreateTable(string text = "")
	{
		var table = new PieceTable(text);
		SelectionSet selection = new([new Range(0)]);
		table.BeginNewTransaction(selection);
		return table;
	}

	private static string ReadSlice(PieceTable table, int offset, int length)
	{
		var chars = new char[Math.Max(0, length)];
		var written = table.Slice(offset, length, chars);
		return new string(chars, 0, written);
	}

	[TestMethod]
	public void Constructor_InitializesTextAndMetadata()
	{
		var cases = new[]
		{
			(text: "", lines: 1),
			(text: "hello", lines: 1),
			(text: "one\ntwo", lines: 2),
			(text: "one\n", lines: 2),
			(text: "\n\n", lines: 3),
		};

		foreach (var (text, lines) in cases)
		{
			var table = new PieceTable(text);
			Assert.AreEqual(text, table.Print());
			Assert.AreEqual(text.Length, table.BufferLength);
			Assert.AreEqual(lines, table.LineFeedCount);
		}
	}

	[TestMethod]
	public void Insert_AddsTextAtBeginningMiddleAndEnd()
	{
		var table = CreateTable("abcd");
		table.Insert("<", 0);
		table.Insert("XY", 3);
		table.Insert(">", table.BufferLength);
		table.CommitTransaction(null);

		Assert.AreEqual("<abXYcd>", table.Print());
		Assert.AreEqual(8, table.BufferLength);
	}

	[TestMethod]
	public void Insert_HandlesEmptyBufferAndNewLines()
	{
		var table = CreateTable();
		table.Insert("first\nsecond\n", 0);
		table.CommitTransaction(null);

		Assert.AreEqual("first\nsecond\n", table.Print());
		Assert.AreEqual(13, table.BufferLength);
		Assert.AreEqual(3, table.LineFeedCount);
		Assert.AreEqual("first\n", table.PrintCleanLine(0));
		Assert.AreEqual("second\n", table.PrintCleanLine(1));
		Assert.AreEqual("", table.PrintCleanLine(2));
	}

	[TestMethod]
	public void Insert_EmptyTextIsNoOp()
	{
		var table = CreateTable("abc");
		var netChanges = new List<BufferNetChangeData>();
		table.BufferNetChangedEvent += (_, change) => netChanges.Add(change);

		table.Insert("", 1);
		table.CommitTransaction(null);

		Assert.AreEqual("abc", table.Print());
		Assert.AreEqual(3, table.BufferLength);
		Assert.AreEqual(0, netChanges.Count);
	}

	[TestMethod]
	public void Delete_RemovesWithinAndAcrossPieces()
	{
		var table = CreateTable("abcdef");
		table.Insert("XYZ", 3);
		table.Delete(2, 4);
		table.CommitTransaction(null);

		Assert.AreEqual("abdef", table.Print());
		Assert.AreEqual(5, table.BufferLength);
	}

	[TestMethod]
	public void Delete_CanRemoveWholeBufferAndThenInsertAgain()
	{
		var table = CreateTable("remove me");
		table.Delete(0, table.BufferLength);
		table.Insert("restored", 0);
		table.CommitTransaction(null);

		Assert.AreEqual("restored", table.Print());
		Assert.AreEqual(8, table.BufferLength);
	}

	[TestMethod]
	public void Delete_ZeroOrNegativeLengthIsNoOp()
	{
		var table = CreateTable("abc");
		var netChanges = new List<BufferNetChangeData>();
		table.BufferNetChangedEvent += (_, change) => netChanges.Add(change);

		table.Delete(1, 0);
		table.Delete(1, -2);
		table.CommitTransaction(null);

		Assert.AreEqual("abc", table.Print());
		Assert.AreEqual(0, netChanges.Count);
	}

	[TestMethod]
	public void Slice_ReturnsTextAcrossPieceBoundariesAndClampsAtEnd()
	{
		var table = CreateTable("abcdef");
		table.Insert("12", 2);
		table.Insert("34", 6);
		table.CommitTransaction(null);

		Assert.AreEqual("ab12cd34ef", table.Print());
		Assert.AreEqual("b12cd3", ReadSlice(table, 1, 6));
		Assert.AreEqual("ef", ReadSlice(table, 8, 20));
		Assert.AreEqual("", ReadSlice(table, table.BufferLength, 4));
		Assert.AreEqual("", ReadSlice(table, 2, 0));
	}

	[TestMethod]
	public void Slice_InvalidOffsetReturnsNoCharacters()
	{
		var table = CreateTable("abc");
		Assert.AreEqual("", ReadSlice(table, -1, 2));
		Assert.AreEqual("", ReadSlice(table, 4, 2));
	}

	[TestMethod]
	public void Lines_ReturnRawLinesIncludingNewlinesAndTrailingEmptyLine()
	{
		var table = CreateTable("one\ntwo\n\nlast");
		Assert.AreEqual("one\n", table.PrintCleanLine(0));
		Assert.AreEqual("two\n", table.PrintCleanLine(1));
		Assert.AreEqual("\n", table.PrintCleanLine(2));
		Assert.AreEqual("last", table.PrintCleanLine(3));
		Assert.AreEqual("", table.PrintCleanLine(4));
	}

	[TestMethod]
	public void Coordinates_RoundTripAtEveryOffset()
	{
		var table = CreateTable("ab\ncd\n");
		table.Insert("X\nY", 1);
		table.CommitTransaction(null);

		for (var offset = 0; offset <= table.BufferLength; offset++)
		{
			var coordinate = table.GetCoordinate(offset);
			Assert.AreEqual(offset, table.GetOffset(coordinate), $"Offset {offset} mapped through {coordinate}");
		}
	}

	[TestMethod]
	public void Coordinates_UseUtf16CodeUnitOffsets()
	{
		var text = "A😀\nB";
		var table = CreateTable(text);
		Assert.AreEqual(text.Length, table.BufferLength);
		Assert.AreEqual(new Coordinate(3, 0), table.GetCoordinate(3));
		Assert.AreEqual(new Coordinate(0, 1), table.GetCoordinate(4));
	}

	[TestMethod]
	public async Task Search_FindsMatchesAcrossPiecesAndHonorsCaseAndRange()
	{
		var table = CreateTable("aba");
		table.Insert("ABA", 3);
		table.CommitTransaction(null);

		var insensitive = await table.SearchAsync("aba", 0, table.BufferLength, CancellationToken.None);
		var sensitive = await table.SearchAsync("aba", 0, table.BufferLength, CancellationToken.None, ignoreCase: false);
		var bounded = await table.SearchAsync("aba", 0, 4, CancellationToken.None, ignoreCase: false);

		CollectionAssert.AreEqual(new[] { new SearchHit(0, 3), new SearchHit(3, 3) }, insensitive);
		CollectionAssert.AreEqual(new[] { new SearchHit(0, 3) }, sensitive);
		CollectionAssert.AreEqual(new[] { new SearchHit(0, 3) }, bounded);

		var splitPieceTable = CreateTable("ab");
		splitPieceTable.Insert("a", 2);
		splitPieceTable.CommitTransaction(null);
		var crossPieceHit = await splitPieceTable.SearchAsync("aba", 0, 3, CancellationToken.None, ignoreCase: false);
		CollectionAssert.AreEqual(new[] { new SearchHit(0, 3) }, crossPieceHit);
	}

	[TestMethod]
	public async Task Search_EmptyPatternAndEmptyRangeReturnNoHits()
	{
		var table = CreateTable("abc");
		Assert.AreEqual(0, (await table.SearchAsync("", 0, 3, CancellationToken.None)).Count);
		Assert.AreEqual(0, (await table.SearchAsync("a", 2, 2, CancellationToken.None)).Count);
		Assert.AreEqual(0, (await table.SearchAsync("a", -1, 3, CancellationToken.None)).Count);
	}


	[TestMethod]
	public void UndoRedo_RestoresInsertionsAndDeletions()
	{
		var table = CreateTable("abcd");
		table.Insert("XY", 2);
		table.CommitTransaction(null);
		Assert.AreEqual("abXYcd", table.Print());

		table.BeginNewTransaction(new([new Range(6)]));
		table.Delete(1, 4);
		table.CommitTransaction(null);
		Assert.AreEqual("ad", table.Print());

		table.DoUndo(out var undoRange);
		Assert.AreEqual("abXYcd", table.Print());

		table.DoUndo(out _);
		Assert.AreEqual("abcd", table.Print());

		table.DoRedo(out _);
		Assert.AreEqual("abXYcd", table.Print());
		table.DoRedo(out var redoRange);
		Assert.AreEqual("ad", table.Print());
	}

	[TestMethod]
	public void UndoGroupsEditsInTransactionAndNewEditClearsRedo()
	{
		var table = CreateTable("base");
		table.Insert(" one", 4);
		table.Insert("!", 8);
		table.CommitTransaction(null);
		Assert.AreEqual("base one!", table.Print());

		table.DoUndo(out _);
		Assert.AreEqual("base", table.Print());
		table.DoRedo(out _);
		Assert.AreEqual("base one!", table.Print());
		table.DoUndo(out _);

		table.BeginNewTransaction(new([new Range(0)]));
		table.Insert(" new", 4);
		table.CommitTransaction(null);
		table.DoRedo(out _);

		Assert.AreEqual("base new", table.Print());
	}

	[TestMethod]
	public void NetChangeEvents_ReportInsertionAndDeletionDetails()
	{
		var table = CreateTable("ab\ncd");
		var changes = new List<BufferNetChangeData>();
		table.BufferNetChangedEvent += (_, change) => changes.Add(change);

		table.Insert("X\n", 1);
		table.Delete(0, 2);
		table.CommitTransaction(null);

		Assert.AreEqual(2, changes.Count);
		Assert.AreEqual(new BufferNetChangeData(1, 2, null, BufferOperation.Insertion), changes[0]);
		Assert.AreEqual(0, changes[1].StartOffset);
		Assert.AreEqual(2, changes[1].Length);
		Assert.AreEqual(BufferOperation.Deletion, changes[1].Operation);
		Assert.AreEqual(new Coordinate(2, 0), changes[1].OldEndPointBeforeDelete);
	}

	[TestMethod]
	public void LineChangeEvents_ReportLinesAddedAndRemoved()
	{
		var table = CreateTable("ab\ncd");
		var changes = new List<BufferLineChangeData>();
		table.BufferLineChangedEvent += (_, change) => changes.Add(change);

		table.Insert("\n", 1);
		table.Delete(1, 1);
		table.CommitTransaction(null);

		Assert.AreEqual(4, changes.Count);
		Assert.AreEqual(new BufferLineChangeData(new LineRange(0, null), BufferLineChangeKind.LineChanged), changes[0]);
		Assert.AreEqual(new BufferLineChangeData(new LineRange(1, 1), BufferLineChangeKind.LineAdded), changes[1]);
		Assert.AreEqual(new BufferLineChangeData(new LineRange(0, null), BufferLineChangeKind.LineChanged), changes[2]);
		Assert.AreEqual(new BufferLineChangeData(new LineRange(1, 1), BufferLineChangeKind.LineRemoved), changes[3]);
	}

	//Don't even know what these clankers are smoking
	[TestMethod]
	public void DeterministicEditSequence_MatchesStringReferenceAfterEveryOperation()
	{
		var random = new Random(0x5ECA);
		var reference = new StringBuilder("seed\ntext");
		var table = CreateTable(reference.ToString());
		var insertions = new[] { "x", "YZ", "\n", "ab\n", "😀" };

		for (var operation = 0; operation < 250; operation++)
		{
			if (reference.Length == 0 || random.Next(100) < 60)
			{
				var offset = random.Next(reference.Length + 1);
				var inserted = insertions[random.Next(insertions.Length)];
				table.Insert(inserted, offset);
				reference.Insert(offset, inserted);
			}
			else
			{
				var offset = random.Next(reference.Length);
				var length = random.Next(1, reference.Length - offset + 1);
				table.Delete(offset, length);
				reference.Remove(offset, length);
			}

			Assert.AreEqual(reference.ToString(), table.Print(), $"Mismatch after operation {operation}");
			Assert.AreEqual(reference.Length, table.BufferLength, $"Length mismatch after operation {operation}");
			Assert.AreEqual(1 + reference.ToString().Count(c => c == '\n'), table.LineFeedCount,
				$"Line count mismatch after operation {operation}");
		}

		table.CommitTransaction(null);
	}
}
