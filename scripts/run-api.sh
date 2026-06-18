#!/bin/bash

# Script to run the OrderManagement API from the solution root
echo "Starting OrderManagement API..."

# Run the API project from the solution root
dotnet run --project src/OrderManagement.Api/OrderManagement.Api.csproj "$@"