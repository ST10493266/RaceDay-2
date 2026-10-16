/* =====================================================================
   RaceDay - Part 1, Section C: SQL Server database script
   Target: SQL Server (tested design for SSMS on a clean instance)

   What this script does
     1. Creates the RaceDayDb database (if it does not exist)
     2. Drops and re-creates all six tables (safe to re-run)
     3. Seeds realistic sample data:
          - 2 Organisers, 3 Participants
          - 4 Events (3 upcoming + 1 completed), with categories for each
          - 7 Enrolments and 3 Results (for the completed event)

   Sample login for EVERY seeded user:   Password123!
   (PasswordHash holds a BCrypt hash - the plain password is never stored.)

   Table / column / constraint names deliberately mirror what the Entity
   Framework Core model in src/RaceDay.Api/Data/RaceDayDbContext.cs produces,
   so the script and the code-first schema match.
   ===================================================================== */

IF DB_ID(N'RaceDayDb') IS NULL
    CREATE DATABASE RaceDayDb;
GO

USE RaceDayDb;
GO

/* ---------------------------------------------------------------------
   Clean-up (reverse dependency order) so the script can be re-run
   --------------------------------------------------------------------- */
DROP TABLE IF EXISTS dbo.Results;
DROP TABLE IF EXISTS dbo.Enrolments;
DROP TABLE IF EXISTS dbo.Categories;
DROP TABLE IF EXISTS dbo.Events;
DROP TABLE IF EXISTS dbo.Users;
DROP TABLE IF EXISTS dbo.Roles;
GO

/* ---------------------------------------------------------------------
   1. Roles            (Role 1 --- * User)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Roles
(
    RoleId   INT IDENTITY(1,1) NOT NULL,
    RoleName NVARCHAR(50)      NOT NULL,
    CONSTRAINT PK_Roles PRIMARY KEY (RoleId)
);
CREATE UNIQUE INDEX IX_Roles_RoleName ON dbo.Roles (RoleName);
GO

/* ---------------------------------------------------------------------
   2. Users            (User[Organiser] 1 --- * Event,
                        User[Participant] 1 --- * Enrolment)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Users
(
    UserId       INT IDENTITY(1,1) NOT NULL,
    FirstName    NVARCHAR(100)     NOT NULL,
    LastName     NVARCHAR(100)     NOT NULL,
    Email        NVARCHAR(256)     NOT NULL,
    PasswordHash NVARCHAR(255)     NOT NULL,
    PhoneNumber  NVARCHAR(20)      NULL,
    DateOfBirth  DATE              NULL,
    RoleId       INT               NOT NULL,
    CreatedAt    DATETIME2         NOT NULL
        CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Users PRIMARY KEY (UserId),
    CONSTRAINT FK_Users_Roles_RoleId FOREIGN KEY (RoleId)
        REFERENCES dbo.Roles (RoleId) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX IX_Users_Email  ON dbo.Users (Email);
CREATE INDEX        IX_Users_RoleId ON dbo.Users (RoleId);
GO

/* ---------------------------------------------------------------------
   3. Events           (Event 1 --- * Category, Event 1 --- * Enrolment)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Events
(
    EventId     INT IDENTITY(1,1) NOT NULL,
    Name        NVARCHAR(150)     NOT NULL,
    Description NVARCHAR(1000)    NOT NULL,
    EventDate   DATETIME2         NOT NULL,
    Location    NVARCHAR(200)     NOT NULL,
    DistanceKm  DECIMAL(6,2)      NOT NULL,
    EventType   NVARCHAR(10)      NOT NULL,
    OrganiserId INT               NOT NULL,
    CreatedAt   DATETIME2         NOT NULL
        CONSTRAINT DF_Events_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Events PRIMARY KEY (EventId),
    CONSTRAINT FK_Events_Users_OrganiserId FOREIGN KEY (OrganiserId)
        REFERENCES dbo.Users (UserId) ON DELETE NO ACTION,
    CONSTRAINT CK_Events_EventType  CHECK (EventType IN (N'Run', N'Walk', N'Cycle')),
    CONSTRAINT CK_Events_DistanceKm CHECK (DistanceKm > 0)
);
CREATE INDEX IX_Events_OrganiserId ON dbo.Events (OrganiserId);
GO

/* ---------------------------------------------------------------------
   4. Categories       (Category 1 --- * Enrolment)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Categories
(
    CategoryId INT IDENTITY(1,1) NOT NULL,
    EventId    INT               NOT NULL,
    Name       NVARCHAR(100)     NOT NULL,
    MinAge     INT               NULL,
    MaxAge     INT               NULL,
    DistanceKm DECIMAL(6,2)      NULL,
    CONSTRAINT PK_Categories PRIMARY KEY (CategoryId),
    CONSTRAINT FK_Categories_Events_EventId FOREIGN KEY (EventId)
        REFERENCES dbo.Events (EventId) ON DELETE CASCADE,
    CONSTRAINT CK_Categories_AgeRange
        CHECK (MinAge IS NULL OR MaxAge IS NULL OR MinAge <= MaxAge)
);
CREATE UNIQUE INDEX IX_Categories_EventId_Name ON dbo.Categories (EventId, Name);
GO

/* ---------------------------------------------------------------------
   5. Enrolments       (resolves the many-to-many between User and Event;
                        also records the chosen Category)
                        Enrolment 1 --- 0..1 Result
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Enrolments
(
    EnrolmentId INT IDENTITY(1,1) NOT NULL,
    UserId      INT               NOT NULL,
    EventId     INT               NOT NULL,
    CategoryId  INT               NOT NULL,
    EnrolledAt  DATETIME2         NOT NULL
        CONSTRAINT DF_Enrolments_EnrolledAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Enrolments PRIMARY KEY (EnrolmentId),
    CONSTRAINT FK_Enrolments_Users_UserId FOREIGN KEY (UserId)
        REFERENCES dbo.Users (UserId) ON DELETE NO ACTION,
    -- Deleting an event removes its enrolments (single cascade path).
    CONSTRAINT FK_Enrolments_Events_EventId FOREIGN KEY (EventId)
        REFERENCES dbo.Events (EventId) ON DELETE CASCADE,
    -- NO ACTION here avoids SQL Server's "multiple cascade paths" error.
    CONSTRAINT FK_Enrolments_Categories_CategoryId FOREIGN KEY (CategoryId)
        REFERENCES dbo.Categories (CategoryId) ON DELETE NO ACTION
);
-- A participant may enter a given event only once.
CREATE UNIQUE INDEX IX_Enrolments_UserId_EventId ON dbo.Enrolments (UserId, EventId);
CREATE INDEX        IX_Enrolments_EventId        ON dbo.Enrolments (EventId);
CREATE INDEX        IX_Enrolments_CategoryId     ON dbo.Enrolments (CategoryId);
GO

/* ---------------------------------------------------------------------
   6. Results          (one result per enrolment)
   --------------------------------------------------------------------- */
