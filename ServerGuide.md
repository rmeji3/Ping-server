# Ping Server Guide

Comprehensive internal documentation of the current server codebase. This guide is designed to give an AI agent (and developers) full context of the project structure: endpoints, models, DTOs, services, data contexts, and architectural decisions.

## Table of Contents
1. Overview
2. Project Structure
3. Configuration & Startup (`Program.cs`)
4. Authentication & Authorization
5. Database Contexts & Persistence Layer
6. Domain Models
7. DTO Contracts
8. Services
9. Controllers & Endpoints (Full Reference)
10. Validation & Business Rules
11. Indexes, Seed Data, and Performance Notes
12. Migration & EF Core Operations (Legacy Notes Included)
13. Conventions & Extension Points
14. Redis & Caching
15. Rate Limiting
16. Content Moderation & AI
17. Notifications
18. Quick Reference Summary
19. Error Response Formats
20. Suggested Future Enhancements
21. Agent Usage Notes

---
## 1. Overview
Ping is an ASP.NET Core API (targeting .NET 9) that manages users, pings, activities at pings, events, friendships, and reviews. It uses:
- ASP.NET Core MVC + minimal hosting model
- **Service Layer Architecture** ("Thin Controller, Fat Service" pattern)
- Identity (custom `AppUser`) stored in `AuthDbContext` (SQLite)
- Application domain stored in `AppDbContext` (SQLite **OR** PostgreSQL + **PostGIS** for geospatial)
- JWT-based authentication
- **Redis** for distributed caching, rate limiting, and session management
- **Rate Limiting Middleware** for API protection
- **AI Integration**: OpenAI for Content Moderation and Semantic Deduplication
- Global Exception Handling (standardized JSON responses)
- Swagger/OpenAPI (exposed at root path `/`)

---
## 2. Project Structure (Key Folders)
- `Program.cs` – service registration, middleware pipeline, auto-migrations, Redis setup.
- `Data/Auth/AuthDbContext.cs` – Identity + Friendships + UserBlocks.
- `Data/App/AppDbContext.cs` – Pings, Activities, Reviews, Tags, CheckIns, Events.
- `Models/*` – EF Core entity classes.
- `Dtos/*` – Records/classes exposed via API boundary.
- `Controllers/*` – Thin orchestrators handling HTTP concerns only.
- `Services/*` – Business logic, database queries, and domain operations.
- `Services/Redis/*` – Redis service for caching and rate limiting.
- `Services/Moderation/*` - Content moderation services.
- `Services/AI/*` - Semantic analysis services.
- `Middleware/*` – Global exception handling and rate limiting middleware.

---
## 3. Configuration & Startup (`Program.cs`)
Registered services:
- **ImageService**: `IImageService` (scoped) for resizing and uploading.
- Two DbContexts (`AuthDbContext`, `AppDbContext`) using either **SQLite** or **PostgreSQL** based on `DatabaseProvider` config.
- IdentityCore for `AppUser` + Roles + SignInManager + TokenProviders.
- JWT options bound from `configuration["Jwt"]` (Key, Issuer, Audience, AccessTokenMinutes).
- **Redis**: `IConnectionMultiplexer` (singleton), distributed cache, `IRedisService` (scoped).
- **Session**: Distributed session state with Redis backend (30-minute timeout).
- **Service Layer** (all scoped): `ITokenService`, `IPingService`, `IPingNameService` (Google Places API), `IEventService`, `IReviewService`, `IPingActivityService`, `IFriendService`, `IBlockService`, `IProfileService`, `IAuthService`, `IRedisService`, `IModerationService`, `ISemanticService`, `ITagService`, `RecommendationService`, `IBanningService`.
- **Middleware**: `GlobalExceptionHandler` (transient), `RateLimitMiddleware` (scoped), `BanningMiddleware` (scoped).
- Authentication: JWT Bearer with validation (issuer, audience, lifetime, signing key).
- Swagger with Bearer security scheme.
- Auto migration is performed for both contexts at startup inside a scope.
- Redis connection health check logged at startup.

Pipeline order:
1. Global Exception Handler
2. Swagger + UI at root
3. Routing
4. **Session Middleware**
5. Authentication
6. **Banning Middleware**
7. **Rate Limiting Middleware**
8. Authorization
9. Static files
10. Controller endpoints via `app.MapControllers()`

### 3.1. Configuration Reference

Required `appsettings.json` keys:
```json
{
  "DatabaseProvider": "Sqlite", // or "Postgres"
  "ConnectionStrings": {
    "AuthConnection": "", // Set in .env
    "AppConnection": "",  // Set in .env
    "RedisConnection": "localhost:6379"
  },
  "Jwt": {
    "Key": "[secret-key-minimum-32-chars]",
    "Issuer": "PingAPI",
    "Audience": "PingApp",
    "AccessTokenMinutes": 60
  },
  "Google": {
    "ApiKey": "[google-places-api-key]",
    "ClientId": "[google-oauth-client-id]"
  },
  "Apple": {
    "ClientId": "[apple-bundle-id]"
  },
  "RateLimiting": {
    "GlobalLimitPerMinute": 100,
    "AuthenticatedLimitPerMinute": 200,
    "AuthEndpointsLimitPerMinute": 5,
    "PingCreationLimitPerDay": 10
  }
}
```

**Environment Variables (.env)**:
- `AUTH_CONNECTION`: Connection string (Postgres or SQLite file path).
- `APP_CONNECTION`: Connection string.
- `OPENAI_API_KEY`: Required for AI Services.
- `DatabaseProvider`: Override default provider (e.g. `Postgres`).

**Development overrides** (`appsettings.Development.json`):
```json
{
  "ConnectionStrings": {
    "RedisConnection": "localhost:6379,abortConnect=false"
  }
}
```

---
## 4. Authentication & Authorization
### Identity
- `AppUser : IdentityUser` adds: `FirstName`, `LastName`, `ProfileImageUrl`, `IsBanned`, `BanCount`, `LastIpAddress`, `BanReason`.
- Unique index on `UserName` enforced in `AuthDbContext`.
- Roles: `Admin`, `User`, `Business`.

### JWT
- Claims added: `sub`, `email`, `nameidentifier`, `name`, plus each role.
- Token lifespan: `AccessTokenMinutes` from config (default 60).
- `AuthResponse` returns: `AccessToken`, `ExpiresUtc`, `User` (`UserDto`).

### Password Flows
- Register: validates uniqueness of normalized `UserName` manually. Usernames are reserved for 12 hours pending verification.
- Login: email + password; uses `CheckPasswordSignInAsync` with lockout. Checks `IsBanned` (403) and `EmailConfirmed` (403). Users cannot login until verified.
- Login: email + password; uses `CheckPasswordSignInAsync` with lockout. Checks `IsBanned` and returns 403 if banned.
- Forgot Password: generates 6-digit code, stores in Redis (15m), emails via SES.
- Reset Password: validates 6-digit code from Redis, then resets password.
- Change Password: requires existing password & JWT auth.
- **Google OAuth**:
  - Client-side flow: Client sends `idToken` to server.
  - Server verifies token with Google.
  - **Account Merging**: If email exists, logs into existing account (email verified automatically).
  - **Registration**: If email is new, creates account with random username (based on name/email).
- **Apple OAuth**:
  - Similar flow to Google.
  - **Name Privacy**: Apple only sends `firstName` and `lastName` on the **first** login.
  - Server validates `identityToken` against Apple's JWKS (cached).

### Authorization
- Most controllers require `[Authorize]` at class level.
- Admin endpoints require `[Authorize(Roles = "Admin")]` (e.g., Tag Moderation).

