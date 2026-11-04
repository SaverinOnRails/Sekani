using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ZLinq;
using System.Text.RegularExpressions;
using ZLinq.Linq;
namespace Sekani.Integrations.TreeSitter;

public class TreeSitterClient : IDisposable
{
	public bool Active { get; private set; } = false;
	private TSLanguage _tsLanguage = null!;
	private TSParser _tsParser = new();
	private TSTree? _tsTree;
	private TSQuery? _tsHighlightQuery;
	private string _treeSitterlib = "/home/noble/Documents/sekanitestfiles/treesitter/C/c.so";
	private string _treeSitterHighlightQuery = "/home/noble/Documents/sekanitestfiles/treesitter/C/highlights.scm";
	private int _readLength = 0;
	private int _defaultPriority = 100;

	private List<TextPredicate> _textPredicates = new();
	private List<Range> _patternData = [];
	private readonly TSReadCallback _tsInputRead;
	private readonly Func<int, int, Span<char>, int> _getBufferSlice;
	private char[] _tsReadBuffer = Array.Empty<char>();
	private nint _tsReadPtr;

	public TreeSitterClient(Func<int, int, Span<char>, int> getBufferSlice)
	{
		LoadNative();
		_tsInputRead = TsInputRead;
		_readLength = 8192;
		_getBufferSlice = getBufferSlice;
		if (_tsLanguage.Ptr == nint.Zero) return;
		_tsParser.set_language(_tsLanguage);
		var highlightsQuery = _tsLanguage.query_new(File.ReadAllText(_treeSitterHighlightQuery), out uint errorCode, out TSQueryError error);
		if (highlightsQuery is null) return;
		_tsHighlightQuery = highlightsQuery;
		ParsePatternPredicates();
		EnsureReadBuffer(_readLength);
		Active = true;
	}
	private void LoadNative()
	{
		try
		{
			var libraryHandle = NativeLibrary.Load(_treeSitterlib);
			var functionPtr = NativeLibrary.GetExport(
				libraryHandle,
				"tree_sitter_c");
			var getLanguage =
				Marshal.GetDelegateForFunctionPointer<GetLanguageDelegate>(
					functionPtr);
			nint languagePtr = getLanguage();
			_tsLanguage = new(languagePtr);
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex);
		}
	}

	public void ParseBuffer(string buf)
	{
		if (!Active) return;
		_tsTree = _tsParser.parse_string(null, buf);
	}

	public void DoEdit(TSInputEdit edit)
	{
		if (Active == false || _tsTree is null || _tsHighlightQuery is null) return;
		_tsTree.edit(edit);
		TSInput input = new()
		{
			Payload = nint.Zero,
			Encoding = TSInputEncoding.TSInputEncodingUTF16,
			Read = _tsInputRead
		};

		var newtree = _tsParser.parse(_tsTree, input);
		if (newtree is not null)
		{
			_tsTree.Dispose();
			_tsTree = newtree;
		}
	}

	private unsafe void EnsureReadBuffer(int chars)
	{
		_tsReadBuffer = GC.AllocateArray<char>(chars, pinned: true);
		_tsReadPtr = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_tsReadBuffer));
	}
	private nint TsInputRead(nint payload, uint byte_index, TSPoint pos, out uint readBytes)
	{
		var index = byte_index / sizeof(ushort);
		var charsRead = _getBufferSlice((int)index, _readLength, _tsReadBuffer.AsSpan());
		var bytesRead = charsRead * sizeof(ushort);
		readBytes = (uint)bytesRead;
		return bytesRead == 0 ? nint.Zero : _tsReadPtr;
	}

	public List<TreeSitterHighlightSpan> GetHighlights(int start, int end)
	{
		if (Active == false || _tsTree == null || _tsHighlightQuery is null) throw new InvalidOperationException();
		using var cursor = new TSQueryCursor();
		var root = _tsTree!.root_node();
		cursor.set_range((uint)start, (uint)end);
		cursor.exec(_tsHighlightQuery, root);
		List<TreeSitterHighlightSpan> spans = new();
		unsafe
		{
			while (cursor.next_capture(out TSQueryMatch match, out uint index))
			{
				//handle predicates
				if (!SatisifiesPredicate(match))
				{
					cursor.remove_match(match.id);
					continue;
				}
				var caps = (TSQueryCapture*)match.captures;
				var capture = caps[index];
				var node = capture.node;
				int node_start = (int)node.start_offset();
				int node_end = (int)node.end_offset();
				var name = _tsHighlightQuery.capture_name_for_id(capture.index, out uint _);
				spans.Add(new(name, node_start, node_end));
			}
		}
		return spans;
	}

	private unsafe void ParsePatternPredicates()
	{
		var patterns = _tsHighlightQuery!.pattern_count();
		for (uint i = 0; i < patterns; i++)
		{
			ReadOnlySpan<TSQueryPredicateStep> predicateSteps = new(
				(TSQueryPredicateStep*)_tsHighlightQuery.predicates_for_pattern(i, out uint length),
				(int)length);
			int start = 0;
			var text_predicate_start = _textPredicates.Count;
			for (int j = 0; j < predicateSteps.Length; j++)
			{
				if (predicateSteps[j].type != TSQueryPredicateStepType.TSQueryPredicateStepTypeDone)
					continue;
				var predicate = predicateSteps[start..j];
				if (!predicate.IsEmpty)
				{
					ParsePredicate(predicate);
				}
				start = j + 1;
			}
			_patternData.Add((new(text_predicate_start, _textPredicates.Count)));
		}
	}
	//Borrowed shamelessly from tree-house
	private void ParsePredicate(ReadOnlySpan<TSQueryPredicateStep> predicateSteps)
	{
		if (predicateSteps[0].type != TSQueryPredicateStepType.TSQueryPredicateStepTypeString) return;
		var predicate = new Predicate(predicateSteps, _tsHighlightQuery!);
		if (predicate.Matches("eq?", "not-eq?", "any-eq?", "any-not-eq?"))
		{
			if (predicate.Args.Length != 2) return;
			var capture_idx = predicate.Args[0];
			if (capture_idx.type != TSQueryPredicateStepType.TSQueryPredicateStepTypeCapture) return;
			var arg2 = predicate.Args[1];
			var negated = predicate.Matches("not-eq?", "any-not-eq?");
			var match_all = predicate.Matches("eq?", "not-eq?");
			if (arg2.type != TSQueryPredicateStepType.TSQueryPredicateStepTypeCapture && arg2.type != TSQueryPredicateStepType.TSQueryPredicateStepTypeString) return;
			ITextPredicateKind kind = arg2.type switch
			{
				TSQueryPredicateStepType.TSQueryPredicateStepTypeCapture => new EqualCapture(arg2.value_id),
				TSQueryPredicateStepType.TSQueryPredicateStepTypeString => new EqualString(arg2.value_id),
				_ => throw new ArgumentOutOfRangeException()
			};
			_textPredicates.Add(new(capture_idx.value_id, kind, negated, match_all));
		}
		else if (predicate.Matches("match?", "not-match?", "any-match?", "any-not-match?"))
		{
			if (predicate.Args.Length != 2) return;
			var capture_idx = predicate.Args[0];
			if (capture_idx.type != TSQueryPredicateStepType.TSQueryPredicateStepTypeCapture) return;
			if (predicate.Args[1].type != TSQueryPredicateStepType.TSQueryPredicateStepTypeString) return;
			var regex = _tsHighlightQuery!.string_value_for_id(predicate.Args[1].value_id, out _);
			var negated = predicate.Matches("not-match?", "any-not-match?");
			var match_all = predicate.Matches("match?", "not-match?");
			var regexVal = new Regex(regex);
			_textPredicates.Add(new(capture_idx.value_id, new MatchString(regexVal), negated, match_all));
		}

		else if (predicate.Matches("any-of?", "not-any-of?"))
		{
			if (predicate.Args.Length < 1) return;
			var capture_idx = predicate.Args[0];
			if (capture_idx.type != TSQueryPredicateStepType.TSQueryPredicateStepTypeCapture) return;
			var negated = predicate.Matches("not-any-of?");
			var values = new string[predicate.Args.Length - 1];
			for (int i = 1; i < predicate.Args.Length; i++)
			{
				var arg = predicate.Args[i];
				if (arg.type != TSQueryPredicateStepType.TSQueryPredicateStepTypeString) return;
				values[i - 1] = _tsHighlightQuery!.string_value_for_id(arg.value_id, out _);
			}
			_textPredicates.Add(new(capture_idx.value_id, new AnyString([.. values]), negated, false));
		}
	}

	private unsafe bool PredicateSatisfied(TextPredicate predicate, TSQueryCapture* matchedNodes, int matchedNodesCount)
	{
		var nodes = new ReadOnlySpan<TSQueryCapture>(matchedNodes, matchedNodesCount).AsValueEnumerable().Where(p => p.index == predicate.CaptureId);
		switch (predicate.PredicateKind)
		{
			case EqualString eqs:
				return SatisfiedHelper(predicate, nodes.Select(p =>
				{
					var buffer_offset = p.node.start_offset();
					var end_offset = p.node.end_offset();
					var length = end_offset - buffer_offset;
					//TODO: Do something about this allocation
					var buf = new char[length];
					var written = _getBufferSlice((int)buffer_offset, (int)length, buf.AsSpan());
					var slice = buf.AsSpan().Slice(0, written);
					if (slice.SequenceEqual(_tsHighlightQuery!.string_value_for_id(eqs.value, out _))) return true;
					return false;
				}));
			case MatchString mts:
				return SatisfiedHelper(predicate, nodes.Select(p =>
				{
					var buffer_offset = p.node.start_offset();
					var end_offset = p.node.end_offset();
					var length = end_offset - buffer_offset;
					var buf = new char[length];
					var written = _getBufferSlice((int)buffer_offset, (int)length, buf.AsSpan());
					var slice = buf.AsSpan().Slice(0, written);
					var ismatch = mts.value.IsMatch(slice);
					return ismatch;
				}));
			case AnyString any:
				return SatisfiedHelper(predicate, nodes.Select(p =>
				{
					var buffer_offset = p.node.start_offset();
					var end_offset = p.node.end_offset();
					var length = end_offset - buffer_offset;
					//TODO: Do something about this allocation
					var buf = new char[length];
					var written = _getBufferSlice((int)buffer_offset, (int)length, buf.AsSpan());
					var slice = buf.AsSpan().Slice(0, written);
					return any.value.GetAlternateLookup<ReadOnlySpan<char>>().Contains(slice);
				}));
		}
		return true;
	}

	private bool SatisfiedHelper(TextPredicate predicate, ValueEnumerable<WhereSelect<FromSpan<TSQueryCapture>, TSQueryCapture, bool>, bool> nodes)
	{
		if (predicate.MatchAll)
			return nodes.All(p => p != predicate.Negated);
		else
			return nodes.Any(p => p != predicate.Negated);
	}

	private unsafe bool SatisifiesPredicate(TSQueryMatch match)
	{
		var matchedNodes = (TSQueryCapture*)match.captures;
		var range = _patternData[match.pattern_index];
		int start = range.Start.GetOffset(_textPredicates.Count);
		int end = range.End.GetOffset(_textPredicates.Count);
		var predicates = CollectionsMarshal.AsSpan(_textPredicates).Slice(start, end - start);
		return predicates.AsValueEnumerable().All(p => PredicateSatisfied(p, matchedNodes, match.capture_count));
	}


	public void Dispose()
	{
		_tsLanguage.Dispose();
		_tsParser.Dispose();
		_tsHighlightQuery?.Dispose();
		_tsTree?.Dispose();
	}
}

public readonly record struct TreeSitterHighlightSpan(string Capture, int Start, int End);

internal readonly ref struct Predicate
{
	public readonly ReadOnlySpan<TSQueryPredicateStep> Args;
	public readonly string Name;
	public Predicate(ReadOnlySpan<TSQueryPredicateStep> steps, TSQuery _query)
	{
		Name = _query.string_value_for_id(steps[0].value_id, out _);
		Args = steps[1..];
	}

	public bool Matches(params string[] names)
	{
		return names.Contains(Name);
	}
}

internal readonly record struct TextPredicate(uint CaptureId, ITextPredicateKind PredicateKind, bool Negated, bool MatchAll);

internal interface ITextPredicateKind;
internal record struct EqualString(uint value) : ITextPredicateKind;
internal record struct EqualCapture(uint value) : ITextPredicateKind;
internal record struct MatchString(Regex value) : ITextPredicateKind;
internal record struct AnyString(HashSet<string> value) : ITextPredicateKind;


