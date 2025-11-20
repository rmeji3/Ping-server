# User guide

## Migrating sqlite Servers
If you get the following error:
```
Unhandled exception. System.InvalidOperationException: An error was generated for warning 'Microsoft.EntityFrameworkCore.Migrations.PendingModelChangesWarning': 
The model for context 'AuthDbContext' has pending changes. Add a new migration before updating the database. See https://aka.ms/efcore-docs-pending-changes. 
This exception can be suppressed or logged by passing event ID 'RelationalEventId.PendingModelChangesWarning' to the 'ConfigureWarnings' method in 
'DbContext.OnConfiguring' or 'AddDbContext'.
```
### Run the following commands to migrate sqlite server (AuthDBContext in this case):
```bash
dotnet ef migrations add initAuthDb --context Conquest.Data.Auth.AuthDbContext
dotnet ef database update --context Conquest.Data.Auth.AuthDbContext
```
### Run the following commands to migrate sqlite server (AppDBContext in this case):
```bash
dotnet ef migrations add initAppDb --context Conquest.Data.App.AppDBContext
dotnet ef database update --context Conquest.Data.App.AppDBContext
```
### Note:
These are the commands without names so you can replace the names with your own if needed.
```bash
dotnet ef migrations add <MigrationName> --context <YourDbContext>
dotnet ef database update --context <YourDbContext>
```

## Setting Up Redis with Docker

Redis is used for rate limiting and session management in the Conquest server. The easiest way to run Redis locally is using Docker.

### Step 1: Download and Install Docker Desktop

1. **Download Docker Desktop for Windows**:
   - Visit [https://www.docker.com/products/docker-desktop](https://www.docker.com/products/docker-desktop)
   - Click **Download for Windows**
   - Wait for the installer to download (approximately 500MB)

2. **Install Docker Desktop**:
   - Run the downloaded `Docker Desktop Installer.exe`
   - Follow the installation wizard:
     - Check **Use WSL 2 instead of Hyper-V** (recommended)
     - Click **OK** to proceed with installation
   - Wait for the installation to complete
   - Click **Close and restart** when prompted

3. **Start Docker Desktop**:
   - After restart, Docker Desktop should start automatically
   - If not, search for "Docker Desktop" in the Start menu and launch it
   - Accept the Docker Subscription Service Agreement if prompted
   - Wait for Docker Engine to start (you'll see "Docker Desktop is running" in the system tray)

### Step 2: Verify Docker Installation

Open PowerShell or Command Prompt and run:

```powershell
docker --version
```

You should see output like: `Docker version 24.x.x, build xxxxxxx`

### Step 3: Run Redis Container

Run the following command to start Redis in a Docker container:

```powershell
docker run -d --name redis-conquest -p 6379:6379 redis:latest
```

**What this command does**:
- `-d`: Runs the container in detached mode (background)
- `--name redis-conquest`: Names the container "redis-conquest"
- `-p 6379:6379`: Maps port 6379 on your machine to port 6379 in the container
- `redis:latest`: Uses the latest Redis image from Docker Hub

### Step 4: Verify Redis is Running

Check if the Redis container is running:

```powershell
docker ps
```

You should see the `redis-conquest` container in the list with status "Up".

**Test Redis connection** using the Redis CLI:

```powershell
docker exec -it redis-conquest redis-cli ping
```

If Redis is working correctly, you should see: `PONG`

### Managing Redis Container

**Stop Redis**:
```powershell
docker stop redis-conquest
```

**Start Redis** (after stopping):
```powershell
docker start redis-conquest
```

**Remove Redis container** (if you need to start fresh):
```powershell
docker stop redis-conquest
docker rm redis-conquest
```

**View Redis logs**:
```powershell
docker logs redis-conquest
```

### Troubleshooting

**Docker Desktop won't start**:
- Ensure virtualization is enabled in your BIOS
- Make sure WSL 2 is installed and updated: `wsl --update`
- Try restarting your computer

**Port 6379 already in use**:
- Check if another Redis instance is running
- Use a different port: `docker run -d --name redis-conquest -p 6380:6379 redis:latest`
- Update `appsettings.json` to use the new port: `localhost:6380`

**Container keeps restarting**:
- Check logs: `docker logs redis-conquest`
- Ensure no other service is using port 6379

## Troubleshooting Network Issues (Expo Go)

If you see `Network request failed` on your mobile device, it is likely the **Windows Firewall** blocking the connection.

### How to fix Firewall
1.  Open **Windows Defender Firewall with Advanced Security**.
2.  Click **Inbound Rules** -> **New Rule...**.
3.  Select **Port** -> **Next**.
4.  Select **TCP** and enter **5055** in **Specific local ports** -> **Next**.
5.  Select **Allow the connection** -> **Next**.
6.  Check all profiles (Domain, Private, Public) -> **Next**.
7.  Name it "Conquest API" -> **Finish**.

Alternatively, run this command in an **Administrator** PowerShell:
```powershell
New-NetFirewallRule -DisplayName "Conquest API" -Direction Inbound -LocalPort 5055 -Protocol TCP -Action Allow
```