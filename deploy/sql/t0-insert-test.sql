-- T0.1 / T0.2 scratch test: 100k synthetic rows, timed. Safe to re-run; uses its own database.
SET NOCOUNT ON;
IF DB_ID('LabScratch') IS NOT NULL BEGIN ALTER DATABASE LabScratch SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE LabScratch; END
CREATE DATABASE LabScratch;
GO
USE LabScratch;
GO
-- Same shape as the id strategy: bigint IDENTITY clustered PK + GUID PublicId with a unique index.
-- Table A: random GUIDs (NEWID). Table B: sequential GUIDs (NEWSEQUENTIALID), the SQL Server ordering reference.
CREATE TABLE dbo.TicketRandom (
    Id bigint IDENTITY PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL DEFAULT NEWID(),
    PassengerName nvarchar(60) NOT NULL,
    SeatNo int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE UNIQUE INDEX UX_TicketRandom_PublicId ON dbo.TicketRandom(PublicId);

CREATE TABLE dbo.TicketSequential (
    Id bigint IDENTITY PRIMARY KEY,
    PublicId uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID(),
    PassengerName nvarchar(60) NOT NULL,
    SeatNo int NOT NULL,
    CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
CREATE UNIQUE INDEX UX_TicketSequential_PublicId ON dbo.TicketSequential(PublicId);
GO
DECLARE @t0 datetime2 = SYSUTCDATETIME();
;WITH n AS (SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT dbo.TicketRandom (PassengerName, SeatNo) SELECT CONCAT('Passenger ', i), i % 80 + 1 FROM n;
PRINT CONCAT('Random GUID insert, 100k rows: ', DATEDIFF(millisecond, @t0, SYSUTCDATETIME()), ' ms');

SET @t0 = SYSUTCDATETIME();
;WITH n AS (SELECT TOP (100000) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT dbo.TicketSequential (PassengerName, SeatNo) SELECT CONCAT('Passenger ', i), i % 80 + 1 FROM n;
PRINT CONCAT('Sequential GUID insert, 100k rows: ', DATEDIFF(millisecond, @t0, SYSUTCDATETIME()), ' ms');
GO
-- Fragmentation of the PublicId index (the ADR 2 evidence). Lower avg_fragmentation is better.
SELECT OBJECT_NAME(ips.object_id) AS TableName, i.name AS IndexName,
       ips.avg_fragmentation_in_percent, ips.page_count, ips.avg_page_space_used_in_percent
FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'DETAILED') ips
JOIN sys.indexes i ON i.object_id = ips.object_id AND i.index_id = ips.index_id
WHERE ips.index_level = 0 AND OBJECT_NAME(ips.object_id) LIKE 'Ticket%';
GO
SELECT COUNT(*) AS RandomRows FROM dbo.TicketRandom;
SELECT COUNT(*) AS SequentialRows FROM dbo.TicketSequential;
SELECT TOP 5 Id, PublicId, PassengerName, SeatNo FROM dbo.TicketSequential ORDER BY Id;
