# Conquest Server Guide

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

---
## 1. Overview
Conquest is an ASP.NET Core API (targeting .NET 9) that manages users, places, activities at places, events, friendships, and reviews. It uses:
- ASP.NET Core MVC + minimal hosting model
- **Service Layer Architecture** ("Thin Controller, Fat Service" pattern)
- Identity (custom `AppUser`) stored in `AuthDbContext` (SQLite)
- Application domain stored in `AppDbContext` (SQLite)
- JWT-based authentication
- **Redis** for distributed caching, rate limiting, and session management
- **Rate Limiting Middleware** for API protection
- Global Exception Handling (standardized JSON responses)
- Swagger/OpenAPI (exposed at root path `/`)

---
## 2. Project Structure (Key Folders)
- `Program.cs` – service registration, middleware pipeline, auto-migrations, Redis setup.
- `Data/Auth/AuthDbContext.cs` – Identity + Friendships.
- `Data/App/AppDbContext.cs` – Places, Activities, Reviews, Tags, CheckIns, Events.
- `Models/*` – EF Core entity classes.
- `Dtos/*` – Records/classes exposed via API boundary.
- `Controllers/*` – Thin orchestrators handling HTTP concerns only.
- `Services/*` – Business logic, database queries, and domain operations.
- `Services/Redis/*` – Redis service for caching and rate limiting.
- `Middleware/*` – Global exception handling and rate limiting middleware.

---
## 3. Configuration & Startup (`Program.cs`)
Registered services:
- Two DbContexts (`AuthDbContext`, `AppDbContext`) using separate SQLite connection strings: `AuthConnection`, `AppConnection`.
- IdentityCore for `AppUser` + Roles + SignInManager + TokenProviders.
- JWT options bound from `configuration["Jwt"]` (Key, Issuer, Audience, AccessTokenMinutes).
- **Redis**: `IConnectionMultiplexer` (singleton), distributed cache, `IRedisService` (scoped).
- **Session**: Distributed session state with Redis backend (30-minute timeout).
- **Service Layer** (all scoped): `ITokenService`, `IPlaceService`, `IPlaceNameService` (Google Places API), `IEventService`, `IReviewService`, `IActivityService`, `IFriendService`, `IProfileService`, `IAuthService`, `IRedisService`, `RecommendationService`.
- **Middleware**: `GlobalExceptionHandler` (transient), `RateLimitMiddleware` (scoped).
- Authentication: JWT Bearer with validation (issuer, audience, lifetime, signing key).
- Swagger with Bearer security scheme.
- Auto migration is performed for both contexts at startup inside a scope.
- Redis connection health check logged at startup.

Pipeline order:
1. Global Exception Handler
2. Swagger + UI at root
3. Routing
4. **Rate Limiting Middleware**
5. **Session Middleware**
6. Authentication
7. Authorization
8. Static files
9. Controller endpoints via `app.MapControllers()`

### 3.1. Configuration Reference

Required `appsettings.json` keys:
```json
{
  "ConnectionStrings": {
    "AuthConnection": "Data Source=auth.db",
    "AppConnection": "Data Source=app.db",
    "RedisConnection": "localhost:6379"
  },
  "Jwt": {
    "Key": "[secret-key-minimum-32-chars]",
    "Issuer": "ConquestAPI",
    "Audience": "ConquestApp",
    "AccessTokenMinutes": 60
  },
  "Google": {
    "ApiKey": "[google-places-api-key]"
  }
}
```

