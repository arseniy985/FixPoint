using System;
using System.Drawing;
using System.Windows.Forms;

namespace FixPoint
{
    internal sealed class MainForm : Form
    {
        private readonly Employee employee;
        private readonly PageGrid repairs = new PageGrid();
        private readonly PageGrid clients = new PageGrid();
        private readonly ComboBox searchField = Ui.Combo();
        private readonly ComboBox status = Ui.Combo();
        private readonly TextBox search = new TextBox { Width = 230, MaxLength = 160 };
        private readonly DateTimePicker from = DateFilter();
        private readonly DateTimePicker until = DateFilter();
        private int currentField, currentStatus;
        private string query = "", clientQuery = "";
        private DateTime? currentFrom, currentUntil;
        public bool SignOut { get; private set; }

        public MainForm(Employee employee)
        {
            this.employee = employee;
            Text = "FixPoint — учёт заявок сервисного центра";
            ClientSize = new Size(1130, 615);
            MinimumSize = new Size(950, 420);
            StartPosition = FormStartPosition.CenterScreen;

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildRepairs());
            tabs.TabPages.Add(BuildClients());
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var bar = Ui.Bar(Ui.Caption(employee.Name + " — " + Employee.RoleName(employee.AccessRole)));
            bar.Controls.Add(Ui.Button("Сменить пользователя", () => { SignOut = true; Close(); }));
            if (employee.AccessRole == Role.Manager)
            {
                bar.Controls.Add(Ui.Button("Пользователи", () => Ui.Try(this, () =>
                {
                    using (var form = new UsersForm(employee))
                        form.ShowDialog(this);
                })));
                bar.Controls.Add(Ui.Button("Журнал событий", () => Ui.Try(this, () => OpenJournal(null))));
            }
            frame.Controls.Add(bar, 0, 0);
            frame.Controls.Add(tabs, 0, 1);
            Controls.Add(frame);

            Shown += (sender, args) => Ui.Try(this, () =>
            {
                Ui.Choices(status, Repairs.Statuses(), "Все статусы");
                repairs.Reload();
                clients.Reload();
            });
        }

        private static DateTimePicker DateFilter()
        {
            return new DateTimePicker { Format = DateTimePickerFormat.Short, ShowCheckBox = true, Checked = false,
                Width = 120, MinDate = new DateTime(1900, 1, 1) };
        }

        private TabPage BuildRepairs()
        {
            var page = new TabPage("Заявки");
            searchField.Width = 145;
            searchField.Items.AddRange(new object[] { "Все поля", "Номер заявки", "Телефон клиента",
                "ФИО клиента", "Модель", "Серийный номер / IMEI" });
            searchField.SelectedIndex = 0;
            status.Width = 120;
            var find = Ui.Button("Найти", () => Ui.Try(this, FindRepairs));
            search.KeyDown += (sender, args) => { if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; find.PerformClick(); } };
            var filters = Ui.Bar(searchField, search, Ui.Caption("Статус"), status,
                Ui.Caption("С"), from, Ui.Caption("По"), until, find,
                Ui.Button("Сбросить", () => Ui.Try(this, ResetFilters)));
            var actions = Ui.Bar();
            if (employee.AccessRole == Role.Reception)
                actions.Controls.Add(Ui.Button("Новая заявка", () => Ui.Try(this, () => OpenRepair(0, 0))));
            actions.Controls.Add(Ui.Button("Открыть заявку", () => Ui.Try(this, OpenSelectedRepair)));
            if (employee.AccessRole == Role.Manager)
                actions.Controls.Add(Ui.Button("История заявки", () => Ui.Try(this, () =>
                    OpenJournal(SelectedId(repairs, "Выберите заявку в списке.")))));

            repairs.LoadRows = offset => Repairs.List(currentField, query, currentStatus, currentFrom, currentUntil, offset);
            repairs.OpenRow += () => Ui.Try(this, OpenSelectedRepair);
            page.Controls.Add(ListFrame(filters, repairs, actions));
            return page;
        }

        private TabPage BuildClients()
        {
            var page = new TabPage("Клиенты");
            var field = new TextBox { Width = 280, MaxLength = 160 };
            var find = Ui.Button("Найти", () => Ui.Try(this, () =>
            {
                clientQuery = field.Text.Trim();
                clients.Reload();
            }));
            field.KeyDown += (sender, args) => { if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; find.PerformClick(); } };
            var filters = Ui.Bar(Ui.Caption("ФИО или телефон"), field, find,
                Ui.Button("Сбросить", () => { field.Clear(); find.PerformClick(); }));
            var actions = Ui.Bar();
            if (employee.AccessRole == Role.Reception)
                actions.Controls.Add(Ui.Button("Новый клиент", () => Ui.Try(this, () => OpenClient(0))));
            actions.Controls.Add(Ui.Button("Открыть клиента", () => Ui.Try(this, OpenSelectedClient)));
            clients.LoadRows = offset => Clients.List(clientQuery, offset);
            clients.OpenRow += () => Ui.Try(this, OpenSelectedClient);
            page.Controls.Add(ListFrame(filters, clients, actions));
            return page;
        }

        public static Control ListFrame(Control filters, Control grid, Control actions)
        {
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.Controls.Add(filters, 0, 0);
            frame.Controls.Add(grid, 0, 1);
            frame.Controls.Add(actions, 0, 2);
            return frame;
        }

        private void FindRepairs()
        {
            if (from.Checked && until.Checked && from.Value.Date > until.Value.Date)
            {
                Ui.Warn(this, "Дата начала периода должна быть не позже даты окончания.");
                return;
            }
            currentField = searchField.SelectedIndex;
            query = search.Text.Trim();
            currentStatus = Ui.Selected(status);
            currentFrom = from.Checked ? (DateTime?)from.Value.Date : null;
            currentUntil = until.Checked ? (DateTime?)until.Value.Date.AddDays(1) : null;
            repairs.Reload();
        }

        private void ResetFilters()
        {
            searchField.SelectedIndex = 0;
            search.Clear();
            if (status.Items.Count > 0) status.SelectedIndex = 0;
            from.Checked = until.Checked = false;
            if (status.DataSource == null) Ui.Choices(status, Repairs.Statuses(), "Все статусы");
            FindRepairs();
        }

        private static int SelectedId(PageGrid grid, string message)
        {
            Database.Require(grid.Current != null, message);
            return (int)grid.Current["Id"];
        }

        private void OpenSelectedRepair()
        {
            OpenRepair(SelectedId(repairs, "Выберите заявку в списке."), 0);
        }

        private void OpenRepair(int id, int clientId)
        {
            using (var form = new RepairForm(employee, id, clientId))
                form.ShowDialog(this);
            repairs.Reload(id == 0);
        }

        private void OpenSelectedClient()
        {
            OpenClient(SelectedId(clients, "Выберите клиента в списке."));
        }

        private void OpenClient(int id)
        {
            using (var form = new ClientForm(employee, id))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;
                clients.Reload(id == 0);
                repairs.Reload(false);
                if (form.CreateRepair)
                    OpenRepair(0, form.ClientId);
            }
        }

        private void OpenJournal(int? id)
        {
            using (var form = new JournalForm(employee, id))
                form.ShowDialog(this);
        }
    }
}