CREATE TABLE dbo.Results
(
    ResultId    INT IDENTITY(1,1) NOT NULL,
    EnrolmentId INT               NOT NULL,
    FinishTime  TIME(0)           NOT NULL,
    Position    INT               NOT NULL,
    RecordedAt  DATETIME2         NOT NULL
        CONSTRAINT DF_Results_RecordedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Results PRIMARY KEY (ResultId),
    CONSTRAINT FK_Results_Enrolments_EnrolmentId FOREIGN KEY (EnrolmentId)
        REFERENCES dbo.Enrolments (EnrolmentId) ON DELETE CASCADE,
    CONSTRAINT CK_Results_Position CHECK (Position > 0)
);
CREATE UNIQUE INDEX IX_Results_EnrolmentId ON dbo.Results (EnrolmentId);
GO

/* =====================================================================
   SEED DATA
   Explicit identity values are used so foreign keys below are readable.
   ===================================================================== */

-- Roles ---------------------------------------------------------------
SET IDENTITY_INSERT dbo.Roles ON;
INSERT INTO dbo.Roles (RoleId, RoleName) VALUES
    (1, N'Organiser'),
    (2, N'Participant');
SET IDENTITY_INSERT dbo.Roles OFF;
GO

-- Users: 2 Organisers + 3 Participants --------------------------------
-- Every PasswordHash below is a BCrypt hash of:  Password123!
SET IDENTITY_INSERT dbo.Users ON;
INSERT INTO dbo.Users (UserId, FirstName, LastName, Email, PasswordHash, PhoneNumber, DateOfBirth, RoleId) VALUES
    (1, N'Thandi', N'Mokoena',      N'thandi.mokoena@raceday.co.za', N'$2a$11$/2bk63LUxaypuQgBlgV8eOqvB7kQI9MrD4x6fuDykoSK/RbRCYeVy', N'0821234567', '1985-04-12', 1),
    (2, N'Pieter', N'van der Merwe', N'pieter.vdm@raceday.co.za',      N'$2a$11$/2bk63LUxaypuQgBlgV8eOqvB7kQI9MrD4x6fuDykoSK/RbRCYeVy', N'0839876543', '1979-11-30', 1),
    (3, N'Lerato', N'Dlamini',      N'lerato.dlamini@example.com',    N'$2a$11$/2bk63LUxaypuQgBlgV8eOqvB7kQI9MrD4x6fuDykoSK/RbRCYeVy', N'0715551234', '1998-07-21', 2),
    (4, N'Sipho',  N'Ndlovu',       N'sipho.ndlovu@example.com',      N'$2a$11$/2bk63LUxaypuQgBlgV8eOqvB7kQI9MrD4x6fuDykoSK/RbRCYeVy', N'0768889999', '2006-02-03', 2),
    (5, N'Aisha',  N'Patel',        N'aisha.patel@example.com',       N'$2a$11$/2bk63LUxaypuQgBlgV8eOqvB7kQI9MrD4x6fuDykoSK/RbRCYeVy', NULL,          '1990-09-15', 2);
