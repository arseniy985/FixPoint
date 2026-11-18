using System;
using System.Windows.Forms;

namespace FixPoint
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--backup")
            {
                try
                {
                    Database.BackupToday(true);
                }
                catch
                {
                    Environment.ExitCode = 1;
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool ready = false;
            Ui.Try(null, () => { Database.Initialize(); ready = true; });
            if (!ready)
                return;
            Ui.Try(null, () => Database.BackupToday());
            try
            {
                RunForms();
            }
            finally
            {
                Ui.Try(null, () => Database.BackupToday(true));
            }
        }

        private static void RunForms()
        {
            while (true)
            {
                using (var login = new LoginForm())
                {
                    if (login.ShowDialog() != DialogResult.OK)
                        return;

                    using (var main = new MainForm(login.Employee))
                    {
                        main.ShowDialog();
                        if (!main.SignOut)
                            return;
                    }
                }
            }
        }
    }
}
