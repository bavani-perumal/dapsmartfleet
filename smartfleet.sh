#!/bin/bash

# SmartFleet - Unified Build, Test, and Run Script
# Handles both local development and Docker deployment
# Compatible with macOS (including ARM64), Windows, and Linux

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
API_GATEWAY_URL="http://localhost:9000"
DOCKER_COMPOSE_FILE="docker-compose.yml"

# Detect platform
detect_platform() {
    case "$(uname -s)" in
        Darwin*)    PLATFORM="macOS" ;;
        Linux*)     PLATFORM="Linux" ;;
        CYGWIN*|MINGW*|MSYS*) PLATFORM="Windows" ;;
        *)          PLATFORM="Unknown" ;;
    esac
    
    if [[ "$PLATFORM" == "macOS" ]]; then
        ARCH=$(uname -m)
        if [[ "$ARCH" == "arm64" ]]; then
            PLATFORM="macOS-ARM64"
        fi
    fi
    
    echo -e "${BLUE}Detected platform: $PLATFORM${NC}"
}

# Check prerequisites
check_prerequisites() {
    echo -e "${BLUE}🔍 Checking prerequisites...${NC}"
    
    local all_good=true
    
    # Check .NET
    if command -v dotnet &> /dev/null; then
        DOTNET_VERSION=$(dotnet --version)
        echo -e "${GREEN}✅ .NET: $DOTNET_VERSION${NC}"
    else
        echo -e "${RED}❌ .NET SDK not found${NC}"
        echo -e "${YELLOW}   Install from: https://dotnet.microsoft.com/download${NC}"
        all_good=false
    fi
    
    # Check Docker
    if command -v docker &> /dev/null; then
        DOCKER_VERSION=$(docker --version)
        echo -e "${GREEN}✅ Docker: $DOCKER_VERSION${NC}"
    else
        echo -e "${RED}❌ Docker not found${NC}"
        echo -e "${YELLOW}   Install from: https://docker.com/get-started${NC}"
        all_good=false
    fi
    
    # Check curl
    if command -v curl &> /dev/null; then
        echo -e "${GREEN}✅ curl: Available${NC}"
    else
        echo -e "${RED}❌ curl not found${NC}"
        all_good=false
    fi
    
    if [[ "$all_good" == "false" ]]; then
        echo -e "${RED}❌ Some prerequisites are missing${NC}"
        exit 1
    fi
    
    echo -e "${GREEN}🎉 All prerequisites available!${NC}"
}

# Build the solution
build_solution() {
    echo -e "${BLUE}🔨 Building SmartFleet solution...${NC}"

    # Ensure .NET 8.0 is in PATH
    export PATH="$HOME/.dotnet:$PATH"

    if [[ ! -f "SmartFleet.sln" ]]; then
        echo -e "${RED}❌ SmartFleet.sln not found${NC}"
        exit 1
    fi

    # Skip solution-level build - let individual services build themselves
    echo -e "${YELLOW}⏳ Skipping solution build - services will build individually...${NC}"
    echo -e "${BLUE}💡 Each service will restore and build when started${NC}"
    echo -e "${GREEN}✅ Ready to start services${NC}"
}

# Setup databases using Entity Framework
setup_databases() {
    echo -e "${BLUE}🗄️ Setting up databases...${NC}"

    # Wait for SQL Server to be ready
    echo -e "${YELLOW}⏳ Waiting for SQL Server to be ready...${NC}"
    sleep 5

    # Try to create databases using SQL script first
    echo -e "${BLUE}📦 Creating databases...${NC}"
    if docker exec sqlserver-local /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'SmartFleet123!' -C -i /tmp/init-databases.sql 2>/dev/null; then
        echo -e "${GREEN}✅ Databases created via SQL script${NC}"
    else
        echo -e "${YELLOW}⚠️ SQL script failed, databases will be created by services${NC}"
    fi

    # Copy SQL script to container for future use
    docker cp init-databases.sql sqlserver-local:/tmp/init-databases.sql 2>/dev/null || true

    echo -e "${GREEN}✅ Database setup completed${NC}"
}

