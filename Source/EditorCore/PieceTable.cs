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
	public RBColor Color { get; internal set; } = RBColor.Red;

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
		var root = CreatePieceNode(PieceBuffer.Original, 0, original.Length);
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

	private PieceNode CreatePieceNode(PieceBuffer pieceBuffer, int start, int end)
	{
		var node = new PieceNode(pieceBuffer, start, end)
		{
			Parent = NULL_NODE,
			Left = NULL_NODE,
			Right = NULL_NODE
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
			Console.WriteLine("extending last add buffer piece");
			ExtendLastAddBufferPiece(piece, text);
		}
		//if inserting at the begining of the piece, 
		else if (targetNodePosition.LocalOffset == 0)
		{
			Console.WriteLine("inserting into left");
			InsertLeft(piece, text);
		}
		//if inserting at the end of the piece
		else if (targetNodePosition.LocalOffset == piece.Length)
		{
			Console.WriteLine("inserting into right");
			InsertRight(piece, text);
		}
		//inserting into the middle of the piece
		else
		{
			Console.WriteLine("inserting into middle");
			InsertMiddle(piece, text, targetNodePosition.LocalOffset);
		}
	}

	private void Delete(int startOffset, int length)
	{
		if (length < 0) return;
		var startPosition = GetPieceByOffset(startOffset);
		var endPosition = GetPieceByOffset(startOffset + length);

		var startPiece = startPosition.Piece;
		var endPiece = startPosition.Piece;

		//deletion range is in one piece. Simple case
		if (startPiece == endPiece)
		{
			//if deletion starts at the beginning of the piece
			if (startPiece.Start == startOffset)
			{
				//if deleting the entire piece
				if (startPiece.Length == length)
				{

				}
			}
		}
	}

	private void InsertMiddle(PieceNode piece, string text, int localOffset)
	{
		//offset is guaranteed to be somewhere inside the piece and not the edges
		//firstpiece
		var firstPiece = CreatePieceNode(piece.pieceBuffer, piece.Start, piece.Start + localOffset);
		var pieceBufferLineStarts = GetPieceBufferLineStartsForNodeBuffer(firstPiece);
		var pieceBuffer = GetPieceBuffer(piece);

		//how many linebreaks between piece.start and piece.start + localOffset?
		var l = LowerBound(pieceBufferLineStarts, piece.Start);
		var h = LowerBound(pieceBufferLineStarts, piece.Start + localOffset);
		firstPiece.LineFeedCount = h - l;

		//middle piece
		var bufferInsertOffset = _addBuffer.Count;
		var linestarts = GetLineStarts(text, bufferInsertOffset);
		_addBufferLineStarts.AddRange(linestarts);
		_addBuffer.AddRange(text);

		var middlePiece = CreatePieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		middlePiece.LineFeedCount = linestarts.Count;

		//last piece
		var lastPiece = CreatePieceNode(piece.pieceBuffer, piece.Start + localOffset, piece.End);
		lastPiece.LineFeedCount = piece.LineFeedCount - firstPiece.LineFeedCount;

		// Shorten piece to match firstPiece
		var oldLfCount = piece.LineFeedCount;
		var oldLength = piece.Length;
		piece.End = firstPiece.End;
		piece.LineFeedCount = firstPiece.LineFeedCount;
		var lfDelta = piece.LineFeedCount - oldLfCount;
		var bufferLengthDelta = piece.Length - oldLength;
		UpdatePieceMetadataWithDelta(piece, bufferLengthDelta, lfDelta);

		InsertAsSuccessor(piece, middlePiece);

		InsertAsSuccessor(middlePiece, lastPiece);
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
	public int GetOffset(Coordinate pos)
	{
		return LogicalCoorinatesToOffset(pos);
	}
	private void ForEach(Action<PieceNode> fn)
	{
		InOrder(_root, fn);
	}

	private void InOrder(PieceNode? node, Action<PieceNode> fn)
	{
		if (node is null) return;
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
		FixAfterInsertion(newPiece);
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
			if (piece.Parent!.Left == piece)
			{
				piece.Parent.LeftSubtreeBufferLength += bufferLengthDelta;
				piece.Parent.LeftSubtreeLineFeedCount += lfdelta;
			}
			piece = piece.Parent;
		}
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

	private int LogicalCoorinatesToOffset(Coordinate pos)
	{
		var line = pos.Line;
		var col = pos.Col;
		var node = _root;
		int offset = 0;
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
