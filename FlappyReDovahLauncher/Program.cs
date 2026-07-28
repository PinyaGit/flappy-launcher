using System;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.ThreadException += Application_ThreadException;

            // Before UI: if a newer launcher is on CDN, download and restart into it.
            try
            {
                if (LauncherSelfUpdate.TryUpdateAndExit(args))
                    return;
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Self-update entry", ex);
                // continue to normal launch
            }

            System.Windows.Forms.Application.Run(new Application());
        }

        private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            MessageBox.Show(FlappyException.FormatForUser(e.Exception), "Launcher");
        }
    }
}
