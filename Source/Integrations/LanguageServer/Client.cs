namespace Sekani.Integrations.LanguageServer;

public class LSPClient
{
	private string _rootPath;
	private string _serverProgram;

	public bool Active { get; private set; }

	public LSPClient(string rootPath, string serverProgram)
	{
		_rootPath = rootPath;
		_serverProgram = serverProgram;
	}
}
