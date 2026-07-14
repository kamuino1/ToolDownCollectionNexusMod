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
        Console.WriteLine("Waiting for the IDM window to pop up...");

        for (int i = 0; i < 20; i++) // Up to 10 seconds (20 x 500ms)
        {
            var hWnd = FindWindow(null, "Download File Info");
            if (hWnd != IntPtr.Zero)
            {
                Console.WriteLine("IDM window detected");

                SetForegroundWindow(hWnd);
                Thread.Sleep(1000);

                SendKeys.SendWait("{ENTER}"); // Press the Start Download button
                Thread.Sleep(6000);
                SendKeys.SendWait("^p");      // Ctrl + P: Pause download

                break;
            }

            Thread.Sleep(500);
        }
    }
}
