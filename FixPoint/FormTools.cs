using System;
using System.Data;
using System.IO;
using System.Drawing;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FixPoint
{
    internal static class Ui
    {
        public static void Try(IWin32Window owner, Action action)
        {
            try
            {
                action();
            }
            catch (SqliteException error)
            {
                string message = error.UniqueConstraint ? "Запись с такими данными уже существует. Проверьте телефон или логин."
                    : (error.Code & 255) == 5 || (error.Code & 255) == 6
                        ? "Файл базы временно занят. Повторите операцию."
                        : "SQLite не выполнила операцию. Код " + error.Code + ": " + error.Message;
                Warn(owner, message);
            }
            catch (DllNotFoundException)
            {
                Warn(owner, "Для запуска FixPoint нужен системный компонент SQLite из Windows 10 или 11.");
            }
            catch (EntryPointNotFoundException)
            {
                Warn(owner, "Системная библиотека SQLite неполная. Для FixPoint нужна актуальная версия Windows 10 или 11.");
            }
            catch (IOException error)
            {
                Warn(owner, "Операция с файлом базы не выполнена. " + error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                Warn(owner, "Нет доступа к папке базы FixPoint. " + error.Message);
            }
            catch (InvalidOperationException error)
            {
                Warn(owner, error.Message);
            }
        }

        public static void Warn(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, "FixPoint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static bool Confirm(IWin32Window owner, string text)
        {
            return MessageBox.Show(owner, text, "FixPoint", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        public static Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(0, 28) };
            button.Click += (sender, args) => action();
            return button;
        }

        public static FlowLayoutPanel Bar(params Control[] controls)
        {
            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(5) };
            bar.Controls.AddRange(controls);
            return bar;
        }

        public static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(4, 8, 4, 4) };
        }

        public static ComboBox Combo()
        {
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        }

        public static void Choices(ComboBox box, DataTable table, string empty = null)
        {
            if (empty != null)
            {
                var row = table.NewRow();
                row["Id"] = 0;
                row["Name"] = empty;
                table.Rows.InsertAt(row, 0);
            }
            box.DisplayMember = "Name";
            box.ValueMember = "Id";
            box.DataSource = table;
        }

        public static int Selected(ComboBox box)
        {
            return box.SelectedValue is int ? (int)box.SelectedValue : 0;
        }

        public static string TextOf(DataRow row, string column)
        {
            return Convert.ToString(row[column]);
        }

        public static string MoneyText(DataRow row, string column)
        {
            return row.IsNull(column) ? "" : ((decimal)row[column]).ToString("0.00", CultureInfo.GetCultureInfo("ru-RU"));
        }
    }

    internal class FieldsForm : Form
    {
        private readonly TableLayoutPanel fields;
        private readonly FlowLayoutPanel buttons;
        private readonly ErrorProvider errors;

        public FieldsForm(string title, int width, int height)
        {
            Text = title;
            ClientSize = new Size(width, height);
            MinimumSize = new Size(width + 16, Math.Min(height + 39, 300));
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            ShowInTaskbar = false;

            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            fields = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2, Padding = new Padding(10), GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            scroll.Controls.Add(fields);
            buttons = Ui.Bar();
            frame.Controls.Add(scroll, 0, 0);
            frame.Controls.Add(buttons, 0, 1);
            Controls.Add(frame);
            errors = new ErrorProvider { ContainerControl = this, BlinkStyle = ErrorBlinkStyle.NeverBlink };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && errors != null)
                errors.Dispose();
            base.Dispose(disposing);
        }

        protected T Field<T>(string label, T control) where T : Control
        {
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(4, 4, 26, 4);
            fields.Controls.Add(Ui.Caption(label), 0, row);
            fields.Controls.Add(control, 1, row);
            return control;
        }

        protected TextBox Input(string label, int max, bool multiline = false, bool readOnly = false)
        {
            return Field(label, new TextBox
            {
                MaxLength = max, Multiline = multiline, ReadOnly = readOnly,
                Height = multiline ? 47 : 23, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
            });
        }

        protected Button ActionButton(string label, Action action)
        {
            var button = Ui.Button(label, () => Ui.Try(this, action));
            buttons.Controls.Add(button);
            return button;
        }

        protected void Cancel(string text = "Отмена")
        {
            var button = Ui.Button(text, Close);
            button.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(button);
            CancelButton = button;
        }

        protected void ClearErrors()
        {
            errors.Clear();
        }

        protected bool Invalid(Control control, string message)
        {
            errors.SetError(control, message);
            control.Focus();
            return false;
        }

        protected bool Money(TextBox input, bool required, out decimal? amount)
        {
            amount = null;
            string value = input.Text.Trim().Replace(',', '.');
            if (value.Length == 0 && !required)
                return true;
            decimal number;
            if (!Regex.IsMatch(value, @"^[0-9]+(\.[0-9]{1,2})?$") ||
                !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number) ||
                number > 9999999999.99m)
                return Invalid(input, "Введите сумму от 0 до 9 999 999 999,99, до двух знаков после запятой.");
            amount = number;
            return true;
        }
    }

    internal sealed class PageGrid : UserControl
    {
        private readonly DataGridView grid;
        private readonly Button back;
        private readonly Button next;
        private readonly Label pageLabel;
        private int page;
        private string[] columns;

        public Func<int, DataTable> LoadRows;
        public event Action OpenRow;

        public PageGrid()
        {
            Dock = DockStyle.Fill;
            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.CellDoubleClick += (sender, args) =>
            {
                var open = OpenRow;
                if (args.RowIndex >= 0 && open != null)
                    open();
            };
            back = Ui.Button("Назад", () => Ui.Try(this, () => RefreshPage(page - 1)));
            next = Ui.Button("Далее", () => Ui.Try(this, () => RefreshPage(page + 1)));
            back.Enabled = next.Enabled = false;
            pageLabel = Ui.Caption("");
            frame.Controls.Add(grid, 0, 0);
            frame.Controls.Add(Ui.Bar(back, next, pageLabel), 0, 1);
            Controls.Add(frame);
        }

        public DataRow Current
        {
            get
            {
                var view = grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as DataRowView;
                return view == null ? null : view.Row;
            }
        }

        public void Columns(params string[] nameAndTitle)
        {
            columns = nameAndTitle;
        }

        public void Reload(bool firstPage = true)
        {
            RefreshPage(firstPage ? 0 : page);
        }

        private void RefreshPage(int target)
        {
            var rows = LoadRows(target * 100);
            if (target > 0 && rows.Rows.Count == 0)
            {
                RefreshPage(target - 1);
                return;
            }

            page = target;
            next.Enabled = rows.Rows.Count > 100;
            if (next.Enabled)
                rows.Rows.RemoveAt(100);
            back.Enabled = page > 0;
            grid.DataSource = rows;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.Visible = column.Name != "Id" && (columns == null || Array.IndexOf(columns, column.Name) >= 0);
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.MinimumWidth = 75;
                if (columns != null)
                {
                    int index = Array.IndexOf(columns, column.Name);
                    if (index >= 0)
                        column.HeaderText = columns[index + 1];
                }
                if (column.ValueType == typeof(DateTime))
                    column.DefaultCellStyle.Format = "dd.MM.yyyy HH:mm";
            }
            pageLabel.Text = rows.Rows.Count == 0 ? "Записей нет" : "Страница " + (page + 1);
        }
    }
}
