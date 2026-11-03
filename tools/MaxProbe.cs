using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading;

class MaxProbe
{
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Form f = new Form();
        f.Text = "LeftScreenMode MaxProbe";
        f.StartPosition = FormStartPosition.WindowsDefaultLocation;
        f.WindowState = FormWindowState.Maximized;
        f.Show();
        Application.DoEvents();
        Thread.Sleep(400);
        RECT r;
        GetWindowRect(f.Handle, out r);
        Console.WriteLine("maximized-window-rect=" + r.L + "," + r.T + "," + r.R + "," + r.B);
        Console.Out.Flush();
        Thread.Sleep(1200);
        f.Close();
        Console.WriteLine("maxprobe-done");
    }
}