SET IDENTITY_INSERT dbo.Users OFF;
GO

-- Events: 3 upcoming + 1 completed -------------------------------------
SET IDENTITY_INSERT dbo.Events ON;
INSERT INTO dbo.Events (EventId, Name, Description, EventDate, Location, DistanceKm, EventType, OrganiserId) VALUES
    (1, N'Cape Town Sunrise 10km',
        N'A scenic out-and-back road run along the Sea Point Promenade at sunrise.',
        '2027-03-13T06:30:00', N'Sea Point Promenade, Cape Town', 10.00, N'Run', 1),
    (2, N'Winelands Cycle Classic',
        N'A rolling road ride through the Stellenbosch wine estates with a fully supported route.',
        '2027-04-17T07:00:00', N'Stellenbosch, Western Cape', 50.00, N'Cycle', 1),
    (3, N'Joburg Park Walk for Charity',
        N'A family-friendly charity walk through Delta Park. All proceeds go to local schools.',
        '2027-05-08T08:00:00', N'Delta Park, Johannesburg', 5.00, N'Walk', 2),
    (4, N'Durban Beachfront Fun Run',
        N'A flat, fast 10km along the Golden Mile. This event has already taken place.',
        '2026-08-22T07:00:00', N'Golden Mile, Durban', 10.00, N'Run', 2);
SET IDENTITY_INSERT dbo.Events OFF;
GO

-- Categories: several per event ------------------------------------------
SET IDENTITY_INSERT dbo.Categories ON;
INSERT INTO dbo.Categories (CategoryId, EventId, Name, MinAge, MaxAge, DistanceKm) VALUES
    -- Event 1: Cape Town Sunrise 10km
    (1, 1, N'Under 20',     NULL, 19,   10.00),
    (2, 1, N'Senior',       20,   59,   10.00),
    (3, 1, N'Masters 60+',  60,   NULL, 10.00),
    -- Event 2: Winelands Cycle Classic
    (4, 2, N'50km Challenge', NULL, NULL, 50.00),
    (5, 2, N'25km Social',    NULL, NULL, 25.00),
    -- Event 3: Joburg Park Walk
    (6, 3, N'5km Walk',     NULL, NULL, 5.00),
    (7, 3, N'2km Family',   NULL, NULL, 2.00),
    -- Event 4: Durban Beachfront Fun Run
    (8, 4, N'Under 20',     NULL, 19,   10.00),
    (9, 4, N'Senior',       20,   NULL, 10.00);
SET IDENTITY_INSERT dbo.Categories OFF;
GO

-- Enrolments ------------------------------------------------------------
SET IDENTITY_INSERT dbo.Enrolments ON;
INSERT INTO dbo.Enrolments (EnrolmentId, UserId, EventId, CategoryId) VALUES
    (1, 3, 1, 2),   -- Lerato  -> Cape Town Sunrise 10km, Senior
    (2, 4, 1, 1),   -- Sipho   -> Cape Town Sunrise 10km, Under 20
    (3, 3, 2, 5),   -- Lerato  -> Winelands Cycle Classic, 25km Social
    (4, 5, 3, 6),   -- Aisha   -> Joburg Park Walk, 5km Walk
    (5, 3, 4, 9),   -- Lerato  -> Durban Beachfront Fun Run, Senior
    (6, 4, 4, 8),   -- Sipho   -> Durban Beachfront Fun Run, Under 20
    (7, 5, 4, 9);   -- Aisha   -> Durban Beachfront Fun Run, Senior
SET IDENTITY_INSERT dbo.Enrolments OFF;
GO

-- Results (completed event only) -------------------------------------------
SET IDENTITY_INSERT dbo.Results ON;
INSERT INTO dbo.Results (ResultId, EnrolmentId, FinishTime, Position) VALUES
    (1, 6, '00:44:30', 1),   -- Sipho
    (2, 5, '00:47:15', 2),   -- Lerato
    (3, 7, '00:52:41', 3);   -- Aisha
SET IDENTITY_INSERT dbo.Results OFF;
GO

/* Quick sanity checks (optional) */
SELECT 'Roles'      AS TableName, COUNT(*) AS RecordCount FROM dbo.Roles
UNION ALL SELECT 'Users',      COUNT(*) FROM dbo.Users
UNION ALL SELECT 'Events',     COUNT(*) FROM dbo.Events
UNION ALL SELECT 'Categories', COUNT(*) FROM dbo.Categories
UNION ALL SELECT 'Enrolments', COUNT(*) FROM dbo.Enrolments
UNION ALL SELECT 'Results',    COUNT(*) FROM dbo.Results;
GO
