# Conquest Server

Conquest is an ASP.NET Core API for managing users, places, activities, events, and friendships.

## 📚 Documentation

- **[Server Guide](ServerGuide.md)**: The authoritative source for architecture, endpoints, database schema, and business rules. **Consult this first.**

## 🚀 Getting Started

### Prerequisites
- [.NET 9.0.308](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)
- **Redis**
    - `docker run -d --name redis-conquest -p 6379:6379 redis:latest`
- **Monitoring** (Prometheus & Grafana) - *Optional*
    - Run `docker-compose up -d`
- env variables in [`.env`](.env) from [secrets github](https://github.com/rmeji3/Conquest-Secrets/tree/main)
    - Connection Strings
        - AUTH_CONNECTION=
        - APP_CONNECTION=
        - REDIS_CONNECTION=
    - JWT Settings
        - JWT_KEY=
        - JWT_ISSUER=
        - JWT_AUDIENCE=
        - JWT_ACCESS_TOKEN_MINUTES=
    - Google
        - GOOGLE_API_KEY=
    - OpenAI
        - OPENAI_API_KEY=
    - Rate Limiting (change for production)
        - RATE_LIMIT_GLOBAL_PER_MINUTE=
        - RATE_LIMIT_AUTHENTICATED_PER_MINUTE=
        - RATE_LIMIT_AUTH_ENDPOINTS_PER_MINUTE=
        - RATE_LIMIT_PLACE_CREATION_PER_DAY=
    - AWS Credentials
        - AWS__AccessKey=
        - AWS__SecretKey=
        - AWS__Region=
        - AWS__BucketName=
- 
### Build & Run
```bash
dotnet restore tools
dotnet build
dotnet run
```
The API will be available at `http://localhost:5055` (or similar, check launch logs).
Swagger UI is available at the root URL.

## � Observability

### Logging
This project uses **Serilog** for structured logging. Logs are output to the console and can be enriched with environment details. Configuration can be found in `Program.cs` and `appsettings.json`.

### Metrics & Monitoring
Prometheus metrics are exposed at `/metrics`.
- **System Metrics**: .NET Runtime (GC, ThreadPool, etc.)
- **HTTP Metrics**: Request counts, duration, and error rates.
- **Custom Metrics**: Business logic counters (e.g., Check-ins).

To run the monitoring stack (Prometheus + Grafana):
```bash
docker-compose up -d
```
Access Grafana at `http://localhost:3000` (Default login: `admin`/`admin`).

### Health Checks
Health checks are available at `/health`.
- Checks connectivity to **AuthDB**, **AppDB**, and **Redis**.
- Returns `200 OK` if healthy, `503 Service Unavailable` if degraded.


## �🛠 Development

### Database Migrations
This project uses two DbContexts: `AuthDbContext` and `AppDbContext`.

To do migrations, run the following commands:
```bash
dotnet ef migrations add <MigrationName> --context <YourDbContext>
dotnet ef database update --context <YourDbContext>
```

Specifically for this project, run the following commands:
```bash
dotnet ef migrations add <MigrationName> --context AuthDbContext
dotnet ef database update --context AuthDbContext
dotnet ef migrations add <MigrationName> --context AppDbContext
dotnet ef database update --context AppDbContext
```

Or you can run:
- (migrateApp.sh)[mApp.sh]:
``` powershell ./mApp.sh <MigrationName>```
- (migrateAuth.sh)[mAuth.sh]:
``` powershell ./mAuth.sh <MigrationName>```

## Testing Strategy
We use xUnit + Microsoft.AspNetCore.Mvc.Testing for integration testing.

### Infrastructure
Tests/Conquest.Tests: Main test project.
IntegrationTestFactory: WebApplicationFactory customization that replaces the database with InMemory providers for isolation.
BaseIntegrationTest: Base class creating HttpClient and handling auth.

### How to Run
``` powershell dotnet test Tests/Conquest.Tests ```

### Writing Tests
Inherit BaseIntegrationTest. The factory ensures a fresh server instance (logic-wise) and cleanable DBs.





