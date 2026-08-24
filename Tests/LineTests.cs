namespace SekaniTests;

using SekaniUI.Controls;

[TestClass]
public class LineTests
{
	private static Line CreateLine(int tabSize = 4)
		=> new(tabSize);

	[TestMethod]
	public void EmptyLine_HasZeroVisualLength()
	{
		var line = CreateLine();
		Assert.AreEqual(0, line.GraphemeCount);
		Assert.AreEqual(0, line.VisualLength);
		Assert.AreEqual("", line.LineString);
	}

	[TestMethod]
	public void AddingTabs_ContributedToVisualLength()
	{
		var line = CreateLine();
		line.Insert(0, new("a"));
		for (int i = 1; i <= 3; i++)
		{
			line.Insert(i, new("\t"));
		}
		Assert.AreEqual(12, line.VisualLength);
	}

	[TestMethod]
	public void VisualColumnAt_ReturnsVisualColumnOfGrapheme()
	{
		var line = CreateLine(4);

		line.Insert(0, new Grapheme("a"));
		line.Insert(1, new Grapheme("b"));
		line.Insert(2, new Grapheme("\t"));
		line.Insert(3, new Grapheme("c"));

		Assert.AreEqual(0, line.VisualColumnAt(0));
		Assert.AreEqual(1, line.VisualColumnAt(1));
		Assert.AreEqual(2, line.VisualColumnAt(2));
		Assert.AreEqual(4, line.VisualColumnAt(3));
	}

	[TestMethod]
	public void VisualToLogical_MapsNormalCharactersToLogicalColumns()
	{
		var line = CreateLine(4);

		line.Add(new Grapheme("a"));
		line.Add(new Grapheme("b"));
		line.Add(new Grapheme("c"));

		var result = line.VisualToLogical(new Coordinate(0, 0));
		Assert.AreEqual(0, result.Col);

		result = line.VisualToLogical(new Coordinate(1, 0));
		Assert.AreEqual(1, result.Col);

		result = line.VisualToLogical(new Coordinate(2, 0));
		Assert.AreEqual(2, result.Col);
	}

	[TestMethod]
	public void VisualToLogical_MapsAllTabColumnsToSameLogicalColumn()
	{
		var line = CreateLine(4);

		line.Add(new Grapheme("a"));
		line.Add(new Grapheme("b"));
		line.Add(new Grapheme("\t"));
		line.Add(new Grapheme("c"));

		Assert.AreEqual(0, line.VisualToLogical(new Coordinate(0, 0)).Col);
		Assert.AreEqual(1, line.VisualToLogical(new Coordinate(1, 0)).Col);
		Assert.AreEqual(2, line.VisualToLogical(new Coordinate(2, 0)).Col);
		Assert.AreEqual(2, line.VisualToLogical(new Coordinate(3, 0)).Col);
		Assert.AreEqual(3, line.VisualToLogical(new Coordinate(4, 0)).Col);
	}
}
