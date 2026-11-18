using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace FixPoint
{
    internal enum RepairStatus
    {
        Accepted = 1,
        Diagnosis = 2,
        Approval = 3,
        InProgress = 4,
        Ready = 5,
        Issued = 6,
        Declined = 7
    }

    internal sealed class RepairData
    {
        public int ClientId;
        public int DeviceTypeId;
        public string Manufacturer;
        public string Model;
        public string Serial;
        public bool IsImei;
        public string Problem;
        public string Accessories;
        public string Appearance;
        public decimal? Estimate;
        public int? MasterId;
        public string Diagnosis;
        public string WorkDone;
    }

    internal static class Repairs
    {
        public static DataTable Statuses()
        {
            return Database.Read("SELECT Id, Name FROM Statuses ORDER BY Id");
        }

        public static DataTable DeviceTypes()
        {
            return Database.Read("SELECT Id, Name FROM DeviceTypes ORDER BY Id");
        }

        public static DataTable PaymentMethods()
        {
            return Database.Read("SELECT Id, Name FROM PaymentMethods ORDER BY Id");
        }

        public static DataTable List(int field, string text, int status, DateTime? from, DateTime? until, int offset)
        {
            string digits = Clients.Digits(text);
            string part = "%" + Clients.Like(text) + "%";
            var filters = new List<string>();
            if (text.Length > 0)
            {
                switch (field)
                {
                    case 1: filters.Add("r.Number LIKE @prefix ESCAPE '~'"); break;
                    case 2:
                        filters.Add(digits.Length >= 10 && digits.Length <= 15
                            ? "c.PhoneDigits=@digits"
                            : "(@digits <> '' AND c.PhoneDigits LIKE @phone)");
                        break;
                    case 3: filters.Add("c.SearchName LIKE @part ESCAPE '~'"); break;
                    case 4: filters.Add("r.ModelSearch LIKE @part ESCAPE '~'"); break;
                    case 5: filters.Add("r.SerialSearch LIKE @part ESCAPE '~'"); break;
                    default:
                        filters.Add(@"(r.Number LIKE @prefix ESCAPE '~' OR (length(@digits)>0 AND c.PhoneDigits LIKE @phone)
                                        OR c.SearchName LIKE @part ESCAPE '~'
                                        OR r.ModelSearch LIKE @part ESCAPE '~' OR r.SerialSearch LIKE @part ESCAPE '~')");
                        break;
                }
            }
            if (status > 0) filters.Add("r.StatusId=@status");
            if (from.HasValue) filters.Add("r.ReceivedAt>=@from");
            if (until.HasValue) filters.Add("r.ReceivedAt<@until");
            string where = filters.Count == 0 ? "" : " WHERE " + string.Join(" AND ", filters);
            return Database.Read(@"SELECT r.Id, r.Number AS [Номер], " + Clients.FullName + @" AS [Клиент],
                                  c.Phone AS [Телефон], r.Manufacturer AS [Производитель], r.Model AS [Модель],
                                  r.SerialNumber AS [Серийный номер / IMEI],
                                  s.Name AS [Статус], r.ReceivedAt AS [Дата приёма]
                           FROM Repairs r JOIN Clients c ON c.Id=r.ClientId
                           JOIN Statuses s ON s.Id=r.StatusId" + where +
                        " ORDER BY r.Id DESC LIMIT 101 OFFSET @offset",
                Database.P("@prefix", Clients.Like(text) + "%"), Database.P("@part", part), Database.P("@digits", digits),
                Database.P("@phone", "%" + digits + "%"), Database.P("@status", status), Database.P("@from", from),
                Database.P("@until", until), Database.P("@offset", offset));
        }

        public static DataRow Get(int id)
        {
            var rows = Database.Read(@"SELECT r.*, " + Clients.FullName + @" AS ClientName, c.Phone, t.Name AS DeviceType,
                                  s.Name AS StatusName, i.IssuedAt, i.FinalCost, i.PaymentId, i.WarrantyDays,
                                  i.Notes AS IssueNotes, u.FullName AS Issuer
                           FROM Repairs r JOIN Clients c ON c.Id=r.ClientId
                           JOIN DeviceTypes t ON t.Id=r.DeviceTypeId JOIN Statuses s ON s.Id=r.StatusId
                           LEFT JOIN Issues i ON i.RepairId=r.Id LEFT JOIN Users u ON u.Id=i.IssuedBy
                           WHERE r.Id=@id", Database.P("@id", id));
            Database.Require(rows.Rows.Count == 1, "Заявка отсутствует. Обновите список заявок.");
            return rows.Rows[0];
        }

        public static int Save(Employee employee, int id, RepairStatus status, RepairData data)
        {
            bool closed = status == RepairStatus.Issued;
            Database.Require(id == 0 ? employee.AccessRole == Role.Reception
                : closed ? employee.AccessRole == Role.Manager : employee.AccessRole != Role.Manager);
            int saved = id;
            Database.InTransaction(t =>
            {
                if (id == 0)
                {
                    DateTime now = DateTime.Now;
                    Database.Write(t, "INSERT OR IGNORE INTO RequestNumber (RequestYear,LastUsed) VALUES (@year,0)",
                        Database.P("@year", now.Year));
                    int numbered = Database.Write(t, @"UPDATE RequestNumber SET LastUsed=LastUsed+1
                                                       WHERE RequestYear=@year AND LastUsed<99999", Database.P("@year", now.Year));
                    Database.Require(numbered == 1, "Нумерация заявок за этот год исчерпана.");
                    int next = (int)Database.Scalar(t, "SELECT LastUsed FROM RequestNumber WHERE RequestYear=@year",
                        Database.P("@year", now.Year));
                    string number = string.Format(CultureInfo.InvariantCulture, "FP-{0:D4}-{1:D5}", now.Year, next);
                    int created = Database.Write(t, @"INSERT INTO Repairs
                            (Number,ClientId,DeviceTypeId,Manufacturer,Model,ModelSearch,SerialNumber,SerialSearch,IsImei,Problem,Accessories,
                             Appearance,EstimatedCost,MasterId,StatusId,ReceivedAt)
                            SELECT @number,@client,@type,@maker,@model,@modelSearch,@serial,@serialSearch,@imei,@problem,@accessories,
                                   @appearance,@estimate,@master,1,@received
                            WHERE @master IS NULL OR EXISTS
                              (SELECT 1 FROM Users WHERE Id=@master AND RoleId=2 AND Active=1)",
                        Database.P("@number", number), Database.P("@client", data.ClientId), Database.P("@type", data.DeviceTypeId),
                        Database.P("@maker", data.Manufacturer), Database.P("@model", data.Model),
                        Database.P("@modelSearch", data.Model.ToLowerInvariant()), Database.P("@serial", data.Serial),
                        Database.P("@serialSearch", data.Serial.ToLowerInvariant()), Database.P("@imei", data.IsImei),
                        Database.P("@problem", data.Problem), Database.P("@accessories", data.Accessories),
                        Database.P("@appearance", data.Appearance), Database.P("@estimate", data.Estimate), Database.P("@master", data.MasterId),
                        Database.P("@received", now));
                    Database.Require(created == 1, "Выбранный мастер больше недоступен.");
                    saved = (int)Database.Scalar(t, "SELECT last_insert_rowid()");
                    Database.Log(t, employee.Id, saved, "Создана заявка", number);
                    return;
                }

                bool intake = employee.AccessRole != Role.Master;
                bool work = employee.AccessRole != Role.Reception;
                string fields = intake ? @"ClientId=@client, DeviceTypeId=@type, Manufacturer=@maker, Model=@model, ModelSearch=@modelSearch,
                    SerialNumber=@serial, SerialSearch=@serialSearch, IsImei=@imei, Problem=@problem, Accessories=@accessories, Appearance=@appearance,
                    EstimatedCost=@estimate, MasterId=@master" : "";
                if (work)
                    fields += (intake ? ", " : "") + "Diagnosis=@diagnosis, WorkDone=@work";

                string masterCheck = intake ? @" AND (@master IS NULL OR MasterId=@master OR EXISTS
                    (SELECT 1 FROM Users WHERE Id=@master AND RoleId=2 AND Active=1))" : "";
                string before = intake ? (string)Database.Scalar(t, @"SELECT 'Клиент №' || ClientId ||
                      ', мастер №' || COALESCE(CAST(MasterId AS TEXT),'—') ||
                      ', стоимость ' || CASE WHEN EstimatedCost IS NULL THEN '—' ELSE printf('%.2f', EstimatedCost/100.0) END
                      FROM Repairs WHERE Id=@id", Database.P("@id", id)) : "";
                int count = Database.Write(t, "UPDATE Repairs SET " + fields + " WHERE Id=@id AND StatusId=@status" + masterCheck,
                    Database.P("@client", data.ClientId), Database.P("@type", data.DeviceTypeId), Database.P("@maker", data.Manufacturer),
                    Database.P("@model", data.Model), Database.P("@modelSearch", data.Model.ToLowerInvariant()), Database.P("@serial", data.Serial),
                    Database.P("@serialSearch", data.Serial.ToLowerInvariant()), Database.P("@imei", data.IsImei), Database.P("@problem", data.Problem),
                    Database.P("@accessories", data.Accessories), Database.P("@appearance", data.Appearance), Database.P("@estimate", data.Estimate),
                    Database.P("@master", data.MasterId), Database.P("@diagnosis", data.Diagnosis), Database.P("@work", data.WorkDone),
                    Database.P("@id", id), Database.P("@status", (int)status));
                Database.Require(count == 1, "Статус заявки или доступность мастера изменились. Откройте заявку заново.");
                string details = work ? "Сохранены диагностика и выполненные работы. " : "";
                if (intake)
                {
                    string masterName = data.MasterId.HasValue ? data.MasterId.Value.ToString() : "—";
                    string estimate = data.Estimate.HasValue ? data.Estimate.Value.ToString("0.00") : "—";
                    details += string.Format("Было: {0}. Стало: клиент №{1}, мастер №{2}, стоимость {3}.",
                        before, data.ClientId, masterName, estimate);
                }
                Database.Log(t, employee.Id, id, closed ? "Исправлена закрытая заявка" : "Изменена заявка", details);
            });
            return saved;
        }

        public static RepairStatus[] Next(RepairStatus status)
        {
            switch (status)
            {
                case RepairStatus.Accepted: return new[] { RepairStatus.Diagnosis };
                case RepairStatus.Diagnosis: return new[] { RepairStatus.Approval, RepairStatus.Declined };
                case RepairStatus.Approval: return new[] { RepairStatus.InProgress, RepairStatus.Declined };
                case RepairStatus.InProgress: return new[] { RepairStatus.Ready };
                default: return new RepairStatus[0];
            }
        }

        public static void ChangeStatus(Employee employee, int id, RepairStatus oldStatus, RepairStatus newStatus)
        {
            Database.Require(employee.AccessRole != Role.Manager);
            Database.Require(Array.IndexOf(Next(oldStatus), newStatus) >= 0, "Такой переход статуса недоступен.");
            Database.InTransaction(t =>
            {
                int count = Database.Write(t, "UPDATE Repairs SET StatusId=@new WHERE Id=@id AND StatusId=@old",
                    Database.P("@new", (int)newStatus), Database.P("@id", id), Database.P("@old", (int)oldStatus));
                Database.Require(count == 1, "Статус заявки изменился. Откройте её заново.");
                var oldName = Database.Scalar(t, "SELECT Name FROM Statuses WHERE Id=@id", Database.P("@id", (int)oldStatus));
                var newName = Database.Scalar(t, "SELECT Name FROM Statuses WHERE Id=@id", Database.P("@id", (int)newStatus));
                Database.Log(t, employee.Id, id, "Изменён статус", oldName + " → " + newName);
            });
        }

        public static void Issue(Employee employee, int id, RepairStatus status, decimal cost, int payment,
            int warranty, string notes, bool confirmed)
        {
            bool correction = status == RepairStatus.Issued;
            Database.Require(correction ? employee.AccessRole == Role.Manager : employee.AccessRole == Role.Reception);
            Database.Require(correction || status == RepairStatus.Ready || status == RepairStatus.Declined,
                "Выдача возможна после готовности или подтверждённого отказа.");
            Database.Require(confirmed, "Подтвердите выполненную операцию.");
            Database.InTransaction(t =>
            {
                if (!correction)
                {
                    int count = Database.Write(t, "UPDATE Repairs SET StatusId=6 WHERE Id=@id AND StatusId=@status",
                        Database.P("@id", id), Database.P("@status", (int)status));
                    Database.Require(count == 1, "Заявка уже выдана или её статус изменился.");
                }

                string before = correction ? (string)Database.Scalar(t, @"SELECT printf('%.2f', i.FinalCost/100.0) || ' руб., ' || p.Name ||
                     ', гарантия ' || i.WarrantyDays || ' дн.'  FROM Issues i
                     JOIN PaymentMethods p ON p.Id=i.PaymentId WHERE i.RepairId=@id", Database.P("@id", id)) : "";
                string sql = correction
                    ? @"UPDATE Issues SET FinalCost=@cost, PaymentId=@payment, WarrantyDays=@warranty, Notes=@notes
                        WHERE RepairId=@id"
                    : @"INSERT INTO Issues (RepairId,IssuedBy,FinalCost,PaymentId,WarrantyDays,Notes,IssuedAt)
                        VALUES (@id,@user,@cost,@payment,@warranty,@notes,@issuedAt)";
                int changed = Database.Write(t, sql, Database.P("@id", id), Database.P("@user", employee.Id), Database.P("@cost", cost),
                    Database.P("@payment", payment), Database.P("@warranty", warranty), Database.P("@notes", notes),
                    Database.P("@issuedAt", DateTime.Now));
                Database.Require(changed == 1, "Запись о выдаче недоступна.");
                string paymentName = (string)Database.Scalar(t, "SELECT Name FROM PaymentMethods WHERE Id=@id", Database.P("@id", payment));
                string details = string.Format("{0:0.00} руб., {1}, гарантия {2} дн.", cost, paymentName, warranty);
                if (correction)
                    details = "Было: " + before + " Стало: " + details;
                if (status == RepairStatus.Declined)
                    details = "Возврат после отказа. " + details;
                Database.Log(t, employee.Id, id, correction ? "Исправлены сведения о выдаче" : "Выдано устройство", details);
            });
        }
    }
}
