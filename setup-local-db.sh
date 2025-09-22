#!/bin/bash

# Setup Local Database for SmartFleet
# This script configures all services to use the local Docker SQL Server

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🗄️ Setting up Local Database for SmartFleet${NC}"
echo "================================================"

# TODO_CUSTOMIZE: Update this connection string for your environment
# Search for "TODO_CUSTOMIZE" to find all places that need customization
LOCAL_CONNECTION="Server=localhost,1433;Database={DB_NAME};User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true"

# Update Auth Service
echo -e "${BLUE}Updating Auth Service...${NC}"
cat > src/Auth/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  // TODO_CUSTOMIZE: Replace with your JWT secret key
  "JWT_SECRET": "B38A16D1-C130-41A4-A899-2392F791407430F94D47-1582-4417-879A-D43BB5689E90",
  "ConnectionStrings": {
    // TODO_CUSTOMIZE: Update connection string for your SQL Server instance
    "DefaultConnection": "Server=localhost,1433;Database=AuthDb;User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
EOF

# Update Vehicle Service
echo -e "${BLUE}Updating Vehicle Service...${NC}"
cat > src/Vehicle/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  // TODO_CUSTOMIZE: Replace with your JWT secret key
  "JWT_SECRET": "B38A16D1-C130-41A4-A899-2392F791407430F94D47-1582-4417-879A-D43BB5689E90",
  "ConnectionStrings": {
    // TODO_CUSTOMIZE: Update connection string for your SQL Server instance
    "DefaultConnection": "Server=localhost,1433;Database=VehicleDb;User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
EOF

# Update Driver Service
echo -e "${BLUE}Updating Driver Service...${NC}"
cat > src/Driver/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "JWT_SECRET": "B38A16D1-C130-41A4-A899-2392F791407430F94D47-1582-4417-879A-D43BB5689E90",
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=DriverDb;User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
EOF

# Update Trip Service
echo -e "${BLUE}Updating Trip Service...${NC}"
cat > src/Trip/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "JWT_SECRET": "B38A16D1-C130-41A4-A899-2392F791407430F94D47-1582-4417-879A-D43BB5689E90",
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=TripDb;User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true",
    "ReadConnection": "Server=localhost,1433;Database=TripReadDb;User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
EOF

# Update Notification Service
echo -e "${BLUE}Updating Notification Service...${NC}"
cat > src/Notification/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "JWT_SECRET": "B38A16D1-C130-41A4-A899-2392F791407430F94D47-1582-4417-879A-D43BB5689E90",
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=NotificationDb;User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
EOF

# Update Telemetry Service
echo -e "${BLUE}Updating Telemetry Service...${NC}"
cat > SmartFleet.Telemetry/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Kestrel": {
    "EndpointDefaults": {
      "Protocols": "Http2"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=TelemetryDb;User Id=sa;Password=SmartFleet123!;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
EOF

# Update API Gateway
echo -e "${BLUE}Updating API Gateway...${NC}"
cat > src/SmartFleet.ApiGateway/appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "JWT_SECRET": "B38A16D1-C130-41A4-A899-2392F791407430F94D47-1582-4417-879A-D43BB5689E90"
}
EOF

echo -e "${GREEN}✅ All services configured for local SQL Server${NC}"
echo -e "${BLUE}📋 Database Configuration:${NC}"
echo "  - Server: localhost:1433"
echo "  - Username: sa"
echo "  - Password: SmartFleet123!"
echo "  - Databases: AuthDb, VehicleDb, DriverDb, TripDb, TripReadDb, NotificationDb, TelemetryDb"
echo ""
echo -e "${YELLOW}💡 Next steps:${NC}"
echo "  1. Run: ./smartfleet.sh local"
echo "  2. Test: ./test-complete-flow.sh"
echo ""
echo -e "${BLUE}🔧 Customization Note:${NC}"
echo "  - All configuration files now contain 'TODO_CUSTOMIZE' comments"
echo "  - Search for 'TODO_CUSTOMIZE' to find all customization points"
echo "  - See CUSTOMIZATION_GUIDE.md for detailed instructions"
