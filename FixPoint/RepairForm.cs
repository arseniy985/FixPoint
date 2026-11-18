using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FixPoint
{
    internal sealed class RepairForm : FieldsForm
    {
        private readonly Employee employee;
        private readonly int id;
        private int clientId;
        private RepairStatus status;
        private readonly TextBox client, manufacturer, model, serial, problem, accessories, appearance, estimate;
        private readonly TextBox diagnosis, work;
        private readonly ComboBox type, master, statusBox;
        private readonly CheckBox imei;
        private readonly Button changeStatus, goToIssue;
        private bool edited;

        public RepairForm(Employee employee, int id, int clientId) : base("Заявка на ремонт", 820, 635)
        {
            this.employee = employee;
            this.id = id;
            var row = id == 0 ? null : Repairs.Get(id);
            status = row == null ? RepairStatus.Accepted : (RepairStatus)(int)row["StatusId"];
            bool closed = status == RepairStatus.Issued;
            bool intake = closed ? employee.AccessRole == Role.Manager : employee.AccessRole == Role.Reception;
            bool technical = id > 0 && (closed ? employee.AccessRole == Role.Manager : employee.AccessRole == Role.Master);
            bool canChangeStatus = id > 0 && !closed && employee.AccessRole != Role.Manager;

            var number = Input("Номер заявки", 20, readOnly: true);
            number.Text = row == null ? "При сохранении" : Ui.TextOf(row, "Number");

            var clientBar = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
            clientBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            clientBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            client = new TextBox { ReadOnly = true, Dock = DockStyle.Top, Margin = new Padding(0, 3, 3, 0) };
            clientBar.Controls.Add(client, 0, 0);
            if (intake)
                clientBar.Controls.Add(Ui.Button("Выбрать клиента", () => Ui.Try(this, ChooseClient)), 1, 0);
            Field("Клиент *", clientBar);

            type = Field("Тип устройства *", Ui.Combo());
            Ui.Choices(type, Repairs.DeviceTypes(), "Выберите тип");
            type.Enabled = intake;
            manufacturer = Input("Производитель *", 50, readOnly: !intake);
            model = Input("Модель *", 100, readOnly: !intake);
            serial = Input("Серийный номер / IMEI", 50, readOnly: !intake);
            imei = Field("", new CheckBox { Text = "Введён IMEI", AutoSize = true, Enabled = intake });
            problem = Input("Заявленная неисправность *", 1000, true, !intake);
            accessories = Input("Комплектность *", 500, true, !intake);
            appearance = Input("Внешнее состояние", 500, true, !intake);
            estimate = Input("Предв. стоимость, руб.", 16, readOnly: !intake);
            master = Field("Ответственный мастер", Ui.Combo());
            int currentMaster = row == null || row.IsNull("MasterId") ? 0 : (int)row["MasterId"];
            Ui.Choices(master, Accounts.Masters(currentMaster), "Не назначен");
            master.Enabled = intake;

            statusBox = Field("Статус *", Ui.Combo());
            statusBox.Enabled = canChangeStatus && Repairs.Next(status).Length > 0;
            diagnosis = Input("Результат диагностики", 1500, true, !technical);
            work = Input("Выполненные работы", 1500, true, !technical);
            var received = Input("Дата приёма", 30, readOnly: true);
            received.Text = row == null ? "При сохранении" : ((DateTime)row["ReceivedAt"]).ToString("dd.MM.yyyy HH:mm");

            if (row != null)
            {
                clientId = (int)row["ClientId"];
                type.SelectedValue = (int)row["DeviceTypeId"];
                manufacturer.Text = Ui.TextOf(row, "Manufacturer");
                model.Text = Ui.TextOf(row, "Model");
                serial.Text = Ui.TextOf(row, "SerialNumber");
                imei.Checked = (bool)row["IsImei"];
                problem.Text = Ui.TextOf(row, "Problem");
                accessories.Text = Ui.TextOf(row, "Accessories");
                appearance.Text = Ui.TextOf(row, "Appearance");
                estimate.Text = Ui.MoneyText(row, "EstimatedCost");
                master.SelectedValue = currentMaster;
                diagnosis.Text = Ui.TextOf(row, "Diagnosis");
                work.Text = Ui.TextOf(row, "WorkDone");
            }
            SetClient(clientId);
            LoadStatuses();

            if (intake || technical)
                AcceptButton = ActionButton("Сохранить", Save);
            if (canChangeStatus)
            {
                changeStatus = ActionButton("Изменить статус", ChangeStatus);
                changeStatus.Enabled = false;
                statusBox.SelectedIndexChanged += (sender, args) =>
                    changeStatus.Enabled = Ui.Selected(statusBox) > 0 && Ui.Selected(statusBox) != (int)status;
            }
            ActionButton("Открыть клиента", OpenClient);
            if (id > 0 && (employee.AccessRole == Role.Reception || closed))
                goToIssue = ActionButton("Перейти к выдаче", OpenIssue);
            Cancel(intake || technical ? "Отмена" : "Закрыть");
            UpdateIssueButton();
            foreach (var field in new[] { manufacturer, model, serial, problem, accessories, appearance, estimate, diagnosis, work })
                field.TextChanged += (sender, args) => edited = true;
            type.SelectedIndexChanged += (sender, args) => edited = true;
            master.SelectedIndexChanged += (sender, args) => edited = true;
            imei.CheckedChanged += (sender, args) => edited = true;
        }

        private void SetClient(int selectedId)
        {
            if (selectedId == 0)
            {
                clientId = 0;
                client.Text = "";
                return;
            }
            DataRow row = Clients.Get(selectedId);
            clientId = selectedId;
            client.Text = Ui.TextOf(row, "FullName") + " — " + Ui.TextOf(row, "Phone");
        }

        private void ChooseClient()
        {
            using (var picker = new ClientPicker())
                if (picker.ShowDialog(this) == DialogResult.OK && clientId != picker.ClientId)
                {
                    SetClient(picker.ClientId);
                    edited = true;
                }
        }

        private void OpenClient()
        {
            Database.Require(clientId > 0, "Сначала выберите клиента.");
            using (var form = new ClientForm(employee, clientId, false))
                if (form.ShowDialog(this) == DialogResult.OK && form.ClientId == clientId)
                    SetClient(clientId);
        }

        private void LoadStatuses()
        {
            var list = Repairs.Statuses();
            var next = employee.AccessRole != Role.Manager && id > 0 ? Repairs.Next(status) : new RepairStatus[0];
            for (int index = list.Rows.Count - 1; index >= 0; index--)
            {
                var item = (RepairStatus)(int)list.Rows[index]["Id"];
                if (item != status && Array.IndexOf(next, item) < 0)
                    list.Rows.RemoveAt(index);
            }
            Ui.Choices(statusBox, list);
            statusBox.SelectedValue = (int)status;
            statusBox.Enabled = next.Length > 0;
        }

        private void ChangeStatus()
        {
            var newStatus = (RepairStatus)Ui.Selected(statusBox);
            if (newStatus == status)
                return;
            if (newStatus == RepairStatus.Declined && !Ui.Confirm(this, "Подтвердить отказ клиента от ремонта?"))
                return;
            if (edited && !Ui.Confirm(this, "Изменить статус? Правки полей останутся в форме до нажатия «Сохранить»."))
                return;
            Repairs.ChangeStatus(employee, id, status, newStatus);
            status = newStatus;
            LoadStatuses();
            changeStatus.Enabled = false;
            UpdateIssueButton();
        }

        private void UpdateIssueButton()
        {
            if (goToIssue == null)
                return;
            if (status == RepairStatus.Issued)
            {
                goToIssue.Text = "Сведения о выдаче";
                goToIssue.Enabled = true;
            }
            else if (status == RepairStatus.Declined)
            {
                goToIssue.Text = "Возврат при отказе";
                goToIssue.Enabled = true;
            }
            else
            {
                goToIssue.Text = "Перейти к выдаче";
                goToIssue.Enabled = status == RepairStatus.Ready;
            }
        }

        private void OpenIssue()
        {
            if (edited && !Ui.Confirm(this, "В форме есть несохранённые правки. Открыть выдачу с сохранёнными данными?"))
                return;
            var row = Repairs.Get(id);
            Database.Require((int)row["StatusId"] == (int)status, "Статус заявки изменился. Откройте заявку заново.");
            using (var form = new IssueForm(employee, row))
                if (form.ShowDialog(this) == DialogResult.OK && status != RepairStatus.Issued)
                    DialogResult = DialogResult.OK;
        }

        private bool Required(TextBox field)
        {
            return field.Text.Trim().Length > 0 || Invalid(field, "Заполните обязательное поле.");
        }

        private void Save()
        {
            ClearErrors();
            if (clientId == 0) { Invalid(client, "Выберите клиента из списка."); return; }
            if (Ui.Selected(type) == 0) { Invalid(type, "Выберите тип устройства."); return; }
            if (!Required(manufacturer) || !Required(model) || !Required(problem) || !Required(accessories))
                return;

            string serialNumber = serial.Text.Trim();
            if (imei.Checked && !Regex.IsMatch(serialNumber, @"^[0-9]{15}$"))
            {
                Invalid(serial, "Введите IMEI из 15 цифр.");
                return;
            }
            if (!imei.Checked && serialNumber.Length > 0 && !Regex.IsMatch(serialNumber, @"^[\p{L}0-9]+$"))
            {
                Invalid(serial, "Введите серийный номер буквами и цифрами.");
                return;
            }

            decimal? amount;
            if (!Money(estimate, false, out amount))
                return;
            if (status == RepairStatus.Issued && !Ui.Confirm(this, "Сохранить исправления в закрытой заявке?"))
                return;

            Repairs.Save(employee, id, status, new RepairData
            {
                ClientId = clientId, DeviceTypeId = Ui.Selected(type), Manufacturer = manufacturer.Text.Trim(),
                Model = model.Text.Trim(), Serial = serialNumber, IsImei = imei.Checked, Problem = problem.Text.Trim(),
                Accessories = accessories.Text.Trim(), Appearance = appearance.Text.Trim(), Estimate = amount,
                MasterId = Ui.Selected(master) == 0 ? (int?)null : Ui.Selected(master),
                Diagnosis = diagnosis.Text.Trim(), WorkDone = work.Text.Trim()
            });
            DialogResult = DialogResult.OK;
        }
    }
}
