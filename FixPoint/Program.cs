using System;
using System.Windows.Forms;

namespace FixPoint
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var window = new Form
            {
                Text = "FixPoint — учёт заявок сервисного центра",
                StartPosition = FormStartPosition.CenterScreen,
                Width = 800,
                Height = 500
            })
            {
                Application.Run(window);
            }
        }
    }
}