**Environment Variables**:
- `OPENAI_API_KEY`: Required for AI Recommendations (Semantic Kernel).,
  "RateLimiting": {
    "GlobalLimitPerMinute": 100,
    "AuthenticatedLimitPerMinute": 200,
    "AuthEndpointsLimitPerMinute": 5,
    "PlaceCreationLimitPerDay": 10
  }
}
```

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
- `AppUser : IdentityUser` adds: `FirstName`, `LastName`, `ProfileImageUrl`.
- Unique index on `UserName` enforced in `AuthDbContext`.

### JWT
- Claims added: `sub`, `email`, `nameidentifier`, `name`, plus each role.
- Token lifespan: `AccessTokenMinutes` from config (default 60).
- `AuthResponse` returns: `AccessToken`, `ExpiresUtc`, `User` (`UserDto`).

### Password Flows
- Register: validates uniqueness of normalized `UserName` manually.
- Login: email + password; uses `CheckPasswordSignInAsync` with lockout.
- Forgot Password: generates identity reset token, returns encoded version in DEV.
- Reset Password: base64url decode then `ResetPasswordAsync`.
- Change Password: requires existing password & JWT auth.

### Authorization
- Most controllers require `[Authorize]` at class level (Events, Places, Profiles, Reviews implicitly via needing User ID). Some endpoints explicitly `[AllowAnonymous]` (register/login/forgot/reset).

---
## 5. Database Contexts
### AuthDbContext
DbSets:
- `Friendships` (composite PK: `{UserId, FriendId}`)
Relationships:
- `Friendship.User` and `Friendship.Friend` each `Restrict` delete.
Indexes:
- Unique index on `AppUser.UserName`.

### AppDbContext
DbSets:
- `Places`, `ActivityKinds`, `PlaceActivities`, `Reviews`, `Tags`, `ReviewTags`, `Events`, `EventAttendees`, `Favorited`

Seed Data:
- `ActivityKind` seeded with ids 1–20 (Sports, Food, Outdoors, Art, etc.).

Indexes:
- `Place (Latitude, Longitude)` for geo bounding.
- Unique composite `Favorited (UserId, PlaceId)` prevents duplicate favorites.
- Unique `ActivityKind.Name`.
- Unique composite `PlaceActivity (PlaceId, Name)`.
- Review uniqueness: No unique constraint (allows multiple reviews/checkins per user).
- Unique `Tag.Name`.
- Composite PK `ReviewTag (ReviewId, TagId)`.
- Composite PK `EventAttendee (EventId, UserId)`.

Relationships & Cascades:
- `Favorited` → `Place` cascade delete.
- `PlaceActivity` → `Place` cascade delete.
- `PlaceActivity` → `ActivityKind` restrict delete.
- `Review` → `PlaceActivity` cascade.
- `ReviewTag` → `Review` cascade, `ReviewTag` → `Tag` cascade.
- `EventAttendee` → `Event` cascade.

Property Configuration:
- `Review.Content` max length 2000 (model class shows 1000 – guide notes difference; EF config wins at runtime).
- Timestamp defaults via `CURRENT_TIMESTAMP` for `Review.CreatedAt`.
- `Tag.Name` max 30.

---
## 6. Domain Models (Summaries)
| Entity        | Key                | Core Fields                                                                                                              | Navigation                             | Notes                                                                                                                 |
| ------------- | ------------------ | ------------------------------------------------------------------------------------------------------------------------ | -------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| AppUser       | `IdentityUser`     | FirstName, LastName, ProfileImageUrl                                                                                     | (Friends)                              | Stored in Auth DB                                                                                                     |
| Friendship    | (UserId, FriendId) | Status (Pending/Accepted/Blocked), CreatedAt                                                                             | User, Friend                           | Symmetric friendship stored as two Accepted rows after accept                                                         |
| Place         | Id                 | Name, Address, Latitude, Longitude, OwnerUserId, Visibility (Public/Private/Friends), Type (Verified/Custom), CreatedUtc | PlaceActivities                        | OwnerUserId is string (Identity FK); Visibility controls access; Type determines duplicate logic and Google API usage |
| Favorited     | Id                 | UserId, PlaceId                                                                                                          | Place                                  | Unique per user per place; cascade deletes with Place                                                                 |
| ActivityKind  | Id                 | Name                                                                                                                     | PlaceActivities                        | Seeded                                                                                                                |
| PlaceActivity | Id                 | PlaceId, ActivityKindId?, Name, CreatedUtc                                                                               | Place, ActivityKind, Reviews, CheckIns | Unique per place by Name                                                                                              |
| Review        | Id                 | UserId, UserName, PlaceActivityId, Rating, Type, Content, CreatedAt, Likes                                               | PlaceActivity, ReviewTags              | Rating required; Type (Review/CheckIn); First post is Review, subsequent are CheckIns                                 |
| Event         | Id                 | Title, Description?, IsPublic, StartTime, EndTime, Location, CreatedById, CreatedAt, Latitude, Longitude                 | Attendees (EventAttendee)              | Status computed dynamically                                                                                           |
| EventAttendee | (EventId, UserId)  | JoinedAt                                                                                                                 | Event                                  | Many-to-many join                                                                                                     |

---
## 7. DTO Contracts
### Activities
- `ActivitySummaryDto(Id, Name, ActivityKindId?, ActivityKindName?)`
- `CreateActivityDto(PlaceId, Name, ActivityKindId?)`
- `ActivityDetailsDto(Id, PlaceId, Name, ActivityKindId?, ActivityKindName?, CreatedUtc)`
- `ActivityKindDto(Id, Name)` / `CreateActivityKindDto(Name)`

### Auth
- `RegisterDto(Email, Password, FirstName, LastName, UserName)`
- `LoginDto(Email, Password)`
- `ForgotPasswordDto(Email)`
- `ResetPasswordDto(Email, Token, NewPassword)`
- `ChangePasswordDto(CurrentPassword, NewPassword)`
- `UserDto(Id, Email, DisplayName, FirstName, LastName, ProfileImageUrl)`
- `AuthResponse(AccessToken, ExpiresUtc, User)`
- `JwtOptions(Key, Issuer, Audience, AccessTokenMinutes)`

### Events
- `EventDto(Id, Title, Description?, IsPublic, StartTime, EndTime, Location, CreatedBy(UserSummaryDto), CreatedAt, Attendees[List<UserSummaryDto>], Status, Latitude, Longitude)`
- `UserSummaryDto(Id, UserName, FirstName, LastName)`
- `CreateEventDto(Title, Description?, IsPublic, StartTime, EndTime, Location, Latitude, Longitude)`
- `UpdateEventDto` (all optional patch fields)

### Friends
- `FriendSummaryDto(Id, UserName, FirstName, LastName, ProfileImageUrl?)`

### Places
- `PlaceVisibility` enum: `Private = 0`, `Friends = 1`, `Public = 2`
- `PlaceType` enum: `Custom = 0`, `Verified = 1`
- `UpsertPlaceDto(Name, Address?, Latitude, Longitude, Visibility, Type)`
- `PlaceDetailsDto(Id, Name, Address, Latitude, Longitude, Visibility, Type, IsOwner, IsFavorited, Favorites, Activities[ActivitySummaryDto], ActivityKinds[string])`

### Profiles
- `ProfileDto(Id, DisplayName, FirstName, LastName, ProfilePictureUrl?)`
- `PersonalProfileDto(Id, DisplayName, FirstName, LastName, ProfilePictureUrl?, Email)`

### Reviews
- `UserReviewsDto(Review, History[])` - Grouped response
- `ReviewDto(Id, Rating, Content?, UserId, UserName, CreatedAt, Likes, IsLiked)`
- `CreateReviewDto(Rating, Content?)`
- `ExploreReviewDto(ReviewId, PlaceActivityId, PlaceId, PlaceName, PlaceAddress, ActivityName, ActivityKindName?, Latitude, Longitude, Rating, Content?, UserName, CreatedAt, Likes, IsLiked, Tags[])`
- `ExploreReviewsFilterDto(Latitude?, Longitude?, RadiusKm?, SearchQuery?, ActivityKindIds?[], PageSize, PageNumber)`

### Recommendations
- `RecommendationDto(Name, Address, Latitude?, Longitude?, Source, LocalPlaceId?)`

---
## 8. Services (Service Layer Architecture)

**Architecture Pattern**: The application follows the "Thin Controller, Fat Service" pattern. Controllers handle only HTTP concerns (request/response, status codes), while all business logic, database queries, and domain operations are encapsulated in service classes.

### Service Interfaces & Implementations

#### TokenService (`ITokenService`)
- **Purpose**: JWT token generation
- **Methods**: `CreateAuthResponseAsync(AppUser)`
- **Logic**: Generates JWT with configured options, includes roles and identity claims

#### PlaceService (`IPlaceService`)
- **Purpose**: Place management with geo-spatial operations, privacy controls, and official place verification
- **Dependencies**: `IPlaceNameService` (Google Places API), `IRedisService`, `IFriendService`
- **Methods**:
  - `CreatePlaceAsync(UpsertPlaceDto, userId)` - Creates place with type-specific duplicate detection and rate limiting
  - `GetPlaceByIdAsync(id, userId)` - Retrieves place with privacy checks (Private/Friends/Public visibility)
  - `SearchNearbyAsync(lat, lng, radiusKm, activityName, activityKind, visibility, type, userId)` - Geo-spatial search with filters
  - `AddFavoriteAsync(id, userId)` - Adds place to user's favorites, increments `Favorites` counter
  - `UnfavoriteAsync(id, userId)` - Removes place from favorites, decrements `Favorites` counter
  - `GetFavoritedPlacesAsync(userId)` - Retrieves all favorited places (includes deleted places for history)
- **PlaceType Logic**:
  - **Verified Places** (Public only): Require address, check duplicates by address only, auto-fetch name from Google Places API
  - **Custom Places**: Address optional, check duplicates by coordinates (~50m), use user-provided name
  - **Visibility Rule**: Private/Friends places are automatically converted to Custom type (no Google API calls for non-public places)
- **Privacy Enforcement**:
  - **Private**: Only owner can view
  - **Friends**: Owner + accepted friends can view
  - **Public**: Everyone can view
- **Duplicate Detection** (Public places only):
  - Verified: Address match only (no coordinate checking)
  - Custom: Coordinates within ~50m (0.0005 degrees)
  - Private/Friends: No duplicate checking (always create new)
- **Rate Limiting**: Max 10 places per user per day (Redis-backed)

#### GooglePlacesService (`IPlaceNameService`)
- **Purpose**: Fetch official place names from Google Places API (New)
- **Methods**:
  - `GetPlaceNameAsync(lat, lng)` - Returns official place name from Google or null if not found
  - `SearchPlacesAsync(query, lat, lng, radiusKm)` - Text search with strict rectangular location restriction
- **Implementation**:
  - Uses Google Places API `places:searchNearby` endpoint
  - 50m search radius, max 1 result
  - Field mask: `places.displayName`
  - Falls back to user-provided name if no POI found
- **Configuration**: Requires `Google:ApiKey` in `appsettings.json`
- **Rate Limiting**: Only called for Public Verified places (not for Custom or Private/Friends)

#### RecommendationService (`RecommendationService`)
- **Purpose**: AI-powered place recommendations based on "vibe"
- **Dependencies**: `Kernel` (Semantic Kernel), `IPlaceNameService`, `AppDbContext`
- **Methods**:
  - `GetRecommendationsAsync(vibe, lat, lng, radiusKm)` - Orchestrates the search flow
- **Logic**:
  1.  **Analyze Vibe**: Uses OpenAI (`gpt-3.5-turbo`) to translate user vibe (e.g., "cozy study spot") into specific search terms (e.g., "library", "cafe"). Includes location context for local language support.
  2.  **Local Search**: Searches local `AppDbContext` for places matching terms in Name, Activities, or Review Tags within radius.
  3.  **Fallback**: If < 3 local matches, calls Google Places API (Text Search with strict location restriction).
  4.  **Merge**: Combines and deduplicates results, prioritizing local places. Returns "System" message if no results found.

#### EventService (`IEventService`)
- **Purpose**: Event lifecycle and attendance management
- **Methods**:
  - `CreateEventAsync(CreateEventDto, userId)` - Creates event and auto-joins creator
  - `GetEventByIdAsync(id)` - Retrieves single event with attendees
  - `GetMyEventsAsync(userId)` - Events owned by user
  - `GetEventsAttendingAsync(userId)` - Events user is attending
  - `GetPublicEventsAsync(minLat, maxLat, minLng, maxLng)` - Public events in bounding box
  - `DeleteEventAsync(id, userId)` - Deletes event (owner only)
  - `JoinEventAsync(id, userId)` - Join event
  - `LeaveEventAsync(id, userId)` - Leave event
- **Logic**: N+1 query optimization (batch user loading), status calculation (mine/attending/not-attending), ownership validation
- **Helper**: `EventMapper.MapToDto` - Converts Event entities to EventDto with user summaries

#### ReviewService (`IReviewService`)
- **Purpose**: Review creation and retrieval with scope filtering
- **Methods**:
  - `CreateReviewAsync(placeActivityId, CreateReviewDto, userId, userName)` - Creates review or checkin (based on history)
  - `GetReviewsAsync(placeActivityId, scope, userId)` - Retrieves reviews grouped by user (`UserReviewsDto`)
  - `GetExploreReviewsAsync(ExploreReviewsFilterDto, userId)` - Retrieves paginated feed of reviews with filters (location, category, search) and `IsLiked` status
  - `LikeReviewAsync(reviewId, userId)` - Adds like to review (idempotent)
  - `UnlikeReviewAsync(reviewId, userId)` - Removes like from review (idempotent)
  - `GetLikedReviewsAsync(userId)` - Retrieves all reviews liked by user
- **Logic**: Activity validation, friend-based filtering via `IFriendService`, pagination with max 100 items per page (default 20), newest reviews first, batch `IsLiked` checking to avoid N+1 queries

#### ActivityService (`IActivityService`)
- **Purpose**: Activity creation and validation
- **Methods**:
  - `CreateActivityAsync(CreateActivityDto)` - Creates place activity
- **Logic**: Place validation, name normalization, uniqueness enforcement (per place), optional ActivityKind validation

#### FriendService (`IFriendService`)
- **Purpose**: Friendship management and relationship queries
- **Methods**:
  - `GetFriendIdsAsync(userId)` - Returns all accepted friend IDs (bidirectional)
  - `GetMyFriendsAsync(userId)` - Returns friend summaries
  - `AddFriendAsync(userId, friendUsername)` - Sends friend request
  - `AcceptFriendAsync(userId, friendUsername)` - Accepts pending request, creates bidirectional relationship
  - `GetIncomingRequestsAsync(userId)` - Returns pending requests
  - `RemoveFriendAsync(userId, friendUsername)` - Removes friendship (both directions)
- **Logic**: Bidirectional relationship management, blocking checks, duplicate prevention, self-friendship prevention

#### ProfileService (`IProfileService`)
- **Purpose**: User profile operations
- **Methods**:
  - `GetMyProfileAsync(userId)` - Returns personal profile with email
  - `SearchProfilesAsync(query, currentUsername)` - Searches users by username (excludes self)
- **Logic**: Username normalization, self-exclusion from search results

#### AuthService (`IAuthService`)
- **Purpose**: Authentication and password management
- **Methods**:
  - `RegisterAsync(RegisterDto)` - User registration
  - `LoginAsync(LoginDto)` - User login with lockout
  - `GetCurrentUserAsync(userId)` - Returns current user info
  - `ForgotPasswordAsync(ForgotPasswordDto, scheme, host)` - Generates password reset token
  - `ResetPasswordAsync(ResetPasswordDto)` - Resets password with token
  - `ChangePasswordAsync(userId, ChangePasswordDto)` - Changes password (authenticated)
- **Logic**: Username uniqueness validation, password validation, token generation, account enumeration prevention

### Service Layer Benefits
- **Separation of Concerns**: Controllers handle HTTP, services handle business logic
- **Testability**: Services can be unit tested independently
- **Reusability**: Business logic can be shared across controllers or other contexts
- **Maintainability**: Easier to locate and modify business rules
- **Dependency Injection**: Promotes loose coupling through interfaces

---
## 9. Controllers & Endpoints
Notation: `[]` = route parameter, `(Q)` = query parameter, `(Body)` = JSON body. Auth: A=Requires JWT, An=AllowAnonymous.

### ActivitiesController (`/api/activities`)
| Method | Route           | Auth | Body                | Returns              | Notes                                                |
| ------ | --------------- | ---- | ------------------- | -------------------- | ---------------------------------------------------- |
| POST   | /api/activities | A    | `CreateActivityDto` | `ActivityDetailsDto` | Validates place, optional kind, uniqueness per place |

### ActivityKindsController (`/api/activity-kinds`)
| Method | Route                    | Auth | Body                    | Returns             | Notes                                                 |
| ------ | ------------------------ | ---- | ----------------------- | ------------------- | ----------------------------------------------------- |
| GET    | /api/activity-kinds      | A    | —                       | `ActivityKindDto[]` | Ordered by Name                                       |
| POST   | /api/activity-kinds      | A    | `CreateActivityKindDto` | `ActivityKindDto`   | Enforces unique name (case-insensitive)               |
| DELETE | /api/activity-kinds/{id} | A    | —                       | 204 NoContent       | Remove kind (restrict prevents cascade on activities) |

### AuthController (`/api/auth`)
| Method | Route                     | Auth | Body                | Returns           | Notes                                     |
| ------ | ------------------------- | ---- | ------------------- | ----------------- | ----------------------------------------- |
| POST   | /api/auth/register        | An   | `RegisterDto`       | `AuthResponse`    | Username uniqueness manual check          |
| POST   | /api/auth/login           | An   | `LoginDto`          | `AuthResponse`    | Lockout on failures (Identity configured) |
| GET    | /api/auth/me              | A    | —                   | `UserDto`         | Uses `ClaimTypes.NameIdentifier`          |
| POST   | /api/auth/password/forgot | An   | `ForgotPasswordDto` | Dev returns token | Avoids enumeration                        |
| POST   | /api/auth/password/reset  | An   | `ResetPasswordDto`  | 200 status        | Decodes base64url token                   |
| POST   | /api/auth/password/change | A    | `ChangePasswordDto` | 200 status        | Validates current password                |

### EventsController (`/api/Events`)
| Method | Route                                            | Auth | Body             | Returns      | Notes                                     |
| ------ | ------------------------------------------------ | ---- | ---------------- | ------------ | ----------------------------------------- |
| POST   | /api/Events/create                               | A    | `CreateEventDto` | `EventDto`   | Validates times (future, duration ≥15m)   |
| GET    | /api/Events/{id}                                 | A    | —                | `EventDto`   | Loads attendees + creator                 |
| GET    | /api/Events/mine                                 | A    | —                | `EventDto[]` | Events created by requester               |
| GET    | /api/Events/attending                            | A    | —                | `EventDto[]` | Where user is attendee                    |
| POST   | /api/Events/{id}/attend                          | A    | —                | 200          | Prevents attending own event & duplicates |
| POST   | /api/Events/{id}/leave                           | A    | —                | 200          | Removes attendee row                      |
| DELETE | /api/Events/{id}                                 | A    | —                | 200          | Only creator                              |
| PATCH  | /api/Events/{id}                                 | A    | `UpdateEventDto` | 200          | Partial updates                           |
| GET    | /api/Events/public (Q: from,to,lat,lng,radiusKm) | A    | —                | `EventDto[]` | Bounding box geo filter + upcoming only   |

### FriendsController (`/api/Friends`)
| Method | Route                          | Auth | Body | Returns              | Notes                                                    |
| ------ | ------------------------------ | ---- | ---- | -------------------- | -------------------------------------------------------- |
| GET    | /api/Friends/friends           | A    | —    | `FriendSummaryDto[]` | Accepted friendships only                                |
| POST   | /api/Friends/add/{username}    | A    | —    | 200                  | Creates Pending request if no existing relations         |
| POST   | /api/Friends/accept/{username} | A    | —    | 200                  | Converts Pending to Accepted + adds reverse Accepted row |
| POST   | /api/Friends/requests/incoming | A    | —    | `FriendSummaryDto[]` | Pending where current user is FriendId                   |
| POST   | /api/Friends/remove/{username} | A    | —    | 200                  | Deletes both Accepted rows                               |

### PlacesController (`/api/places`)
| Method | Route                                                                              | Auth | Body             | Returns             | Notes                                                                                                           |
| ------ | ---------------------------------------------------------------------------------- | ---- | ---------------- | ------------------- | --------------------------------------------------------------------------------------------------------------- |
| POST   | /api/places                                                                        | A    | `UpsertPlaceDto` | `PlaceDetailsDto`   | Daily per-user creation limit (10); Verified type requires address; Private/Friends auto-converted to Custom    |
| GET    | /api/places/{id}                                                                   | A    | —                | `PlaceDetailsDto`   | Respects visibility: Private (owner only), Friends (owner + friends), Public (all)                              |
| GET    | /api/places/nearby (Q: lat,lng,radiusKm,activityName,activityKind,visibility,type) | A    | —                | `PlaceDetailsDto[]` | Geo-search with optional filters: visibility (Public/Private/Friends), type (Verified/Custom), activity filters |
| POST   | /api/places/favorited/{id}                                                         | A    | —                | 200 OK              | Adds place to favorites; prevents duplicates, validates place exists                                            |
| DELETE | /api/places/favorited/{id}                                                         | A    | —                | 204 NoContent       | Removes place from favorites; idempotent                                                                        |
| GET    | /api/places/favorited                                                              | A    | —                | `PlaceDetailsDto[]` | Returns all favorited places with activities                                                                    |

### ProfilesController (`/api/profiles`)
| Method | Route                          | Auth | Body | Returns              | Notes                                               |
| ------ | ------------------------------ | ---- | ---- | -------------------- | --------------------------------------------------- |
| GET    | /api/profiles/me               | A    | —    | `PersonalProfileDto` | Current user profile                                |
| GET    | /api/profiles/search?username= | A    | —    | `ProfileDto[]`       | Prefix search on normalized username; excludes self |

### ReviewsController (`/api/reviews`)
| Method | Route                                                      | Auth | Body                      | Returns              | Notes                                              |
| ------ | ---------------------------------------------------------- | ---- | ------------------------- | -------------------- | -------------------------------------------------- |
| POST   | /api/reviews/{placeActivityId}                             | A    | `CreateReviewDto`         | `ReviewDto`          | Creates review for activity                        |
| GET    | /api/reviews/{placeActivityId}?scope={mine/friends/global} | A    | —                         | `UserReviewsDto[]`   | Returns reviews grouped by user (Review + History) |
| GET    | /api/reviews/explore                                       | A    | `ExploreReviewsFilterDto` | `ExploreReviewDto[]` | Paginated review feed with filters                 |
| POST   | /api/reviews/{reviewId}/like                               | A    | —                         | 200 OK               | Like a review (idempotent)                         |
| DELETE | /api/reviews/{reviewId}/like                               | A    | —                         | 204 NoContent        | Unlike a review (idempotent)                       |
| GET    | /api/reviews/liked                                         | A    | —                         | `ExploreReviewDto[]` | User's liked reviews                               |

### RecommendationController (`/api/recommendations`)
| Method | Route                                            | Auth | Body | Returns               | Notes                                   |
| ------ | ------------------------------------------------ | ---- | ---- | --------------------- | --------------------------------------- |
| GET    | /api/recommendations (Q: vibe, lat, lng, radius) | A    | —    | `RecommendationDto[]` | AI-powered search. Radius default 10km. |

---
## 10. Validation & Business Rules

### Authentication
- Username must be unique (case-insensitive)
- Password flows do not leak account existence (uniform responses for missing users)
- JWT tokens expire after configured minutes (default: 60)
- Account lockout enforced after failed login attempts

### Places
- Daily creation limit: 10 per user (Redis-backed)
- Privacy enforcement:
  - **Private**: Only owner can view
  - **Friends**: Owner + accepted friends can view
  - **Public**: Everyone can view
- Duplicate detection (Public places only):
  - Verified: Address match only
  - Custom: Coordinates within ~50m (0.0005 degrees)
  - Private/Friends: No duplicate checking

### PlaceType Business Rules
- **Verified Places**:
  - Must be Public visibility
  - Address is required
  - Name auto-fetched from Google Places API
  - Duplicates checked by exact address match only
  
- **Custom Places**:
  - Can be any visibility (Public/Friends/Private)
  - Address is optional
  - User-provided name is used
  - Duplicates checked by coordinate proximity (~50m)

- **Automatic Conversions**:
  - Private/Friends places → automatically converted to Custom type
  - Prevents unnecessary Google API calls for non-public places

### Reviews
- Scope filtering: mine/friends/global
- **Review vs CheckIn**: First post by user is `Review`, subsequent posts are `CheckIn`.
- **Grouping**: API returns reviews grouped by user to show history.
- Likes are idempotent (re-liking/re-unliking does nothing)
- Batch `IsLiked` checking prevents N+1 queries
- Pagination: max 100 items per page (default 20)
- Rating required for both Reviews and CheckIns

### Events
- Start time must be in future
- Duration must be ≥15 minutes
- Creators auto-join their events
- Only creator can delete event
- Cannot attend own event (creator is already an attendee)

### Activities
- Name must be unique per place (case-insensitive)
- ActivityKind is optional
- Cascade deletes when parent Place is deleted

### Friends
- Prevents: duplicates, self-addition, blocked users
- Bidirectional relationship: accepting creates two Accepted rows
- Friend requests are Pending until accepted

### Tags (Pending Full Implementation)
- Tag moderation flags exist: `IsBanned`, `IsApproved`
- Canonical tag relationship via `CanonicalTagId`
- **Status**: Models and database schema exist, but no controller endpoints or moderation workflow implemented yet

### CheckIns
- Merged into `Review` model via `ReviewType` enum.
- **Status**: Fully implemented.

---
## 11. Indexes, Seed Data, Performance Notes
- Geospatial queries use bounding box (approximate) before distance calculation (Haversine) filtered by radius.
- Suggest future optimization: create a covering index on `(Latitude, Longitude, Visibility, Type)` if query frequency increases.
- Event attendee queries are batched (N+1 fixed) in `EventMapper` to reduce database round-trips.
- Review `IsLiked` status is batch-checked to avoid N+1 queries.

---
## 12. Migration & EF Core Operations
### Auto Migrations
`Program.cs` executes `Database.Migrate()` for both contexts at startup.

### Manual Commands
Use full context type when generating migrations:
```bash
dotnet ef migrations add <MigrationName> --context Conquest.Data.Auth.AuthDbContext
dotnet ef database update --context Conquest.Data.Auth.AuthDbContext