---
## 5. Database Contexts
### AuthDbContext
DbSets:
- `Follows` (composite PK: `{FollowerId, FolloweeId}`)
- `UserBlocks` (composite PK: `{BlockerId, BlockedId}`)
- `IpBans` (PK: `IpAddress`)
Relationships:
- `Follow.Follower` and `Follow.Followee` each `Restrict` delete.
- `UserBlock.Blocker` and `UserBlock.Blocked` each `Cascade` delete.
Indexes:
- Unique index on `AppUser.UserName`.
- `UserActivityLogs` (Id, UserId, Date, LoginCount, LastActivityUtc).
- `DailySystemMetrics` (Id, Date, MetricType, Value, Dimensions).


### AppDbContext
DbSets:
- `Pings`, `PingGenres`, `PingActivities`, `Reviews`, `Tags`, `ReviewTags`, `Events`, `EventAttendees`, `Favorited`

Seed Data:
- `PingGenre` seeded with ids 1–20 (Sports, Food, Outdoors, Art, etc.).

Indexes:
- `Ping (Location)` Spatial Index. NTS `Point`.
- **PostgreSQL Only**: `Ping (SearchVector)` GIN Index for Full-Text Search.
- Unique composite `Favorited (UserId, PingId)` prevents duplicate favorites.
- Unique `PingGenre.Name`.
- Unique composite `PingActivity (PingId, Name)`.
- Review uniqueness: No unique constraint (allows multiple reviews/checkins per user).
- Unique `Tag.Name`.
- Composite PK `ReviewTag (ReviewId, TagId)`.
- Composite PK `EventAttendee (EventId, UserId)`.

Relationships & Cascades:
- `Favorited` → `Ping` cascade delete.
- `PingActivity` → `Ping` cascade delete.
- `PingActivity` → `PingGenre` restrict delete.
- `Review` → `PingActivity` cascade.
- `ReviewTag` → `Review` cascade, `ReviewTag` → `Tag` cascade.
- `EventAttendee` → `Event` cascade.

Property Configuration:
- `Review.Content` max length 1000.
- Timestamp defaults via `CURRENT_TIMESTAMP` for `Review.CreatedAt`.
- `Tag.Name` max 30.

---
## 6. Domain Models (Summaries)
| Entity        | Key                | Core Fields                                                                                                              | Navigation                             | Notes                                                                                                                 |
| ------------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------ | -------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| AppUser       | `IdentityUser`     | FirstName, LastName, ProfileImageUrl, IsBanned, BanCount, LastIpAddress, BanReason, LastLoginUtc, CreatedUtc |(Friends)                              | Stored in Auth DB; Unverified users deleted after 12h; PingsPrivacy/ReviewsPrivacy settings |
| IpBan         | IpAddress          | Reason, CreatedAt, ExpiresAt                                                                                             | (None)                                 | Stores banned IPs                                                                                                     |
| Follow        | (FollowerId, FolloweeId) | CreatedAt                                                                                                                | Follower, Followee                     | Unidirectional follow. Friendship = Mutual Follow.                                                                    |
| Ping          | Id                 | Name, Address, **Location (Point)**, Latitude*, Longitude*, OwnerUserId, Visibility (Public/Private/Friends), Type (Verified/Custom), CreatedUtc, GooglePlaceId? | PingActivities                        | OwnerUserId is string (Identity FK); Visibility controls access; Type determines duplicate logic; *Lat/Lon are computed props mapped to Location (SRID 4326) |
| Favorited     | Id                 | UserId, PingId                                                                                                          | Ping                                  | Unique per user per ping; cascade deletes with Ping                                                                 |
| PingGenre     | Id                 | Name                                                                                                                     | PingActivities                         | Seeded                                                                                                                |
| PingActivity  | Id                 | PingId, PingGenreId?, Name, CreatedUtc                                                                               | Ping, PingGenre, Reviews, CheckIns     | Unique per ping by Name                                                                                              |
| Review        | Id                 | UserId, UserName, PingActivityId, Rating, Type, Content, ImageUrl, CreatedAt, Likes                      | PingActivity, ReviewTags               | Rating required; Type (Review/CheckIn); First post is Review, subsequent are CheckIns; ImageUrl required                              |
| Event         | Id                 | Title, Description?, IsPublic, StartTime, EndTime, Location, PingId?, CreatedById, CreatedAt, Latitude, Longitude                 | Attendees (EventAttendee), Ping?       | Status computed dynamically; PingId links to Ping entity (optional)                                                                   |
| EventAttendee | (EventId, UserId)  | JoinedAt                                                                                                                 | Event                                  | Many-to-many join                                                                                                     |
| Tag           | Id                 | Name, IsApproved, IsBanned, CanonicalTagId                                                                               | ReviewTags                             | Used for categorizing reviews                                                                                         |
| UserBlock     | (BlockerId, BlockedId) | CreatedAt                                                                                                                | Blocker, Blocked                       | Separation of concern for blocking users; masks content bidirectionally                                               |
| Report        | Id                 | ReporterId, TargetId, TargetType, Reason, Description, Status, CreatedAt                                                 | (None - Polymorphic)                    | TargetId is string; TargetType enum (Ping/PingActivity/Review/Profile/Bug); Status enum (Pending/Reviewed/Dismissed)      |
| PingClaim     | Id                 | UserId, PingId, Proof, Status, CreatedUtc, ReviewedUtc, ReviewerId                                                      | Ping                                   | Tracks ownership claim requests; Logic FK to User                                                                     |
| UserActivityLog | Id               | UserId, Date, LoginCount, LastActivityUtc                                                                                | User                                   | Tracks daily unique logins per user for analytics                                                                     |
| DailySystemMetric | Id             | Date, MetricType, Value, Dimensions                                                                                      | (None)                                 | Stores historical aggregated stats (DAU, WAU, MAU, etc.)                                                              |

---
## 7. DTO Contracts
### Activities
- `PingActivitySummaryDto(Id, Name, PingGenreId?, PingGenreName?)`
- `CreatePingActivityDto(PingId, Name, PingGenreId?)`
- `PingActivityDetailsDto(Id, PingId, Name, PingGenreId?, PingGenreName?, CreatedUtc, WarningMessage?)`
- `PingGenreDto(Id, Name)` / `CreatePingGenreDto(Name)`

### Auth
- `RegisterDto(Email, Password, FirstName, LastName, UserName)`
- `LoginDto(Email, Password)`
- `ForgotPasswordDto(Email)`
- `VerifyEmailDto(Email, Code)`
- `ResendVerificationDto(Email)`
- `ResetPasswordDto(Email, Code, NewPassword)`
- `ChangePasswordDto(CurrentPassword, NewPassword)`
- `UserDto(Id, Email, DisplayName, FirstName, LastName, ProfileImageUrl, Roles[])`
- `AuthResponse(AccessToken, ExpiresUtc, User)`
- `JwtOptions(Key, Issuer, Audience, AccessTokenMinutes)`

### Events
- `EventDto(Id, Title, Description?, IsPublic, StartTime, EndTime, Location, CreatedBy(UserSummaryDto), CreatedAt, Attendees[List<UserSummaryDto>], Status, Latitude, Longitude, PingId?, EventGenreId?, EventGenreName?, IsAdHoc)`
- `UserSummaryDto(Id, UserName, FirstName, LastName)`
- `CreateEventDto(Title, Description?, IsPublic, StartTime, EndTime, Location, Latitude, Longitude, PingId?, EventGenreId?)`
- `UpdateEventDto(Title?, Description?, IsPublic?, StartTime?, EndTime?, Location?, Latitude?, Longitude?, PingId?, EventGenreId?, ImageUrl?, ThumbnailUrl?, Price?)`
- `EventFilterDto(MinPrice?, MaxPrice?, FromDate?, ToDate?, GenreId?, Latitude, Longitude, RadiusKm)`
- `EventCommentDto(Id, Content, CreatedAt, UserId, UserName, UserProfileImageUrl, UserProfileThumbnailUrl)`
- `CreateEventCommentDto(Content)`
- `CreateEventDto` also includes fields: `ImageUrl, ThumbnailUrl, Price`.
- `ReviewDto`, `AppUser`, `EventDto` include `ThumbnailUrl`. `EventDto` includes `IsHosting`, `FriendThumbnails`, `Price`, `ImageUrl`.

