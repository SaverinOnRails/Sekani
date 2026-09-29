using System.Text;

namespace Sekani.EditorCore;

public class PieceNode
{
	public PieceBuffer pieceBuffer { get; }
	public int Start { get; }
	public int End { get; }

	public PieceNode(PieceBuffer pieceBufferType, int start, int end)
	{
		pieceBuffer = pieceBufferType;
		Start = start;
		End = end;
	}

	public int Length => End - Start;
}

public enum PieceBuffer
{
	Add,
	Original
}

public class PieceTable
{
	private List<PieceNode> _pieces = [];
	private readonly IReadOnlyList<char> originalBuffer;
	private readonly List<char> _addBuffer = [];

	public PieceTable(string original)
	{
		originalBuffer = [.. original];
		if (original.Length > 0)
			_pieces = [new(PieceBuffer.Original, 0, original.Length)];
	}

	public void Insert(string text, Coordinate pos)
	{
		var offset = GetOffsetFromLogicalCoordinates(pos);
		var (pieceIndex, localOffset) = GetPieceIndexFromOffset(offset);
		var addStart = _addBuffer.Count;
		_addBuffer.AddRange(text);
		var middle = new PieceNode(PieceBuffer.Add, addStart, addStart + text.Length);
		if (pieceIndex == _pieces.Count)
		{
			_pieces.Add(middle);
			return;
		}
		(PieceNode? first, PieceNode? last) = Split(_pieces[pieceIndex], localOffset);
		_pieces.RemoveAt(pieceIndex);
		if (first is not null)
		{
			_pieces.Insert(pieceIndex, first);
			pieceIndex++;
		}
		_pieces.Insert(pieceIndex, middle);
		pieceIndex++;
		if (last is not null)
		{
			_pieces.Insert(pieceIndex, last);
		}
	}

	public void Print()
	{
		StringBuilder builder = new();
		for (int i = 0; i < _pieces.Count; i++)
		{
			var piece = _pieces[i];
			var buffer = GetPieceBuffer(piece);
			for (int j = piece.Start; j < piece.End; j++)
			{
				builder.Append(buffer[j]);
			}
		}
		Console.WriteLine(builder.ToString());
	}

	public void Delete(Coordinate start, Coordinate end)
	{
		var startOffset = GetOffsetFromLogicalCoordinates(start);
		var endOffset = GetOffsetFromLogicalCoordinates(end);

		var (startPieceIndex, startLocalOffset) =
			GetPieceIndexFromOffset(startOffset);

		var (endPieceIndex, endLocalOffset) =
			GetPieceIndexFromOffset(endOffset);

		var (firstNode, _) =
			Split(_pieces[startPieceIndex], startLocalOffset);

		var (_, lastNode) =
			Split(_pieces[endPieceIndex], endLocalOffset);

		if (startPieceIndex == endPieceIndex)
		{
			_pieces.RemoveAt(startPieceIndex);
		}
		else
		{
			_pieces.RemoveRange(
				startPieceIndex,
				endPieceIndex - startPieceIndex + 1);
		}
		var index = startPieceIndex;

		if (firstNode is not null)
			_pieces.Insert(index++, firstNode);

		if (lastNode is not null)
			_pieces.Insert(index, lastNode);
	}
	public Coordinate GetCoordinateFromOffset(int offset)
	{
		var line = 0;
		var col = 0;
		int utf16offset = 0;
		foreach (var piece in _pieces)
		{
			var buffer = GetPieceBuffer(piece);
			for (int i = piece.Start; i < piece.End; i++)
			{
				if (utf16offset == offset)
				{
					return new(col, line);
				}
				char c = buffer[i];
				if (c == '\n')
				{
					line++;
					col = 0;
				}
				else
				{
					col++;
				}
				utf16offset++;
			}
		}
		if (utf16offset == offset) return new(col, line);
		return new(0, 0);
	}

	private (PieceNode? first, PieceNode? last) Split(
		PieceNode node,
		int offset)
	{
		PieceNode? first = null;
		PieceNode? last = null;

		if (offset > 0)
		{
			first = new PieceNode(
				node.pieceBuffer,
				node.Start,
				node.Start + offset);
		}

		if (offset < node.Length)
		{
			last = new PieceNode(
				node.pieceBuffer,
				node.Start + offset,
				node.End);
		}

		return (first, last);
	}
	//naive buffer walkthrough, get utf-16 offset
	public int GetOffsetFromLogicalCoordinates(Coordinate pos)
	{
		int utf16offset = 0;
		int line = 0;
		int col = 0;
		foreach (var piece in _pieces)
		{
			var buffer = GetPieceBuffer(piece);
			for (int j = piece.Start; j < piece.End; j++)
			{
				if (col == pos.Col && line == pos.Line)
				{
					return utf16offset;
				}
				char c = buffer[j];
				if (c == '\n')
				{
					line++;
					col = 0;
				}
				else
				{
					col++;
				}
				utf16offset++;
			}
		}
		if (line == pos.Line && col == pos.Col)
			return utf16offset;
		return 0;
	}

	private (int pieceIndex, int localOffset) GetPieceIndexFromOffset(int offset)
	{
		var sum = 0;
		for (int i = 0; i < _pieces.Count; i++)
		{
			var piece = _pieces[i];
			var length = piece.End - piece.Start;
			if (length + sum > offset)
			{
				return (i, offset - sum);
			}
			sum += length;
		}
		return (_pieces.Count, 0);
	}

	private IReadOnlyList<char> GetPieceBuffer(PieceNode node)
	{
		if (node.pieceBuffer == PieceBuffer.Original) return originalBuffer;
		return _addBuffer;
	}
}