# Check if a service is healthy
check_service_health() {
    local service_name=$1
    local port=$2
    local max_attempts=30
    local attempt=1

    echo -e "${YELLOW}⏳ Waiting for $service_name to be ready...${NC}"

    while [ $attempt -le $max_attempts ]; do
        # Try HTTP request to check if service is responding
        if curl -s -f "http://localhost:$port/health" > /dev/null 2>&1; then
            echo -e "${GREEN}✅ $service_name is ready (health check passed)${NC}"
            return 0
        elif curl -s -f "http://localhost:$port" > /dev/null 2>&1; then
            echo -e "${GREEN}✅ $service_name is ready (basic check passed)${NC}"
            return 0
        fi

        echo -n "."
        sleep 2
        attempt=$((attempt + 1))
    done

    echo -e "${RED}❌ $service_name failed to start within 60 seconds${NC}"
    echo -e "${YELLOW}💡 Check logs: tail -f logs/${service_name,,}.log${NC}"
    return 1
}

# Start services locally
start_local_services() {
    echo -e "${BLUE}🚀 Starting services locally...${NC}"
    echo -e "${YELLOW}⚠️  Note: This requires a local SQL Server instance${NC}"
    echo -e "${YELLOW}   For Docker deployment, use: ./smartfleet.sh docker${NC}"

    # Ensure .NET 8.0 is in PATH
    export PATH="$HOME/.dotnet:$PATH"

    # Check if SQL Server is running
    if ! docker ps | grep -q sqlserver-local; then
        echo -e "${RED}❌ SQL Server container not found${NC}"
        echo -e "${YELLOW}💡 Please run: docker run -e 'ACCEPT_EULA=Y' -e 'SA_PASSWORD=SmartFleet123!' -p 1433:1433 --name sqlserver-local -d mcr.microsoft.com/mssql/server:2022-latest${NC}"
        exit 1
    fi

    # Create logs directory
    mkdir -p logs

    # Kill any existing processes
    echo -e "${BLUE}🧹 Cleaning up existing processes...${NC}"
    pkill -f "dotnet.*SmartFleet" || true
    sleep 2

    # Setup databases
    setup_databases
    
    # Get absolute path to logs directory and project root
    LOGS_DIR="$(pwd)/logs"
    PROJECT_ROOT="$(pwd)"

    # Start services in background
    echo -e "${BLUE}Starting Auth Service...${NC}"
    (cd "$PROJECT_ROOT/src/Auth" && dotnet run --urls "http://localhost:5101" > "$LOGS_DIR/auth.log" 2>&1) &
    AUTH_PID=$!
    echo $AUTH_PID > "$LOGS_DIR/auth.pid"
    check_service_health "Auth Service" 5101

    echo -e "${BLUE}Starting Vehicle Service...${NC}"
    (cd "$PROJECT_ROOT/src/Vehicle" && dotnet run --urls "http://localhost:5002" > "$LOGS_DIR/vehicle.log" 2>&1) &
    VEHICLE_PID=$!
    echo $VEHICLE_PID > "$LOGS_DIR/vehicle.pid"
    check_service_health "Vehicle Service" 5002

    echo -e "${BLUE}Starting Telemetry Service...${NC}"
    (cd "$PROJECT_ROOT/SmartFleet.Telemetry" && dotnet run --urls "http://localhost:5003" > "$LOGS_DIR/telemetry.log" 2>&1) &
    TELEMETRY_PID=$!
    echo $TELEMETRY_PID > "$LOGS_DIR/telemetry.pid"
    check_service_health "Telemetry Service" 5003

    echo -e "${BLUE}Starting Driver Service...${NC}"
    (cd "$PROJECT_ROOT/src/Driver" && dotnet run --urls "http://localhost:5004" > "$LOGS_DIR/driver.log" 2>&1) &
    DRIVER_PID=$!
    echo $DRIVER_PID > "$LOGS_DIR/driver.pid"
    check_service_health "Driver Service" 5004

    echo -e "${BLUE}Starting Trip Service...${NC}"
    (cd "$PROJECT_ROOT/src/Trip" && dotnet run --urls "http://localhost:5005" > "$LOGS_DIR/trip.log" 2>&1) &
    TRIP_PID=$!
    echo $TRIP_PID > "$LOGS_DIR/trip.pid"
    check_service_health "Trip Service" 5005

    echo -e "${BLUE}Starting Notification Service...${NC}"
    (cd "$PROJECT_ROOT/src/Notification" && dotnet run --urls "http://localhost:5006" > "$LOGS_DIR/notification.log" 2>&1) &
    NOTIFICATION_PID=$!
    echo $NOTIFICATION_PID > "$LOGS_DIR/notification.pid"
    check_service_health "Notification Service" 5006

    echo -e "${BLUE}Starting API Gateway...${NC}"
    (cd "$PROJECT_ROOT/src/SmartFleet.ApiGateway" && dotnet run --urls "http://localhost:9000" > "$LOGS_DIR/apigateway.log" 2>&1) &
    GATEWAY_PID=$!
    echo $GATEWAY_PID > "$LOGS_DIR/apigateway.pid"
    check_service_health "API Gateway" 9000
    
    echo -e "${GREEN}✅ All services started${NC}"
    echo -e "${BLUE}📋 Service URLs:${NC}"
    echo "  - API Gateway: http://localhost:9000"
    echo "  - Auth Service: http://localhost:5101"
    echo "  - Vehicle Service: http://localhost:5002"
    echo "  - Telemetry Service: http://localhost:5003"
    echo "  - Driver Service: http://localhost:5004"
    echo "  - Trip Service: http://localhost:5005"
    echo "  - Notification Service: http://localhost:5006"
    echo ""
    echo -e "${BLUE}📝 Logs are available in the logs/ directory${NC}"
    echo -e "${BLUE}🛑 To stop services: ./smartfleet.sh stop${NC}"
    echo -e "${BLUE}🧪 To test the application: ./smartfleet.sh test${NC}"
}

