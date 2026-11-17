-- Repeatable init: one database, one schema and one SQL login per service.
-- Passwords come from sqlcmd scripting variables ($(NAME)), which sqlcmd reads from environment variables.
-- Safe to re-run: existing logins get their password reset, existing grants are re-applied.
SET NOCOUNT ON;
IF DB_ID('RailwayLab') IS NULL CREATE DATABASE RailwayLab;
GO
USE RailwayLab;
GO
DECLARE @svc TABLE (Name sysname PRIMARY KEY, Password nvarchar(128) NOT NULL);
INSERT @svc (Name, Password) VALUES
    ('schedule',    N'$(SCHEDULE_DB_PASSWORD)'),
    ('reservation', N'$(RESERVATION_DB_PASSWORD)'),
    ('payment',     N'$(PAYMENT_DB_PASSWORD)'),
    ('analytics',   N'$(ANALYTICS_DB_PASSWORD)'),
    ('helpdesk',    N'$(HELPDESK_DB_PASSWORD)'),
    ('auth',        N'$(AUTH_DB_PASSWORD)');

DECLARE @n sysname, @p nvarchar(128), @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Name, Password FROM @svc;
OPEN c;
FETCH NEXT FROM c INTO @n, @p;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @login sysname = @n + N'_svc';
    DECLARE @pw nvarchar(300) = REPLACE(@p, N'''', N'''''');

    IF SUSER_ID(@login) IS NULL
        SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = N''' + @pw + N''', CHECK_POLICY = OFF;';
    ELSE
        SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = N''' + @pw + N''';';
    EXEC (@sql);

    IF SCHEMA_ID(@n) IS NULL
    BEGIN
        SET @sql = N'CREATE SCHEMA ' + QUOTENAME(@n) + N' AUTHORIZATION dbo;';
        EXEC (@sql);
    END

    IF USER_ID(@login) IS NULL
    BEGIN
        SET @sql = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N' WITH DEFAULT_SCHEMA = ' + QUOTENAME(@n) + N';';
        EXEC (@sql);
    END

    -- Full control of its own schema; object creation rights (for EF migrations) only land in schemas it controls.
    SET @sql = N'GRANT CONTROL ON SCHEMA::' + QUOTENAME(@n) + N' TO ' + QUOTENAME(@login) + N';'
             + N'GRANT CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE FUNCTION, CREATE TYPE TO ' + QUOTENAME(@login) + N';';
    EXEC (@sql);

    FETCH NEXT FROM c INTO @n, @p;
END
CLOSE c;
DEALLOCATE c;
GO
