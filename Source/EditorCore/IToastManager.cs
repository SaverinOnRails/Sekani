namespace Sekani.EditorCore;

public interface IToastManager
{
	public void ShowSimpleToast(string message);

	public void ShowErrorToast(string header, string message);
}
