using System;
using System.Threading;
using System.Windows.Forms;

namespace ScrollerCapture;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, name: "ScrollerCapture.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            using var context = new TrayApplicationContext();
            Application.Run(context);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "ScrollerCapture crashed:\n\n" + ex,
                "ScrollerCapture",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            GC.KeepAlive(mutex);
        }
    }
}
