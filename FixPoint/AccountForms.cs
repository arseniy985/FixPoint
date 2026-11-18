using System;
using System.Data;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FixPoint
{
    internal sealed class LoginForm : FieldsForm
    {
        private readonly TextBox login, password;
        public Employee Employee { get; private set; }

        public LoginForm() : base("Вход в FixPoint", 430, 150)
        {
            StartPosition = FormStartPosition.CenterScreen;
            login = Input("Логин *", 40);
            password = Input("Пароль *", 128);
            password.UseSystemPasswordChar = true;
            AcceptButton = ActionButton("Войти", SignIn);
            Cancel("Выход");
        }

        private void SignIn()
        {
            ClearErrors();
            Employee = Accounts.Login(login.Text.Trim(), password.Text);
            if (Employee == null)
            {
                password.Clear();
                Invalid(password, "Проверьте логин и пароль. Учетная запись должна быть активной.");
                Ui.Warn(this, "Вход не выполнен. Проверьте логин и пароль.");
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    internal sealed class UsersForm : Form
    {
        private readonly Employee employee;
        private readonly PageGrid grid = new PageGrid();

        public UsersForm(Employee employee)
        {
            Database.Require(employee.AccessRole == Role.Manager);
            this.employee = employee;
            Text = "Пользователи";
            ClientSize = new Size(760, 420);
            StartPosition = FormStartPosition.CenterParent;
            grid.Columns("Login", "Логин", "FullName", "ФИО", "RoleName", "Роль", "Active", "Активен");
            grid.LoadRows = offset => Accounts.Users(employee, offset);
            grid.OpenRow += () => Ui.Try(this, EditSelected);
            var actions = Ui.Bar(
                Ui.Button("Добавить", () => Ui.Try(this, () => Edit(null))),
                Ui.Button("Изменить", () => Ui.Try(this, EditSelected)),
                Ui.Button("Закрыть", Close));
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.Controls.Add(grid, 0, 0);
            frame.Controls.Add(actions, 0, 1);
            Controls.Add(frame);
            Shown += (sender, args) => Ui.Try(this, () => grid.Reload());
        }

        private void EditSelected()
        {
            Database.Require(grid.Current != null, "Выберите пользователя в списке.");
            Edit(grid.Current);
        }

        private void Edit(DataRow row)
        {
            using (var form = new UserForm(employee, row))
                if (form.ShowDialog(this) == DialogResult.OK)
                    grid.Reload(row == null);
        }
    }

    internal sealed class UserForm : FieldsForm
    {
        private readonly Employee employee;
        private readonly int id;
        private readonly TextBox login, name, password;
        private readonly ComboBox role;
        private readonly CheckBox active;

        public UserForm(Employee employee, DataRow row) : base("Учётная запись", 640, 270)
        {
            this.employee = employee;
            id = row == null ? 0 : (int)row["Id"];
            login = Input("Логин *", 40);
            name = Input("ФИО *", 120);
            role = Field("Роль *", Ui.Combo());
            foreach (Role value in Enum.GetValues(typeof(Role)))
                role.Items.Add(Employee.RoleName(value));
            role.SelectedIndex = row == null ? 0 : (int)row["RoleId"] - 1;
            active = Field("Состояние", new CheckBox { Text = "Активен", AutoSize = true,
                Checked = row == null || (bool)row["Active"] });
            password = Input(id == 0 ? "Пароль *" : "Новый пароль", 128);
            password.UseSystemPasswordChar = true;
            if (row != null)
            {
                login.Text = Ui.TextOf(row, "Login");
                name.Text = Ui.TextOf(row, "FullName");
            }
            if (employee.Id == id)
                role.Enabled = active.Enabled = false;
            AcceptButton = ActionButton("Сохранить", Save);
            Cancel();
        }

        private void Save()
        {
            ClearErrors();
            if (!Regex.IsMatch(login.Text.Trim(), @"^[a-zA-Z0-9_.-]{1,40}$"))
            {
                Invalid(login, "Используйте латинские буквы, цифры, точку, дефис или подчёркивание.");
                return;
            }
            if (name.Text.Trim().Length == 0)
            {
                Invalid(name, "Введите ФИО пользователя.");
                return;
            }
            if ((id == 0 || password.Text.Length > 0) &&
                (password.Text.Length < 8 || string.IsNullOrWhiteSpace(password.Text)))
            {
                Invalid(password, "Укажите пароль длиной не менее 8 символов.");
                return;
            }
            Accounts.Save(employee, id, login.Text.Trim(), name.Text.Trim(),
                (Role)(role.SelectedIndex + 1), active.Checked, password.Text);
            DialogResult = DialogResult.OK;
        }
    }

    internal sealed class JournalForm : Form
    {
        public JournalForm(Employee employee, int? repair)
        {
            Text = repair.HasValue ? "История заявки" : "Журнал событий";
            ClientSize = new Size(950, 450);
            StartPosition = FormStartPosition.CenterParent;
            var grid = new PageGrid { LoadRows = offset => Accounts.Journal(employee, repair, offset) };
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(grid, 0, 0);
            panel.Controls.Add(Ui.Bar(Ui.Button("Обновить", () => Ui.Try(this, () => grid.Reload())),
                Ui.Button("Закрыть", Close)), 0, 1);
            Controls.Add(panel);
            Shown += (sender, args) => Ui.Try(this, () => grid.Reload());
        }
    }
}
