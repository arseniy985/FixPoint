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
            Database.Initialize();
            using (var window = new Form
            {
                Text = "FixPoint — учёт заявок сервисного центра",
                StartPosition = FormStartPosition.CenterScreen,
                Width = 800,
                Height = 500
            })
            {
                window.Controls.Add(new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    DataSource = Database.Read("SELECT Id AS [Код], Name AS [Статус] FROM Statuses ORDER BY Id")
                });
                Application.Run(window);
            }
        }
    }
}
