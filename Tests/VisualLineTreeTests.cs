using Sekani.EditorCore.Utils;

namespace Tests;

[TestClass]
public sealed class VisualLineTreeTests
{
	private static void AssertMatchesModel(VisualLineTree tree, IReadOnlyList<int> values)
	{
		Assert.AreEqual(values.Count, tree.Count, "Node count differs from the model.");
		Assert.AreEqual(values.Sum(), tree.Total, "Aggregate sum differs from the model.");

		for (var i = 0; i < values.Count; i++)
			Assert.AreEqual(values[i], tree[i], $"Value at index {i} differs from the model.");

		var expectedPrefix = 0;
		Assert.AreEqual(0, tree.PrefixSum(0));
		for (var count = 1; count <= values.Count; count++)
		{
			expectedPrefix += values[count - 1];
			Assert.AreEqual(expectedPrefix, tree.PrefixSum(count), $"Prefix sum for {count} values differs.");
		}

		for (var target = 0; target < values.Sum(); target++)
		{
			var expectedIndex = 0;
			var prefixBeforeIndex = 0;
			while (expectedIndex < values.Count && target >= prefixBeforeIndex + values[expectedIndex])
			{
				prefixBeforeIndex += values[expectedIndex];
				expectedIndex++;
			}

			var actualIndex = tree.FindByPrefixSum(target, out var actualPrefix);
			Assert.AreEqual(expectedIndex, actualIndex, $"Target {target} resolved to the wrong item.");
			Assert.AreEqual(prefixBeforeIndex, actualPrefix, $"Target {target} returned the wrong prefix.");
		}

		var endIndex = tree.FindByPrefixSum(values.Sum(), out var endPrefix);
		Assert.AreEqual(values.Count, endIndex, "A target at the total should resolve just past the last item.");
		Assert.AreEqual(values.Sum(), endPrefix);
	}

	[TestMethod]
	public void EmptyTree_HasZeroCountAndSumAndSupportsEmptyBuildAndClear()
	{
		var tree = new VisualLineTree();

		AssertMatchesModel(tree, Array.Empty<int>());
		tree.Build(Array.Empty<int>());
		AssertMatchesModel(tree, Array.Empty<int>());
		tree.Clear();
		AssertMatchesModel(tree, Array.Empty<int>());
	}

	[TestMethod]
	public void Build_CreatesValuesAndAggregatesAndCanReplaceExistingTree()
	{
		var tree = new VisualLineTree();
		tree.Build(new[] { 2, 1, 4, 3 });
		AssertMatchesModel(tree, new[] { 2, 1, 4, 3 });

		tree.Build(new[] { 5, 2 });
		AssertMatchesModel(tree, new[] { 5, 2 });
	}

	[TestMethod]
	public void FindByPrefixSum_ResolvesBlockStartsAndEnds()
	{
		var tree = new VisualLineTree();
		var values = new[] { 2, 1, 3 };
		tree.Build(values);

		Assert.AreEqual(0, tree.FindByPrefixSum(0, out var prefix0));
		Assert.AreEqual(0, prefix0);
		Assert.AreEqual(1, tree.FindByPrefixSum(2, out var prefix1));
		Assert.AreEqual(2, prefix1);
		Assert.AreEqual(2, tree.FindByPrefixSum(3, out var prefix2));
		Assert.AreEqual(3, prefix2);
		Assert.AreEqual(3, tree.FindByPrefixSum(6, out var prefixEnd));
		Assert.AreEqual(6, prefixEnd);
	}

	[TestMethod]
	public void InsertAndInsertRange_PreserveSequenceAndDefaultValues()
	{
		var tree = new VisualLineTree();
		var values = new List<int> { 2, 5 };
		tree.Build(values);

		tree.Insert(0, 3);
		values.Insert(0, 3);
		AssertMatchesModel(tree, values);

		tree.Insert(2, 4);
		values.Insert(2, 4);
		AssertMatchesModel(tree, values);

		tree.InsertRange(1, count: 3, defaultValue: 2);
		values.InsertRange(1, Enumerable.Repeat(2, 3).ToArray());
		AssertMatchesModel(tree, values);

		tree.InsertRange(values.Count, count: 0, defaultValue: 9);
		AssertMatchesModel(tree, values);
	}

	[TestMethod]
	public void RemoveAtAndRemoveRange_RemoveExpectedValuesAndClampRangeLength()
	{
		var tree = new VisualLineTree();
		var values = new List<int> { 1, 2, 3, 4, 5, 6 };
		tree.Build(values);

		tree.RemoveAt(2);
		values.RemoveAt(2);
		AssertMatchesModel(tree, values);

		tree.RemoveRange(1, 2);
		values.RemoveRange(1, 2);
		AssertMatchesModel(tree, values);

		tree.RemoveRange(1, 100);
		values.RemoveRange(1, values.Count - 1);
		AssertMatchesModel(tree, values);

		tree.RemoveRange(0, 0);
		AssertMatchesModel(tree, values);
	}

	[TestMethod]
	public void IndexerAndSetVisualLineCount_UpdateValuesAndAggregates()
	{
		var tree = new VisualLineTree();
		var values = new List<int> { 1, 2, 1, 4 };
		tree.Build(values);

		tree.SetVisualLineCount(1, 5);
		values[1] = 5;
		AssertMatchesModel(tree, values);

		tree[3] = 2;
		values[3] = 2;
		AssertMatchesModel(tree, values);
	}

	[TestMethod]
	public void Indexer_ThrowsForIndexOutsideTree()
	{
		var tree = new VisualLineTree();
		tree.Build(new[] { 1, 2 });

		Assert.Throws<ArgumentOutOfRangeException>(() => _ = tree[-1]);
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = tree[2]);
	}

	[TestMethod]
	public void DeterministicMixedOperations_MatchListModelAfterEveryOperation()
	{
		var random = new Random(0x71AEE);
		var tree = new VisualLineTree();
		var values = new List<int>();

		for (var operation = 0; operation < 500; operation++)
		{
			switch (random.Next(7))
			{
				case 0:
				{
					var index = random.Next(values.Count + 1);
					var value = random.Next(1, 6);
					tree.Insert(index, value);
					values.Insert(index, value);
					break;
				}
				case 1:
				{
					var index = random.Next(values.Count + 1);
					var count = random.Next(0, 4);
					var value = random.Next(1, 6);
					tree.InsertRange(index, count, value);
					values.InsertRange(index, Enumerable.Repeat(value, count).ToArray());
					break;
				}
				case 2 when values.Count > 0:
				{
					var index = random.Next(values.Count);
					tree.RemoveAt(index);
					values.RemoveAt(index);
					break;
				}
				case 3 when values.Count > 0:
				{
					var index = random.Next(values.Count);
					var count = random.Next(0, values.Count - index + 4);
					tree.RemoveRange(index, count);
					values.RemoveRange(index, Math.Min(count, values.Count - index));
					break;
				}
				case 4 when values.Count > 0:
				{
					var index = random.Next(values.Count);
					var value = random.Next(1, 6);
					tree.SetVisualLineCount(index, value);
					values[index] = value;
					break;
				}
				case 5:
				{
					values.Clear();
					tree.Clear();
					break;
				}
				case 6:
				{
					values = Enumerable.Range(0, random.Next(0, 10))
						.Select(_ => random.Next(1, 6))
						.ToList();
					tree.Build(values);
					break;
				}
			}

			AssertMatchesModel(tree, values);
		}
	}
}
