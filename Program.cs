using System;
using System.Windows.Forms;

namespace MidiTool
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            MainForm form;
            try { form = new MainForm(); }
            catch (Exception ex)
            {
                MessageBox.Show("The program could not start.\nProgram nije mogao da se pokrene.\n\n" + ex.Message,
                    "MIDI Trim & Merge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // errors on the screen's thread: tell the user, keep the list safe, let them continue
            Application.ThreadException += (s, e) => form.HandleCrash(e.Exception, false);
            // errors on any other thread cannot be continued from
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                form.HandleCrash(e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject)), true);

            Application.Run(form);
        }
    }
}