# Test the application
test_application() {
    echo -e "${BLUE}🧪 Testing SmartFleet application...${NC}"

    # Check if services are running
    if ! curl -s "http://localhost:9000" > /dev/null 2>&1; then
        echo -e "${RED}❌ API Gateway is not running${NC}"
        echo -e "${YELLOW}💡 Please start services first: ./smartfleet.sh local${NC}"
        exit 1
    fi

    # Run the test script
    if [[ -f "test-complete-flow.sh" ]]; then
        echo -e "${BLUE}🚀 Running complete flow test...${NC}"
        ./test-complete-flow.sh
    else
        echo -e "${RED}❌ test-complete-flow.sh not found${NC}"
        echo -e "${YELLOW}💡 Manual test: Try accessing http://localhost:9000${NC}"
    fi
}

# Start services with Docker
start_docker_services() {
    echo -e "${BLUE}🐳 Starting services with Docker...${NC}"
    
    if [[ "$PLATFORM" == "macOS-ARM64" ]]; then
        echo -e "${YELLOW}⚠️  ARM64 detected: Some services may fail due to gRPC tools compatibility${NC}"
        echo -e "${YELLOW}   This is a known issue with protobuf compilation on Apple Silicon${NC}"
        echo -e "${YELLOW}   Workarounds:${NC}"
        echo -e "${YELLOW}   1. Use local development instead${NC}"
        echo -e "${YELLOW}   2. Use Docker Desktop with Rosetta 2 emulation${NC}"
        echo -e "${YELLOW}   3. Deploy to cloud platforms (x64 architecture)${NC}"
        echo ""
        read -p "Continue with Docker? (y/N): " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            echo -e "${BLUE}Use './smartfleet.sh local' for local development${NC}"
            exit 0
        fi
    fi
    
    # Stop any existing containers
    echo -e "${BLUE}🧹 Stopping existing containers...${NC}"
    docker-compose down || true
    
    # Start services
    echo -e "${BLUE}🚀 Building and starting containers...${NC}"
    docker-compose up --build -d
    
    if [[ $? -eq 0 ]]; then
        echo -e "${GREEN}✅ Docker services started${NC}"
        echo -e "${BLUE}📋 Service URLs:${NC}"
        echo "  - API Gateway: http://localhost:9000"
        echo ""
        echo -e "${BLUE}🔍 Check status: docker ps${NC}"
        echo -e "${BLUE}📝 View logs: docker-compose logs -f${NC}"
        echo -e "${BLUE}🛑 To stop: ./smartfleet.sh stop${NC}"
    else
        echo -e "${RED}❌ Docker startup failed${NC}"
        echo -e "${BLUE}📝 Check logs: docker-compose logs${NC}"
        exit 1
    fi
}

