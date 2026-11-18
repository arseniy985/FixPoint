using System;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FixPoint
{
    internal sealed class ClientForm : FieldsForm
    {
        private readonly Employee employee;
        private readonly TextBox idBox, surname, name, patronymic, phone, email, notes, date;
        private int id;
        public int ClientId { get { return id; } }
        public bool CreateRepair { get; private set; }

        public ClientForm(Employee employee, int id, bool allowCreate = true) : base("Клиент", 600, 420)
        {
            this.employee = employee;
            bool readOnly = employee.AccessRole != Role.Reception;
            idBox = Input("ID клиента", 20, readOnly: true);
            surname = Input("Фамилия *", 50, readOnly: readOnly);
            name = Input("Имя *", 50, readOnly: readOnly);
            patronymic = Input("Отчество", 50, readOnly: readOnly);
            phone = Input("Телефон *", 20, readOnly: readOnly);
            email = Input("E-mail", 100, readOnly: readOnly);
            notes = Input("Комментарий", 500, true, readOnly);
            date = Input("Дата создания", 30, readOnly: true);
            LoadClient(id);

            if (!readOnly)
            {
                AcceptButton = ActionButton("Сохранить", () => Save(false));
                if (allowCreate)
                    ActionButton("Сохранить и создать заявку", () => Save(true));
            }
            Cancel(readOnly ? "Закрыть" : "Отмена");
        }

        private void LoadClient(int client)
        {
            if (client == 0)
            {
                id = 0;
                idBox.Text = "При сохранении";
                return;
            }
            var row = Clients.Get(client);
            id = client;
            idBox.Text = id.ToString();
            surname.Text = Ui.TextOf(row, "LastName");
            name.Text = Ui.TextOf(row, "FirstName");
            patronymic.Text = Ui.TextOf(row, "MiddleName");
            phone.Text = Ui.TextOf(row, "Phone");
            email.Text = Ui.TextOf(row, "Email");
            notes.Text = Ui.TextOf(row, "Notes");
            date.Text = ((DateTime)row["CreatedAt"]).ToString("dd.MM.yyyy HH:mm");
            ClearErrors();
        }

        private bool ValidName(TextBox field, bool required)
        {
            string value = field.Text.Trim();
            if (!required && value.Length == 0)
                return true;
            return Regex.IsMatch(value, @"^(?=.*\p{L})[\p{L} -]+$") ||
                   Invalid(field, "Введите имя буквами. Допустимы пробел и дефис.");
        }

        private void Save(bool createRepair)
        {
            ClearErrors();
            if (!ValidName(surname, true) || !ValidName(name, true) || !ValidName(patronymic, false))
                return;

            string number = phone.Text.Trim();
            string digits = Clients.Digits(number);
            if (!Regex.IsMatch(number, @"^\+?[0-9 ()-]{9,20}$") || number.Length < 10 ||
                digits.Length < 10 || digits.Length > 15)
            {
                Invalid(phone, "Введите телефон из 10–15 цифр. Допустимы +, пробел, скобки и дефис.");
                return;
            }

            string address = email.Text.Trim();
            if (address.Length > 0)
            {
                try
                {
                    var parsed = new MailAddress(address);
                    if (parsed.Address != address || !parsed.Host.Contains("."))
                    {
                        Invalid(email, "Введите адрес в формате name@example.ru.");
                        return;
                    }
                }
                catch (FormatException)
                {
                    Invalid(email, "Введите адрес в формате name@example.ru.");
                    return;
                }
            }

            if (HandleDuplicate(digits))
                return;
            try
            {
                id = Clients.Save(employee, id, surname.Text.Trim(), name.Text.Trim(), patronymic.Text.Trim(),
                    number, address, notes.Text.Trim());
            }
            catch (SqliteException error)
            {
                if (error.UniqueConstraint && HandleDuplicate(digits))
                    return;
                throw;
            }
            CreateRepair = createRepair;
            DialogResult = DialogResult.OK;
        }

        private bool HandleDuplicate(string digits)
        {
            int duplicate = Clients.Duplicate(digits, id);
            if (duplicate == 0)
                return false;
            Invalid(phone, "Этот телефон уже указан у другого клиента.");
            if (Ui.Confirm(this, "Клиент с таким телефоном уже есть. Открыть его карточку?"))
                LoadClient(duplicate);
            return true;
        }
    }

    internal sealed class ClientPicker : Form
    {
        private readonly PageGrid grid = new PageGrid();
        public int ClientId { get; private set; }

        public ClientPicker()
        {
            Text = "Выбор клиента";
            ClientSize = new System.Drawing.Size(720, 420);
            StartPosition = FormStartPosition.CenterParent;
            var search = new TextBox { Width = 280, MaxLength = 120 };
            string query = "";
            var find = Ui.Button("Найти", () => Ui.Try(this, () =>
            {
                query = search.Text.Trim();
                grid.Reload();
            }));
            var choose = Ui.Button("Выбрать", Choose);
            var cancel = Ui.Button("Отмена", Close);
            cancel.DialogResult = DialogResult.Cancel;
            AcceptButton = find;
            CancelButton = cancel;
            grid.OpenRow += Choose;
            grid.LoadRows = offset => Clients.List(query, offset);
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            frame.Controls.Add(Ui.Bar(Ui.Caption("ФИО или телефон"), search, find), 0, 0);
            frame.Controls.Add(grid, 0, 1);
            frame.Controls.Add(Ui.Bar(choose, cancel), 0, 2);
            Controls.Add(frame);
            Shown += (sender, args) => Ui.Try(this, () => grid.Reload());
        }

        private void Choose()
        {
            if (grid.Current == null)
            {
                Ui.Warn(this, "Выберите клиента в списке.");
                return;
            }
            ClientId = (int)grid.Current["Id"];
            DialogResult = DialogResult.OK;
        }
    }
}
