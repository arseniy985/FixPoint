CREATE TABLE Users (
    Id INTEGER PRIMARY KEY,
    Login TEXT NOT NULL COLLATE NOCASE UNIQUE,
    FullName TEXT NOT NULL,
    RoleId INTEGER NOT NULL CHECK (RoleId BETWEEN 1 AND 3),
    Salt BLOB NOT NULL CHECK (length(Salt)=16),
    PasswordHash BLOB NOT NULL CHECK (length(PasswordHash)=32),
    Active BOOLEAN NOT NULL DEFAULT 1 CHECK (Active IN (0,1))
);

CREATE TABLE Clients (
    Id INTEGER PRIMARY KEY,
    LastName TEXT NOT NULL,
    FirstName TEXT NOT NULL,
    MiddleName TEXT NOT NULL DEFAULT '',
    SearchName TEXT NOT NULL,
    Phone TEXT NOT NULL,
    PhoneDigits TEXT NOT NULL UNIQUE,
    Email TEXT NOT NULL DEFAULT '',
    Notes TEXT NOT NULL DEFAULT '',
    CreatedAt DATETIME NOT NULL,
    CHECK (length(PhoneDigits) BETWEEN 10 AND 15 AND PhoneDigits NOT GLOB '*[^0-9]*')
);

CREATE TABLE Statuses (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE);
CREATE TABLE DeviceTypes (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE);
CREATE TABLE PaymentMethods (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE);

CREATE TABLE RequestNumber (
    RequestYear INTEGER PRIMARY KEY,
    LastUsed INTEGER NOT NULL CHECK (LastUsed BETWEEN 0 AND 99999)
);

CREATE TABLE Repairs (
    Id INTEGER PRIMARY KEY,
    Number TEXT NOT NULL COLLATE NOCASE UNIQUE,
    ClientId INTEGER NOT NULL REFERENCES Clients(Id),
    DeviceTypeId INTEGER NOT NULL REFERENCES DeviceTypes(Id),
    Manufacturer TEXT NOT NULL,
    Model TEXT NOT NULL,
    ModelSearch TEXT NOT NULL,
    SerialNumber TEXT NOT NULL DEFAULT '',
    SerialSearch TEXT NOT NULL DEFAULT '',
    IsImei BOOLEAN NOT NULL DEFAULT 0 CHECK (IsImei IN (0,1)),
    Problem TEXT NOT NULL,
    Accessories TEXT NOT NULL,
    Appearance TEXT NOT NULL DEFAULT '',
    EstimatedCost MONEY_INT CHECK (EstimatedCost BETWEEN 0 AND 999999999999),
    MasterId INTEGER REFERENCES Users(Id),
    StatusId INTEGER NOT NULL REFERENCES Statuses(Id),
    Diagnosis TEXT NOT NULL DEFAULT '',
    WorkDone TEXT NOT NULL DEFAULT '',
    ReceivedAt DATETIME NOT NULL,
    CHECK (IsImei=0 OR (length(SerialNumber)=15 AND SerialNumber NOT GLOB '*[^0-9]*'))
);

CREATE TABLE Issues (
    RepairId INTEGER PRIMARY KEY REFERENCES Repairs(Id),
    IssuedBy INTEGER NOT NULL REFERENCES Users(Id),
    IssuedAt DATETIME NOT NULL,
    FinalCost MONEY_INT NOT NULL CHECK (FinalCost BETWEEN 0 AND 999999999999),
    PaymentId INTEGER NOT NULL REFERENCES PaymentMethods(Id),
    WarrantyDays INTEGER NOT NULL CHECK (WarrantyDays BETWEEN 0 AND 365),
    Notes TEXT NOT NULL DEFAULT '',
    CHECK (PaymentId<>4 OR FinalCost=0)
);

CREATE TABLE Audit (
    Id INTEGER PRIMARY KEY,
    LoggedAt DATETIME NOT NULL,
    UserId INTEGER REFERENCES Users(Id),
    RepairId INTEGER REFERENCES Repairs(Id),
    Action TEXT NOT NULL,
    Details TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IX_Repairs_Client ON Repairs(ClientId, Id DESC);
CREATE INDEX IX_Repairs_Received ON Repairs(ReceivedAt, Id DESC);
CREATE INDEX IX_Repairs_Status ON Repairs(StatusId, Id DESC);
CREATE INDEX IX_Audit_Repair ON Audit(RepairId, Id DESC);

INSERT INTO Statuses (Id, Name) VALUES
    (1,'Принято'), (2,'Диагностика'), (3,'Согласование'), (4,'В ремонте'),
    (5,'Готово'), (6,'Выдано'), (7,'Отказ');
INSERT INTO DeviceTypes (Name) VALUES ('Смартфон'), ('Ноутбук'), ('Планшет'), ('Прочее');
INSERT INTO PaymentMethods (Id, Name) VALUES
    (1,'Наличные'), (2,'Банковская карта'), (3,'Перевод'), (4,'Без оплаты');
INSERT INTO Users (Login, FullName, RoleId, Salt, PasswordHash) VALUES
    ('priem', 'Иванов Алексей Петрович', 1, X'768eb0124a9441e25b96853f93df4d8f', X'aa01386371221f33214c5e4018356f315274315cce735acc2be0d52b7d100b45'),
    ('master', 'Смирнов Олег Сергеевич', 2, X'3688d41132ba817ad0db4506766c3fd8', X'53247137db658104492720208bc2a64647a486f03679841653fa6efb18c06f20'),
    ('boss', 'Петрова Анна Игоревна', 3, X'6d39d3360be0a1c3aa7d86d8f4a3493f', X'bf4b131326b44cb6f11cd7976bcd9840c026b14cdb4ce04c616e802eaeff8afe');
PRAGMA user_version = 1;
