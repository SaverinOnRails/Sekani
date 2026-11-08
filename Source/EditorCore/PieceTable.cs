using System.Text;
namespace Sekani.EditorCore;

public enum RBColor : byte
{
	Red,
	Black,
}
public enum BufferLineChangeKind
{
	LineChanged,
	LineAdded,
	LineRemoved,
}
public enum BufferOperation
{
	Insertion,
	Deletion,
}
public record struct BufferLineChangeData(int Line, BufferLineChangeKind kind);
public record struct BufferNetChangeData(int StartOffset, int Length, Coordinate? OldEndPointBeforeDelete, BufferOperation Operation);
public readonly record struct PieceDescriptor(PieceBuffer Buffer, int Start, int End, int LineFeedCount)
{
	public int Length => End - Start;
}

internal interface ITextEdit;
internal record struct TextInsertionEdit(int Offset, PieceDescriptor InsertedPiece) : ITextEdit;
internal record struct TextDeletionEdit(int Offset, IReadOnlyList<PieceDescriptor> DeletedPieces) : ITextEdit;


internal sealed class EditTransaction(Range caretBefore)
{
	public List<ITextEdit> TextEdits { get; } = [];
	public Range CaretBefore => caretBefore;
	public Range CaretAfter { get; set; } = caretBefore;

}

internal record struct PiecePosition(PieceNode Piece, int AbsoluteOffset, int LocalOffset);

internal sealed class PieceNode
{
	public PieceBuffer pieceBuffer { get; }
	public int Start { get; internal set; }
	public int End { get; internal set; }

	public required PieceNode Parent { get; internal set; }
	public required PieceNode Right { get; internal set; }
	public required PieceNode Left { get; internal set; }

	public int LeftSubtreeBufferLength { get; internal set; } = 0;
	public int LeftSubtreeLineFeedCount { get; internal set; } = 0;

	public int LineFeedCount { get; internal set; }
	public RBColor Color { get; internal set; } = RBColor.Black;
	public int Width { get; internal set; } = 0;

	public PieceNode(PieceBuffer pieceBufferType, int start, int end)
	{
		pieceBuffer = pieceBufferType;
		Start = start;
		End = end;
	}

	public static PieceNode NULL_NODE => PieceTable.NULL_NODE;
	public int Length => End - Start;


	public static int TotalBufferLength(PieceNode node)
	{
		if (node == NULL_NODE) return 0;
		return node.LeftSubtreeBufferLength + node.Length + TotalBufferLength(node.Right);
	}

	public static int TotalLineFeedCount(PieceNode node)
	{
		if (node == NULL_NODE) return 0;
		return node.LeftSubtreeLineFeedCount + node.LineFeedCount + TotalLineFeedCount(node.Right);
	}

	public static PieceNode RightMost(PieceNode node)
	{
		if (node.Right == NULL_NODE) return NULL_NODE;
		while (node.Right != NULL_NODE)
		{
			node = node.Right;
		}
		return node;
	}


	public static PieceNode LeftMost(PieceNode node)
	{
		if (node.Left == NULL_NODE) return NULL_NODE;
		while (node.Left != NULL_NODE)
		{
			node = node.Left;
		}
		return node;
	}

	public static void Isolate(PieceNode node)
	{
		node.Left = NULL_NODE;
		node.Right = NULL_NODE;
		node.Parent = NULL_NODE;
	}

	public static PieceNode Next(PieceNode node)
	{
		if (node.Right != NULL_NODE)
		{
			var leftMost = PieceNode.LeftMost(node.Right);
			return leftMost == NULL_NODE ? node.Right : leftMost;
		}
		while (node.Parent != NULL_NODE)
		{
			if (node.Parent.Left == node)
			{
				break;
			}

			node = node.Parent;
		}
		if (node.Parent == NULL_NODE)
		{
			return NULL_NODE;
		}

		return node.Parent;
	}

	public PieceDescriptor ToImmutableDescriptor()
	{
		return new(pieceBuffer, Start, End, LineFeedCount);
	}

	internal static void Copy(PieceNode to, PieceNode from)
	{
		to.Left = from.Left;
		to.Right = from.Right;
		to.Parent = from.Parent;
		to.Color = from.Color;
	}
}

public enum PieceBuffer
{
	Add,
	Original
}

internal class PieceTable
{
	private readonly IReadOnlyList<char> _originalBuffer;
	private IReadOnlyList<int> _originalBufferLineStarts = [0];
	private PieceNode _root;
	private readonly List<char> _addBuffer = [];
	private readonly List<int> _addBufferLineStarts = [0];
	private bool _replayingHistory = false;

	private EditTransaction? _activeTransaction = null;

	//History
	private readonly Stack<EditTransaction> _undoStack = new();
	private readonly Stack<EditTransaction> _redoStack = new();

	//this is for the line cache and layouting, easier to think and update in terms of lines there
	public event EventHandler<BufferLineChangeData>? BufferLineChangedEvent;

	//this is more precise and specific for subscribers like treesitter than want to know exactly what changed and where
	public event EventHandler<BufferNetChangeData>? BufferNetChangedEvent;

	public int BufferLength { get; private set; } = 0;
	public int LineFeedCount { get; private set; } = 0;

	//static sentinel
	public static readonly PieceNode NULL_NODE;

	public PieceTable(string original)
	{
		_originalBuffer = [.. original];
		var root = CreatePieceNode(PieceBuffer.Original, 0, original.Length, RBColor.Black);
		var originalLineStarts = GetLineStarts(original, 0);
		_originalBufferLineStarts = [0, .. originalLineStarts];
		root.LineFeedCount = originalLineStarts.Count;
		_root = root;
		UpdateTableMetadata();
	}

