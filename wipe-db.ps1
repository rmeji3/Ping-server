# PowerShell script to drop the Ping databases
# Requires dotnet-ef tool installed

Write-Host "WARNING: This will drop the Ping databases!" -ForegroundColor Red
Write-Host "Connection strings are pointing to: pingdb.cgxoy8w8eaea.us-east-1.rds.amazonaws.com" -ForegroundColor Yellow
$confirmation = Read-Host "Are you sure you want to continue? (yes/no)"

if ($confirmation -ne "yes") {
    Write-Host "Cancelled." -ForegroundColor Green
    exit 0
}

Write-Host "Dropping Databases..." -ForegroundColor Yellow

# Auth Database
Write-Host "Dropping Auth Database..." -ForegroundColor Cyan
dotnet ef database drop --context AuthDbContext --force

# App Database
Write-Host "Dropping App Database..." -ForegroundColor Cyan
dotnet ef database drop --context AppDbContext --force

Write-Host "Done. Run 'dotnet run' to re-create and migrate." -ForegroundColor Green