### Friends & Blocks
- `FriendSummaryDto(Id, UserName, FirstName, LastName, ProfileImageUrl?)`
- `BlockDto(BlockedUserId, BlockedUserName, BlockedAt)`

### Pings
- `PingVisibility` enum: `Private = 0`, `Friends = 1`, `Public = 2`
- `PingType` enum: `Custom = 0`, `Verified = 1`
- `UpsertPingDto(Name, Address?, Latitude, Longitude, Visibility, Type, GooglePlaceId?)`
- `PingDetailsDto(Id, Name, Address, Latitude, Longitude, Visibility, Type, IsOwner, IsFavorited, Favorites, Activities[PingActivitySummaryDto], PingGenre?, ClaimStatus?, IsClaimed, GooglePlaceId?)`

### Profiles
- `ProfileDto(Id, DisplayName, FirstName, LastName, ProfilePictureUrl?)`
- `PersonalProfileDto(Id, DisplayName, FirstName, LastName, ProfilePictureUrl, Email, Events[], Pings[], Reviews[], Roles[])`

### Reviews
- `UserReviewsDto(Review, History[])` - Grouped response
- `ReviewDto(Id, Rating, Content?, UserId, UserName, ProfilePictureUrl, ImageUrl, CreatedAt, Likes, IsLiked, Tags[])`
- `CreateReviewDto(Rating, Content?, ImageUrl, Tags[])`
- `ExploreReviewDto(ReviewId, PingActivityId, PingId, PingName, PingAddress, ActivityName, PingGenreName?, Latitude, Longitude, Rating, Content?, UserId, UserName, ProfilePictureUrl, ImageUrl, CreatedAt, Likes, IsLiked, Tags[], IsPingDeleted)`
- `ExploreReviewsFilterDto(Latitude?, Longitude?, RadiusKm?, SearchQuery?, PingGenreIds?[], PageSize, PageNumber)`

### Tags
- `TagDto(Id, Name, Count, IsApproved, IsBanned)`

### Recommendations
- `RecommendationDto(Name, Address, Latitude?, Longitude?, Source, LocalPingId?)`

### Pagination
- `PaginationParams(PageNumber=1, PageSize=20)` - Max PageSize 50
- `PaginationParams(PageNumber=1, PageSize=20)` - Max PageSize 50
- `PaginatedResult<T>(Items[], TotalCount, PageNumber, PageSize, TotalPages)`

### Reporting
- `CreateReportDto(TargetId, TargetType, Reason, Description?)` - TargetId is string.
- `Report(Id, ReporterId, TargetId, TargetType, Reason, Description?, CreatedAt, Status)`

### Business & Claims
- `CreateClaimDto(PingId, Proof)`
- `ClaimDto(Id, PingId, PingName, UserId, UserName, Proof, Status, CreatedUtc, ReviewedUtc)`

### Analytics
- `DashboardStatsDto(Dau, Wau, Mau, TotalUsers, NewUsersToday)`
- `TrendingPingDto(PingId, Name, ReviewCount, CheckInCount, TotalInteractions)`
- `ModerationStatsDto(PendingReports, BannedUsers, BannedIps, RejectedReviews)`

### Business Analytics
- `BusinessPingAnalyticsDto(PingId, TotalViews, TotalFavorites, TotalReviews, AvgRating, EventCount, ViewsHistory, PeakHours)`
- `PingDailyStatDto(Date, Value)`



---
## 8. Services (Service Layer Architecture)

**Architecture Pattern**: The application follows the "Thin Controller, Fat Service" pattern.

### Service Interfaces & Implementations

#### TokenService (`ITokenService`)
- Generates JWT with configured options, includes roles and identity claims.

#### PingService (`IPingService`)
- Manages pings, geo-spatial search, favoriting, and visibility rules.
- **AI Moderation**: Checks Ping Name for offensive content (Custom pings).

#### GooglePingsService (`IPingNameService`)
- Fetches official ping names from Google Places API for verified pings.

#### RecommendationService (`RecommendationService`)
- Provides AI-powered ping recommendations based on "vibe" using Semantic Kernel + Google Pings fallback.

#### EventService (`IEventService`)
- Manages event lifecycle and attendance.