	static PieceTable()
	{
		NULL_NODE = new PieceNode(0, 0, 0)
		{
			Left = null!,
			Right = null!,
			Parent = null!,
			Color = RBColor.Black
		};

		NULL_NODE.Left = NULL_NODE;
		NULL_NODE.Right = NULL_NODE;
		NULL_NODE.Parent = NULL_NODE;
	}
	private List<int> GetLineStarts(string text, int offset)
	{
		List<int> lineBreakOffsets = [];
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\n')
			{
				lineBreakOffsets.Add(i + 1 + offset);
			}
		}
		return lineBreakOffsets;
	}

	private PieceNode CreatePieceNode(PieceBuffer pieceBuffer, int start, int end, RBColor color = RBColor.Red)
	{
		var node = new PieceNode(pieceBuffer, start, end)
		{
			Parent = NULL_NODE,
			Left = NULL_NODE,
			Right = NULL_NODE,
			Color = color,
		};
		return node;
	}

	private PieceNode CreatePieceNode(PieceDescriptor desc)
	{
		var node = CreatePieceNode(desc.Buffer, desc.Start, desc.End);
		node.LineFeedCount = desc.LineFeedCount;
		return node;
	}

	//this is obviously retarded which is why it is temp
	public int TempHardLoopLogicalCoordinatedToOffset(Coordinate pos)
	{
		var text = Print();
		var line = 0;
		var col = 0;
		int offset = 0;
		for (int i = 0; i < text.Length; i++)
		{
			if (col == pos.Col && line == pos.Line) return offset;
			if (text[i] == '\n')
			{
				col = 0;
				line++;
			}
			else
			{
				col++;
			}
			offset++;
		}
		return offset;
	}

	public void Insert(string text, int offset)
	{
		try
		{
			if (text.Length == 0) return;
			int line = GetCoordinate(offset).Line;
			var linestarts = GetLineStarts(text, 0);
			int addedLines = linestarts.Count - 1;
			var bufStart = _addBuffer.Count;
			InsertCore(text, offset);
			var descriptor = new PieceDescriptor(PieceBuffer.Add, bufStart, bufStart + text.Length, linestarts.Count);
			PushInsertionToActiveTransaction(offset, descriptor); //replaying history will never hit this path
			RaiseBufferLineChangedEvent(line, BufferLineChangeKind.LineChanged);
			for (int i = 0; i <= addedLines; i++)
			{
				RaiseBufferLineChangedEvent(line, BufferLineChangeKind.LineAdded);
			}
			UpdateTableMetadata();
			RaiseBufferNetChangedEvent(offset, text.Length, null, BufferOperation.Insertion);
		}
		catch (ArgumentOutOfRangeException)
		{
			Console.WriteLine("argument was out of range, ignoring action");
		}
	}


	//Recreate Pieces that were recently deleted, the text they point to is already spread across _addBuffer and _originalBuffer
	private void Reinsert(int offset, IReadOnlyList<PieceDescriptor> pieceDescriptors)
	{
		if (pieceDescriptors.Count == 0) return;
		int line = GetCoordinate(offset).Line;
		int addedLines = pieceDescriptors.Sum(p => p.LineFeedCount) - 1;
		int totalLength = pieceDescriptors.Sum(p => p.Length);
		ReinsertCore(offset, pieceDescriptors);
		RaiseBufferLineChangedEvent(line, BufferLineChangeKind.LineChanged);
		for (int i = 0; i <= addedLines; i++)
		{
			RaiseBufferLineChangedEvent(line, BufferLineChangeKind.LineAdded);
		}
		UpdateTableMetadata();
		RaiseBufferNetChangedEvent(offset, totalLength, null, BufferOperation.Insertion);
	}

	private void ReinsertCore(int offset, IReadOnlyList<PieceDescriptor> pieces)
	{
		if (pieces.Count == 0) return;
		//If tree is empty
		if (_root == NULL_NODE)
		{
			var newroot = CreatePieceNode(pieces[0]);
			InsertAsSuccessor(NULL_NODE, newroot);
			var prevNode = _root;
			for (int i = 1; i < pieces.Count; i++)
			{
				var nextNode = CreatePieceNode(pieces[i]);
				InsertAsSuccessor(prevNode, nextNode);
				prevNode = nextNode;
			}
			return;
		}
		var targetPos = GetPieceByOffset(offset);
		var targetPiece = targetPos.Piece;

		//Insert at the beginning of targetPiece (Left)
		if (targetPos.LocalOffset == 0)
		{
			var first = CreatePieceNode(pieces[0]);
			InsertAsPredecessor(targetPiece, first);
			var prev = first;
			for (int i = 1; i < pieces.Count; i++)
			{
				var next = CreatePieceNode(pieces[i]);
				InsertAsSuccessor(prev, next);
				prev = next;
			}
		}
		//Insert at the end of targetPiece (Right)
		else if (targetPos.LocalOffset == targetPiece.Length)
		{
			var prev = targetPiece;
			foreach (var desc in pieces)
			{
				var next = CreatePieceNode(desc);
				InsertAsSuccessor(prev, next);
				prev = next;
			}
		}
		//Insert into the middle of targetPiece
		else
		{
			var (head, tail) = SplitNodeAtMiddle(targetPiece, targetPos.LocalOffset);
			ResizeTo(targetPiece, head, copyStart: false);
			var prev = targetPiece;
			foreach (var desc in pieces)
			{
				var next = CreatePieceNode(desc);
				InsertAsSuccessor(prev, next);
				prev = next;
			}
			InsertAsSuccessor(prev, tail);
		}
	}
	public void PushInsertionToActiveTransaction(int offset, PieceDescriptor insertedPieceDescriptor, bool extendedLastAddBufferPiece = false)
	{
		if (_activeTransaction is null) throw new InvalidOperationException("No Active Transaction To Push To");
		var edit = new TextInsertionEdit(offset, insertedPieceDescriptor);
		_activeTransaction!.TextEdits.Add(edit);
		_activeTransaction.CaretAfter = new Range(offset + insertedPieceDescriptor.Length);
		_redoStack.Clear();
	}

	private void PushDeletionToActiveTransaction(int offset, IReadOnlyList<PieceDescriptor> deletedPiecesDescriptors)
	{
		if (_activeTransaction is null) throw new InvalidOperationException("No Active Transaction To Push To");
		var edit = new TextDeletionEdit(offset, deletedPiecesDescriptors);
		_activeTransaction!.TextEdits.Add(edit);
		_activeTransaction.CaretAfter = new Range(offset);
		_redoStack.Clear();
	}


	public void CommitTransaction()
	{
		if (_activeTransaction is null) return;
		if (_activeTransaction.TextEdits.Count > 0) _undoStack.Push(_activeTransaction);
		_activeTransaction = null;
	}

	private EditTransaction? PopUndo()
	{
		CommitTransaction();
		if (_undoStack.Count == 0) return null;
		var transaction = _undoStack.Pop();
		_redoStack.Push(transaction);
		return transaction;
	}

	private EditTransaction? PopRedo()
	{
		CommitTransaction();
		if (_redoStack.Count == 0) return null;
		var transaction = _redoStack.Pop();
		_undoStack.Push(transaction);
		return transaction;
	}

	public void DoUndo(out Range? range)
	{
		_replayingHistory = true;
		range = null;
		try
		{
			var transaction = PopUndo();
			if (transaction is null) return;
			//invert the edits
			for (int i = transaction.TextEdits.Count - 1; i >= 0; i--)
			{
				var edit = transaction.TextEdits[i];
				if (edit is TextInsertionEdit insertion)
					Delete(insertion.Offset, insertion.InsertedPiece.Length);
				else if (edit is TextDeletionEdit deletion)
					Reinsert(deletion.Offset, deletion.DeletedPieces);
			}
			range = transaction.CaretBefore;
		}
		finally
		{
			_replayingHistory = false;
		}
	}

	public void DoRedo(out Range? range)
	{
		range = null;
		var transaction = PopRedo();
		if (transaction == null) return;
		_replayingHistory = true;
		try
		{
			//replay the edits
			for (int i = 0; i < transaction.TextEdits.Count; i++)
			{
				var edit = transaction.TextEdits[i];
				if (edit is TextInsertionEdit insertion)
					Reinsert(insertion.Offset, [insertion.InsertedPiece]);
				else if (edit is TextDeletionEdit deletion)
					Delete(deletion.Offset, deletion.DeletedPieces.Sum(p => p.Length));
			}
			range = transaction.CaretAfter;
		}
		finally
		{
			_replayingHistory = false;
		}
	}
	public void BeginNewTransaction(Range CaretRange)
	{
		CommitTransaction();
		_activeTransaction ??= new(CaretRange);
	}

	private void InsertCore(string text, int offset)
	{
		if (_root == NULL_NODE)
		{
			InsertRight(NULL_NODE, text);
			return;
		}
		var targetNodePosition = GetPieceByOffset(offset);
		var nodeAbsoluteOffset = targetNodePosition.AbsoluteOffset;
		var piece = targetNodePosition.Piece;
		//if inserting into the end of piece at the end of the add buffer, extend the the piece instead of creating one
		if (piece.pieceBuffer == PieceBuffer.Add && piece.End == _addBuffer.Count && targetNodePosition.LocalOffset == piece.Length)
		{
			// Console.WriteLine("extending last add buffer piece");
			ExtendLastAddBufferPiece(piece, text);
		}
		//if inserting at the begining of the piece, 
		else if (targetNodePosition.LocalOffset == 0)
		{
			// Console.WriteLine("inserting into left");
			InsertLeft(piece, text);
		}
		//if inserting at the end of the piece
		else if (targetNodePosition.LocalOffset == piece.Length)
		{
			// Console.WriteLine("inserting into right");
			InsertRight(piece, text);
		}
		//inserting into the middle of the piece
		else
		{
			// Console.WriteLine("inserting into middle");
			InsertMiddle(piece, text, targetNodePosition.LocalOffset);
		}
	}

	public void Delete(int startOffset, int length)
	{
		try
		{
			if (length <= 0) return;
			int first = GetCoordinate(startOffset).Line;
			int endOffset = startOffset + length;
			var endCoordinate = GetCoordinate(endOffset);
			int last = endCoordinate.Line;
			DeleteCore(startOffset, length, out List<PieceDescriptor> deletedPieceDescriptors);
			if (!_replayingHistory)
				PushDeletionToActiveTransaction(startOffset, deletedPieceDescriptors);
			RaiseBufferLineChangedEvent(first, BufferLineChangeKind.LineChanged);
			//TODO: This performs like shit. Should send a complete line range instead;
			for (int i = first; i < last; i++)
			{
				RaiseBufferLineChangedEvent(first + 1, BufferLineChangeKind.LineRemoved);
			}
			UpdateTableMetadata();
			RaiseBufferNetChangedEvent(startOffset, length, endCoordinate, BufferOperation.Deletion);
		}
		catch (ArgumentOutOfRangeException)
		{
			Console.WriteLine("argument was out of range, ignoring action");
		}
	}


	private void DeleteCore(int startOffset, int length, out List<PieceDescriptor> deletedPieceDescriptors)
	{
		deletedPieceDescriptors = [];
		if (length <= 0) return;
		var startPosition = GetPieceByOffset(startOffset);
		var endPosition = GetPieceByOffset(startOffset + length);

		var startPiece = startPosition.Piece;
		var endPiece = endPosition.Piece;

		//deletion range is in one piece. Simple case
		if (startPiece == endPiece)
		{
			var targetPiece = startPiece;
			//if deletion starts at the beginning of the piece
			if (startPosition.AbsoluteOffset == startOffset)
			{
				//if deleting the entire piece
				if (targetPiece.Length == length)
				{
					deletedPieceDescriptors.Add(targetPiece.ToImmutableDescriptor());
					DeleteNode(targetPiece);
					return;
				}
				else
				{
					//conserve the last piece from the split
					TrimHead(targetPiece, length, out PieceNode head);
					deletedPieceDescriptors.Add(head.ToImmutableDescriptor());
				}
			}
			//if deletion ends at the end of the piece
			else if (startPosition.AbsoluteOffset + targetPiece.Length == startOffset + length)
			{
				//conserve the first piece from the split
				TrimTail(targetPiece, length, out PieceNode tail);
				deletedPieceDescriptors.Add(tail.ToImmutableDescriptor());
			}
			//deletion falls inside middle of piece
			else
			{
				//split the head
				var (firstPiece, firstSplitTail) = SplitNodeAtMiddle(targetPiece, startOffset - startPosition.AbsoluteOffset);
				//split the tail
				var (_, lastPiece) = SplitNodeAtMiddle(targetPiece, (startOffset - startPosition.AbsoluteOffset) + length);
				//resize piece
				ResizeTo(targetPiece, firstPiece, false);

				//Build one deleted piece from the trim
				var start = targetPiece.Start + firstPiece.Length;
				var end = start + length;
				var lfcount = firstSplitTail.LineFeedCount - lastPiece.LineFeedCount;
				deletedPieceDescriptors.Add(new PieceDescriptor(targetPiece.pieceBuffer, start, end, lfcount));

				//insert the last node directly as successor of the resized node, discarding the middle
				InsertAsSuccessor(targetPiece, lastPiece);
			}
			return;
		}
		//deletion spans multiple nodes

		List<PieceNode> dirtyPieces = [];
		//i like to do this in a direct order
		bool trimFirst = true;
		bool trimLast = true;

		if (startPosition.LocalOffset == 0)
		{
			dirtyPieces.Add(startPiece);
			trimFirst = false;
		}
		var node = PieceNode.Next(startPiece);
		while (node != endPiece)
		{
			dirtyPieces.Add(node);
			node = PieceNode.Next(node);
		}
		if (endPosition.LocalOffset == endPiece.Length)
		{
			dirtyPieces.Add(endPiece);
			trimLast = false;
		}
		if (trimFirst)
		{
			//Trim start first node
			TrimTail(startPiece, startPiece.Length - startPosition.LocalOffset, out PieceNode tail);
			deletedPieceDescriptors.Add(tail.ToImmutableDescriptor());
		}
		//delete dirty pieces
		foreach (var dirtyPiece in dirtyPieces)
		{
			deletedPieceDescriptors.Add(dirtyPiece.ToImmutableDescriptor());
			DeleteNode(dirtyPiece);
		}
		if (trimLast)
		{
			//Trim End of Last Node
			TrimHead(endPiece, endPosition.LocalOffset, out PieceNode head);
			deletedPieceDescriptors.Add(head.ToImmutableDescriptor());
		}
	}

	private void TrimHead(PieceNode piece, int length, out PieceNode head)
	{
		PieceNode tail;
		(head, tail) = SplitNodeAtMiddle(piece, length);
		ResizeTo(piece, tail, copyStart: true);
	}

	private void TrimTail(PieceNode piece, int length, out PieceNode tail)
	{
		PieceNode head;
		(head, tail) = SplitNodeAtMiddle(piece, piece.Length - length);
		ResizeTo(piece, head, copyStart: false);
	}

	private void RaiseBufferLineChangedEvent(int line, BufferLineChangeKind kind)
	{
		BufferLineChangedEvent?.Invoke(this, new(line, kind));
	}

	private void RaiseBufferNetChangedEvent(int startOffset, int length, Coordinate? oldEndPointBeforeDelete, BufferOperation operation)
	{
		BufferNetChangedEvent?.Invoke(this, new(startOffset, length, oldEndPointBeforeDelete, operation));
	}
	private void ResizeTo(PieceNode piece, PieceNode source, bool copyStart)
	{
		int oldLfCount = piece.LineFeedCount;
		int oldLength = piece.Length;

		if (copyStart) piece.Start = source.Start;
		piece.End = source.End;
		piece.LineFeedCount = source.LineFeedCount;

		UpdatePieceMetadataWithDelta(
			piece,
			piece.Length - oldLength,
			piece.LineFeedCount - oldLfCount);
	}

	private void DeleteNode(PieceNode z)
	{
		PieceNode x; // always points to replacement for y or z.
		PieceNode y; // always points to node to be deleted or replaced.

		if (z.Left == NULL_NODE)
		{
			y = z;
			x = y.Right;
		}
		else if (z.Right == NULL_NODE)
		{
			y = z;
			x = y.Left;
		}
		else
		{
			var leftMost = PieceNode.LeftMost(z.Right);
			var predecessor = leftMost == NULL_NODE ? z.Right : leftMost;
			y = predecessor;
			x = y.Right;
		}

		if (y == _root)
		{
			_root = x;
			x.Color = RBColor.Black;
			PieceNode.Isolate(z);
			NULL_NODE.Parent = NULL_NODE;
			_root.Parent = NULL_NODE;
			return;
		}

		var yWasRed = y.Color == RBColor.Red;

		if (y == y.Parent.Left)
		{
			y.Parent.Left = x;
		}
		else
		{
			y.Parent.Right = x;
		}

		if (y == z)
		{
			x.Parent = y.Parent;
			UpdatePieceMetadata(x);
		}
		else
		{
			if (y.Parent == z)
			{
				x.Parent = y;
			}
			else
			{
				x.Parent = y.Parent;
			}

			UpdatePieceMetadata(x);
			PieceNode.Copy(y, z);

			if (z == _root)
			{
				_root = y;
			}
			else
			{
				if (z == z.Parent.Left)
				{
					z.Parent.Left = y;
				}
				else
				{
					z.Parent.Right = y;
				}
			}

			if (y.Left != NULL_NODE)
			{
				y.Left.Parent = y;
			}

			if (y.Right != NULL_NODE)
			{
				y.Right.Parent = y;
			}

			y.LeftSubtreeBufferLength = z.LeftSubtreeBufferLength;
			y.LeftSubtreeLineFeedCount = z.LeftSubtreeLineFeedCount;
			UpdatePieceMetadata(y);
		}

		PieceNode.Isolate(z);

		if (x.Parent.Left == x)
		{
			var newBufferLength = PieceNode.TotalBufferLength(x);
			var newLfCount = PieceNode.TotalLineFeedCount(x);

			if (newBufferLength != x.Parent.LeftSubtreeBufferLength ||
				newLfCount != x.Parent.LeftSubtreeLineFeedCount)
			{
				var bufferDelta =
					newBufferLength - x.Parent.LeftSubtreeBufferLength;

				var lfDelta =
					newLfCount - x.Parent.LeftSubtreeLineFeedCount;

				x.Parent.LeftSubtreeBufferLength = newBufferLength;
				x.Parent.LeftSubtreeLineFeedCount = newLfCount;

				UpdatePieceMetadataWithDelta(
					x.Parent,
					bufferDelta,
					lfDelta);
			}
		}

		UpdatePieceMetadata(x.Parent);

		if (yWasRed)
		{
			NULL_NODE.Parent = NULL_NODE;
			return;
		}
		FixAfterDeletion(x);
	}

	private void FixAfterDeletion(PieceNode node)
	{
		PieceNode sibling;
		while (node != _root && node.Color == RBColor.Black)
		{
			if (node == node.Parent.Left)
			{
				sibling = node.Parent.Right;

				if (sibling.Color == RBColor.Red)
				{
					sibling.Color = RBColor.Black;
					node.Parent.Color = RBColor.Red;
					LeftRotate(node.Parent);
					sibling = node.Parent.Right;
				}

				if (sibling.Left.Color == RBColor.Black &&
					sibling.Right.Color == RBColor.Black)
				{
					sibling.Color = RBColor.Red;
					node = node.Parent;
				}
				else
				{
					if (sibling.Right.Color == RBColor.Black)
					{
						sibling.Left.Color = RBColor.Black;
						sibling.Color = RBColor.Red;
						RightRotate(sibling);
						sibling = node.Parent.Right;
					}

					sibling.Color = node.Parent.Color;
					node.Parent.Color = RBColor.Black;
					sibling.Right.Color = RBColor.Black;
					LeftRotate(node.Parent);
					node = _root;
				}
			}
			else
			{
				sibling = node.Parent.Left;

				if (sibling.Color == RBColor.Red)
				{
					sibling.Color = RBColor.Black;
					node.Parent.Color = RBColor.Red;
					RightRotate(node.Parent);
					sibling = node.Parent.Left;
				}

				if (sibling.Left.Color == RBColor.Black &&
					sibling.Right.Color == RBColor.Black)
				{
					sibling.Color = RBColor.Red;
					node = node.Parent;
				}
				else
				{
					if (sibling.Left.Color == RBColor.Black)
					{
						sibling.Right.Color = RBColor.Black;
						sibling.Color = RBColor.Red;
						LeftRotate(sibling);
						sibling = node.Parent.Left;
					}

					sibling.Color = node.Parent.Color;
					node.Parent.Color = RBColor.Black;
					sibling.Left.Color = RBColor.Black;
					RightRotate(node.Parent);
					node = _root;
				}
			}
		}

		node.Color = RBColor.Black;
		NULL_NODE.Parent = NULL_NODE;
	}

	private void InsertMiddle(PieceNode piece, string text, int localOffset)
	{
		//offset is guaranteed to be somewhere inside the piece and not the edges
		var (firstPiece, lastPiece) = SplitNodeAtMiddle(piece, localOffset);

		//middle piece
		var bufferInsertOffset = _addBuffer.Count;
		var linestarts = GetLineStarts(text, bufferInsertOffset);
		_addBufferLineStarts.AddRange(linestarts);
		_addBuffer.AddRange(text);

		var middlePiece = CreatePieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		middlePiece.LineFeedCount = linestarts.Count;

		//Shorten piece to match firstPiece
		ResizeTo(piece, firstPiece, false);

		InsertAsSuccessor(piece, middlePiece);
		InsertAsSuccessor(middlePiece, lastPiece);
	}

	private (PieceNode, PieceNode) SplitNodeAtMiddle(PieceNode piece, int offset)
	{
		var linestarts = GetPieceBufferLineStartsForNodeBuffer(piece);
		var firstPiece = CreatePieceNode(piece.pieceBuffer, piece.Start, piece.Start + offset);

		//how many linebreaks between piece.start and piece.start + localOffset?
		var l = CountLineBreaksUntil(linestarts, piece.Start);
		var h = CountLineBreaksUntil(linestarts, piece.Start + offset);
		firstPiece.LineFeedCount = h - l;

		var lastPiece = CreatePieceNode(piece.pieceBuffer, piece.Start + offset, piece.End);
		lastPiece.LineFeedCount = piece.LineFeedCount - firstPiece.LineFeedCount;

		return (firstPiece, lastPiece);

	}

	private int CountLineBreaksUntil(IReadOnlyList<int> buf, int target)
	{
		int lo = 0, hi = buf.Count;
		while (lo < hi)
		{
			int mid = lo + ((hi - lo) >> 1);
			var val = buf[mid] - 1; //subtracting one because array contains linestarts while we want linebreaks
			if (val < target) lo = mid + 1;
			else hi = mid;
		}
		return lo;
	}
	private void InsertAsSuccessor(PieceNode target, PieceNode newNode)
	{
		if (_root == NULL_NODE)
		{
			_root = newNode;
			_root.Color = RBColor.Black;
			return;
		}
		else if (target.Right == NULL_NODE)
		{
			target.Right = newNode;
			newNode.Parent = target;
		}
		else
		{
			var leftMost = PieceNode.LeftMost(target.Right);
			var successor = leftMost == NULL_NODE ? target.Right : leftMost;
			successor.Left = newNode;
			newNode.Parent = successor;
		}
		FixAfterInsertion(newNode);
	}

	private void InsertAsPredecessor(PieceNode target, PieceNode newNode)
	{
		if (_root == NULL_NODE)
		{
			_root = newNode;
			_root.Color = RBColor.Black;
		}
		else if (target.Left == NULL_NODE)
		{
			target.Left = newNode;
			newNode.Parent = target;
		}
		else
		{
			var rightMost = PieceNode.RightMost(target.Left);
			var predecessor = rightMost == NULL_NODE ? target.Left : rightMost;
			predecessor.Right = newNode;
			newNode.Parent = predecessor;
		}
		FixAfterInsertion(newNode);
	}
	public string Print()
	{
		StringBuilder builder = new();
		ForEach((PieceNode node) =>
		{
			var buffer = GetPieceBuffer(node);
			for (int i = node.Start; i < node.End; i++)
			{
				builder.Append(buffer[i]);
			}
		});
		return builder.ToString();
	}

	public Coordinate GetCoordinate(int offset)
	{
		var node = _root;
		var line = 0;
		var initOffset = offset;
		while (node != NULL_NODE)
		{
			if (node.LeftSubtreeBufferLength >= offset)
			{
				node = node.Left;
			}
			else if (node.Length >= offset - node.LeftSubtreeBufferLength)
			{
				var k = offset - node.LeftSubtreeBufferLength;
				line += node.LeftSubtreeLineFeedCount;
				var linestarts = GetPieceBufferLineStartsForNodeBuffer(node);
				var target = node.Start + k;
				var first = CountLineBreaksUntil(linestarts, node.Start);
				var last = CountLineBreaksUntil(linestarts, target);
				var lfDelta = last - first;
				line += lfDelta;

				if (lfDelta == 0)
				{
					var lineStartOffset = GetOffset(new(0, line));
					return new(initOffset - lineStartOffset, line);
				}
				var column = target - linestarts[last - 1];
				return new(column, line);
			}
			else
			{
				offset -= node.LeftSubtreeBufferLength + node.Length;
				line += node.LeftSubtreeLineFeedCount + node.LineFeedCount;
				if (node.Right == NULL_NODE)
				{
					var lineStartOffset = GetOffset(new(0, line));
					var lineOffset = initOffset - offset - lineStartOffset;
					return new(lineOffset, line);
				}
				node = node.Right;
			}
		}
		return new(0, 0);
	}

	private string PrintRawLine(int line)
	{
		return PrintRawLineCore(line);
	}

	public string PrintCleanLine(int line)
	{
		return PrintRawLine(line);
	}

	// Locates the pieced together text from offset to offset + length and fills it into a preallocated buffer of atleast size length
	public int Slice(int offset, int length, Span<char> spanBuf)
	{
		var node = _root;
		if (offset < 0 || offset >= BufferLength || length <= 0) return 0;
		length = Math.Min(length, BufferLength - offset);
		int remainder = 0;
		int insertionIndex = 0;
		while (node != NULL_NODE)
		{
			if (node.LeftSubtreeBufferLength > offset)
			{
				node = node.Left;
			}
			else if (node.Length >= offset - node.LeftSubtreeBufferLength)
			{
				//this piece contains offset
				var local = offset - node.LeftSubtreeBufferLength;
				var pieceBuffer = GetPieceBuffer(node);
				var start = local + node.Start;
				var end = Math.Min(node.End, start + length);
				AppendRange(spanBuf, pieceBuffer, local + node.Start, end, ref insertionIndex);
				remainder = length - (end - start); //some of the text may be in subsequent pieces
				break;
			}
			else
			{
				var bufferLength = node.LeftSubtreeBufferLength + node.Length;
				offset -= bufferLength;
				node = node.Right;
			}
		}
		if (remainder <= 0) return length;
		for (var next = PieceNode.Next(node); next != NULL_NODE; next = PieceNode.Next(next))
		{
			var buffer = GetPieceBuffer(next);
			var start = next.Start;
			var end = Math.Min(next.End, start + remainder);
			AppendRange(spanBuf, buffer, start, end, ref insertionIndex);
			remainder -= end - start; //subtract data written
			if (remainder <= 0) break;
		}
		return length;
	}

	private string PrintRawLineCore(int line)
	{
		var sb = new StringBuilder();
		var node = _root;

		while (node != NULL_NODE)
		{
			if (node.Left != NULL_NODE && node.LeftSubtreeLineFeedCount >= line)
			{
				node = node.Left;
			}
			else if (node.LeftSubtreeLineFeedCount + node.LineFeedCount > line)
			{
				// line starts and ends inside this piece
				var k = line - node.LeftSubtreeLineFeedCount;
				var buffer = GetPieceBuffer(node);
				var linestarts = GetPieceBufferLineStartsForNodeBuffer(node);
				var first = CountLineBreaksUntil(linestarts, node.Start);

				var start = k == 0 ? node.Start : linestarts[first + k - 1];
				var end = linestarts[first + k];          // just past the terminating newline
				AppendRange(sb, buffer, start, end);
				return sb.ToString();
			}
			else if (node.LeftSubtreeLineFeedCount + node.LineFeedCount == line)
			{
				// line starts in this piece (after its last newline) and runs off the end
				var buffer = GetPieceBuffer(node);
				var linestarts = GetPieceBufferLineStartsForNodeBuffer(node);
				var first = CountLineBreaksUntil(linestarts, node.Start);

				var start = node.LineFeedCount == 0 ? node.Start : linestarts[first + node.LineFeedCount - 1];
				var end = node.Start + node.Length;
				AppendRange(sb, buffer, start, end);
				break;
			}
			else
			{
				line -= node.LeftSubtreeLineFeedCount + node.LineFeedCount;
				node = node.Right;
			}
		}

		if (node == NULL_NODE) return sb.ToString();   // line is past the end

		// the tail of the line continues in the following pieces
		for (var next = PieceNode.Next(node); next != NULL_NODE; next = PieceNode.Next(next))
		{
			var buffer = GetPieceBuffer(next);
			if (next.LineFeedCount > 0)
			{
				var linestarts = GetPieceBufferLineStartsForNodeBuffer(next);
				var first = CountLineBreaksUntil(linestarts, next.Start);
				var end = linestarts[first];              // just past the first newline
				AppendRange(sb, buffer, next.Start, end);
				break;
			}
			AppendRange(sb, buffer, next.Start, next.Start + next.Length);
		}
		return sb.ToString();
	}
	private static void AppendRange(StringBuilder sb, IReadOnlyList<char> buffer, int start, int end)
	{
		for (int i = start; i < end; i++)
			sb.Append(buffer[i]);
	}
	private static void AppendRange(Span<char> sb, IReadOnlyList<char> buffer, int start, int end, ref int insertionIndex)
	{
		for (int i = start; i < end; i++)
		{
			sb[insertionIndex] = buffer[i];
			insertionIndex++;
		}
	}
	public int GetOffset(Coordinate pos)
	{
		return LogicalCoorinatesToOffset(pos);
	}
	private void ForEach(Action<PieceNode> fn)
	{
		InOrder(_root, fn);
	}


	//TODO: DO this iteratively
	private void InOrder(PieceNode node, Action<PieceNode> fn)
	{
		if (node == NULL_NODE) return;
		InOrder(node.Left, fn);
		fn(node);
		InOrder(node.Right, fn);
	}


	private void InsertRight(PieceNode piece, string text)
	{
		var bufferInsertOffset = _addBuffer.Count;
		var linestarts = GetLineStarts(text, bufferInsertOffset);

		//push to addbuffer line starts
		_addBufferLineStarts.AddRange(linestarts);
		_addBuffer.AddRange(text);
		var newPiece = CreatePieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		newPiece.LineFeedCount = linestarts.Count;

		//insert after piece
		InsertAsSuccessor(piece, newPiece);
	}

	private void InsertLeft(PieceNode piece, string text)
	{
		var bufferInsertOffset = _addBuffer.Count;
		var linestarts = GetLineStarts(text, bufferInsertOffset);
		//push to addbuffer line starts
		_addBufferLineStarts.AddRange(linestarts);

		//push text to buffer
		_addBuffer.AddRange(text);
		var newPiece = CreatePieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		newPiece.LineFeedCount = linestarts.Count;

		//insert before piece
		InsertAsPredecessor(piece, newPiece);

	}

	private void FixAfterInsertion(PieceNode node)
	{
		UpdatePieceMetadata(node);

		while (node != _root && node.Parent.Color == RBColor.Red)
		{
			if (node.Parent == node.Parent.Parent.Left)
			{
				var parentSibling = node.Parent.Parent.Right;

				if (parentSibling.Color == RBColor.Red)
				{
					node.Parent.Color = RBColor.Black;
					parentSibling.Color = RBColor.Black;
					node.Parent.Parent.Color = RBColor.Red;
					node = node.Parent.Parent;
				}
				else
				{
					if (node == node.Parent.Right)
					{
						node = node.Parent;
						LeftRotate(node);
					}

					node.Parent.Color = RBColor.Black;
					node.Parent.Parent.Color = RBColor.Red;
					RightRotate(node.Parent.Parent);
				}
			}
			else
			{
				var parentSibling = node.Parent.Parent.Left;

				if (parentSibling.Color == RBColor.Red)
				{
					node.Parent.Color = RBColor.Black;
					parentSibling.Color = RBColor.Black;
					node.Parent.Parent.Color = RBColor.Red;
					node = node.Parent.Parent;
				}
				else
				{
					if (node == node.Parent.Left)
					{
						node = node.Parent;
						RightRotate(node);
					}

					node.Parent.Color = RBColor.Black;
					node.Parent.Parent.Color = RBColor.Red;
					LeftRotate(node.Parent.Parent);
				}
			}
		}

		_root.Color = RBColor.Black;
	}

	private void LeftRotate(PieceNode node)
	{
		var rightNode = node.Right;

		node.Right = rightNode.Left;

		if (rightNode.Left != NULL_NODE)
		{
			rightNode.Left.Parent = node;
		}

		rightNode.Parent = node.Parent;

		if (node.Parent == NULL_NODE)
		{
			_root = rightNode;
		}
		else if (node.Parent.Left == node)
		{
			node.Parent.Left = rightNode;
		}
		else
		{
			node.Parent.Right = rightNode;
		}

		rightNode.Left = node;
		node.Parent = rightNode;

		rightNode.LeftSubtreeBufferLength +=
			node.LeftSubtreeBufferLength + node.Length;

		rightNode.LeftSubtreeLineFeedCount +=
			node.LeftSubtreeLineFeedCount + node.LineFeedCount;
	}

	private void RightRotate(PieceNode node)
	{
		var leftNode = node.Left;

		node.Left = leftNode!.Right;

		if (leftNode.Right != NULL_NODE)
		{
			leftNode.Right.Parent = node;
		}

		leftNode.Parent = node.Parent;

		if (node.Parent == NULL_NODE)
		{
			_root = leftNode;
		}
		else if (node.Parent.Right == node)
		{
			node.Parent.Right = leftNode;
		}
		else
		{
			node.Parent.Left = leftNode;
		}

		leftNode.Right = node;
		node.Parent = leftNode;

		// Update metadata
		node.LeftSubtreeBufferLength -=
			leftNode.LeftSubtreeBufferLength + leftNode.Length;

		node.LeftSubtreeLineFeedCount -=
			leftNode.LeftSubtreeLineFeedCount + leftNode.LineFeedCount;
	}
	private void ExtendLastAddBufferPiece(PieceNode piece, string text)
	{
		var bufferInsertOffset = _addBuffer.Count;
		var linestarts = GetLineStarts(text, bufferInsertOffset);

		//push to addbuffer line starts
		_addBufferLineStarts.AddRange(linestarts);

		//push text to buffer
		_addBuffer.AddRange(text);
		var oldLfCount = piece.LineFeedCount;
		piece.End += text.Length;
		piece.LineFeedCount += linestarts.Count;
		var lfdelta = piece.LineFeedCount - oldLfCount;
		UpdatePieceMetadataWithDelta(piece, text.Length, lfdelta);
	}

	private void UpdatePieceMetadataWithDelta(PieceNode piece, int bufferLengthDelta, int lfdelta)
	{
		if (bufferLengthDelta == 0 && lfdelta == 0) return;
		while (piece != _root)
		{
			if (piece.Parent.Left == piece)
			{
				piece.Parent.LeftSubtreeBufferLength += bufferLengthDelta;
				piece.Parent.LeftSubtreeLineFeedCount += lfdelta;
			}
			piece = piece.Parent;
		}
	}

	private void UpdateTableMetadata()
	{
		var node = _root;
		var pieceLength = 0;
		var lfCount = 1;
		while (node != NULL_NODE)
		{
			pieceLength += node.LeftSubtreeBufferLength + node.Length;
			lfCount += node.LeftSubtreeLineFeedCount + node.LineFeedCount;
			node = node.Right;
		}
		BufferLength = pieceLength;
		LineFeedCount = lfCount;
	}
	private void UpdatePieceMetadata(PieceNode node)
	{
		if (node == _root) return;
		while (node != _root && node != node.Parent.Left)
		{
			node = node.Parent;
		}

		if (node == _root) return;
		node = node.Parent;
		var lengthDelta = PieceNode.TotalBufferLength(node.Left) - node.LeftSubtreeBufferLength;
		var lfDelta = PieceNode.TotalLineFeedCount(node.Left) - node.LeftSubtreeLineFeedCount;
		node.LeftSubtreeBufferLength += lengthDelta;
		node.LeftSubtreeLineFeedCount += lfDelta;
		UpdatePieceMetadataWithDelta(node, lengthDelta, lfDelta);
	}

	//TODO: this func is weird as hell. Have to make it simpler
	private int LogicalCoorinatesToOffset(Coordinate pos)
	{
		var line = pos.Line;
		var col = pos.Col;
		var node = _root;
		int offset = 0;
		if (line == 0) return col;
		while (node != NULL_NODE)
		{
			if (node.Left != NULL_NODE && node.LeftSubtreeLineFeedCount >= line)
			{
				node = node.Left;
			}
			else if (node.LineFeedCount >= line - node.LeftSubtreeLineFeedCount)
			{
				var k = line - node.LeftSubtreeLineFeedCount;
				var starts = GetPieceBufferLineStartsForNodeBuffer(node);
				var first = CountLineBreaksUntil(starts, node.Start);
				var local = starts[first + k - 1] - node.Start;
				return offset + node.LeftSubtreeBufferLength + local + col;
			}
			else
			{
				var totalLines = node.LeftSubtreeLineFeedCount + node.LineFeedCount;
				line -= totalLines;
				offset += node.LeftSubtreeBufferLength + node.Length;
				node = node.Right;
			}
		}
		return offset;
	}


	private PiecePosition GetPieceByOffset(int offset)
	{
		var node = _root;
		var absoluteOffset = 0;
		int originalOffset = offset;
		while (node != NULL_NODE)
		{
			if (node.LeftSubtreeBufferLength > offset)
			{
				node = node.Left;
			}
			else if (node.Length >= offset - node.LeftSubtreeBufferLength)
			{
				absoluteOffset += node.LeftSubtreeBufferLength;
				return new(node, absoluteOffset, offset - node.LeftSubtreeBufferLength);
			}
			else
			{
				var bufferLength = node.LeftSubtreeBufferLength + node.Length;
				offset -= bufferLength;
				absoluteOffset += bufferLength;
				node = node.Right;
			}
		}
		throw new ArgumentOutOfRangeException($"Max Offset : {BufferLength} , original passed offset {originalOffset}");
	}

	private IReadOnlyList<char> GetPieceBuffer(PieceNode node)
	{
		if (node.pieceBuffer == PieceBuffer.Original) return _originalBuffer;
		return _addBuffer;
	}
	private IReadOnlyList<int> GetPieceBufferLineStartsForNodeBuffer(PieceNode node)
	{
		if (node.pieceBuffer == PieceBuffer.Original) return _originalBufferLineStarts;
		return _addBufferLineStarts;
	}
}
