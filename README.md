# SmartFleet - Fleet Management Microservices Platform

A complete fleet management platform built with .NET 8 microservices architecture, implementing JWT authentication, CQRS pattern, gRPC communication, and comprehensive observability.

## 🎯 Implementation Summary

This project implements a comprehensive Fleet Management Microservices Platform with all features from the requirements specification:
- **7 Microservices** with clear domain boundaries
- **JWT Authentication** with role-based access control
- **CQRS Pattern** for trip management
- **gRPC Streaming** for real-time telemetry
- **API Gateway** for centralized routing
- **Cross-Platform Support** with Docker and local development

## 🏗️ Architecture Overview

SmartFleet follows a modern microservices architecture with the API Gateway serving as the single entry point for all client requests.

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Mobile App    │    │   Web Dashboard │    │   Admin Panel   │
└─────────┬───────┘    └─────────┬───────┘    └─────────┬───────┘
          │                      │                      │
          └──────────────────────┼──────────────────────┘
                                 │
                    ┌─────────────▼─────────────┐
                    │      API Gateway         │
                    │    (Port 9000)           │
                    │  - Authentication        │
                    │  - Request Routing       │
                    │  - Load Balancing        │
                    └─────────────┬─────────────┘
                                 │
        ┌────────────────────────┼────────────────────────┐
        │                       │                        │
┌───────▼───────┐    ┌──────────▼──────────┐    ┌────────▼────────┐
│ Auth Service  │    │  Vehicle Service    │    │ Driver Service  │
│ (Port 5101)   │    │  (Port 5002)        │    │ (Port 5004)     │
│ - JWT Tokens  │    │ - Vehicle Metadata  │    │ - Driver Profiles│
│ - User Mgmt   │    │ - Status Tracking   │    │ - License Mgmt  │
│ - RBAC        │    │ - Maintenance       │    │ - Assignments   │
└───────────────┘    └─────────────────────┘    └─────────────────┘
        │                       │                        │
        │            ┌──────────▼──────────┐             │
        │            │   Trip Service      │             │
        │            │   (Port 5005)       │             │
        │            │ - CQRS Pattern      │             │
        │            │ - Trip Lifecycle    │             │
        │            │ - Route Management  │             │
        │            └──────────┬──────────┘             │
        │                       │                        │
        │            ┌──────────▼──────────┐             │
        │            │ Telemetry Service   │             │
        │            │   (Port 5003)       │             │
        │            │ - gRPC Streaming    │             │
        │            │ - Real-time Data    │             │
        │            │ - GPS Tracking      │             │
        │            └──────────┬──────────┘             │
        │                       │                        │
        │            ┌──────────▼──────────┐             │
        │            │ Notification Service│             │
        │            │   (Port 5006)       │             │
        │            │ - Email/SMS Alerts  │             │
        │            │ - Template System   │             │
        │            │ - Delivery Status   │             │
        │            └─────────────────────┘             │
        │                                                │
        └────────────────────────────────────────────────┘
                                 │
              ┌─────────────────────┐
              │   SQL Server        │
              │ - Separate DBs      │
              │ - Per Service       │
              │ - CQRS Read/Write   │
              └─────────────────────┘
```

## 🚀 Quick Start Guide



## 🌐 Service URLs & Access

### Service Endpoints
- **API Gateway**: http://localhost:9000 (Main entry point)
- **Auth Service**: http://localhost:5101
- **Vehicle Service**: http://localhost:5002
- **Telemetry Service**: http://localhost:5003 (gRPC)
- **Driver Service**: http://localhost:5004
- **Trip Service**: http://localhost:5005
- **Notification Service**: http://localhost:5006

### Default Users
- **Admin**: `admin` / `admin123`
- **Dispatcher**: `dispatcher` / `dispatcher123`
- **Driver**: `driver` / `driver123`

### Example API Usage
```bash
# Get authentication token
curl -X POST http://localhost:9000/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# Use token to access vehicles (replace YOUR_TOKEN)
curl -X GET http://localhost:9000/api/vehicles \
  -H "Authorization: Bearer YOUR_TOKEN"

# Create a new vehicle
curl -X POST http://localhost:9000/api/vehicles \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "registrationNumber": "ABC123",
    "make": "Toyota",
    "model": "Camry",
    "year": 2023,
    "vin": "1HGBH41JXMN109186",
    "fuelCapacity": 60.0,
    "fuelType": "Gasoline",
    "vehicleType": "Sedan",
    "capacity": 5
  }'
