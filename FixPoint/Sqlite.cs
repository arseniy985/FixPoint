using System;
using System.Data;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace FixPoint
{
    internal sealed class SqliteException : Exception
    {
        public int Code { get; private set; }
        public bool UniqueConstraint { get { return Code == 1555 || Code == 2067; } }

        public SqliteException(int code, string message) : base(message)
        {
            Code = code;
        }
    }

    internal sealed class DbParameter
    {
        public readonly string Name;
        public readonly object Value;
        public DbParameter(string name, object value)
        {
            Name = name;
            Value = value;
        }
    }

    internal sealed class Sqlite : IDisposable
    {
        private const int OpenReadWrite = 2;
        private const int OpenCreate = 4;
        private const int Row = 100;
        private const int Done = 101;
        private const string Library = "winsqlite3.dll";
        // Windows' own SQLite exports use stdcall on x86.
        private const CallingConvention WindowsSqliteConvention = CallingConvention.StdCall;
        private static readonly IntPtr CopyValue = new IntPtr(-1);
        private IntPtr handle;

        public Sqlite(string path, bool create = false)
        {
            try
            {
                int flags = OpenReadWrite | (create ? OpenCreate : 0);
                Verify(sqlite3_open_v2(Utf8(path), out handle, flags, IntPtr.Zero));
                ExecuteScript("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero)
                return;
            sqlite3_close(handle);
            handle = IntPtr.Zero;
        }

        public void ExecuteScript(string sql)
        {
            Verify(sqlite3_exec(handle, Utf8(sql), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        }

        public int Write(string sql, params DbParameter[] parameters)
        {
            IntPtr statement = Prepare(sql, parameters);
            try
            {
                int result = sqlite3_step(statement);
                Verify(result);
                if (result != Done)
                    throw new InvalidOperationException("Команда записи SQLite не была завершена.");
                return sqlite3_changes(handle);
            }
            finally { sqlite3_finalize(statement); }
        }

        public object Scalar(string sql, params DbParameter[] parameters)
        {
            IntPtr statement = Prepare(sql, parameters);
            try
            {
                int result = sqlite3_step(statement);
                Verify(result);
                return result == Row ? Value(statement, 0, ColumnType(statement, 0)) : null;
            }
            finally { sqlite3_finalize(statement); }
        }

        public DataTable Read(string sql, params DbParameter[] parameters)
        {
            IntPtr statement = Prepare(sql, parameters);
            try
            {
                var table = new DataTable();
                int columns = sqlite3_column_count(statement);
                for (int i = 0; i < columns; i++)
                    table.Columns.Add(ReadUtf8(sqlite3_column_name(statement, i)), ColumnType(statement, i));

                int result;
                while ((result = sqlite3_step(statement)) == Row)
                {
                    var values = new object[columns];
                    for (int i = 0; i < columns; i++)
                        values[i] = Value(statement, i, table.Columns[i].DataType);
                    table.Rows.Add(values);
                }
                Verify(result);
                return table;
            }
            finally { sqlite3_finalize(statement); }
        }

        public void BackupTo(string path)
        {
            using (var destination = new Sqlite(path, create: true))
            {
                byte[] main = Utf8("main");
                IntPtr backup = sqlite3_backup_init(destination.handle, main, handle, main);
                if (backup == IntPtr.Zero)
                    destination.Verify(sqlite3_errcode(destination.handle));
                else
                {
                    int result = sqlite3_backup_step(backup, -1);
                    int finished = sqlite3_backup_finish(backup);
                    destination.Verify(finished);
                    destination.Verify(result);
                    if (result != Done)
                        throw new InvalidOperationException("Резервная копия SQLite создана не полностью.");
                }
            }
        }

        private IntPtr Prepare(string sql, DbParameter[] parameters)
        {
            IntPtr statement;
            Verify(sqlite3_prepare_v2(handle, Utf8(sql), -1, out statement, IntPtr.Zero));
            try
            {
                if (statement == IntPtr.Zero)
                    throw new InvalidOperationException("Передана пустая команда SQLite.");

                int count = sqlite3_bind_parameter_count(statement);
                for (int index = 1; index <= count; index++)
                {
                    string name = ReadUtf8(sqlite3_bind_parameter_name(statement, index));
                    var parameter = Array.Find(parameters, item => item.Name == name);
                    if (parameter == null)
                        throw new InvalidOperationException("Для запроса SQLite не задан параметр " + name + ".");
                    Bind(statement, index, parameter.Value);
                }
                return statement;
            }
            catch
            {
                sqlite3_finalize(statement);
                throw;
            }
        }

        private void Bind(IntPtr statement, int index, object value)
        {
            int result;
            if (value == null || value == DBNull.Value)
                result = sqlite3_bind_null(statement, index);
            else if (value is bool)
                result = sqlite3_bind_int64(statement, index, (bool)value ? 1 : 0);
            else if (value is int)
                result = sqlite3_bind_int64(statement, index, (int)value);
            else if (value is decimal)
            {
                decimal cents = (decimal)value * 100;
                if (cents != decimal.Truncate(cents))
                    throw new InvalidOperationException("В сумме должно быть не более двух знаков после запятой.");
                result = sqlite3_bind_int64(statement, index, decimal.ToInt64(cents));
            }
            else if (value is byte[])
            {
                var bytes = (byte[])value;
                result = sqlite3_bind_blob(statement, index, bytes, bytes.Length, CopyValue);
            }
            else
            {
                string text = value is DateTime
                    ? ((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    : (string)value;
                byte[] bytes = Utf8(text);
                result = sqlite3_bind_text(statement, index, bytes, bytes.Length - 1, CopyValue);
            }
            Verify(result);
        }

        private static Type ColumnType(IntPtr statement, int column)
        {
            string name = ReadUtf8(sqlite3_column_decltype(statement, column));
            switch (name.ToUpperInvariant())
            {
                case "INTEGER": return typeof(int);
                case "BOOLEAN": return typeof(bool);
                case "DATETIME": return typeof(DateTime);
                case "MONEY_INT": return typeof(decimal);
                case "TEXT": return typeof(string);
                case "BLOB": return typeof(byte[]);
                default: return typeof(object);
            }
        }

        private static object Value(IntPtr statement, int column, Type type)
        {
            int storage = sqlite3_column_type(statement, column);
            if (storage == 5)
                return DBNull.Value;
            if (type == typeof(DateTime))
            {
                DateTime date;
                if (storage != 3 || !DateTime.TryParseExact(TextValue(statement, column),
                    "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    throw new InvalidOperationException("Некорректная дата в базе FixPoint: " +
                        ReadUtf8(sqlite3_column_name(statement, column)) + ".");
                return date;
            }
            if (type == typeof(bool) || type == typeof(decimal) || type == typeof(int))
            {
                if (storage != 1)
                    throw new InvalidOperationException("Некорректное число в базе FixPoint: " +
                        ReadUtf8(sqlite3_column_name(statement, column)) + ".");
                long number = sqlite3_column_int64(statement, column);
                if (type == typeof(bool))
                    return number != 0;
                if (type == typeof(decimal))
                    return number / 100m;
                if (number < int.MinValue || number > int.MaxValue)
                    throw new InvalidOperationException("Число в базе FixPoint вне допустимого диапазона.");
                return (int)number;
            }
            if ((type == typeof(string) && storage != 3) || (type == typeof(byte[]) && storage != 4))
                throw new InvalidOperationException("Некорректный тип данных в базе FixPoint: " +
                    ReadUtf8(sqlite3_column_name(statement, column)) + ".");
            if (storage == 1)
            {
                long number = sqlite3_column_int64(statement, column);
                if (number >= int.MinValue && number <= int.MaxValue)
                    return (int)number;
                return number;
            }
            if (storage == 4)
            {
                IntPtr data = sqlite3_column_blob(statement, column);
                int size = sqlite3_column_bytes(statement, column);
                var bytes = new byte[size];
                if (size > 0)
                    Marshal.Copy(data, bytes, 0, size);
                return bytes;
            }

            return TextValue(statement, column);
        }

        private static string TextValue(IntPtr statement, int column)
        {
            IntPtr text = sqlite3_column_text(statement, column);
            return ReadUtf8(text, sqlite3_column_bytes(statement, column));
        }

        private void Verify(int result)
        {
            if (result == 0 || result == Row || result == Done)
                return;
            int code = handle == IntPtr.Zero ? result : sqlite3_extended_errcode(handle);
            string message = handle == IntPtr.Zero ? "Ошибка открытия файла SQLite." : ReadUtf8(sqlite3_errmsg(handle));
            throw new SqliteException(code == 0 ? result : code, message);
        }

        private static byte[] Utf8(string s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }

        private static string ReadUtf8(IntPtr pointer, int length = -1)
        {
            if (pointer == IntPtr.Zero)
                return "";
            if (length < 0)
            {
                length = 0;
                while (Marshal.ReadByte(pointer, length) != 0)
                    length++;
            }
            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_close(IntPtr db);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, IntPtr error);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_bind_parameter_count(IntPtr statement);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern IntPtr sqlite3_bind_parameter_name(IntPtr statement, int index);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_bind_null(IntPtr statement, int index);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_bind_text(IntPtr statement, int index, byte[] text, int size, IntPtr destructor);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_bind_blob(IntPtr statement, int index, byte[] data, int size, IntPtr destructor);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_changes(IntPtr db);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_column_count(IntPtr statement);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern IntPtr sqlite3_column_name(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern IntPtr sqlite3_column_decltype(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_column_type(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern long sqlite3_column_int64(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern IntPtr sqlite3_column_blob(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_column_bytes(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_errcode(IntPtr db);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_extended_errcode(IntPtr db);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern IntPtr sqlite3_backup_init(IntPtr target, byte[] targetName, IntPtr source, byte[] sourceName);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_backup_step(IntPtr backup, int pages);
        [DllImport(Library, CallingConvention = WindowsSqliteConvention, ExactSpelling = true)] private static extern int sqlite3_backup_finish(IntPtr backup);
    }
}
