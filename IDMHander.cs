using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

class IDMHandler
{
    [DllImport("user32.dll")]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void HandleIDMWindow()
    {
        Console.WriteLine("Chờ cửa sổ IDM bật lên...");

        for (int i = 0; i < 20; i++) // Tối đa 10 giây (20 x 500ms)
        {
            var hWnd = FindWindow(null, "Download File Info");
            if (hWnd != IntPtr.Zero)
            {
                Console.WriteLine("Đã phát hiện cửa sổ IDM");

                SetForegroundWindow(hWnd);
                Thread.Sleep(1000);

                SendKeys.SendWait("{ENTER}"); // Nhấn nút Start Download
                Thread.Sleep(6000);
                SendKeys.SendWait("^p");      // Ctrl + P: Pause download

                break;
            }

            Thread.Sleep(500);
        }
    }
}
