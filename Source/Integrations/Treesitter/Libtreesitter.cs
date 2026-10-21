//Lifted verbatim from: https://github.com/tree-sitter/csharp-tree-sitter
//This will only work on Linux for now 

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sekani.Integrations.TreeSitter;

internal enum TSInputEncoding
{
	TSInputEncodingUTF8,
	TSInputEncodingUTF16
}

internal enum TSSymbolType
{
	TSSymbolTypeRegular,
	TSSymbolTypeAnonymous,
	TSSymbolTypeAuxiliary,
}

internal enum TSLogType
{
	TSLogTypeParse,
	TSLogTypeLex,
}

internal enum TSQuantifier
{
	TSQuantifierZero = 0, // must match the array initialization value
	TSQuantifierZeroOrOne,
	TSQuantifierZeroOrMore,
	TSQuantifierOne,
	TSQuantifierOneOrMore,
}

internal enum TSQueryPredicateStepType
{
	TSQueryPredicateStepTypeDone,
	TSQueryPredicateStepTypeCapture,
	TSQueryPredicateStepTypeString,
}

internal enum TSQueryError
{
	TSQueryErrorNone = 0,
	TSQueryErrorSyntax,
	TSQueryErrorNodeType,
	TSQueryErrorField,
	TSQueryErrorCapture,
	TSQueryErrorStructure,
	TSQueryErrorLanguage,
}

[StructLayout(LayoutKind.Sequential)]
internal struct TSPoint
{
	internal uint row;
	internal uint column;

	internal TSPoint(uint row, uint column)
	{
		this.row = row;
		this.column = column;
	}
}

