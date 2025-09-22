#!/bin/bash

# Script to create EF Core migrations for all services

echo "Creating migrations for SmartFleet services..."

# Vehicle Service
echo "Creating Vehicle service migration..."
cd src/Vehicle
dotnet ef migrations add EnhancedVehicleModel --output-dir Migrations
cd ../..

# Driver Service  
echo "Creating Driver service migration..."
cd src/Driver
dotnet ef migrations add EnhancedDriverModel --output-dir Migrations
cd ../..

# Trip Service
echo "Creating Trip service migration..."
cd src/Trip
dotnet ef migrations add EnhancedTripModel --output-dir Migrations
cd ../..

# Auth Service
echo "Creating Auth service migration..."
cd src/Auth
dotnet ef migrations add UserManagement --output-dir Migrations
cd ../..

# Notification Service
echo "Creating Notification service migration..."
cd src/Notification
dotnet ef migrations add NotificationSystem --output-dir Migrations
cd ../..

# Telemetry Service
echo "Creating Telemetry service migration..."
cd SmartFleet.Telemetry
dotnet ef migrations add TelemetryPersistence --output-dir Migrations
cd ..

echo "All migrations created successfully!"
echo "To apply migrations, run: docker-compose up --build"
