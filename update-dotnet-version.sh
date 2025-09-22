#!/bin/bash

# Update all projects to target .NET 9.0
# This script updates all .csproj files to use .NET 9.0 instead of .NET 8.0

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

echo -e "${BLUE}🔄 Updating SmartFleet projects to .NET 9.0${NC}"
echo "=============================================="

# Find all .csproj files and update them
find . -name "*.csproj" -type f | while read -r file; do
    echo -e "${BLUE}Updating: $file${NC}"
    
    # Update TargetFramework from net8.0 to net9.0
    sed -i '' 's/<TargetFramework>net8.0<\/TargetFramework>/<TargetFramework>net9.0<\/TargetFramework>/g' "$file"
    
    # Update package references from 8.0.x to 9.0.x
    sed -i '' 's/Version="8\.0\.[0-9]*"/Version="9.0.0"/g' "$file"
done

echo -e "${GREEN}✅ All projects updated to .NET 9.0${NC}"
echo -e "${YELLOW}💡 Note: This change allows the application to run with your installed .NET 9.0${NC}"
echo -e "${BLUE}🔧 Customization: Search for 'TODO_CUSTOMIZE' to find other customization points${NC}"
