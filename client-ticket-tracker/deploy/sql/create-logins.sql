/*
    Creates the database and two SQL logins with different powers.
    Run ONCE on the server, as sa (DEPLOY.md step 5):

      sqlcmd -S localhost -U sa -C -i deploy/sql/create-logins.sql \
             -v AppPassword="..." MigratorPassword="..."

    Least privilege:
      tickettracker_migrator  db_owner. Used only by deploy.sh to apply migrations
                              (CREATE/ALTER TABLE). Its password is in deploy.env.
      tickettracker_app       Read and write rows only. Used by the running app.
                              If the app were ever compromised (e.g. SQL injection
                              somewhere), the attacker couldn't DROP tables or
                              change the schema with this login.
    Nobody uses sa day to day.
*/
IF DB_ID(N'TicketTracker') IS NULL
    CREATE DATABASE TicketTracker;
GO

IF SUSER_ID(N'tickettracker_migrator') IS NULL
    CREATE LOGIN tickettracker_migrator WITH PASSWORD = N'$(MigratorPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = TicketTracker;
IF SUSER_ID(N'tickettracker_app') IS NULL
    CREATE LOGIN tickettracker_app WITH PASSWORD = N'$(AppPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = TicketTracker;
GO

USE TicketTracker;
GO

IF USER_ID(N'tickettracker_migrator') IS NULL
    CREATE USER tickettracker_migrator FOR LOGIN tickettracker_migrator;
IF USER_ID(N'tickettracker_app') IS NULL
    CREATE USER tickettracker_app FOR LOGIN tickettracker_app;
GO

ALTER ROLE db_owner      ADD MEMBER tickettracker_migrator;
ALTER ROLE db_datareader ADD MEMBER tickettracker_app;
ALTER ROLE db_datawriter ADD MEMBER tickettracker_app;
GO

-- Check: expect two rows, with the roles above.
SELECT dp.name AS [user], r.name AS [role]
FROM sys.database_role_members AS rm
JOIN sys.database_principals AS dp ON dp.principal_id = rm.member_principal_id
JOIN sys.database_principals AS r  ON r.principal_id  = rm.role_principal_id
WHERE dp.name LIKE N'tickettracker%'
ORDER BY dp.name, r.name;
