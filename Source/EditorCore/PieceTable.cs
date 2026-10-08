using System.Diagnostics;
using System.Text;

namespace Sekani.EditorCore;

public enum RBColor : byte
{
	Red,
	Black,
}
internal record struct PiecePosition(PieceNode Piece, int AbsoluteOffset, int LocalOffset);

internal class PieceNode
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

		//TODO: do we need to handle empty tree case here?
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
					DeleteNode(targetPiece);
					return;
				}
				else
				{
					//conserve the last piece from the split
					TrimStart(targetPiece, length);
				}
			}
			//if deletion ends at the end of the piece
			else if (startPosition.AbsoluteOffset + targetPiece.Length == startOffset + length)
			{
				//conserve the first piece from the split
				TrimEnd(targetPiece, length);
			}
			//deletion falls inside middle of piece
			else
			{
				//split the head
				var (firstPiece, _) = SplitNodeAtMiddle(targetPiece, startOffset - startPosition.AbsoluteOffset);
				//split the tail
				var (_, lastPiece) = SplitNodeAtMiddle(targetPiece, (startOffset - startPosition.AbsoluteOffset) + length);
				//resize piece
				ResizeTo(targetPiece, firstPiece, false);

				//insert the last node directly as successor of the resized node, discarding the middle
				InsertAsSuccessor(targetPiece, lastPiece);
			}
			Console.WriteLine("DELTED IN PIECE");
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
			// TODO: will probably not work if the whole piece is covered in deletion
			TrimEnd(startPiece, startPiece.Length - startPosition.LocalOffset);
		}
		//delete dirty pieces
		foreach (var dirtyPiece in dirtyPieces)
		{
			DeleteNode(dirtyPiece);
		}
		if (trimLast)
		{
			//Trim End of Last Node
			TrimStart(endPiece, endPosition.LocalOffset);
		}
	}

	private void TrimStart(PieceNode piece, int length)
	{
		var (_, tail) = SplitNodeAtMiddle(piece, length);
		ResizeTo(piece, tail, copyStart: true);
	}

	private void TrimEnd(PieceNode piece, int length)
	{
		var (head, _) = SplitNodeAtMiddle(piece, piece.Length - length);
		ResizeTo(piece, head, copyStart: false);
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
		var l = LowerBound(linestarts, piece.Start);
		var h = LowerBound(linestarts, piece.Start + offset);
		firstPiece.LineFeedCount = h - l;


		var lastPiece = CreatePieceNode(piece.pieceBuffer, piece.Start + offset, piece.End);
		lastPiece.LineFeedCount = piece.LineFeedCount - firstPiece.LineFeedCount;

		return (firstPiece, lastPiece);

	}

	private int LowerBound(IReadOnlyList<int> buf, int target)
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
		if (target.Right == NULL_NODE)
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
				var first = LowerBound(linestarts, node.Start);
				var last = LowerBound(linestarts, target);
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

	// public string PrintLine(int line)
	// {
	// 	StringBuilder lineBuilder = new("");
	// 	var node = _root;
	// 	while (node != NULL_NODE)
	// 	{
	// 		if (node.Left != NULL_NODE && node.LeftSubtreeLineFeedCount >= line)
	// 		{
	// 			node = node.Left;
	// 		}
	// 		//line is within current piece
	// 		else if (node.LineFeedCount > line - node.LeftSubtreeLineFeedCount)
	// 		{
	// 			var buffer = GetPieceBuffer(node);
	// 			var pieceStart = node.Start;
	// 			var k = line - node.LeftSubtreeLineFeedCount;

	// 		}
	// 	}
	// }

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
		if (piece.Left == NULL_NODE)
		{
			piece.Left = newPiece;
			newPiece.Parent = piece;
		}
		else
		{
			var rightMost = PieceNode.RightMost(piece.Left);
			var predecessor = rightMost == NULL_NODE ? piece.Left : rightMost;
			predecessor.Right = newPiece;
			newPiece.Parent = predecessor;
		}
		FixAfterInsertion(newPiece);
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

	// private void UpdateTableMetadata() {
	// 	var node = _root;
	// 	var pieceLength = 0;
	// 	var lfCount = 1;
	// 	while(node != NULL_NODE) {
	// 		pieceLength += node.LeftSubtreeBufferLength + node.Length;
	// 		lfCount += node.LeftSubtreeLineFeedCount + node.LineFeedCount;
	// 		node = node.Right;
	// 	}
	// }
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
				var first = LowerBound(starts, node.Start);
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


	//TODO: handle when all piece is deleted and when offset > total buffer
	private PiecePosition GetPieceByOffset(int offset)
	{
		var node = _root;
		var absoluteOffset = 0;
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
		throw new ArgumentOutOfRangeException();
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
