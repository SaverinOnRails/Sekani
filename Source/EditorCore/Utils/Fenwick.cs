namespace Sekani.EditorCore.Utils;

//https://www.geeksforgeeks.org/dsa/binary-indexed-tree-or-fenwick-tree-2/
public class FenwickTree
{
	private int[] _tree = [];
	public FenwickTree(int[] freq, int length)
	{
		_tree = new int[length + 1];
		ConstructBITree(freq, length);
	}

	public void UpdateBIT(int n, int index, int val)
	{
		index += 1;  // Convert 0-indexed to 1-indexed

		while (index <= n)
		{
			_tree[index] += val;
			index += index & (-index);  // Move to next ancestor
		}
	}

	public int GetSum(int index)
	{
		int sum = 0;
		index += 1;  // Convert 0-indexed to 1-indexed

		while (index > 0)
		{
			sum += _tree[index];
			index -= index & (-index);  // Move to parent node
		}
		return sum;
	}

	private void ConstructBITree(int[] arr, int n)
	{
		for (int i = 0; i < n; i++)
		{
			UpdateBIT(n, i, arr[i]);
		}

	}
}