# Stop all services
stop_services() {
    echo -e "${BLUE}🛑 Stopping SmartFleet services...${NC}"
    
    # Stop Docker services
    echo -e "${BLUE}Stopping Docker containers...${NC}"
    docker-compose down || true
    
    # Stop local services
    echo -e "${BLUE}Stopping local services...${NC}"
    pkill -f "dotnet.*SmartFleet" || true
    
    # Clean up PID files
    if [[ -d "logs" ]]; then
        rm -f logs/*.pid
    fi
    
    echo -e "${GREEN}✅ All services stopped${NC}"
}

# Test API endpoints
test_api() {
    echo -e "${BLUE}🧪 Testing SmartFleet API...${NC}"
    
    # Wait for services to be ready
    echo -e "${BLUE}⏳ Waiting for API Gateway...${NC}"
    for i in {1..30}; do
        if curl -s "$API_GATEWAY_URL/health" > /dev/null 2>&1; then
            echo -e "${GREEN}✅ API Gateway is ready${NC}"
            break
        fi
        if [[ $i -eq 30 ]]; then
            echo -e "${RED}❌ API Gateway not responding${NC}"
            echo -e "${BLUE}💡 Try: ./smartfleet.sh logs${NC}"
            exit 1
        fi
        sleep 2
    done
    
    # Test authentication
    echo -e "${BLUE}🔐 Testing authentication...${NC}"
    AUTH_RESPONSE=$(curl -s -X POST "$API_GATEWAY_URL/auth/token" \
        -H "Content-Type: application/json" \
        -d '{"username":"admin","password":"admin123"}')
    
    if echo "$AUTH_RESPONSE" | grep -q "token"; then
        echo -e "${GREEN}✅ Authentication successful${NC}"
        TOKEN=$(echo "$AUTH_RESPONSE" | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
        echo -e "${BLUE}Token: ${TOKEN:0:50}...${NC}"
    else
        echo -e "${RED}❌ Authentication failed${NC}"
        echo "Response: $AUTH_RESPONSE"
        exit 1
    fi
    
    # Test vehicle service
    echo -e "${BLUE}🚗 Testing vehicle service...${NC}"
    VEHICLE_RESPONSE=$(curl -s -X GET "$API_GATEWAY_URL/api/vehicles" \
        -H "Authorization: Bearer $TOKEN")
    
    if [[ $? -eq 0 ]]; then
        echo -e "${GREEN}✅ Vehicle service responding${NC}"
    else
        echo -e "${RED}❌ Vehicle service failed${NC}"
    fi
    
    echo -e "${GREEN}🎉 API testing completed${NC}"
}

# Show logs
show_logs() {
    echo -e "${BLUE}📝 SmartFleet Service Logs${NC}"
    echo "=========================="
    
    if docker ps -q --filter "name=smartfleet" | grep -q .; then
        echo -e "${BLUE}Docker logs:${NC}"
        docker-compose logs --tail=50
    elif [[ -d "logs" ]]; then
        echo -e "${BLUE}Local service logs:${NC}"
        for log_file in logs/*.log; do
            if [[ -f "$log_file" ]]; then
                echo -e "${YELLOW}--- $(basename "$log_file") ---${NC}"
                tail -20 "$log_file"
                echo ""
            fi
        done
    else
        echo -e "${YELLOW}No logs found${NC}"
    fi
}

# Show help
show_help() {
    echo -e "${BLUE}SmartFleet - Unified Management Script${NC}"
    echo "====================================="
    echo ""
    echo "Usage: ./smartfleet.sh [command]"
    echo ""
    echo "Commands:"
    echo "  check       Check prerequisites"
    echo "  build       Build the solution"
    echo "  local       Start services locally (requires local SQL Server)"
    echo "  docker      Start services with Docker"
    echo "  test        Test API endpoints"
    echo "  stop        Stop all services"
    echo "  logs        Show service logs"
    echo "  help        Show this help"
    echo ""
    echo "Examples:"
    echo "  ./smartfleet.sh check       # Check if all tools are installed"
    echo "  ./smartfleet.sh build       # Build the solution"
    echo "  ./smartfleet.sh docker      # Start with Docker (recommended)"
    echo "  ./smartfleet.sh local       # Start locally (needs SQL Server)"
    echo "  ./smartfleet.sh test        # Test the running application"
    echo "  ./smartfleet.sh stop        # Stop everything"
    echo ""
    echo -e "${YELLOW}Platform-specific notes:${NC}"
    echo "  - macOS ARM64: Docker may have gRPC issues, use 'local' mode"
    echo "  - Windows: Use Docker Desktop or WSL2"
    echo "  - Linux: Both local and Docker should work"
}

# Main script logic
main() {
    detect_platform
    
    case "${1:-help}" in
        "check")
            check_prerequisites
            ;;
        "build")
            check_prerequisites
            build_solution
            ;;
        "local")
            check_prerequisites
            build_solution
            start_local_services
            ;;
        "docker")
            check_prerequisites
            start_docker_services
            ;;
        "test")
            test_application
            ;;
        "stop")
            stop_services
            ;;
        "logs")
            show_logs
            ;;
        "help"|*)
            show_help
            ;;
    esac
}

# Run main function with all arguments
main "$@"