#### ReviewService (`IReviewService`)
- Manages reviews, check-ins, likes, and feed retrieval.
- **Methods**: `GetFriendsFeedAsync` (paginated friends' reviews), `GetExploreReviewsAsync`.
- **AI Moderation**: Checks Review Content and new Tags for offensive content.
- **Tag Integration**: Automatically creates or links tags during review creation.

#### TagService (`ITagService`)
- Manages tags and admin moderation.
- Methods: `GetPopularTagsAsync`, `SearchTagsAsync`, `ApproveTagAsync`, `BanTagAsync`, `MergeTagAsync`.

#### PingActivityService (`IPingActivityService`)
- Manages activity creation.
- **AI Moderation**: Checks Activity Name.
- **Semantic Deduplication**: Uses AI to detect and merge duplicate activities (e.g., "Hoops" -> "Basketball"). Returns `WarningMessage` to frontend.

#### FollowService (`IFollowService`)
- Manages follower relationships (follow, unfollow, get followers/following).
- **Mutuals**: Users who follow each other are considered "Friends".

#### BlockService (`IBlockService`)
- Manages user blocking interactions.
- Stores blocks in `AuthDbContext`.
- **Bidirectional Filtering**: Ensures neither user can see the other's content (reviews, events, profile).
- **Friendship Removal**: Automatically removes mutual follows upon blocking.

#### ProfileService (`IProfileService`)
- Manages user profiles and search.

#### AuthService (`IAuthService`)
- Manages registration, login, and password flows.

#### ModerationService (`IModerationService`)
- **Implementation**: `OpenAIModerationService`.
- **Method**: `CheckContentAsync(text)`.
- **Backing**: OpenAI Free Moderation API (`v1/moderations`).
- **Behavior**: Returns `ModerationResult(IsFlagged, Reason)`. Rejects offensive content with `ArgumentException`.

#### SemanticService (`ISemanticService`)
- **Implementation**: `OpenAISemanticService`.
- **Method**: `FindDuplicateAsync(newItem, existingItems)`.
- **Backing**: OpenAI Chat Completion (GPT-3.5/4o).
- **Purpose**: Identifies semantic duplicates for activity merging.

#### ReportService (`IReportService`)
- Manages reporting logic.
- Methods: `CreateReportAsync` (User), `GetReportsAsync` (Admin).
- **Polymorphism**: Handles reports for multiple target types (Ping, Review, etc.).

#### BusinessService (`IBusinessService`)
- Manages ping claim requests.
- **Methods**: `SubmitClaimAsync`, `GetPendingClaimsAsync`, `ApproveClaimAsync` (Transfers ownership + adds Role), `RejectClaimAsync`.
- **BanningService** (`IBanningService`):
  - Manages User and IP bans.
  - Integration with Redis for fast middleware checks.
  - Support for temporary or permanent IP bans.

#### UnverifiedUserCleanupService (Hosted Service)
- **Purpose**: Background task that runs hourly.
- **Logic**: Deletes user accounts where `EmailConfirmed == false` AND `CreatedUtc` > 12 hours ago.
- **Effect**: Releases reserved usernames if they are not verified in time.

#### EmailService (`IEmailService`)
- **Implementation**: `SesEmailService`.
- **Backing**: Amazon SES.
- **Methods**: `SendEmailAsync(to, subject, body)`.
- **Rate Limit**: 5 emails per hour per recipient (enforced in `AuthService`).

---
## 9. Controllers & Endpoints
Notation: `[]` = route parameter, `(Q)` = query parameter, `(Body)` = JSON body. Auth: A=Requires JWT, An=AllowAnonymous, Adm=Admin Only.

### ActivitiesController (`/api/activities`)

### 9.1. Versioning Strategy
- **Version**: 1.0 (Default)
- **Support**: 
  - **URL Path**: `/api/v1/resource` (Preferred)
  - **Query String**: `/api/resource?api-version=1.0`
  - **Header**: `X-Api-Version: 1.0`
- **Compatibility**: The API defaults to v1.0 if no version is specified. Existing routes (e.g., `/api/activities`) function as aliases for v1.0.

### PingActivitiesController (`/api/ping-activities`)
| Method | Route           | Auth | Body                | Returns              | Notes                                                |
| ------ | --------------- | ---- | ------------------- | -------------------- | ---------------------------------------------------- |
| POST   | /api/ping-activities | A    | `CreatePingActivityDto` | `PingActivityDetailsDto` | Validates ping, optional genre, uniqueness per ping |

### PingGenresController (`/api/ping-genres`)
| Method | Route                    | Auth | Body                    | Returns             | Notes                                                 |
| ------ | ------------------------ | ---- | ----------------------- | ------------------- | ----------------------------------------------------- |
| GET    | /api/ping-genres      | A    | —                       | `PingGenreDto[]` | Ordered by Name                                       |
| POST   | /api/ping-genres      | A    | `CreatePingGenreDto` | `PingGenreDto`   | Enforces unique name (case-insensitive)               |
| DELETE | /api/ping-genres/{id} | A    | —                       | 204 NoContent       | Remove genre (restrict prevents cascade on activities) |

### AdminController (`/api/admin`)
**Requires `Admin` Role**

| Method | Route                       | Query/Body                      | Returns     | Notes                                   |
|:-------|:----------------------------|:--------------------------------|:------------|:----------------------------------------|
| DELETE | /pings/{id}               | -                               | Msg         | Soft delete (sets IsDeleted=true)       |
| DELETE | /reviews/{id}              | -                               | Msg         | Hard delete                             |
| DELETE | /events/{id}               | -                               | Msg         | Hard delete                             |
| DELETE | /activities/{id}           | -                               | Msg         | Hard delete                             |
| DELETE | /tags/{id}                 | -                               | Msg         | Hard delete                             |
| POST   | /users/{id}/ban            | `?reason=...`                   | Msg         | Ban user by ID                          |
| POST   | /users/ban                 | `?username=...&reason=...`      | Msg         | Ban user by Username                    |
| POST   | /users/{id}/unban          | -                               | Msg         | Unban user by ID                        |
| POST   | /users/unban               | `?username=...`                 | Msg         | Unban user by Username                  |
| DELETE | /users/{id}                | -                               | Msg         | Delete user account                     |
| POST   | /users/make-admin          | `?email=...`                    | Msg         | Grant Admin role                        |
| POST   | /moderation/ip/ban         | `IpBanRequest`                  | Msg         | Ban IP address                          |
| POST   | /moderation/ip/unban       | `IpUnbanRequest`                | Msg         | Unban IP address                        |
| GET    | /business/claims           | -                               | List<Claim> | Get pending business claims             |
| POST   | /business/claims/{id}/approve | -                            | Msg         | Approve claim & transfer ownership      |
| POST   | /business/claims/{id}/reject  | -                            | Msg         | Reject claim                            |
| POST   | /tags/{id}/approve         | -                               | 200 OK      | Approve tag                             |
| POST   | /tags/{id}/ban             | -                               | 200 OK      | Ban tag                                 |
| POST   | /tags/{id}/merge/{targetId}| -                               | 200 OK      | Merge tag into target                   |

### AuthController (`/api/auth`)
| Method | Route                     | Auth | Body                | Returns           | Notes                                     |
| ------ | ------------------------- | ---- | ------------------- | ----------------- | ----------------------------------------- |
| POST   | /api/auth/register        | An   | `RegisterDto`       | 200 Message       | Sends verification code (Rate Limit: 5/hr) |
| POST   | /api/auth/login           | An   | `LoginDto`          | `AuthResponse`    | Blocks if `EmailConfirmed=false`          |
| POST   | /api/auth/verify-email    | An   | `VerifyEmailDto`    | `AuthResponse`    | Verifies email & Logs user in             |
| POST   | /api/auth/verify-email/resend | An | `ResendVerificationDto` | 200 Message   | Resends code if unverified (Rate Limit: 5/hr) |
| GET    | /api/auth/me              | A    | —                   | `UserDto`         | Uses `ClaimTypes.NameIdentifier`          |
| POST   | /api/auth/password/forgot | An   | `ForgotPasswordDto` | Dev returns code  | Avoids enumeration (Rate Limit: 5/hr)     |
| POST   | /api/auth/password/reset  | An   | `ResetPasswordDto`  | 200 status        | Validates code from Redis                 |
| POST   | /api/auth/password/change | A    | `ChangePasswordDto` | 200 status        | Validates current password                |
| POST   | /api/auth/google          | An   | `GoogleLoginDto`    | `AuthResponse`    | Google Sign-In (Login or Register)        |
| POST   | /api/auth/apple           | An   | `AppleLoginDto`     | `AuthResponse`    | Apple Sign-In (Login or Register)         |
| DELETE | /api/auth/me              | A    | —                   | Msg               | Self-delete account                       |

### BlocksController (`/api/blocks`)
| Method | Route                       | Auth | Body | Returns     | Notes                                           |
| ------ | --------------------------- | ---- | ---- | ----------- | ----------------------------------------------- |
| POST   | /api/blocks/{userId}        | A    | —    | 200 OK      | Blocks user, removes friendship, hides content  |
| DELETE | /api/blocks/{userId}        | A    | —    | 200 OK      | Unblocks user                                   |
| GET    | /api/blocks                 | A    | —    | `BlockDto[]`| List of users blocked by current user           |

### BusinessController (`/api/business`)
| Method | Route                       | Auth | Body | Returns     | Notes                        |
| ------ | --------------------------- | ---- | ---- | ----------- | ---------------------------- |
| POST   | /api/business/claim         | A    | `CreateClaimDto` | `PingClaim` | Submit claim |
| GET    | /api/business/claims        | Adm  | —    | `ClaimDto[]`| List pending claims |
| POST   | /api/business/claims/{id}/approve | Adm | — | 200 | Transfers ownership, adds Business role |
| POST   | /api/business/claims/{id}/reject  | Adm | — | 200 | Rejects claim |
| GET    | /api/business/analytics/{id}| Bus  | —    | AnalyticsObj| Ping Owner/Admin only |

### EventsController (`/api/Events`)
| Method | Route                                            | Auth | Body             | Returns      | Notes                                     |
| ------ | ------------------------------------------------ | ---- | ---------------- | ------------ | ----------------------------------------- |
| POST   | /api/Events/create                               | A    | `CreateEventDto` | `EventDto`   | Validates times (future, duration ≥15m)   |
| GET    | /api/Events/{id}                                 | A    | —                | `EventDto`   | Loads attendees + creator                 |
| GET    | /api/Events/mine (Q: pageNumber, pageSize)       | A    | —                | `PaginatedResult<EventDto>` | Events created by requester               |
| GET    | /api/Events/attending (Q: pageNumber, pageSize)  | A    | —                | `PaginatedResult<EventDto>` | Where user is attendee                    |
| POST   | /api/Events/{id}/attend                          | A    | —                | 200          | Prevents attending own event & duplicates |
| POST   | /api/Events/{id}/leave                           | A    | —                | 200          | Removes attendee row                      |
| DELETE | /api/Events/{id}                                 | A    | —                | 200          | Only creator                              |
| PATCH  | /api/Events/{id}                                 | A    | `UpdateEventDto` | 200          | Partial updates                           |
| PATCH  | /api/Events/{id}                                 | A    | `UpdateEventDto` | 200          | Partial updates (incl Price, Image)       |
| GET    | /api/Events/public (Q: EventFilterDto)           | A    | —                | `PaginatedResult<EventDto>` | Filters: Price, Date, Genre, Location     |
| POST   | /api/Events/{id}/comments                        | A    | `CreateEventCommentDto` | `EventCommentDto` | Add comment (Max 100 words, Moderated) |
| GET    | /api/Events/{id}/comments (Q: pageNumber, pageSize) | A | —               | `PaginatedResult` | Get comments (Newest first)               |
| DELETE | /api/Events/comments/{id}                        | A    | —                | 204          | Delete comment (Owner only)               |

### FollowsController (`/api/follows`)
| Method | Route                          | Auth | Body | Returns              | Notes                                                    |
| ------ | ------------------------------ | ---- | ---- | -------------------- | -------------------------------------------------------- |
| GET    | /api/follows/followers (Q: pageNumber, pageSize) | A | — | `PaginatedResult<FriendSummaryDto>` | List who follows me |
| GET    | /api/follows/following (Q: pageNumber, pageSize) | A | — | `PaginatedResult<FriendSummaryDto>` | List who I follow |
| GET    | /api/follows/mutuals (Q: pageNumber, pageSize)   | A | — | `PaginatedResult<FriendSummaryDto>` | List mutual connections (Friends) |
| POST   | /api/follows/{targetId}        | A    | —    | 200                  | Follow a user |
| DELETE | /api/follows/{targetId}        | A    | —    | 200                  | Unfollow a user |

### PingsController (`/api/pings`)
| Method | Route                                                                              | Auth | Body             | Returns             | Notes                                                                                                           |
| ------ | ---------------------------------------------------------------------------------- | ---- | ---------------- | ------------------- | --------------------------------------------------------------------------------------------------------------- |
| POST   | /api/pings                                                                        | A    | `UpsertPingDto` | `PingDetailsDto`   | Daily per-user creation limit (10); Verified type requires address; Private/Friends auto-converted to Custom    |
| GET    | /api/pings/{id}                                                                   | A    | —                | `PingDetailsDto`   | Respects visibility: Private (owner only), Friends (owner + friends), Public (all)                              |
| GET    | /api/pings/nearby (Q: lat,lng,radiusKm,activityName,pingGenre,visibility,type, pageNumber, pageSize) | A    | —                | `PaginatedResult<PingDetailsDto>` | Geo-search with optional filters: visibility (Public/Private/Friends), type (Verified/Custom), activity filters |
| POST   | /api/pings/favorited/{id}                                                         | A    | —                | 200 OK              | Adds ping to favorites; prevents duplicates, validates ping exists                                            |
| DELETE | /api/pings/favorited/{id}                                                         | A    | —                | 204 NoContent       | Removes ping from favorites; idempotent                                                                        |
| GET    | /api/pings/favorited (Q: pageNumber, pageSize)                                    | A    | —                | `PaginatedResult<PingDetailsDto>` | Returns all favorited pings with activities                                                                    |

### ProfilesController (`/api/profiles`)
| Method | Route                          | Auth | Body | Returns              | Notes                                               |
| ------ | ------------------------------ | ---- | ---- | -------------------- | --------------------------------------------------- |
| GET    | /api/profiles/me               | A    | —    | `PersonalProfileDto` | Current user profile                                |
| POST   | /api/profiles/me/image         | A    | Form | `{ url: string }`    | Upload profile picture (Multipart/Form-Data)        |
| GET    | /api/profiles/search?username= | A    | —    | `ProfileDto[]`       | Prefix search on normalized username; excludes self |
| GET    | /api/profiles/{id}             | A    | —    | `ProfileDto`         | Full public profile details                         |
| GET    | /api/profiles/{id}/summary     | A    | —    | `QuickProfileDto`    | Lightweight profile summary for headers/cards       |
| GET    | /api/profiles/{id}/pings      | A    | —    | `PaginatedResult`    | Pings created by user (respects privacy)           |
| GET    | /api/profiles/{id}/reviews     | A    | —    | `PaginatedResult`    | Reviews by user (respects privacy)                  |
| GET    | /api/profiles/{id}/events      | A    | —    | `PaginatedResult`    | Events created by user (respects privacy)           |
| GET    | /api/profiles/{id}/places      | A    | —    | `PaginatedResult<PlaceReviewSummaryDto>` | Grouped places reviewed by user (Strict Privacy)        |
| GET    | /api/profiles/me/places        | A    | —    | `PaginatedResult<PlaceReviewSummaryDto>` | All places reviewed by current user (No filtering)      |
| GET    | /api/profiles/{id}/places/{pId}/reviews | A | — | `PaginatedResult<ReviewDto>` | Drill down into reviews for a place (Strict Privacy) |
| GET    | /api/profiles/me/places/{pId}/reviews | A | — | `PaginatedResult<ReviewDto>` | Drill down into own reviews (No filtering) |
| GET    | /api/profiles/{id}/likes       | A    | —    | `PaginatedResult`    | Reviews liked by user (respects privacy)            |
| GET    | /api/profiles/me/likes         | A    | —    | `PaginatedResult`    | Reviews liked by me                                 |
| PATCH  | /api/profiles/me/privacy       | A    | `PrivacySettingsDto` | 200 OK | Update privacy (`ReviewsPrivacy`, `PingsPrivacy`, `LikesPrivacy`) |

### ImagesController (`/api/images`)
| Method | Route          | Auth | Body | Returns            | Notes |
| ------ | -------------- | ---- | ---- | ------------------ | ----- |
| POST   | /api/images (Q: folder) | A | Form | `{ originalUrl, thumbnailUrl }` | Resize to 500x500 thumbnail. Validates size/type. |

### ReportsController (`/api/reports`)
| Method | Route           | Auth | Body               | Returns              | Notes                                               |
| ------ | --------------- | ---- | ------------------ | -------------------- | --------------------------------------------------- |
| POST   | /api/reports    | A    | `CreateReportDto`  | `Report`             | Polymorphic (TargetType enum). Returns 201.         |
| GET    | /api/reports    | Adm  | —                  | `PaginatedResult`    | Admin only. Filters by status/page.                 |

### ReviewsController (`/api/reviews`)
| Method | Route                                                      | Auth | Body                      | Returns              | Notes                                              |
| ------ | ---------------------------------------------------------- | ---- | ------------------------- | -------------------- | -------------------------------------------------- |
| POST   | /api/reviews/{pingActivityId}                             | A    | `CreateReviewDto`         | `ReviewDto`          | Creates review for activity                        |
| GET    | /api/reviews/{pingActivityId}?scope={mine/friends/global}&pageNumber&pageSize | A    | —                         | `PaginatedResult<UserReviewsDto>`   | Returns reviews grouped by user (Review + History) |
| GET    | /api/reviews/explore                                       | A    | `ExploreReviewsFilterDto` | `PaginatedResult<ExploreReviewDto>` | Paginated review feed with filters                 |
| POST   | /api/reviews/{reviewId}/like                               | A    | —                         | 200 OK               | Like a review (idempotent)                         |
| DELETE | /api/reviews/{reviewId}/like                               | A    | —                         | 204 NoContent        | Unlike a review (idempotent)                       |
| GET    | /api/reviews/liked (Q: pageNumber, pageSize)               | A    | —                         | `PaginatedResult<ExploreReviewDto>` | User's liked reviews                               |
| GET    | /api/reviews/my-reviews (Q: pageNumber, pageSize)          | A    | —                         | `PaginatedResult<ExploreReviewDto>` | User's own reviews                                 |
| GET    | /api/reviews/friends (Q: pageNumber, pageSize)             | A    | —                         | `PaginatedResult<ExploreReviewDto>` | Friends' reviews sorted by date                    |

### TagsController (`/api/tags`)
| Method | Route                                        | Auth | Body | Returns     | Notes                        |
| ------ | -------------------------------------------- | ---- | ---- | ----------- | ---------------------------- |
| GET    | /api/tags/popular (Q: count)                 | An   | —    | `TagDto[]`  | Popular approved tags        |
| GET    | /api/tags/search (Q: q, count)               | A    | —    | `TagDto[]`  | Search tags                  |
| POST   | /api/admin/tags/{id}/approve                 | Adm  | —    | 200         | Admin: Approve tag           |
| POST   | /api/admin/tags/{id}/ban                     | Adm  | —    | 200         | Admin: Ban tag               |
| POST   | /api/admin/tags/{id}/merge/{targetId}        | Adm  | —    | 200         | Admin: Merge source to target|

### RecommendationController (`/api/recommendations`)
| Method | Route                                            | Auth | Body | Returns               | Notes                                   |
| ------ | ------------------------------------------------ | ---- | ---- | --------------------- | --------------------------------------- |
| GET    | /api/recommendations (Q: vibe, lat, lng, radius) | A    | —    | `RecommendationDto[]` | AI-powered search. Radius default 10km. |
 
### ModerationController (`/api/moderation`)
| Method | Route                       | Auth | Body | Returns     | Notes                        |
| ------ | --------------------------- | ---- | ---- | ----------- | ---------------------------- |
| POST   | /api/moderation/ban/user/{id} | Adm | (Q: reason) | 200 | Sets IsBanned, checks limit |
| POST   | /api/moderation/unban/user/{id} | Adm | — | 200 | Unbans user |
| POST   | /api/moderation/ban/ip      | Adm | `IpBanRequest` | 200 | Bans IP (Redis + DB) |
| POST   | /api/moderation/unban/ip    | Adm | `IpUnbanRequest` | 200 | Unbans IP |

### Admin AnalyticsController (`/api/admin/analytics`)
**Requires `Admin` Role**
| Method | Route                       | Auth | Returns                   | Notes                                                |
| ------ | --------------------------- | ---- | ------------------------- | ---------------------------------------------------- |
| GET    | /dashboard                  | Adm  | `DashboardStatsDto`       | Real-time stats: DAU, WAU, MAU, Total Users          |
| GET    | /trending                   | Adm  | `TrendingPingDto[]`      | Top 10 pings by interactions (Reviews + CheckIns) in last 7 days |
| GET    | /moderation                 | Adm  | `ModerationStatsDto`      | Pending reports, banned user count, banned IP count  |
| GET    | /growth (Q: type, days)     | Adm  | `DailySystemMetric[]`     | Historical growth data (e.g., DAU over 30 days)      |
| POST   | /compute-now                | Adm  | String (Msg)              | Manually trigger daily metrics computation           |

---
## 10. Validation & Business Rules

### Authentication
- Username must be unique (case-insensitive)
- Password flows do not leak account existence (uniform responses for missing users)
- JWT tokens expire after configured minutes (default: 60)
- Account lockout enforced after failed login attempts

### Pings
- Daily creation limit: 10 per user (Redis-backed)
- Privacy enforcement:
  - **Private**: Only owner can view
  - **Friends**: Owner + accepted friends can view
  - **Public**: Everyone can view
- Duplicate detection (Public pings only):
  - Verified: Address match only
  - Custom: Coordinates within ~50m (0.0005 degrees)
  - Private/Friends: No duplicate checking

### PingType Business Rules
- **Verified Pings**:
  - Must be Public visibility
  - Address is required
  - Name auto-fetched from Google Pings API
  - Duplicates checked by exact address match only
  - **Ownership Transfer**: Can be claimed via Business workflows; `PingDetailsDto.ClaimStatus` will reflect pending/approved claims for the viewer.
  
- **Custom Pings**:
  - Can be any visibility (Public/Friends/Private)
  - Address is optional
  - User-provided name is used
  - Duplicates checked by coordinate proximity (~50m)

- **Automatic Conversions**:
  - Private/Friends pings → automatically converted to Custom type
  - Prevents unnecessary Google API calls for non-public pings

### Reviews
- Scope filtering: mine/friends/global
- **Review vs CheckIn**: First post by user is `Review`, subsequent posts are `CheckIn`.
- **Grouping**: API returns reviews grouped by user to show history.
- Likes are idempotent (re-liking/re-unliking does nothing)
- Batch `IsLiked` checking prevents N+1 queries
- Pagination: max 100 items per page (default 20)
- Rating required for both Reviews and CheckIns
- **Moderation**: Content is checked against OpenAI policies.

### Events
- Start time must be in future
- Duration must be ≥15 minutes
- Creators auto-join their events
- Only creator can delete event
- Cannot attend own event (creator is already an attendee)
- **Ping Linking**: Events can be optionally linked to a `Ping` via `PingId`.
  - If `PingId` is provided, the Event inherits `Location`, `Latitude`, and `Longitude` from the Ping.
  - Useful for "Quick Add" workflows where users select an existing ping.
- **IsAdHoc**: Computed flag (`PingId == null`). True if the event uses a custom/manual location.
- **Update Logic**:
  - Providing a new `PingId` links the event and overwrites location fields.
  - Providing manual location fields (`Location`, `Latitude`, `Longitude`) WITHOUT a `PingId` **unlinks** the event (`PingId` becomes null).
- **Moderation**: `Title`, `Description`, and `Location` (Ping Name) are moderated.
  - Violations result in `400 Bad Request` with `ArgumentException`.

### Activities
- Name must be unique per ping (case-insensitive)
- PingGenre is optional
- Cascade deletes when parent Ping is deleted
- **Moderation**: Content is checked.
- **Deduplication**: Semantically similar activities are merged (e.g. "Hoops" = "Basketball").

### Followers & Friends
- **Follow**: Unidirectional. User A can follow User B without approval (unless private, future feature).
- **Friend**: Defined as a **Mutual Follow** (A follows B AND B follows A).
- **Visibility**: "Friends Only" visibility now means "Mutuals Only".
- **Blocks**: Blocking removes follows in *both* directions.

### User Blocking
- **Blocking is final for friendship**: If two users are friends and one blocks the other, the friendship is immediately deleted (both rows).
- **Content Masking**:
  - Blocked users cannot see the blocker's Profile, Reviews, or Events.
  - The blocker cannot see the blocked user's content (Bidirectional masking).
  - API endpoints returning lists (e.g. `ExploreFeed`) filter out blocked content automatically.
  - Direct access endpoints (e.g. `GetProfile`) return `404 Not Found` if a block exists.

### Tags
- Tag moderation flags exist: `IsBanned`, `IsApproved`
- Canonical tag relationship via `CanonicalTagId`
- Admins can merge tags.

### CheckIns
- Merged into `Review` model via `ReviewType` enum.
- **Status**: Fully implemented.

---
## 11. Indexes, Seed Data, Performance Notes
- Geospatial queries use **NetTopologySuite** `IsWithinDistance` which leverages SQLite's R-Tree spatial index.
- Distance logic: 1 degree approx 111km. Service handles conversion.
- Event attendee queries are batched (N+1 fixed) in `EventMapper` to reduce database round-trips.
- Review `IsLiked` status is batch-checked to avoid N+1 queries.

---
## 12. Migration & EF Core Operations
### Auto Migrations
`Program.cs` executes `Database.Migrate()` for both contexts at startup.

### Manual Commands
### Manual Commands
We maintain **separate migration folders** for SQLite and PostgreSQL.

**PostgreSQL (AWS/Prod):**
```bash
# Set provider in .env or shell
$env:DatabaseProvider='Postgres' 
dotnet ef migrations add <Name> --context Ping.Data.Auth.AuthDbContext --output-dir Data/Auth/Migrations/Postgres
dotnet ef database update --context Ping.Data.Auth.AuthDbContext

dotnet ef migrations add <Name> --context Ping.Data.App.AppDbContext --output-dir Data/App/Migrations/Postgres
dotnet ef database update --context Ping.Data.App.AppDbContext
```

**SQLite (Local):**
```bash
# Default provider
dotnet ef migrations add <Name> --context Ping.Data.Auth.AuthDbContext
dotnet ef database update --context Ping.Data.Auth.AuthDbContext

dotnet ef migrations add <Name> --context Ping.Data.App.AppDbContext
dotnet ef database update --context Ping.Data.App.AppDbContext
```

---
## 13. Conventions & Extension Points
- Controllers use explicit route prefixes instead of `[Route("api/[controller]")]` in some cases for clarity (`ActivitiesController`: `api/activities`).
- **Enums**: All enums are serialized as **Strings** via `JsonStringEnumConverter` globally.
- DTOs use C# 9+ records for immutability; patch DTOs use nullable reference types.
- Service extension points: Add domain logic (e.g., tagging, moderation) via new scoped services injected into controllers.
- For geospatial improvements: consider EF Core function mapping to SQLite extensions or moving to PostGIS if precision/radius queries intensify.
- Status computation for Events kept in service; consider background job to denormalize if queries scale.

---
## 14. Redis & Caching

### Architecture
Redis is used for:
- **Distributed Caching**: Session state, temporary data storage
- **Rate Limiting**: Request counters with automatic expiration
- **Scalability**: Enables horizontal scaling across multiple server instances

### Configuration
Connection strings in `appsettings.json`:
```json
"ConnectionStrings": {
  "RedisConnection": "localhost:6379"
}
```

Development settings (`appsettings.Development.json`):
```json
"RedisConnection": "localhost:6379,abortConnect=false"
```

### RedisService (`IRedisService`)
**Purpose**: Abstraction layer for Redis operations

**Methods**:
- `SetAsync<T>(key, value, expiry?)` - Store value with optional TTL
- `GetAsync<T>(key)` - Retrieve value
- `DeleteAsync(key)` - Remove key
- `IncrementAsync(key, expiry?)` - Atomic counter increment
- `ExistsAsync(key)` - Check key existence
- `GetTtlAsync(key)` - Get time-to-live

**Implementation Details**:
- Uses `StackExchange.Redis` (v2.9.32)
- JSON serialization for complex objects
- Automatic key prefixing: `Ping:{key}`
- Connection multiplexing (singleton `IConnectionMultiplexer`)
- Error logging with graceful degradation

### Key Naming Conventions
- Rate limiting: `ratelimit:{clientId}:{timeWindow}`
- Place creation: `ratelimit:ping:create:{userId}:{date}`
- Session data: `Ping:{sessionId}`

### Health Check
On startup, `Program.cs` logs Redis connection status:
- ✓ Success: "Redis connected successfully"
- ✗ Failure: "Redis connection failed - rate limiting and caching will not work"

### Deployment Notes
- **Local Development**: Docker container (`docker run -d --name Ping-redis -p 6379:6379 redis:alpine`)
- **Production**: AWS ElastiCache for Redis or Azure Cache for Redis recommended
- **Fail-Fast**: Application requires Redis to be available for security-critical rate limiting

---
## 15. Rate Limiting

### Overview
Multi-layered rate limiting protects API from abuse and ensures fair resource allocation.

### Global Rate Limiting (`RateLimitMiddleware`)
**Algorithm**: Sliding window (per-minute buckets)

**Limits** (configurable in `appsettings.json`):
| Client Type                | Limit       | Scope                     |
| -------------------------- | ----------- | ------------------------- |
| Anonymous (IP-based)       | 100 req/min | All endpoints except auth |
| Authenticated (User-based) | 200 req/min | All endpoints except auth |
| Auth endpoints (IP-based)  | 5 req/min   | `/api/auth/*`             |

**Configuration**:
```json
"RateLimiting": {
  "GlobalLimitPerMinute": 1000,
  "AuthenticatedLimitPerMinute": 200,
  "AuthEndpointsLimitPerMinute": 5,
  "PingCreationLimitPerDay": 10
}
```

**Response Headers**:
- `X-RateLimit-Limit`: Maximum requests allowed
- `X-RateLimit-Remaining`: Requests remaining in current window
- `X-RateLimit-Reset`: Unix timestamp when limit resets

**429 Response** (Rate Limit Exceeded):
```json
{
  "error": "Rate limit exceeded",
  "message": "Too many requests. Please try again in 60 seconds.",
  "retryAfter": 60
}
```

### Domain-Specific Rate Limiting
**Ping Creation** (`PingService.CreatePingAsync`):
- Limit: 10 pings per user per day
- Storage: Redis key `ratelimit:ping:create:{userId}:{yyyy-MM-dd}`
- Expiry: 24 hours
- Error: `InvalidOperationException` with message "You've reached the daily limit for adding pings."

### Implementation Details
**Client Identification**:
- Authenticated requests: User ID from JWT claims (`ClaimTypes.NameIdentifier`)
- Anonymous requests: IP address from `X-Forwarded-For` header or `RemoteIpAddress`

**Graceful Degradation**:
- If Redis fails, middleware logs error and **allows request** (fail-open)
- Prevents Redis outages from taking down entire API
- Logged as warning for monitoring

### Monitoring Recommendations
- Track rate limit violations per client
- Alert on high 429 response rates
- Monitor Redis memory usage for counter keys
- Set up Redis key expiration monitoring

---
## 16. Content Moderation & AI

### Moderation
- **Scope**: User-generated text (Reviews, Ping Names, Activity Names, Tags).
- **Service**: OpenAI Moderation API (Free).
- **Policy**:
  - Hate, Harassment, Self-harm, Violence, Sexual content => Rejected.
  - Error: 400 Bad Request with "Content rejected: Reason".
  - Fail-Open: If moderation API fails, we allow content (except on explicit safety triggers).

### Semantic Logic
- **Scope**: Duplicate Activity Names (e.g., "Hoops" & "Basketball").
- **Service**: OpenAI Chat Completion (GPT-4o/3.5).
- **Behavior**: Merges new activity into existing one if semantically identical.
- **Feedback**: Returns warning message in `ActivityDetailsDto`.

---
## 17. Metrics & Monitoring

### Overview
The server exposes Prometheus-compatible metrics at `/metrics` for monitoring performance, errors, and resource usage.

### Infrastructure
- **Endpoint**: `/metrics` (Public, but can be secured later).
- **Library**: `prometheus-net.AspNetCore`.
- **Runtime Metrics**: `prometheus-net.DotNetRuntime` is enabled to capture GC, ThreadPool, and Lock contention stats.

### Custom Metrics
#### Middleware (`ResponseMetricMiddleware`)
We use a custom middleware to track request and response sizes, useful for correlating latency with payload size.
- **Metric**: `http_response_size_bytes` (Histogram).
- **Labels**: `method`, `endpoint`, `code`.

### Dashboarding
A `docker-compose.yml` is provided to spin up:
- **Prometheus**: Scrapes the API at `host.docker.internal:5055`.
- **Grafana**: Visualizes the data (Port 3000).

### Health Checks
The server exposes a health check endpoint:
- **Endpoint**: `/health`
- **Logic**: Verifies connectivity to `AuthDbContext` and `AppDbContext`.
- **Response**:
  - `200 OK` "Healthy"
  - `503 Service Unavailable` "Unhealthy" (if DB is unreachable)

**Prometheus Integration**:
Health check status is automatically exported as a metric:
- **Metric**: `aspnetcore_healthcheck_status` (Gauge)
  - `1` = Healthy
  - `0` = Unhealthy
  - `0.5` = Degraded
- **Label**: `name` (e.g., `npgsql`, `auth_db_context`, etc.)

### Structured Logging (Serilog)
The application uses **Serilog** for structured logging, which enables better log analysis and correlation.

**Packages**:
- `Serilog.AspNetCore` - Core integration
- `Serilog.Enrichers.Environment` - Machine name and environment enrichment

**Configuration** (in `Program.cs`):
- **Minimum Level**: `Information` (default)
- **Overrides**:
  - `Microsoft.AspNetCore`: Warning (reduces ASP.NET noise)
  - `Microsoft.EntityFrameworkCore`: Warning (reduces EF query noise)
- **Enrichers**: `FromLogContext`, `WithMachineName`, `WithEnvironmentName`
- **Sink**: Console with custom output template

**HTTP Request Logging**:
All HTTP requests are automatically logged via `UseSerilogRequestLogging()`:
```
HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed}ms
```
Enriched with: `RequestHost`, `UserAgent`.

**Startup/Shutdown**:
- Application wrapped in try-catch-finally for clean shutdown
- Startup: `Log.Information("Starting Ping API server")`
- Fatal errors logged before exit
- `Log.CloseAndFlush()` ensures all logs are written before process exits


---

## 18. Quick Reference Summary
| Layer      | Items                                                           |
| ---------- | --------------------------------------------------------------- |
| Auth       | Register, Login, Me, Password flows                             |
| Pings      | Create, GetById, Nearby search, Favorites                       |
| Activities | Create activity, CRUD kinds                                     |
| Events     | Create, View, Manage attendance, Public search                  |
| Events     | Create, View, Manage attendance, Public search                  |
| Friends    | Add, Accept, List, Incoming, Remove                             |
| Blocks     | Block/Unblock users, List blocked                               |
| Profiles   | Me, Search                                                      |
| Reviews    | Create, List by scope, Like/Unlike, Explore Feed (with filters) |
| Reports    | Create (User), View/Filter (Admin)                              |
| Tags       | Search, Popular, Moderation                                     |

---
## 18. Error Response Formats

### Standard Error Response
All errors follow the ProblemDetails format:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "fieldName": ["Error message"]
  }
}
```

### Common Error Status Codes
- `400 Bad Request` - Validation errors, invalid input
- `401 Unauthorized` - Missing or invalid JWT token
- `403 Forbidden` - Insufficient permissions (e.g., accessing Private place)
- `404 Not Found` - Resource does not exist
- `409 Conflict` - Duplicate resource (e.g., existing friendship)
- `429 Too Many Requests` - Rate limit exceeded
- `500 Internal Server Error` - Unhandled exceptions (via GlobalExceptionHandler)

### Rate Limit Error (429)
```json
{
  "error": "Rate limit exceeded",
  "message": "Too many requests. Please try again in 60 seconds.",
  "retryAfter": 60
}
```

### Domain-Specific Error Messages
- Ping creation limit: "You've reached the daily limit for adding pings."
- Verified ping without address: "Address is required for verified pings."
- Duplicate ping: "A ping already exists at this location."
- Event duration: "Event duration must be at least 15 minutes."
- Self-friend request: "You cannot add yourself as a friend."
- Content moderation: "Content rejected: Hate/Harassment."

---
## 19. Planned Features & Roadmap

**Feature Roadmap:**

### 1. Communication & Notifications
- **Email Service**: Transactional emails for welcome, email verification, and password reset flows (replacing dev-only tokens).
- **Push Notifications**: Real-time alerts for:
  - Friend requests/accepts
  - Event invites and updates
  - Review likes/replies
  - "Nearby" recommendations

### 2. Search & Discovery
- **Enhanced Search**: 
  - Full-text search improvement (Elasticsearch or Postgres Full-Text if migrating).
  - Hybrid search (Keyword + Semantic) for Pings and Events.
- **Geospatial Database**: 
  - Migration to PostGIS (PostgreSQL) or similar for strictly superior spatial querying capabilities (knn, shapes, clustering).

### 3. Authentication & Identity
- **OAuth Providers**: 
  - "Sign in with Google"
  - "Sign in with Apple"
- **Device Management**: View and revoke active sessions.

### 4. Architecture Standards
- **Distributed Tracing**: Implementation of OpenTelemetry for full observability across services.
- **Background Jobs**: Integration of Hangfire or Quartz.NET for reliable async task processing (email sending, image processing, periodic cleanup).
- **CDN**: Integration for serving user content (images) globally.
- **CI/CD**: Github Actions pipelines for automated testing and deployment.

---
## 20. Agent Usage Notes
When generating or modifying code:
- Respect existing route and DTO contracts documented above.
- Validate business rules listed in Section 10 for new features.
- When adding new models: update the corresponding DbContext, create migration, and extend this guide.
- Keep new endpoints consistent with the route naming: plural nouns, kebab-case only when explicitly needed.
- Follow "Thin Controller, Fat Service" architecture pattern.
- Use Redis for rate limiting and caching where appropriate.
- Maintain privacy enforcement for all ping-related operations.

---
## 21. Testing Strategy
We use `xUnit` + `Microsoft.AspNetCore.Mvc.Testing` for integration testing.

### Infrastructure
- **Tests/Ping.Tests**: Main test project.
- **IntegrationTestFactory**: `WebApplicationFactory` customization that replaces the database with `InMemory` providers for isolation.
- **BaseIntegrationTest**: Base class creating `HttpClient` and handling auth.

### How to Run
```bash
dotnet test Tests/Ping.Tests
```

### Writing Tests
Inherit `BaseIntegrationTest`. The factory ensures a fresh server instance (logic-wise) and cleanable DBs.

---
End of guide.


---
## 22. User Verification

### Overview
Users with at least 500 followers can apply for verification to receive a "Verified" badge. This process is managed manually by admins.

### Workflow
1. **Application**: User sends `POST /api/verification/apply`. System checks follower count (≥500) and ensures no pending request exists.
2. **Review**: Admins view pending requests via `GET /api/admin/verification/requests`.
3. **Decision**:
   - **Approve**: Admin calls `POST .../approve`. User's `IsVerified` flag is set to `true`. Status becomes `Approved`.
   - **Reject**: Admin calls `POST .../reject` with a reason. Status becomes `Rejected`.

### Models
- `VerificationRequest`: links `UserId`, `SubmittedAt`, `Status` (Pending, Approved, Rejected), `AdminComment`.
- `AppUser`: Added `IsVerified` boolean.

### Endpoints
- **User**:
  - `POST /api/verification/apply`
  - `GET /api/verification/status`
- **Admin**:
  - `GET /api/admin/verification/requests`
  - `POST /api/admin/verification/{id}/approve`
  - `POST /api/admin/verification/{id}/reject`