```

## 🏗️ Microservices Architecture

### 1. Authentication Service (Port 5101)
- **Purpose**: JWT-based authentication with role-based access control
- **Features**: User management, secure password hashing, token validation
- **Database**: AuthDb
- **Key Endpoints**: `/auth/token`, `/auth/users`, `/auth/roles`

### 2. Vehicle Service (Port 5002)
- **Purpose**: Comprehensive vehicle metadata management
- **Features**: Vehicle status tracking, maintenance scheduling, fuel monitoring
- **Database**: VehicleDb
- **Key Endpoints**: `/api/vehicles`, `/api/vehicles/{id}/status`, `/api/vehicles/{id}/maintenance`

### 3. Driver Service (Port 5004)
- **Purpose**: Complete driver profile management
- **Features**: License validation, driver-vehicle assignment, emergency contacts
- **Database**: DriverDb
- **Key Endpoints**: `/api/drivers`, `/api/drivers/{id}/assign-vehicle`, `/api/drivers/{id}/license`

### 4. Trip Service (Port 5005) - CQRS Implementation
- **Purpose**: Trip lifecycle management with read/write separation
- **Features**: Real-time trip status, route management, CQRS pattern
- **Databases**: TripDb (write), TripReadDb (read)
- **Key Endpoints**: `/api/trips`, `/api/trips/{id}/status`, `/api/trips/{id}/complete`

### 5. Telemetry Service (Port 5003) - gRPC
- **Purpose**: Real-time vehicle data ingestion via gRPC streaming
- **Features**: GPS tracking, speed monitoring, fuel level tracking
- **Database**: TelemetryDb
- **Protocol**: gRPC with streaming support

### 6. Notification Service (Port 5006)
- **Purpose**: Email and SMS notification system
- **Features**: Template-based messaging, delivery status tracking
- **Database**: NotificationDb
- **Key Endpoints**: `/api/notifications/email`, `/api/notifications/sms`

### 7. API Gateway (Port 9000)
- **Purpose**: Centralized routing, authentication, and load balancing
- **Features**: Request routing, JWT validation, CORS handling
- **Technology**: Ocelot API Gateway

## 🔄 Data Flow Architecture

### Request Flow
1. **Client Request** → API Gateway (Port 9000)
2. **Authentication** → Auth Service validates JWT token
3. **Authorization** → Role-based access control check
4. **Routing** → Request forwarded to appropriate microservice
5. **Processing** → Business logic execution
6. **Response** → Data returned through API Gateway


#
## 🔧 Key Features Implemented

### ✅ Authentication & Authorization
- JWT-based authentication across all services
- Role-based access control (Admin, Dispatcher, Driver)
- Secure password hashing with salt
- Token validation and refresh

### ✅ Vehicle Management
- Complete vehicle metadata (registration, VIN, make, model, year)
- Fuel capacity and type tracking
- Maintenance scheduling and history
- Vehicle status management (Available, InUse, Maintenance, OutOfService)

### ✅ Driver Management
- Comprehensive driver profiles
- License management with expiry tracking
- Driver-vehicle assignment system
- Emergency contact information
- Performance rating system

### ✅ Trip Management (CQRS)
- Separate read and write models
- Real-time trip status updates
- Route planning and optimization
- Trip scheduling and completion tracking
- Integration with telemetry for real-time updates

### ✅ Real-time Telemetry (gRPC)
- Streaming telemetry data ingestion
- GPS location tracking
- Speed and fuel level monitoring
- Persistent data storage
- Real-time alerts and notifications

### ✅ Notification System
- Email and SMS notification support
- Maintenance due alerts
- Route deviation warnings
- Template-based messaging
- Notification history and delivery status

### ✅ Observability & Monitoring
- Health check endpoints for all services
- Structured logging with Serilog
- Correlation ID tracking
- Service health monitoring
- Database connectivity checks




### Key Items to Customize

#### 1. Database Connection Strings
**Files to update:**
- `src/Auth/appsettings.json`
- `src/Vehicle/appsettings.json`
- `src/Driver/appsettings.json`
- `src/Trip/appsettings.json`
- `src/Notification/appsettings.json`
- `SmartFleet.Telemetry/appsettings.json`

#### 2. JWT Secret Keys
**Files to update:**
- All `appsettings.json` files
- Replace `"JWT_SECRET_KEY"` with a strong secret




### Core Requirements
1. **✅ Microservices Architecture** - 7 independent services with clear boundaries
2. **✅ JWT Authentication** - Role-based access control (Admin, Dispatcher, Driver)
3. **✅ Vehicle Management** - Complete metadata, status tracking, and maintenance
4. **✅ Driver Management** - Profiles, licenses, assignments, and emergency contacts
5. **✅ Trip Management** - CQRS pattern with read/write separation
6. **✅ Telemetry Service** - gRPC streaming with real-time data persistence
7. **✅ Notification System** - Email/SMS alerts with template support
8. **✅ API Gateway** - Centralized routing, authentication, and load balancing

### Technical Requirements
9. **✅ Database per Service** - Separate SQL Server databases for each service
10. **✅ CQRS Implementation** - Trip service with separate read/write models
11. **✅ gRPC Communication** - Streaming telemetry data with type safety
12. **✅ RESTful APIs** - External client communication via Web APIs
13. **✅ Observability** - Health checks, structured logging, and monitoring
14. **✅ Docker Containerization** - Complete containerized deployment
15. **✅ Idempotency** - Implemented in trip creation and updates

## 🏆 Summary

**SmartFleet** is a production-ready Fleet Management Microservices Platform that fully implements all requirements from the problem statement. The system demonstrates modern architecture patterns, best practices, and provides a solid foundation for enterprise fleet management operations.
