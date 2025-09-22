@echo off
REM SmartFleet - Unified Build, Test, and Run Script for Windows
REM Handles both local development and Docker deployment

setlocal enabledelayedexpansion

REM Configuration
set API_GATEWAY_URL=http://localhost:9000
set DOCKER_COMPOSE_FILE=docker-compose.yml

REM Colors (limited in Windows CMD)
set RED=[91m
set GREEN=[92m
set YELLOW=[93m
set BLUE=[94m
set NC=[0m

echo %BLUE%SmartFleet - Windows Management Script%NC%
echo =====================================

REM Check prerequisites
:check_prerequisites
echo %BLUE%Checking prerequisites...%NC%

REM Check .NET
dotnet --version >nul 2>&1
if %errorlevel% equ 0 (
    for /f "tokens=*" %%i in ('dotnet --version') do set DOTNET_VERSION=%%i
    echo %GREEN%✓ .NET: !DOTNET_VERSION!%NC%
) else (
    echo %RED%✗ .NET SDK not found%NC%
    echo %YELLOW%   Install from: https://dotnet.microsoft.com/download%NC%
    goto :error
)

REM Check Docker
docker --version >nul 2>&1
if %errorlevel% equ 0 (
    echo %GREEN%✓ Docker: Available%NC%
) else (
    echo %RED%✗ Docker not found%NC%
    echo %YELLOW%   Install Docker Desktop from: https://docker.com/get-started%NC%
    goto :error
)

REM Check curl
curl --version >nul 2>&1
if %errorlevel% equ 0 (
    echo %GREEN%✓ curl: Available%NC%
) else (
    echo %RED%✗ curl not found%NC%
    echo %YELLOW%   curl is required for API testing%NC%
    goto :error
)

echo %GREEN%All prerequisites available!%NC%
goto :main

:error
echo %RED%Some prerequisites are missing%NC%
exit /b 1

:build_solution
echo %BLUE%Building SmartFleet solution...%NC%

if not exist "SmartFleet.sln" (
    echo %RED%SmartFleet.sln not found%NC%
    exit /b 1
)

dotnet build SmartFleet.sln
if %errorlevel% equ 0 (
    echo %GREEN%Build successful%NC%
) else (
    echo %RED%Build failed%NC%
    exit /b 1
)
goto :eof

:start_docker_services
echo %BLUE%Starting services with Docker...%NC%

REM Stop any existing containers
echo %BLUE%Stopping existing containers...%NC%
docker-compose down

REM Start services
echo %BLUE%Building and starting containers...%NC%
docker-compose up --build -d

if %errorlevel% equ 0 (
    echo %GREEN%Docker services started%NC%
    echo %BLUE%Service URLs:%NC%
    echo   - API Gateway: http://localhost:9000
    echo.
    echo %BLUE%Check status: docker ps%NC%
    echo %BLUE%View logs: docker-compose logs -f%NC%
    echo %BLUE%To stop: smartfleet.bat stop%NC%
) else (
    echo %RED%Docker startup failed%NC%
    echo %BLUE%Check logs: docker-compose logs%NC%
    exit /b 1
)
goto :eof

:stop_services
echo %BLUE%Stopping SmartFleet services...%NC%

REM Stop Docker services
echo %BLUE%Stopping Docker containers...%NC%
docker-compose down

echo %GREEN%All services stopped%NC%
goto :eof

:test_api
echo %BLUE%Testing SmartFleet API...%NC%

REM Wait for services to be ready
echo %BLUE%Waiting for API Gateway...%NC%
set /a count=0
:wait_loop
curl -s "%API_GATEWAY_URL%/health" >nul 2>&1
if %errorlevel% equ 0 (
    echo %GREEN%API Gateway is ready%NC%
    goto :test_auth
)
set /a count+=1
if %count% geq 30 (
    echo %RED%API Gateway not responding%NC%
    echo %BLUE%Try: smartfleet.bat logs%NC%
    exit /b 1
)
timeout /t 2 /nobreak >nul
goto :wait_loop

:test_auth
REM Test authentication
echo %BLUE%Testing authentication...%NC%
curl -s -X POST "%API_GATEWAY_URL%/auth/token" -H "Content-Type: application/json" -d "{\"username\":\"admin\",\"password\":\"admin123\"}" > auth_response.tmp

findstr "token" auth_response.tmp >nul
if %errorlevel% equ 0 (
    echo %GREEN%Authentication successful%NC%
) else (
    echo %RED%Authentication failed%NC%
    type auth_response.tmp
    del auth_response.tmp
    exit /b 1
)

del auth_response.tmp
echo %GREEN%API testing completed%NC%
goto :eof

:show_logs
echo %BLUE%SmartFleet Service Logs%NC%
echo ======================

docker ps -q --filter "name=smartfleet" >nul 2>&1
if %errorlevel% equ 0 (
    echo %BLUE%Docker logs:%NC%
    docker-compose logs --tail=50
) else (
    echo %YELLOW%No Docker containers found%NC%
)
goto :eof

:show_help
echo SmartFleet - Windows Management Script
echo =====================================
echo.
echo Usage: smartfleet.bat [command]
echo.
echo Commands:
echo   check       Check prerequisites
echo   build       Build the solution
echo   docker      Start services with Docker
echo   test        Test API endpoints
echo   stop        Stop all services
echo   logs        Show service logs
echo   help        Show this help
echo.
echo Examples:
echo   smartfleet.bat check       # Check if all tools are installed
echo   smartfleet.bat build       # Build the solution
echo   smartfleet.bat docker      # Start with Docker (recommended)
echo   smartfleet.bat test        # Test the running application
echo   smartfleet.bat stop        # Stop everything
echo.
echo Windows-specific notes:
echo   - Use Docker Desktop for Windows
echo   - PowerShell or Command Prompt both work
echo   - WSL2 backend recommended for Docker
goto :eof

:main
if "%1"=="check" goto :check_prerequisites
if "%1"=="build" (
    call :check_prerequisites
    if !errorlevel! equ 0 call :build_solution
    goto :eof
)
if "%1"=="docker" (
    call :check_prerequisites
    if !errorlevel! equ 0 call :start_docker_services
    goto :eof
)
if "%1"=="test" goto :test_api
if "%1"=="stop" goto :stop_services
if "%1"=="logs" goto :show_logs
if "%1"=="help" goto :show_help
if "%1"=="" goto :show_help

REM Default to help
goto :show_help
