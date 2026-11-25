using Sekani.EditorCore;
using ShadUI;

namespace SekaniUI.Models;

public class ToastManager : IToastManager
{
	private ShadUI.ToastManager _toastManager;
	private double _delayS = 1.5;
	public ToastManager(ShadUI.ToastManager toastManager)
	{
		_toastManager = toastManager;
	}

	public void ShowErrorToast(string header, string message)
	{
		_toastManager.CreateToast("Error!").WithContent(message).WithDelay(_delayS * 2).OnTopRight().ShowError();
	}

	public void ShowSimpleToast(string message)
	{
		_toastManager.CreateToast(message).WithDelay(_delayS).OnTopRight().Show();

	}
}
