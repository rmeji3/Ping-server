#!/bin/bash
set -e

echo "=== Ping Local Development Environment Setup ==="
echo "How would you like to set up your database?"
echo "1) Production Database (AWS RDS) - Best for viewing real data"
echo "2) Local Docker Database (Postgres) - Starts empty, runs entirely locally"
echo "3) Local SQLite - Starts empty, no docker required"
read -p "Select option (1-3): " DB_CHOICE

# Clear any existing DB provider and connection strings from user secrets
dotnet user-secrets remove DatabaseProvider > /dev/null 2>&1 || true
dotnet user-secrets remove ConnectionStrings:AuthConnection > /dev/null 2>&1 || true
dotnet user-secrets remove ConnectionStrings:AppConnection > /dev/null 2>&1 || true

case $DB_CHOICE in
  1)
    echo ""
    echo "--- Connecting to Production Database ---"
    # Try to fetch from AWS SSM if AWS CLI is configured
    if command -v aws &> /dev/null && aws sts get-caller-identity &> /dev/null; then
      echo "AWS CLI detected. Fetching production credentials from SSM..."
      AUTH_CONN=$(aws ssm get-parameter --name "/ping-server/AUTH_CONNECTION" --with-decryption --query "Parameter.Value" --output text --region us-east-1)
      APP_CONN=$(aws ssm get-parameter --name "/ping-server/APP_CONNECTION" --with-decryption --query "Parameter.Value" --output text --region us-east-1)
      
      dotnet user-secrets set "DatabaseProvider" "Postgres" > /dev/null
      dotnet user-secrets set "ConnectionStrings:AuthConnection" "$AUTH_CONN" > /dev/null
      dotnet user-secrets set "ConnectionStrings:AppConnection" "$APP_CONN" > /dev/null
      echo "Successfully configured to use Production Database via SSM!"
    else
      echo "AWS CLI not detected or not logged in. Please enter credentials manually:"
      read -p "RDS Host (e.g. pingdb.cgxoy8w8eaea.us-east-1.rds.amazonaws.com): " RDS_HOST
      read -p "RDS Username (e.g. rmeji3): " RDS_USER
      read -sp "RDS Password: " RDS_PASS
      echo ""
      
      dotnet user-secrets set "DatabaseProvider" "Postgres" > /dev/null
      dotnet user-secrets set "ConnectionStrings:AuthConnection" "Host=$RDS_HOST;Database=ping_auth;Username=$RDS_USER;Password=$RDS_PASS" > /dev/null
      dotnet user-secrets set "ConnectionStrings:AppConnection" "Host=$RDS_HOST;Database=ping_app;Username=$RDS_USER;Password=$RDS_PASS" > /dev/null
      echo "Successfully configured to use Production Database!"
    fi
    ;;
  2)
    echo ""
    echo "--- Setting up Local Docker Database ---"
    dotnet user-secrets set "DatabaseProvider" "Postgres" > /dev/null
    dotnet user-secrets set "ConnectionStrings:AuthConnection" "Host=localhost;Database=ping_auth;Username=postgres;Password=postgres" > /dev/null
    dotnet user-secrets set "ConnectionStrings:AppConnection" "Host=localhost;Database=ping_app;Username=postgres;Password=postgres" > /dev/null
    
    echo "Starting local docker containers..."
    docker compose up -d
    
    echo "Restoring tools and building project..."
    dotnet tool restore
    dotnet build
    
    echo "Applying migrations..."
    dotnet ef database update --context AuthDbContext
    dotnet ef database update --context AppDbContext
    echo "Local Docker Database is ready!"
    echo "Grafana Dashboard: http://localhost:3000 (username: admin, password: admin)"
    ;;
  3)
    echo ""
    echo "--- Setting up Local SQLite ---"
    dotnet user-secrets set "DatabaseProvider" "Sqlite" > /dev/null
    # No connection string needed, will default to local .db files
    
    echo "Restoring tools and building project..."
    dotnet tool restore
    dotnet build
    
    echo "Applying migrations..."
    dotnet ef database update --context AuthDbContext
    dotnet ef database update --context AppDbContext
    echo "Local SQLite is ready!"
    ;;
  *)
    echo "Invalid option. Exiting."
    exit 1
    ;;
esac

echo "You can now run 'dotnet run' to start your local server!"
