using System;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FixPoint
{
    internal static class Clients
    {
        public const string FullName = "c.LastName || ' ' || c.FirstName || CASE WHEN c.MiddleName = '' THEN '' ELSE ' ' || c.MiddleName END";

        public static string Digits(string text)
        {
            return Regex.Replace(text, "[^0-9]", "");
        }

        public static string Like(string text)
        {
            return text.ToLowerInvariant().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_");
        }

        public static DataTable List(string text, int offset)
        {
            return Database.Read(@"SELECT c.Id, " + FullName + @" AS [ФИО], c.Phone AS [Телефон],
                                  c.Email AS [E-mail], c.CreatedAt AS [Создан]
                           FROM Clients c
                           WHERE c.SearchName LIKE @search ESCAPE '~'
                              OR (@digits <> '' AND c.PhoneDigits LIKE @phone)
                           ORDER BY c.Id DESC LIMIT 101 OFFSET @offset",
                Database.P("@search", "%" + Like(text) + "%"), Database.P("@digits", Digits(text)),
                Database.P("@phone", "%" + Digits(text) + "%"), Database.P("@offset", offset));
        }

        public static DataRow Get(int id)
        {
            var rows = Database.Read("SELECT c.*, " + FullName + " AS FullName FROM Clients c WHERE c.Id=@id",
                Database.P("@id", id));
            Database.Require(rows.Rows.Count == 1, "Карточка клиента отсутствует. Обновите список клиентов.");
            return rows.Rows[0];
        }

        public static int Duplicate(string digits, int id)
        {
            var result = Database.Read(@"SELECT Id FROM Clients WHERE PhoneDigits = @phone
                                  AND Id <> @id", Database.P("@phone", digits), Database.P("@id", id));
            return result.Rows.Count == 0 ? 0 : (int)result.Rows[0][0];
        }

        public static int Save(Employee employee, int id, string surname, string name, string patronymic,
            string phone, string email, string notes)
        {
            Database.Require(employee.AccessRole == Role.Reception);
            string sql = id == 0
                ? @"INSERT INTO Clients (LastName, FirstName, MiddleName, SearchName, Phone, PhoneDigits, Email, Notes, CreatedAt)
                    VALUES (@surname,@name,@patronymic,@searchName,@phone,@digits,@email,@notes,@createdAt)"
                : @"UPDATE Clients SET LastName=@surname, FirstName=@name, MiddleName=@patronymic, SearchName=@searchName,
                           Phone=@phone, PhoneDigits=@digits, Email=@email, Notes=@notes
                    WHERE Id=@id";
            int saved = 0;
            Database.InTransaction(t =>
            {
                string searchName = (surname + " " + name + " " + patronymic).TrimEnd().ToLowerInvariant();
                int changed = Database.Write(t, sql, Database.P("@id", id), Database.P("@surname", surname), Database.P("@name", name),
                    Database.P("@patronymic", patronymic), Database.P("@searchName", searchName),
                    Database.P("@phone", phone), Database.P("@digits", Digits(phone)),
                    Database.P("@email", email), Database.P("@notes", notes), Database.P("@createdAt", DateTime.Now));
                Database.Require(changed == 1, "Карточка клиента недоступна.");
                saved = id == 0 ? (int)Database.Scalar(t, "SELECT last_insert_rowid()") : id;
                Database.Log(t, employee.Id, null, id == 0 ? "Создан клиент" : "Изменён клиент", "ID клиента " + saved);
            });
            return saved;
        }
    }

    internal static class Accounts
    {
        public static Employee Login(string name, string password)
        {
            var users = Database.Read("SELECT * FROM Users WHERE Login=@login AND Active=1", Database.P("@login", name));
            Employee employee = null;
            if (users.Rows.Count != 0)
            {
                var row = users.Rows[0];
                if (Database.PasswordHash(password, (byte[])row["Salt"]).SequenceEqual((byte[])row["PasswordHash"]))
                {
                    employee = new Employee
                    {
                        Id = (int)row["Id"],
                        Name = (string)row["FullName"],
                        AccessRole = (Role)(int)row["RoleId"]
                    };
                }
            }
            Database.InTransaction(t => Database.Log(t, employee == null ? (int?)null : employee.Id, null, employee == null ? "Неуспешный вход" : "Вход",
                "Логин: " + name));
            return employee;
        }

        public static DataTable Masters(int current)
        {
            return Database.Read(@"SELECT Id, FullName || CASE WHEN Active=1 THEN '' ELSE ' (неактивен)' END AS Name
                          FROM Users WHERE (RoleId=2 AND Active=1) OR Id=@current ORDER BY FullName",
                Database.P("@current", current));
        }

        public static DataTable Users(Employee employee, int offset)
        {
            Database.Require(employee.AccessRole == Role.Manager);
            var rows = Database.Read(@"SELECT Id, Login, FullName, RoleId, Active FROM Users
                              ORDER BY FullName, Id LIMIT 101 OFFSET @offset", Database.P("@offset", offset));
            rows.Columns.Add("RoleName", typeof(string));
            foreach (DataRow row in rows.Rows)
                row["RoleName"] = Employee.RoleName((Role)(int)row["RoleId"]);
            return rows;
        }

        public static void Save(Employee employee, int id, string login, string name, Role role, bool active,
            string password)
        {
            Database.Require(employee.AccessRole == Role.Manager);
            Database.Require(id != employee.Id || (active && role == Role.Manager),
                "Нельзя отключить свою учётную запись или изменить свою роль.");

            byte[] salt = null;
            byte[] hash = null;
            if (password.Length > 0)
            {
                salt = new byte[16];
                using (var random = RandomNumberGenerator.Create())
                    random.GetBytes(salt);
                hash = Database.PasswordHash(password, salt);
            }

            Database.InTransaction(t =>
            {
                string sql = id == 0
                    ? @"INSERT INTO Users (Login,FullName,RoleId,Active,Salt,PasswordHash)
                        VALUES (@login,@name,@role,@active,@salt,@hash)"
                    : @"UPDATE Users SET Login=@login, FullName=@name, RoleId=@role, Active=@active,
                               Salt=COALESCE(@salt,Salt), PasswordHash=COALESCE(@hash,PasswordHash)
                        WHERE Id=@id";
                int changed = Database.Write(t, sql, Database.P("@id", id), Database.P("@login", login), Database.P("@name", name),
                    Database.P("@role", (int)role), Database.P("@active", active), Database.P("@salt", salt), Database.P("@hash", hash));
                Database.Require(changed == 1, "Учётная запись недоступна.");
                int saved = id == 0 ? (int)Database.Scalar(t, "SELECT last_insert_rowid()") : id;
                Database.Log(t, employee.Id, null, id == 0 ? "Создан пользователь" : "Изменён пользователь",
                    "ID " + saved + ", логин " + login + ", роль «" + Employee.RoleName(role) + "»");
            });
        }

        public static DataTable Journal(Employee employee, int? repair, int offset)
        {
            Database.Require(employee.AccessRole == Role.Manager);
            return Database.Read(@"SELECT a.LoggedAt AS [Дата], COALESCE(u.Login,'') AS [Пользователь],
                                 COALESCE(r.Number,'') AS [Заявка], a.Action AS [Событие], a.Details AS [Подробности]
                          FROM Audit a LEFT JOIN Users u ON u.Id=a.UserId
                          LEFT JOIN Repairs r ON r.Id=a.RepairId
                          WHERE @repair IS NULL OR a.RepairId=@repair
                          ORDER BY a.Id DESC LIMIT 101 OFFSET @offset",
                Database.P("@repair", repair), Database.P("@offset", offset));
        }
    }
}
