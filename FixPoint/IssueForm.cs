using System;
using System.Data;
using System.Windows.Forms;

namespace FixPoint
{
    internal sealed class IssueForm : FieldsForm
    {
        private readonly Employee employee;
        private readonly int id;
        private readonly RepairStatus status;
        private readonly CheckBox confirmed;
        private readonly TextBox cost, notes;
        private readonly ComboBox payment;
        private readonly NumericUpDown warranty;

        public IssueForm(Employee employee, DataRow row) : base("Выдача устройства", 675, 490)
        {
            this.employee = employee;
            id = (int)row["Id"];
            status = (RepairStatus)(int)row["StatusId"];
            bool issued = status == RepairStatus.Issued;
            bool correction = issued && employee.AccessRole == Role.Manager;
            bool readOnly = issued && !correction;
            Database.Require(!issued || !row.IsNull("IssuedAt"),
                "В базе отсутствует запись о выдаче для этой заявки.");
            Database.Require(issued || employee.AccessRole == Role.Reception);
            Database.Require(issued || status == RepairStatus.Ready || status == RepairStatus.Declined,
                "Заявка ещё не готова к выдаче.");

            Input("Номер заявки", 20, readOnly: true).Text = Ui.TextOf(row, "Number");
            Input("Клиент", 200, readOnly: true).Text = Ui.TextOf(row, "ClientName");
            Input("Устройство", 220, readOnly: true).Text =
                Ui.TextOf(row, "DeviceType") + " / " + Ui.TextOf(row, "Manufacturer") + " " + Ui.TextOf(row, "Model");
            Input("Выполненные работы", 1500, true, true).Text = Ui.TextOf(row, "WorkDone");
            if (status == RepairStatus.Declined)
                Input("Основание выдачи", 60, readOnly: true).Text = "Возврат после отказа от ремонта";

            cost = Input("Итоговая стоимость, руб. *", 16, readOnly: readOnly);
            payment = Field("Способ оплаты *", Ui.Combo());
            Ui.Choices(payment, Repairs.PaymentMethods(), "Выберите способ оплаты");
            payment.Enabled = !readOnly;
            warranty = Field("Гарантия, дней *", new NumericUpDown { Minimum = 0, Maximum = 365, ReadOnly = readOnly,
                Enabled = !readOnly });
            notes = Input("Комментарий", 500, true, readOnly);
            Input("Дата выдачи", 32, readOnly: true).Text = issued
                ? ((DateTime)row["IssuedAt"]).ToString("dd.MM.yyyy HH:mm") : "При подтверждении";
            if (issued)
                Input("Выдал сотрудник", 120, readOnly: true).Text = Ui.TextOf(row, "Issuer");

            cost.Text = issued ? Ui.MoneyText(row, "FinalCost")
                : status == RepairStatus.Declined ? "0" : Ui.MoneyText(row, "EstimatedCost");
            payment.SelectedValue = issued ? (int)row["PaymentId"] : status == RepairStatus.Declined ? 4 : 0;
            warranty.Value = issued ? (int)row["WarrantyDays"] : status == RepairStatus.Declined ? 0 : 30;
            notes.Text = Ui.TextOf(row, "IssueNotes");

            if (!readOnly)
            {
                confirmed = Field("Подтверждение *", new CheckBox
                {
                    Text = correction ? "Подтверждаю исправление" : "Устройство передано клиенту", AutoSize = true
                });
                AcceptButton = ActionButton(correction ? "Сохранить исправления" : "Подтвердить выдачу", Save);
            }
            Cancel(readOnly ? "Закрыть" : "Отмена");
        }

        private void Save()
        {
            ClearErrors();
            decimal? amount;
            if (!Money(cost, true, out amount))
                return;
            int paymentId = Ui.Selected(payment);
            if (paymentId == 0) { Invalid(payment, "Выберите способ оплаты."); return; }
            if (paymentId == 4 && amount.Value != 0) { Invalid(cost, "При выдаче без оплаты укажите 0 руб."); return; }
            if (!confirmed.Checked) { Invalid(confirmed, "Установите флажок подтверждения."); return; }

            string question = status == RepairStatus.Issued ? "Сохранить исправления в сведениях о выдаче?"
                : "Подтвердить выдачу устройства и закрыть заявку?";
            if (!Ui.Confirm(this, question))
                return;
            Repairs.Issue(employee, id, status, amount.Value, paymentId, (int)warranty.Value, notes.Text.Trim(), confirmed.Checked);
            DialogResult = DialogResult.OK;
        }
    }
}
