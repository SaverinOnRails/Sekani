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
	private IReadOnlyList<int> _originalBufferLineBreaks;
	private PieceNode _root;
	private readonly List<char> _addBuffer = [];
	private readonly List<int> _addBufferLineBreaks = [];

	public PieceTable(string original)
	{
		_originalBuffer = [.. original];
		var root = new PieceNode(PieceBuffer.Original, 0, original.Length)
		{
			Color = RBColor.Black
		};
		var originalLineBreaks = GetLineBreaks(original, 0);
		_originalBufferLineBreaks = originalLineBreaks;
		root.LineFeedCount = originalLineBreaks.Count;
		_root = root;
	}

	private List<int> GetLineBreaks(string text, int offset)
	{
		List<int> lineBreakOffsets = [];
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\n')
			{
				lineBreakOffsets.Add(i + offset);
			}
		}
		return lineBreakOffsets;
	}

	public void Insert(string text, int offset)
	{
		var targetNodePosition = GetPieceByOffset(offset);
		var nodeAbsoluteOffset = targetNodePosition.AbsoluteOffset;
		var piece = targetNodePosition.Piece;
		//if inserting into the end of piece at the end of the add buffer, extend the the piece instead of creating one
		if (piece.pieceBuffer == PieceBuffer.Add && piece.End == _addBuffer.Count && targetNodePosition.LocalOffset == piece.Length)
		{
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

	private void InsertMiddle(PieceNode piece, string text, int localOffset)
	{
		//offset is guaranteed to be somewhere inside the piece and not the edges
		//firstpiece
		var firstPiece = new PieceNode(piece.pieceBuffer, piece.Start, piece.Start + localOffset);
		firstPiece.LineFeedCount = CountLineBreakInPieceSlice(firstPiece);
		
		//middle piece
		var bufferInsertOffset = _addBuffer.Count;
		var linebreaks = GetLineBreaks(text, bufferInsertOffset);
		//push to addbuffer line breaks
		_addBufferLineBreaks.AddRange(linebreaks);
		//push text to buffer
		_addBuffer.AddRange(text);

		var middlePiece = new PieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		middlePiece.LineFeedCount = linebreaks.Count;

		//last piece
		var lastPiece = new PieceNode(piece.pieceBuffer, piece.Start + localOffset, piece.End);
		lastPiece.LineFeedCount = CountLineBreakInPieceSlice(lastPiece);

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
	public void Print()
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
		Console.WriteLine(builder.ToString());
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

	private int CountLineBreakInPieceSlice(PieceNode piece)
	{
		var bufferLineBreaks = GetPieceBufferLineBreaksCache(piece);
		var list = bufferLineBreaks;
		var start = piece.Start;
		var end = piece.End;
		//thanks gemini
		int low = 0, high = list.Count;
		while (low < high)
		{
			int mid = low + ((high - low) >> 1);
			if (list[mid] < start) low = mid + 1;
			else high = mid;
		}
		int lower = low;

		low = 0;
		high = list.Count;
		while (low < high)
		{
			int mid = low + ((high - low) >> 1);
			if (list[mid] < end) low = mid + 1;
			else high = mid;
		}

		return low - lower;
	}


	private void InsertRight(PieceNode piece, string text)
	{
		var bufferInsertOffset = _addBuffer.Count;
		var linebreaks = GetLineBreaks(text, bufferInsertOffset);

		//push to addbuffer line breaks
		_addBufferLineBreaks.AddRange(linebreaks);
		_addBuffer.AddRange(text);
		var newPiece = new PieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		newPiece.LineFeedCount = linebreaks.Count;

		//insert after piece
		InsertAsSuccessor(piece, newPiece);
		FixAfterInsertion(newPiece);
	}

	private void InsertLeft(PieceNode piece, string text)
	{
		var bufferInsertOffset = _addBuffer.Count;
		var linebreaks = GetLineBreaks(text, bufferInsertOffset);
		//push to addbuffer line breaks
		_addBufferLineBreaks.AddRange(linebreaks);

		//push text to buffer
		_addBuffer.AddRange(text);
		var newPiece = new PieceNode(PieceBuffer.Add, bufferInsertOffset, bufferInsertOffset + text.Length);
		newPiece.LineFeedCount = linebreaks.Count;

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
		var linebreaks = GetLineBreaks(text, bufferInsertOffset);

		//push to addbuffer line breaks
		_addBufferLineBreaks.AddRange(linebreaks);

		//push text to buffer
		_addBuffer.AddRange(text);
		var oldLfCount = piece.LineFeedCount;
		piece.End += text.Length;
		piece.LineFeedCount += linebreaks.Count;
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
	private IReadOnlyList<int> GetPieceBufferLineBreaksCache(PieceNode node)
	{
		if (node.pieceBuffer == PieceBuffer.Original) return _originalBufferLineBreaks;
		return _addBufferLineBreaks;
	}
}