dotnet ef migrations add <MigrationName> --context Conquest.Data.App.AppDbContext
dotnet ef database update --context Conquest.Data.App.AppDbContext
```

### Legacy Note (From Original UserGuide.md)
Pending model changes warning indicates migrations not generated. Resolve by adding and updating the relevant context as above.

---
## 13. Conventions & Extension Points
- Controllers use explicit route prefixes instead of `[Route("api/[controller]")]` in some cases for clarity (`ActivitiesController`: `api/activities`).
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
- Automatic key prefixing: `Conquest:{key}`
- Connection multiplexing (singleton `IConnectionMultiplexer`)
- Error logging with graceful degradation

### Key Naming Conventions
- Rate limiting: `ratelimit:{clientId}:{timeWindow}`
- Place creation: `ratelimit:place:create:{userId}:{date}`
- Session data: `Conquest:{sessionId}`

### Health Check
On startup, `Program.cs` logs Redis connection status:
- ✓ Success: "Redis connected successfully"
- ✗ Failure: "Redis connection failed - rate limiting and caching will not work"

### Deployment Notes
- **Local Development**: Docker container (`docker run -d --name conquest-redis -p 6379:6379 redis:alpine`)
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
  "PlaceCreationLimitPerDay": 10
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
**Place Creation** (`PlaceService.CreatePlaceAsync`):
- Limit: 10 places per user per day
- Storage: Redis key `ratelimit:place:create:{userId}:{yyyy-MM-dd}`
- Expiry: 24 hours
- Error: `InvalidOperationException` with message "You've reached the daily limit for adding places."

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
## 16. Quick Reference Summary
| Layer      | Items                                                           |
| ---------- | --------------------------------------------------------------- |
| Auth       | Register, Login, Me, Password flows                             |
| Places     | Create, GetById, Nearby search, Favorites                       |
| Activities | Create activity, CRUD kinds                                     |
| Events     | Create, View, Manage attendance, Public search                  |
| Friends    | Add, Accept, List, Incoming, Remove                             |
| Profiles   | Me, Search                                                      |
| Reviews    | Create, List by scope, Like/Unlike, Explore Feed (with filters) |

---
## 17. Error Response Formats

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
- Place creation limit: "You've reached the daily limit for adding places."
- Verified place without address: "Address is required for verified places."
- Duplicate place: "A place already exists at this location."
- Event duration: "Event duration must be at least 15 minutes."
- Self-friend request: "You cannot add yourself as a friend."

---
## 18. Suggested Future Enhancements

**High Priority:**
- ✅ ~~PlaceType feature (Verified vs Custom)~~ - COMPLETED
- ✅ ~~Review likes with `IsLiked` flag~~ - COMPLETED
- ✅ ~~Redis integration for rate limiting~~ - COMPLETED
- ✅ ~~AI Recommendations (Semantic Kernel + Google Fallback)~~ - COMPLETED
- ✅ ~~CheckIns feature implementation~~ - COMPLETED (Merged into Reviews)
- ✅ ~~Soft delete for Places with historical review preservation~~ - COMPLETED
- ✅ ~~Enhanced Favorites (Counter + History)~~ - COMPLETED
- Implement tag moderation system (approval/banning workflow, endpoints)
- Normalize and validate rating range (1–5) at DTO/model layer

**Medium Priority:**
- Email service for password reset (production)
- Pagination improvements for large list endpoints
- WebSocket support for real-time event updates
- Profile picture upload functionality

**Performance:**
- Consider PostGIS for advanced geospatial queries
- Add covering index: `(Latitude, Longitude, Visibility, Type)`
- Implement response caching for public data
- Redis cache invalidation strategy for frequently updated data

---
## 19. Agent Usage Notes
When generating or modifying code:
- Respect existing route and DTO contracts documented above.
- Validate business rules listed in Section 10 for new features.
- When adding new models: update the corresponding DbContext, create migration, and extend this guide.
- Keep new endpoints consistent with the route naming: plural nouns, kebab-case only when explicitly needed.
- Follow "Thin Controller, Fat Service" architecture pattern.
- Use Redis for rate limiting and caching where appropriate.
- Maintain privacy enforcement for all place-related operations.

---
End of guide.
