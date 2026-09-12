namespace Sekani.EditorCore.Utils;

/*
 * Treap (Randomized Binary Search Tree + Binary Heap tree). Implemented by Claude Sonnet 5.
 */
internal sealed class TreapNode
{
    public int Value;
    public readonly int Priority;
    public int Size = 1;
    public int Sum;
    public TreapNode? Left;
    public TreapNode? Right;

    public TreapNode(int value, int priority)
    {
        Value = value;
        Priority = priority;
        Sum = value;
    }
}

internal sealed class VisualLineTree
{
    private TreapNode? _root;
    private readonly Random _rng = new();

    public int Count => Size(_root);
    public int Total => Sum(_root);

    private static int Size(TreapNode? n) => n?.Size ?? 0;
    private static int Sum(TreapNode? n) => n?.Sum ?? 0;

    private static void Pull(TreapNode n)
    {
        n.Size = 1 + Size(n.Left) + Size(n.Right);
        n.Sum = n.Value + Sum(n.Left) + Sum(n.Right);
    }

    // First `count` in-order nodes go left, rest go right.
    private static void Split(TreapNode? node, int count, out TreapNode? left, out TreapNode? right)
    {
        if (node is null) { left = null; right = null; return; }

        int leftSize = Size(node.Left);
        if (leftSize < count)
        {
            Split(node.Right, count - leftSize - 1, out var r, out right);
            node.Right = r;
            Pull(node);
            left = node;
        }
        else
        {
            Split(node.Left, count, out left, out var l);
            node.Left = l;
            Pull(node);
            right = node;
        }
    }

    private static TreapNode? Merge(TreapNode? left, TreapNode? right)
    {
        if (left is null) return right;
        if (right is null) return left;

        if (left.Priority > right.Priority)
        {
            left.Right = Merge(left.Right, right);
            Pull(left);
            return left;
        }
        right.Left = Merge(left, right.Left);
        Pull(right);
        return right;
    }

    public void Insert(int index, int value)
    {
        Split(_root, index, out var left, out var right);
        var node = new TreapNode(value, _rng.Next());
        _root = Merge(Merge(left, node), right);
    }

    public void RemoveAt(int index)
    {
        Split(_root, index, out var left, out var rest);
        Split(rest, 1, out _, out var right);
        _root = Merge(left, right);
    }

    public void SetVisualLineCount(int index, int newValue)
        => _root = UpdateRec(_root, index, newValue);

    private static TreapNode? UpdateRec(TreapNode? node, int index, int newValue)
    {
        if (node is null) return null;
        int leftSize = Size(node.Left);
        if (index < leftSize)
            node.Left = UpdateRec(node.Left, index, newValue);
        else if (index > leftSize)
            node.Right = UpdateRec(node.Right, index - leftSize - 1, newValue);
        else
            node.Value = newValue;
        Pull(node);
        return node;
    }

    // Sum of the first `count` elements (0-based, exclusive of index `count`).
    public int PrefixSum(int count) => PrefixSumRec(_root, count);

    private static int PrefixSumRec(TreapNode? node, int count)
    {
        if (node is null || count <= 0) return 0;
        int leftSize = Size(node.Left);
        if (count <= leftSize)
            return PrefixSumRec(node.Left, count);
        return Sum(node.Left) + node.Value + PrefixSumRec(node.Right, count - leftSize - 1);
    }

    // Finds the index whose cumulative visual-line range contains `target`.
    public int FindByPrefixSum(int target, out int prefixSum)
    {
        var node = _root;
        int countBefore = 0;
        int sumBefore = 0;

        while (node is not null)
        {
            int leftSum = Sum(node.Left);
            int leftCount = Size(node.Left);

            if (target < sumBefore + leftSum)
            {
                // answer is inside the left subtree — descend, accumulators unchanged
                node = node.Left;
                continue;
            }

            int blockStart = sumBefore + leftSum;
            if (target < blockStart + node.Value)
            {
                // answer is this node's own block
                prefixSum = blockStart;
                return countBefore + leftCount;
            }

            // answer is inside the right subtree — fold in everything up to and including this node
            sumBefore = blockStart + node.Value;
            countBefore += leftCount + 1;
            node = node.Right;
        }

        prefixSum = sumBefore;
        return countBefore;
    }
    public void Build(IReadOnlyList<int> values)
    {
        _root = null;
        if (values.Count == 0) return;

        var stack = new List<TreapNode>(); // used as a stack via the end
        foreach (var v in values)
        {
            var node = new TreapNode(v, _rng.Next());
            TreapNode? lastPopped = null;

            while (stack.Count > 0 && stack[^1].Priority < node.Priority)
            {
                lastPopped = stack[^1];
                stack.RemoveAt(stack.Count - 1);
            }

            node.Left = lastPopped;
            if (stack.Count > 0)
                stack[^1].Right = node;
            else
                _root = node;

            stack.Add(node);
        }

        PullAll(_root); // one bottom-up pass to fill in Size/Sum
    }

    private static void PullAll(TreapNode? node)
    {
        if (node is null) return;
        PullAll(node.Left);
        PullAll(node.Right);
        Pull(node);
    }
    public void Clear() => _root = null;

    //TODO: we do this quite frequently, we could get this in constant time
    public int this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return GetAt(_root, index);
        }
        set => SetVisualLineCount(index, value);
    }

    private static int GetAt(TreapNode? node, int index)
    {
        // node is guaranteed non-null here since the caller bounds-checked
        int leftSize = Size(node!.Left);
        if (index < leftSize) return GetAt(node.Left, index);
        if (index > leftSize) return GetAt(node.Right, index - leftSize - 1);
        return node.Value;
    }
}
