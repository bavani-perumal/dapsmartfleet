-- SmartFleet Database Initialization Script
-- This script creates all required databases if they don't exist

USE master;
GO

-- Create AuthDb if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'AuthDb')
BEGIN
    CREATE DATABASE AuthDb;
    PRINT 'AuthDb created successfully';
END
ELSE
BEGIN
    PRINT 'AuthDb already exists';
END
GO

-- Create VehicleDb if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'VehicleDb')
BEGIN
    CREATE DATABASE VehicleDb;
    PRINT 'VehicleDb created successfully';
END
ELSE
BEGIN
    PRINT 'VehicleDb already exists';
END
GO

-- Create DriverDb if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'DriverDb')
BEGIN
    CREATE DATABASE DriverDb;
    PRINT 'DriverDb created successfully';
END
ELSE
BEGIN
    PRINT 'DriverDb already exists';
END
GO

-- Create TripDb if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TripDb')
BEGIN
    CREATE DATABASE TripDb;
    PRINT 'TripDb created successfully';
END
ELSE
BEGIN
    PRINT 'TripDb already exists';
END
GO

-- Create TripReadDb if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TripReadDb')
BEGIN
    CREATE DATABASE TripReadDb;
    PRINT 'TripReadDb created successfully';
END
ELSE
BEGIN
    PRINT 'TripReadDb already exists';
END
GO

-- Create NotificationDb if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'NotificationDb')
BEGIN
    CREATE DATABASE NotificationDb;
    PRINT 'NotificationDb created successfully';
END
ELSE
BEGIN
    PRINT 'NotificationDb already exists';
END
GO

-- Create TelemetryDb if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TelemetryDb')
BEGIN
    CREATE DATABASE TelemetryDb;
    PRINT 'TelemetryDb created successfully';
END
ELSE
BEGIN
    PRINT 'TelemetryDb already exists';
END
GO

PRINT 'All SmartFleet databases are ready!';
