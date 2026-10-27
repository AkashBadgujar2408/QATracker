-- Creates the QA Tracker database if it does not exist. Safe to run repeatedly.
-- Run with: sqlcmd -S .\SQLEXPRESS -E -i database\scripts\000_CreateDatabase.sql
IF DB_ID(N'QaTracker') IS NULL
BEGIN
    CREATE DATABASE QaTracker;
END
GO
