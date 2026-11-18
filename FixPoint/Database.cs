using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FixPoint
{
    internal enum Role
    {
        Reception = 1,
        Master = 2,
        Manager = 3
    }

    internal sealed class Employee
    {
        public int Id;
        public string Name;
        public Role AccessRole;

        public static string RoleName(Role role)
        {
            switch (role)
            {
                case Role.Reception: return "Администратор-приёмщик";
                case Role.Master: return "Мастер";
                case Role.Manager: return "Руководитель";
                default: return "";
            }
        }
    }

    internal static class Database
    {
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixPoint");
        private static readonly string FilePath = Path.Combine(Folder, "FixPoint.sqlite");
        private static readonly string BackupFolder = Path.Combine(Folder, "Backups");
        private const string BackupPattern = "FixPoint_????-??-??.sqlite";
        private const string RestoreMessage = "Рабочая база отсутствует или пуста. Восстановите файл FixPoint.sqlite из папки Backups.";

        private static Sqlite Open()
        {
            return new Sqlite(FilePath);
        }

        public static void Initialize()
        {
            Directory.CreateDirectory(Folder);
            bool hasBackups = Directory.Exists(BackupFolder) &&
                Directory.GetFiles(BackupFolder, BackupPattern).Length > 0;
            if (!File.Exists(FilePath) && hasBackups)
                throw new InvalidOperationException(RestoreMessage);

            using (var connection = new Sqlite(FilePath, create: !hasBackups))
            {
                Require(object.Equals(connection.Scalar("PRAGMA foreign_keys"), 1),
                    "Эта библиотека SQLite не обеспечивает целостность связей базы FixPoint.");
                connection.ExecuteScript("BEGIN IMMEDIATE;");
                int version = (int)connection.Scalar("PRAGMA user_version");
                if (version == 0)
                {
                    Require(!hasBackups, RestoreMessage);
                    Require(object.Equals(connection.Scalar(
                        "SELECT count(*) FROM sqlite_master WHERE name NOT GLOB 'sqlite_*'"), 0),
                        "Файл содержит неизвестную схему базы. Проверьте файл FixPoint.sqlite.");
                    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FixPoint.CreateDatabase.sql"))
                    {
                        if (stream == null)
                            throw new InvalidOperationException("В сборке отсутствует схема базы FixPoint.");
                        using (var reader = new StreamReader(stream))
                            connection.ExecuteScript(reader.ReadToEnd());
                    }
                }
                CheckSchema(connection);
                connection.ExecuteScript("COMMIT;");
            }
        }

        private static void CheckSchema(Sqlite connection)
        {
            Require(object.Equals(connection.Scalar("PRAGMA user_version"), 1),
                "Эта версия FixPoint не поддерживает формат файла базы.");
            const string tableCount = @"SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN
                ('Users','Clients','Repairs','Issues','Audit','RequestNumber','Statuses','DeviceTypes','PaymentMethods')";
            Require(object.Equals(connection.Scalar(tableCount), 9),
                "В файле базы FixPoint отсутствуют необходимые таблицы. Используйте рабочую базу или резервную копию.");
        }

        public static void BackupToday(bool replace = false)
        {
            Directory.CreateDirectory(BackupFolder);
            string target = Path.Combine(BackupFolder, "FixPoint_" +
                DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".sqlite");
            if (!replace && File.Exists(target))
                return;

            string temporary = Path.Combine(BackupFolder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var connection = Open())
                {
                    CheckSchema(connection);
                    connection.BackupTo(temporary);
                }
                using (var copy = new Sqlite(temporary))
                {
                    CheckSchema(copy);
                    Require((string)copy.Scalar("PRAGMA quick_check") == "ok",
                        "Проверка резервной копии SQLite не пройдена.");
                }
                if (File.Exists(target))
                    File.Replace(temporary, target, null);
                else
                    File.Move(temporary, target);

                foreach (string file in Directory.GetFiles(BackupFolder, BackupPattern))
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                        File.Delete(file);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        public static DbParameter P(string name, object value)
        {
            return new DbParameter(name, value);
        }

        public static DataTable Read(string sql, params DbParameter[] parameters)
        {
            using (var connection = Open())
                return connection.Read(sql, parameters);
        }

        public static object Scalar(Sqlite transaction, string sql, params DbParameter[] parameters)
        {
            return transaction.Scalar(sql, parameters);
        }

        public static int Write(Sqlite transaction, string sql, params DbParameter[] parameters)
        {
            return transaction.Write(sql, parameters);
        }

        public static void InTransaction(Action<Sqlite> action)
        {
            using (var connection = Open())
            {
                connection.ExecuteScript("BEGIN IMMEDIATE;");
                action(connection);
                connection.ExecuteScript("COMMIT;");
            }
        }

        public static void Require(bool allowed, string message = "Операция недоступна для вашей роли.")
        {
            if (!allowed)
                throw new InvalidOperationException(message);
        }

        public static void Log(Sqlite transaction, int? user, int? repair, string action, string details)
        {
            Write(transaction, @"INSERT INTO Audit (UserId, RepairId, Action, Details, LoggedAt)
                                  VALUES (@user, @repair, @action, @details, @loggedAt)",
                P("@user", user), P("@repair", repair), P("@action", action), P("@details", details), P("@loggedAt", DateTime.Now));
        }

        public static byte[] PasswordHash(string password, byte[] salt)
        {
            // PBKDF2-HMAC-SHA256, one 32-byte block. Keep the existing database hashes.
            const int iterations = 100000;
            var firstBlock = new byte[salt.Length + 4];
            Array.Copy(salt, firstBlock, salt.Length);
            firstBlock[firstBlock.Length - 1] = 1;

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(password)))
            {
                byte[] previous = hmac.ComputeHash(firstBlock);
                var output = (byte[])previous.Clone();
                for (int round = 1; round < iterations; round++)
                {
                    previous = hmac.ComputeHash(previous);
                    for (int index = 0; index < output.Length; index++)
                        output[index] ^= previous[index];
                }
                return output;
            }
        }
    }
}
