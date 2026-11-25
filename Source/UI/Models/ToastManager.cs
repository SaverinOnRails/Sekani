using System;
using Sekani.EditorCore;
using ShadUI;

namespace SekaniUI.Models;

public class ToastManager : IToastManager
{
	private ShadUI.ToastManager _toastManager;
	private double _delayS = 1.5; //1.5 secs delay
	public ToastManager(ShadUI.ToastManager toastManager)
	{
		_toastManager = toastManager;
	}
	public void ShowSimpleToast(string message)
	{
		_toastManager.CreateToast(message).WithDelay(_delayS).Show();

	}
}
