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

	public PieceNode? Parent { get; internal set; }
	public PieceNode? Right { get; internal set; }
	public PieceNode? Left { get; internal set; }

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

	public int Length => End - Start;


	public static int TotalBufferLength(PieceNode? node)
	{
		if (node is null) return 0;
		return node.LeftSubtreeBufferLength + node.Length + TotalBufferLength(node.Right);
	}

	public static int TotalLineFeedCount(PieceNode? node)
	{
		if (node is null) return 0;
		return node.LeftSubtreeLineFeedCount + node.LineFeedCount + TotalLineFeedCount(node.Right);
	}

	public static PieceNode? RightMost(PieceNode? node)
	{
		if (node is null) return null;
		while (node.Right is not null)
		{
			node = node.Right;
		}
		return node;
	}

	public static PieceNode? LeftMost(PieceNode? node)
	{
		if (node is null) return null;
		while (node.Left is not null)
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

	public PieceTable(string original)
	{
		_originalBuffer = [.. original];
		var root = new PieceNode(PieceBuffer.Original, 0, original.Length)
		{
			Color = RBColor.Black
		};
		var originalLineStarts = GetLineStarts(original, 0);
		_originalBufferLineStarts = [0, .. originalLineStarts];
		root.LineFeedCount = originalLineStarts.Count;
		_root = root;
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

	private void InsertMiddle(PieceNode piece, string text, int localOffset)
	{
		//offset is guaranteed to be somewhere inside the piece and not the edges
		//firstpiece
		var firstPiece = new PieceNode(piece.pieceBuffer, piece.Start, piece.Start + localOffset);
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

		var middlePiece = new PieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		middlePiece.LineFeedCount = linestarts.Count;

		//last piece
		var lastPiece = new PieceNode(piece.pieceBuffer, piece.Start + localOffset, piece.End);
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
		if (target.Right is null)
		{
			target.Right = newNode;
			newNode.Parent = target;
		}
		else
		{
			var leftMost = PieceNode.LeftMost(target.Right);
			var successor = leftMost ?? target.Right;
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
		var newPiece = new PieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length)
		{
			LineFeedCount = linestarts.Count
		};

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
		var newPiece = new PieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		newPiece.LineFeedCount = linestarts.Count;

		//insert before piece
		if (piece.Left is null)
		{
			piece.Left = newPiece;
			newPiece.Parent = piece;
		}
		else
		{
			var rightMost = PieceNode.RightMost(piece.Left);
			var predecessor = rightMost ?? piece.Left;
			predecessor.Right = newPiece;
			newPiece.Parent = predecessor;
		}
		FixAfterInsertion(newPiece);
	}

	private void FixAfterInsertion(PieceNode node)
	{
		UpdatePieceMetadata(node);

		while (node != _root && node.Parent!.Color == RBColor.Red)
		{
			if (node.Parent == node.Parent.Parent!.Left)
			{
				var parentSibling = node.Parent.Parent.Right;

				if (parentSibling?.Color == RBColor.Red)
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

					node.Parent!.Color = RBColor.Black;
					node.Parent.Parent!.Color = RBColor.Red;
					RightRotate(node.Parent.Parent);
				}
			}
			else
			{
				var parentSibling = node.Parent.Parent!.Left;

				if (parentSibling?.Color == RBColor.Red)
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

					node.Parent!.Color = RBColor.Black;
					node.Parent.Parent!.Color = RBColor.Red;
					LeftRotate(node.Parent.Parent);
				}
			}
		}

		_root.Color = RBColor.Black;
	}

	private void LeftRotate(PieceNode node)
	{
		var rightNode = node.Right;

		node.Right = rightNode!.Left;

		if (rightNode.Left != null)
		{
			rightNode.Left.Parent = node;
		}

		rightNode.Parent = node.Parent;

		if (node.Parent == null)
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

		if (leftNode.Right != null)
		{
			leftNode.Right.Parent = node;
		}

		leftNode.Parent = node.Parent;

		if (node.Parent == null)
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
		while (node != _root && node != node.Parent!.Left)
		{
			node = node.Parent;
		}

		if (node == _root) return;
		node = node.Parent!;
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
		while (node is not null)
		{
			if (node.Left is not null && node.LeftSubtreeLineFeedCount >= line)
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
		while (node != null)
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