[StructLayout(LayoutKind.Sequential)]
internal struct TSRange
{
	internal TSPoint start_point;
	internal TSPoint end_point;
	internal uint start_byte;
	internal uint end_byte;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TSInputEdit
{
	internal uint start_byte;
	internal uint old_end_byte;
	internal uint new_end_byte;
	internal TSPoint start_point;
	internal TSPoint old_end_point;
	internal TSPoint new_end_point;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TSQueryCapture
{
	internal TSNode node;
	internal uint index;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TSQueryMatch
{
	internal uint id;
	internal ushort pattern_index;
	internal ushort capture_count;
	internal nint captures;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TSQueryPredicateStep
{
	internal TSQueryPredicateStepType type;
	internal uint value_id;
}

internal delegate void TSLogger(TSLogType logType, string message);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nint GetLanguageDelegate();
/********************/
/* Section - Parser */
/********************/

internal sealed class TSParser : IDisposable
{
	private nint Ptr { get; set; }

	internal TSParser()
	{
		Ptr = ts_parser_new();
	}

	public void Dispose()
	{
		if (Ptr != nint.Zero)
		{
			ts_parser_delete(Ptr);
			Ptr = nint.Zero;
		}
	}

	internal bool set_language(TSLanguage language) { return ts_parser_set_language(Ptr, language.Ptr); }

	internal TSLanguage language()
	{
		var ptr = ts_parser_language(Ptr);
		return ptr != nint.Zero ? new TSLanguage(ptr) : null;
	}

	internal bool set_included_ranges(TSRange[] ranges)
	{
		return ts_parser_set_included_ranges(Ptr, ranges, (uint)ranges.Length);
	}
	internal TSRange[] included_ranges()
	{
		uint length;
		return ts_parser_included_ranges(Ptr, out length);
	}

	internal TSTree parse_string(TSTree? oldTree, string input)
	{
		var ptr = ts_parser_parse_string_encoding(Ptr, oldTree != null ? oldTree.Ptr : nint.Zero,
													input, (uint)input.Length * 2, TSInputEncoding.TSInputEncodingUTF16);
		return ptr != nint.Zero ? new TSTree(ptr) : null;
	}

	internal void reset() { ts_parser_reset(Ptr); }
	internal void set_timeout_micros(ulong timeout) { ts_parser_set_timeout_micros(Ptr, timeout); }
	internal ulong timeout_micros() { return ts_parser_timeout_micros(Ptr); }
	internal void set_logger(TSLogger logger)
	{
		var code = new _TSLoggerCode(logger);
		var data = new _TSLoggerData { Log = logger != null ? new TSLogCallback(code.LogCallback) : null };
		ts_parser_set_logger(Ptr, data);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct _TSLoggerData
	{
		private nint Payload;
		internal TSLogCallback Log;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void TSLogCallback(nint payload, TSLogType logType, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);

	private class _TSLoggerCode
	{
		private TSLogger logger;

		internal _TSLoggerCode(TSLogger logger)
		{
			this.logger = logger;
		}

		internal void LogCallback(nint payload, TSLogType logType, string message)
		{
			logger(logType, message);
		}
	}

	#region PInvoke
	[DllImport("tree-sitter-cpp.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint tree_sitter_cpp();

	[DllImport("tree-sitter-c-sharp.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint tree_sitter_c_sharp();

	[DllImport("tree-sitter-rust.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint tree_sitter_rust();


	/**
	* Create a new parser.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_parser_new();

	/**
	* Delete the parser, freeing all of the memory that it used.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_parser_delete(nint parser);

	/**
	* Set the language that the parser should use for parsing.
	*
	* Returns a boolean indicating whether or not the language was successfully
	* assigned. True means assignment succeeded. False means there was a version
	* mismatch: the language was generated with an incompatible version of the
	* Tree-sitter CLI. Check the language's version using `ts_language_version`
	* and compare it to this library's `TREE_SITTER_LANGUAGE_VERSION` and
	* `TREE_SITTER_MIN_COMPATIBLE_LANGUAGE_VERSION` constants.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool ts_parser_set_language(nint parser, nint language);

	/**
	* Get the parser's current language.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_parser_language(nint parser);

	/**
	* Set the ranges of text that the parser should include when parsing.
	*
	* By default, the parser will always include entire documents. This function
	* allows you to parse only a *portion* of a document but still return a syntax
	* tree whose ranges match up with the document as a whole. You can also pass
	* multiple disjoint ranges.
	*
	* The second and third parameters specify the location and length of an array
	* of ranges. The parser does *not* take ownership of these ranges; it copies
	* the data, so it doesn't matter how these ranges are allocated.
	*
	* If `length` is zero, then the entire document will be parsed. Otherwise,
	* the given ranges must be ordered from earliest to latest in the document,
	* and they must not overlap. That is, the following must hold for all
	* `i` < `length - 1`: ranges[i].end_byte <= ranges[i + 1].start_byte
	*
	* If this requirement is not satisfied, the operation will fail, the ranges
	* will not be assigned, and this function will return `false`. On success,
	* this function returns `true`
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	//[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool ts_parser_set_included_ranges(nint parser, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] TSRange[] ranges, uint length);

	/**
	* Get the ranges of text that the parser will include when parsing.
	*
	* The returned pointer is owned by the parser. The caller should not free it
	* or write to it. The length of the array will be written to the given
	* `length` pointer.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
	private static extern TSRange[] ts_parser_included_ranges(nint parser, out uint length);

	/**
	* Use the parser to parse some source code stored in one contiguous buffer.
	* The first two parameters are the same as in the `ts_parser_parse` function
	* above. The second two parameters indicate the location of the buffer and its
	* length in bytes.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_parser_parse_string(nint parser, nint oldTree, [MarshalAs(UnmanagedType.LPUTF8Str)] string input, uint length);

	/**
	* Use the parser to parse some source code stored in one contiguous buffer.
	* The first two parameters are the same as in the `ts_parser_parse` function
	* above. The second two parameters indicate the location of the buffer and its
	* length in bytes.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	//private static extern nint ts_parser_parse_string_encoding(nint parser, nint oldTree, [MarshalAs(UnmanagedType.LPUTF8Str)] string input, uint length, TSInputEncoding encoding);
	private static extern nint ts_parser_parse_string_encoding(nint parser, nint oldTree, [MarshalAs(UnmanagedType.LPWStr)] string input, uint length, TSInputEncoding encoding);

	/**
	* Instruct the parser to start the next parse from the beginning.
	*
	* If the parser previously failed because of a timeout or a cancellation, then
	* by default, it will resume where it left off on the next call to
	* `ts_parser_parse` or other parsing functions. If you don't want to resume,
	* and instead intend to use this parser to parse some other document, you must
	* call `ts_parser_reset` first.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_parser_reset(nint parser);

	/**
	* Set the maximum duration in microseconds that parsing should be allowed to
	* take before halting.
	*
	* If parsing takes longer than this, it will halt early, returning NULL.
	* See `ts_parser_parse` for more information.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_parser_set_timeout_micros(nint parser, ulong timeout);

	/**
	* Get the duration in microseconds that parsing is allowed to take.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern ulong ts_parser_timeout_micros(nint parser);

	/**
	* Set the parser's current cancellation flag pointer.
	*
	* If a non-null pointer is assigned, then the parser will periodically read
	* from this pointer during parsing. If it reads a non-zero value, it will
	* halt early, returning NULL. See `ts_parser_parse` for more information.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_parser_set_cancellation_flag(nint parser, ref nint flag);

	/**
	* Get the parser's current cancellation flag pointer.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_parser_cancellation_flag(nint parser);

	/**
	* Set the logger that a parser should use during parsing.
	*
	* The parser does not take ownership over the logger payload. If a logger was
	* previously assigned, the caller is responsible for releasing any memory
	* owned by the previous logger.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_parser_set_logger(nint parser, _TSLoggerData logger);
	#endregion
}

/******************/
/* Section - Tree */
/******************/

internal sealed class TSTree : IDisposable
{
	internal nint Ptr { get; private set; }

	internal TSTree(nint ptr)
	{
		Ptr = ptr;
	}

	public void Dispose()
	{
		if (Ptr != nint.Zero)
		{
			ts_tree_delete(Ptr);
			Ptr = nint.Zero;
		}
	}

	internal TSTree copy()
	{
		var ptr = ts_tree_copy(Ptr);
		return ptr != nint.Zero ? new TSTree(ptr) : null;
	}
	internal TSNode root_node() { return ts_tree_root_node(Ptr); }
	internal TSNode root_node_with_offset(uint offsetBytes, TSPoint offsetPoint) { return ts_tree_root_node_with_offset(Ptr, offsetBytes, offsetPoint); }
	internal TSLanguage language()
	{
		var ptr = ts_tree_language(Ptr);
		return ptr != nint.Zero ? new TSLanguage(ptr) : null;
	}
	internal void edit(TSInputEdit edit) { ts_tree_edit(Ptr, ref edit); }
	#region PInvoke
	/**
	* Create a shallow copy of the syntax tree. This is very fast.
	*
	* You need to copy a syntax tree in order to use it on more than one thread at
	* a time, as syntax trees are not thread safe.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_tree_copy(nint tree);

	/**
	* Delete the syntax tree, freeing all of the memory that it used.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_tree_delete(nint tree);

	/**
	* Get the root node of the syntax tree.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_tree_root_node(nint tree);

	/**
	* Get the root node of the syntax tree, but with its position
	* shifted forward by the given offset.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_tree_root_node_with_offset(nint tree, uint offsetBytes, TSPoint offsetPoint);

	/**
	* Get the language that was used to parse the syntax tree.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_tree_language(nint tree);

	/**
	* Get the array of included ranges that was used to parse the syntax tree.
	*
	* The returned pointer must be freed by the caller.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_tree_included_ranges(nint tree, out uint length);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_tree_included_ranges_free(nint ranges);

	/**
	* Edit the syntax tree to keep it in sync with source code that has been
	* edited.
	*
	* You must describe the edit both in terms of byte offsets and in terms of
	* (row, column) coordinates.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_tree_edit(nint tree, ref TSInputEdit edit);

	/**
	* Compare an old edited syntax tree to a new syntax tree representing the same
	* document, returning an array of ranges whose syntactic structure has changed.
	*
	* For this to work correctly, the old syntax tree must have been edited such
	* that its ranges match up to the new tree. Generally, you'll want to call
	* this function right after calling one of the `ts_parser_parse` functions.
	* You need to pass the old tree that was passed to parse, as well as the new
	* tree that was returned from that function.
	*
	* The returned array is allocated using `malloc` and the caller is responsible
	* for freeing it using `free`. The length of the array will be written to the
	* given `length` pointer.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_tree_get_changed_ranges(nint old_tree, nint new_tree, out uint length);
	#endregion
}

/******************/
/* Section - Node */
/******************/
[StructLayout(LayoutKind.Sequential)]
internal struct TSNode
{
	private uint context0;
	private uint context1;
	private uint context2;
	private uint context3;
	internal nint id;
	private nint tree;

	internal void clear() { id = nint.Zero; tree = nint.Zero; }
	internal bool is_zero() { return (id == nint.Zero && tree == nint.Zero); }
	internal string type() { return Marshal.PtrToStringAnsi(ts_node_type(this)); }
	internal string type(TSLanguage lang) { return lang.symbol_name(symbol()); }
	internal ushort symbol() { return ts_node_symbol(this); }
	internal uint start_offset() { return ts_node_start_byte(this) / sizeof(ushort); }
	internal TSPoint start_point() { var pt = ts_node_start_point(this); return new TSPoint(pt.row, pt.column / sizeof(ushort)); }
	internal uint end_offset() { return ts_node_end_byte(this) / sizeof(ushort); }
	internal TSPoint end_point() { var pt = ts_node_end_point(this); return new TSPoint(pt.row, pt.column / sizeof(ushort)); }
	internal string to_string() { var dat = ts_node_string(this); var str = Marshal.PtrToStringAnsi(dat); ts_node_string_free(dat); return str; }
	internal bool is_null() { return ts_node_is_null(this); }
	internal bool is_named() { return ts_node_is_named(this); }
	internal bool is_missing() { return ts_node_is_missing(this); }
	internal bool is_extra() { return ts_node_is_extra(this); }
	internal bool has_changes() { return ts_node_has_changes(this); }
	internal bool has_error() { return ts_node_has_error(this); }
	internal TSNode parent() { return ts_node_parent(this); }
	internal TSNode child(uint index) { return ts_node_child(this, index); }
	internal nint field_name_for_child(uint index) { return ts_node_field_name_for_child(this, index); }
	internal uint child_count() { return ts_node_child_count(this); }
	internal TSNode named_child(uint index) { return ts_node_named_child(this, index); }
	internal uint named_child_count() { return ts_node_named_child_count(this); }
	internal TSNode child_by_field_name(string field_name) { return ts_node_child_by_field_name(this, field_name, (uint)field_name.Length); }
	internal TSNode child_by_field_id(ushort fieldId) { return ts_node_child_by_field_id(this, fieldId); }
	internal TSNode next_sibling() { return ts_node_next_sibling(this); }
	internal TSNode prev_sibling() { return ts_node_prev_sibling(this); }
	internal TSNode next_named_sibling() { return ts_node_next_named_sibling(this); }
	internal TSNode prev_named_sibling() { return ts_node_prev_named_sibling(this); }
	internal TSNode first_child_for_offset(uint offset) { return ts_node_first_child_for_byte(this, offset * sizeof(ushort)); }
	internal TSNode first_named_child_for_offset(uint offset) { return ts_node_first_named_child_for_byte(this, offset * sizeof(ushort)); }
	internal TSNode descendant_for_offset_range(uint start, uint end) { return ts_node_descendant_for_byte_range(this, start * sizeof(ushort), end * sizeof(ushort)); }
	internal TSNode descendant_for_point_range(TSPoint start, TSPoint end) { return ts_node_descendant_for_point_range(this, start, end); }
	internal TSNode named_descendant_for_offset_range(uint start, uint end) { return ts_node_named_descendant_for_byte_range(this, start * sizeof(ushort), end * sizeof(ushort)); }
	internal TSNode named_descendant_for_point_range(TSPoint start, TSPoint end) { return ts_node_named_descendant_for_point_range(this, start, end); }
	internal bool eq(TSNode other) { return ts_node_eq(this, other); }

	internal string text(string data)
	{
		uint beg = start_offset();
		uint end = end_offset();
		return data.Substring((int)beg, (int)(end - beg));
	}

	#region PInvoke
	/**
	* Get the node's type as a null-terminated string.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_node_type(TSNode node);

	/**
	* Get the node's type as a numerical id.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern ushort ts_node_symbol(TSNode node);

	/**
	* Get the node's start byte.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_node_start_byte(TSNode node);

	/**
	* Get the node's start position in terms of rows and columns.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSPoint ts_node_start_point(TSNode node);

	/**
	* Get the node's end byte.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_node_end_byte(TSNode node);

	/**
	* Get the node's end position in terms of rows and columns.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSPoint ts_node_end_point(TSNode node);

	/**
	* Get an S-expression representing the node as a string.
	*
	* This string is allocated with `malloc` and the caller is responsible for
	* freeing it using `free`.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_node_string(TSNode node);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_node_string_free(nint str);

	/**
	* Check if the node is null. Functions like `ts_node_child` and
	* `ts_node_next_sibling` will return a null node to indicate that no such node
	* was found.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_node_is_null(TSNode node);

	/**
	* Check if the node is *named*. Named nodes correspond to named rules in the
	* grammar, whereas *anonymous* nodes correspond to string literals in the
	* grammar.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_node_is_named(TSNode node);

	/**
	* Check if the node is *missing*. Missing nodes are inserted by the parser in
	* order to recover from certain kinds of syntax errors.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_node_is_missing(TSNode node);

	/**
	* Check if the node is *extra*. Extra nodes represent things like comments,
	* which are not required the grammar, but can appear anywhere.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_node_is_extra(TSNode node);

	/**
	* Check if a syntax node has been edited.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_node_has_changes(TSNode node);

	/**
	* Check if the node is a syntax error or contains any syntax errors.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_node_has_error(TSNode node);

	/**
	* Get the node's immediate parent.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_parent(TSNode node);

	/**
	* Get the node's child at the given index, where zero represents the first
	* child.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_child(TSNode node, uint index);

	/**
	* Get the field name for node's child at the given index, where zero represents
	* the first child. Returns NULL, if no field is found.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_node_field_name_for_child(TSNode node, uint index);

	/**
	* Get the node's number of children.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_node_child_count(TSNode node);

	/**
	* Get the node's number of *named* children.
	*
	* See also `ts_node_is_named`.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_named_child(TSNode node, uint index);

	/**
	* Get the node's number of *named* children.
	*
	* See also `ts_node_is_named`.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_node_named_child_count(TSNode node);

	/**
	* Get the node's child with the given numerical field id.
	*
	* You can convert a field name to an id using the
	* `ts_language_field_id_for_name` function.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_child_by_field_name(TSNode self, [MarshalAs(UnmanagedType.LPUTF8Str)] string field_name, uint field_name_length);

	/**
	* Get the node's child with the given numerical field id.
	*
	* You can convert a field name to an id using the
	* `ts_language_field_id_for_name` function.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_child_by_field_id(TSNode self, ushort fieldId);

	/**
	* Get the node's next / previous sibling.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_next_sibling(TSNode self);
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_prev_sibling(TSNode self);

	/**
	* Get the node's next / previous *named* sibling.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_next_named_sibling(TSNode self);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_prev_named_sibling(TSNode self);

	/**
	* Get the node's first child that extends beyond the given byte offset.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_first_child_for_byte(TSNode self, uint byteOffset);

	/**
	* Get the node's first named child that extends beyond the given byte offset.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_first_named_child_for_byte(TSNode self, uint byteOffset);

	/**
	* Get the smallest node within this node that spans the given range of bytes
	* or (row, column) positions.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_descendant_for_byte_range(TSNode self, uint startByte, uint endByte);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_descendant_for_point_range(TSNode self, TSPoint startPoint, TSPoint endPoint);

	/**
	* Get the smallest named node within this node that spans the given range of
	* bytes or (row, column) positions.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_named_descendant_for_byte_range(TSNode self, uint startByte, uint endByte);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_node_named_descendant_for_point_range(TSNode self, TSPoint startPoint, TSPoint endPoint);

	/**
	* Check if two nodes are identical.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_node_eq(TSNode node1, TSNode node2);
	#endregion
}

/************************/
/* Section - TreeCursor */
/************************/

internal sealed class TSCursor : IDisposable
{
	[StructLayout(LayoutKind.Sequential)]
	internal struct TSTreeCursor
	{
		private nint Tree;
		private nint Id;
		private uint Context0;
		private uint Context1;
	}


	private nint Ptr;
	private TSTreeCursor cursor;
	internal TSLanguage lang { get; private set; }

	internal TSCursor(TSTreeCursor cursor, TSLanguage lang)
	{
		this.cursor = cursor;
		this.lang = lang;
		Ptr = new nint(1);
	}

	internal TSCursor(TSNode node, TSLanguage lang)
	{
		this.cursor = ts_tree_cursor_new(node);
		this.lang = lang;
		Ptr = new nint(1);
	}

	public void Dispose()
	{
		if (Ptr != nint.Zero)
		{
			ts_tree_cursor_delete(ref cursor);
			Ptr = nint.Zero;
		}
	}

	internal void reset(TSNode node) { ts_tree_cursor_reset(ref cursor, node); }
	internal TSNode current_node() { return ts_tree_cursor_current_node(ref cursor); }
	internal string current_field() { return lang.fields[current_field_id()]; }
	internal string current_symbol()
	{
		ushort symbol = ts_tree_cursor_current_node(ref cursor).symbol();
		return (symbol != UInt16.MaxValue) ? lang.symbols[symbol] : "ERROR";
	}
	internal ushort current_field_id() { return ts_tree_cursor_current_field_id(ref cursor); }
	internal bool goto_parent() { return ts_tree_cursor_goto_parent(ref cursor); }
	internal bool goto_next_sibling() { return ts_tree_cursor_goto_next_sibling(ref cursor); }
	internal bool goto_first_child() { return ts_tree_cursor_goto_first_child(ref cursor); }
	internal long goto_first_child_for_offset(uint offset) { return ts_tree_cursor_goto_first_child_for_byte(ref cursor, offset * sizeof(ushort)); }
	internal long goto_first_child_for_point(TSPoint point) { return ts_tree_cursor_goto_first_child_for_point(ref cursor, point); }
	internal TSCursor copy() { return new TSCursor(ts_tree_cursor_copy(ref cursor), lang); }

	#region PInvoke
	/**
	* Create a new tree cursor starting from the given node.
	*
	* A tree cursor allows you to walk a syntax tree more efficiently than is
	* possible using the `TSNode` functions. It is a mutable object that is always
	* on a certain syntax node, and can be moved imperatively to different nodes.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSTreeCursor ts_tree_cursor_new(TSNode node);

	/**
	* Delete a tree cursor, freeing all of the memory that it used.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_tree_cursor_delete(ref TSTreeCursor cursor);

	/**
	* Re-initialize a tree cursor to start at a different node.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_tree_cursor_reset(ref TSTreeCursor cursor, TSNode node);

	/**
	* Get the tree cursor's current node.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSNode ts_tree_cursor_current_node(ref TSTreeCursor cursor);

	/**
	* Get the field name of the tree cursor's current node.
	*
	* This returns `NULL` if the current node doesn't have a field.
	* See also `ts_node_child_by_field_name`.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_tree_cursor_current_field_name(ref TSTreeCursor cursor);

	/**
	* Get the field id of the tree cursor's current node.
	*
	* This returns zero if the current node doesn't have a field.
	* See also `ts_node_child_by_field_id`, `ts_language_field_id_for_name`.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern ushort ts_tree_cursor_current_field_id(ref TSTreeCursor cursor);

	/**
	* Move the cursor to the parent of its current node.
	*
	* This returns `true` if the cursor successfully moved, and returns `false`
	* if there was no parent node (the cursor was already on the root node).
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_tree_cursor_goto_parent(ref TSTreeCursor cursor);

	/**
	* Move the cursor to the next sibling of its current node.
	*
	* This returns `true` if the cursor successfully moved, and returns `false`
	* if there was no next sibling node.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_tree_cursor_goto_next_sibling(ref TSTreeCursor cursor);

	/**
	* Move the cursor to the first child of its current node.
	*
	* This returns `true` if the cursor successfully moved, and returns `false`
	* if there were no children.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_tree_cursor_goto_first_child(ref TSTreeCursor cursor);

	/**
	* Move the cursor to the first child of its current node that extends beyond
	* the given byte offset or point.
	*
	* This returns the index of the child node if one was found, and returns -1
	* if no such child was found.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern long ts_tree_cursor_goto_first_child_for_byte(ref TSTreeCursor cursor, uint byteOffset);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern long ts_tree_cursor_goto_first_child_for_point(ref TSTreeCursor cursor, TSPoint point);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSTreeCursor ts_tree_cursor_copy(ref TSTreeCursor cursor);
	#endregion
}

/*******************/
/* Section - Query */
/*******************/

internal sealed class TSQuery : IDisposable
{
	internal nint Ptr { get; private set; }

	internal TSQuery(nint ptr)
	{
		Ptr = ptr;
	}

	public void Dispose()
	{
		if (Ptr != nint.Zero)
		{
			ts_query_delete(Ptr);
			Ptr = nint.Zero;
		}
	}

	internal uint pattern_count() { return ts_query_pattern_count(Ptr); }
	internal uint capture_count() { return ts_query_capture_count(Ptr); }
	internal uint string_count() { return ts_query_string_count(Ptr); }
	internal uint start_offset_for_pattern(uint patternIndex) { return ts_query_start_byte_for_pattern(Ptr, patternIndex) / sizeof(ushort); }
	internal nint predicates_for_pattern(uint patternIndex, out uint length) { return ts_query_predicates_for_pattern(Ptr, patternIndex, out length); }
	internal bool is_pattern_rooted(uint patternIndex) { return ts_query_is_pattern_rooted(Ptr, patternIndex); }
	internal bool is_pattern_non_local(uint patternIndex) { return ts_query_is_pattern_non_local(Ptr, patternIndex); }
	internal bool is_pattern_guaranteed_at_offset(uint offset) { return ts_query_is_pattern_guaranteed_at_step(Ptr, offset / sizeof(ushort)); }
	internal string capture_name_for_id(uint id, out uint length) { return Marshal.PtrToStringAnsi(ts_query_capture_name_for_id(Ptr, id, out length)); }
	internal TSQuantifier capture_quantifier_for_id(uint patternId, uint captureId) { return ts_query_capture_quantifier_for_id(Ptr, patternId, captureId); }
	internal string string_value_for_id(uint id, out uint length) { return Marshal.PtrToStringAnsi(ts_query_string_value_for_id(Ptr, id, out length)); }
	internal void disable_capture(string captureName) { ts_query_disable_capture(Ptr, captureName, (uint)captureName.Length); }
	internal void disable_pattern(uint patternIndex) { ts_query_disable_pattern(Ptr, patternIndex); }

	#region PInvoke
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_delete(nint query);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_query_pattern_count(nint query);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_query_capture_count(nint query);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_query_string_count(nint query);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_query_start_byte_for_pattern(nint query, uint patternIndex);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_query_predicates_for_pattern(nint query, uint patternIndex, out uint length);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_query_is_pattern_rooted(nint query, uint patternIndex);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_query_is_pattern_non_local(nint query, uint patternIndex);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_query_is_pattern_guaranteed_at_step(nint query, uint byteOffset);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_query_capture_name_for_id(nint query, uint id, out uint length);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSQuantifier ts_query_capture_quantifier_for_id(nint query, uint patternId, uint captureId);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_query_string_value_for_id(nint query, uint id, out uint length);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_disable_capture(nint query, [MarshalAs(UnmanagedType.LPUTF8Str)] string captureName, uint captureNameLength);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_disable_pattern(nint query, uint patternIndex);
	#endregion
}

internal sealed class TSQueryCursor : IDisposable
{
	private nint Ptr { get; set; }

	private TSQueryCursor(nint ptr)
	{
		Ptr = ptr;
	}

	internal TSQueryCursor()
	{
		Ptr = ts_query_cursor_new();
	}

	public void Dispose()
	{
		if (Ptr != nint.Zero)
		{
			ts_query_cursor_delete(Ptr);
			Ptr = nint.Zero;
		}
	}

	internal void exec(TSQuery query, TSNode node) { ts_query_cursor_exec(Ptr, query.Ptr, node); }
	internal bool did_exceed_match_limit() { return ts_query_cursor_did_exceed_match_limit(Ptr); }
	internal uint match_limit() { return ts_query_cursor_match_limit(Ptr); }
	internal void set_match_limit(uint limit) { ts_query_cursor_set_match_limit(Ptr, limit); }
	internal void set_range(uint start, uint end) { ts_query_cursor_set_byte_range(Ptr, start * sizeof(ushort), end * sizeof(ushort)); }
	internal void set_point_range(TSPoint start, TSPoint end) { ts_query_cursor_set_point_range(Ptr, start, end); }
	internal bool next_match(out TSQueryMatch match, out TSQueryCapture[] captures)
	{
		captures = null;
		if (ts_query_cursor_next_match(Ptr, out match))
		{
			if (match.capture_count > 0)
			{
				captures = new TSQueryCapture[match.capture_count];
				for (ushort i = 0; i < match.capture_count; i++)
				{
					var nint = match.captures + Marshal.SizeOf(typeof(TSQueryCapture)) * i;
					captures[i] = Marshal.PtrToStructure<TSQueryCapture>(nint);
				}
			}
			return true;
		}
		return false;
	}
	internal void remove_match(uint id) { ts_query_cursor_remove_match(Ptr, id); }
	internal bool next_capture(out TSQueryMatch match, out uint index) { return ts_query_cursor_next_capture(Ptr, out match, out index); }

	#region PInvoke
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_query_cursor_new();

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_cursor_delete(nint cursor);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_cursor_exec(nint cursor, nint query, TSNode node);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_query_cursor_did_exceed_match_limit(nint cursor);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_query_cursor_match_limit(nint cursor);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_cursor_set_match_limit(nint cursor, uint limit);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_cursor_set_byte_range(nint cursor, uint start_byte, uint end_byte);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_cursor_set_point_range(nint cursor, TSPoint start_point, TSPoint end_point);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_query_cursor_next_match(nint cursor, out TSQueryMatch match);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern void ts_query_cursor_remove_match(nint cursor, uint id);

	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool ts_query_cursor_next_capture(nint cursor, out TSQueryMatch match, out uint capture_index);
	#endregion
}


/**********************/
/* Section - Language */
/**********************/

internal sealed class TSLanguage : IDisposable
{
	internal nint Ptr { get; private set; }

	internal TSLanguage(nint ptr)
	{
		Ptr = ptr;

		symbols = new String[symbol_count() + 1];
		for (ushort i = 0; i < symbols.Length; i++)
		{
			var symbolName = ts_language_symbol_name(Ptr, i);
			symbols[i] = Marshal.PtrToStringAnsi(symbolName);
		}

		fields = new String[field_count() + 1];
		fieldIds = new Dictionary<string, ushort>((int)field_count() + 1);

		for (ushort i = 0; i < fields.Length; i++)
		{
			fields[i] = Marshal.PtrToStringAnsi(ts_language_field_name_for_id(Ptr, i));
			if (fields[i] != null)
			{
				fieldIds.Add(fields[i], i); // TODO: check for dupes, and throw if found
			}
		}

#if false
            for (int i = 0; i < symbols.Length; i++) {
                for (int j = 0; j < i; j++) {
                    Debug.Assert(symbols[i] != symbols[j]);
                }
            }

            for (int i = 0; i < fields.Length; i++) {
                for (int j = 0; j < i; j++) {
                    Debug.Assert(fields[i] != fields[j]);
                }
            }
#endif
	}

	public void Dispose()
	{
		if (Ptr != nint.Zero)
		{
			//ts_query_cursor_delete(Ptr);
			Ptr = nint.Zero;
		}
	}

	internal TSQuery? query_new(string source, out uint error_offset, out TSQueryError error_type)
	{
		var ptr = ts_query_new(Ptr, source, (uint)source.Length, out error_offset, out error_type);
		return ptr != nint.Zero ? new TSQuery(ptr) : null;
	}

	internal String[] symbols;
	internal String[] fields;
	internal Dictionary<String, ushort> fieldIds;

	internal uint symbol_count() { return ts_language_symbol_count(Ptr); }
	internal string symbol_name(ushort symbol) { return (symbol != UInt16.MaxValue) ? symbols[symbol] : "ERROR"; }
	internal ushort symbol_for_name(string str, bool is_named) { return ts_language_symbol_for_name(Ptr, str, (uint)str.Length, is_named); }
	internal uint field_count() { return ts_language_field_count(Ptr); }
	internal string field_name_for_id(ushort fieldId) { return fields[fieldId]; }
	internal ushort field_id_for_name(string str) { return ts_language_field_id_for_name(Ptr, str, (uint)str.Length); }
	internal TSSymbolType symbol_type(ushort symbol) { return ts_language_symbol_type(Ptr, symbol); }

	#region PInvoke
	/**
	* Create a new query from a string containing one or more S-expression
	* patterns. The query is associated with a particular language, and can
	* only be run on syntax nodes parsed with that language.
	*
	* If all of the given patterns are valid, this returns a `TSQuery`.
	* If a pattern is invalid, this returns `NULL`, and provides two pieces
	* of information about the problem:
	* 1. The byte offset of the error is written to the `error_offset` parameter.
	* 2. The type of error is written to the `error_type` parameter.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_query_new(nint language, [MarshalAs(UnmanagedType.LPUTF8Str)] string source, uint source_len, out uint error_offset, out TSQueryError error_type);

	/**
	* Get the number of distinct node types in the language.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_language_symbol_count(nint language);

	/**
	* Get a node type string for the given numerical id.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_language_symbol_name(nint language, ushort symbol);

	/**
	* Get the numerical id for the given node type string.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern ushort ts_language_symbol_for_name(nint language, [MarshalAs(UnmanagedType.LPUTF8Str)] string str, uint length, bool is_named);

	/**
	* Get the number of distinct field names in the language.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_language_field_count(nint language);

	/**
	* Get the field name string for the given numerical id.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern nint ts_language_field_name_for_id(nint language, ushort fieldId);

	/**
	* Get the numerical id for the given field name string.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern ushort ts_language_field_id_for_name(nint language, [MarshalAs(UnmanagedType.LPUTF8Str)] string str, uint length);

	/**
	* Check whether the given node type id belongs to named nodes, anonymous nodes,
	* or a hidden nodes.
	*
	* See also `ts_node_is_named`. Hidden nodes are never returned from the API.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern TSSymbolType ts_language_symbol_type(nint language, ushort symbol);

	/**
	* Get the ABI version number for this language. This version number is used
	* to ensure that languages were generated by a compatible version of
	* Tree-sitter.
	*
	* See also `ts_parser_set_language`.
	*/
	[DllImport("libtree-sitter.so", CallingConvention = CallingConvention.Cdecl)]
	private static extern uint ts_language_version(nint language);
	#endregion
}
