namespace Sekani.EditorCore;

public class ProjectManager
{
	private List<Project> _projects = [];

	public void OpenProject(string directory)
	{
		var project = new Project(directory);
		_projects.Add(project);
	}

	public List<string> AllFiles()
	{
		List<string> files = [];
		foreach (var project in _projects)
		{
			files.AddRange(project.Files());
		}
		return files;
	}
}

public class Project
{
	private string _directory;
	public Project(string directory)
	{
		if (!Directory.Exists(directory))
		{
			throw new InvalidOperationException($"{directory} is not a valid Directory!");
		}
		_directory = directory;
	}

	public List<string> Files()
	{
		HashSet<string> ignoredDirectories = new(StringComparer.OrdinalIgnoreCase)
		{
			".git",
		};
		List<string> results = [];
		var dirOptions = new EnumerationOptions
		{
			RecurseSubdirectories = false,
			IgnoreInaccessible = true
		};

		Stack<string> dirs = new();
		dirs.Push(_directory);

		while (dirs.Count > 0)
		{
			var currentDir = dirs.Pop();

			foreach (var subDir in Directory.EnumerateDirectories(currentDir, "*", dirOptions))
			{
				var dirName = Path.GetFileName(subDir);
				if (ignoredDirectories.Contains(dirName))
					continue;

				dirs.Push(subDir);
			}

			foreach (var file in Directory.EnumerateFiles(currentDir, "*", dirOptions))
			{
				results.Add(file);
			}
		}

		return results;
	}
}
