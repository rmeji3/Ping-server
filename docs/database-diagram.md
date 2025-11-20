# Conquest Server - Database Architecture

This diagram shows the two separate SQLite databases used in the Conquest server application.

## Database ER Diagram

```mermaid
---
title: Conquest Server - Database Architecture (AuthDB & AppDB)
---
erDiagram
    %% ==========================================
    %% AUTH DATABASE (AuthDbContext)
    %% ==========================================
    
    AppUser {
        string Id PK
        string UserName UK "Unique, indexed"
        string Email
        string NormalizedUserName
        string NormalizedEmail
        string PasswordHash
        string FirstName
        string LastName
        string ProfileImageUrl "Max 512 chars, nullable"
        bool EmailConfirmed
        string PhoneNumber
        bool PhoneNumberConfirmed
        bool TwoFactorEnabled
        datetime LockoutEnd
        bool LockoutEnabled
        int AccessFailedCount
    }
    
    Friendship {
        string UserId PK,FK
        string FriendId PK,FK
        int Status "Enum: Pending=0, Accepted=1, Blocked=2"
        datetime CreatedAt
    }
    
    %% Auth DB Relationships
    AppUser ||--o{ Friendship : "User → UserId"
    AppUser ||--o{ Friendship : "Friend → FriendId"
    
    %% ==========================================
    %% APP DATABASE (AppDbContext)
    %% ==========================================
    
    Place {
        int Id PK
        string Name "Max 200 chars"
        string Address "Max 300 chars, nullable"
        double Latitude "Indexed with Longitude"
        double Longitude "Indexed with Latitude"
        string OwnerUserId "FK to AppUser (string), Max 100"
        bool IsPublic "Default false"
        datetime CreatedUtc
    }
    
    ActivityKind {
        int Id PK
        string Name UK "Max 200 chars, unique"
    }
    
    PlaceActivity {
        int Id PK
        int PlaceId FK
        int ActivityKindId FK "Nullable"
        string Name "Max 200 chars, unique per place"
        string Description "Max 500 chars, nullable"
        datetime CreatedUtc
    }
    
    Review {
        int Id PK
        string UserId "Max 200 chars, FK reference only"
        string UserName "Max 100 chars"
        int PlaceActivityId FK
        int Rating
        string Content "Max 1000 chars (model) / 2000 (EF), nullable"
        datetime CreatedAt "Default CURRENT_TIMESTAMP"
    }
    
    CheckIn {
        int Id PK
        string UserId "Max 200 chars"
        int PlaceActivityId FK
        string Note "Max 500 chars, nullable"
        datetime CreatedAt "Default CURRENT_TIMESTAMP, indexed"
    }
    
    Tag {
        int Id PK
        string Name UK "Max 30 chars, unique normalized"
        int CanonicalTagId FK "Nullable, for synonyms"
        bool IsBanned
        bool IsApproved "Default true"
    }
    
    ReviewTag {
        int ReviewId PK,FK
        int TagId PK,FK
    }
    
    Event {
        int Id PK
        string Title
        string Description "Nullable"
        bool IsPublic
        datetime StartTime
        datetime EndTime
        string Location
        string CreatedById "FK to AppUser (string)"
        datetime CreatedAt
        double Latitude
        double Longitude
        string Status "Computed dynamically"
    }
    
    EventAttendee {
        int EventId PK,FK
        string UserId PK "FK to AppUser (string)"
        datetime JoinedAt
    }
    
    %% App DB Relationships
    Place ||--o{ PlaceActivity : "has many"
    ActivityKind ||--o{ PlaceActivity : "categorizes (optional)"
    PlaceActivity ||--o{ Review : "has many"
    PlaceActivity ||--o{ CheckIn : "has many"
    Review ||--o{ ReviewTag : "has many"
    Tag ||--o{ ReviewTag : "has many"
    Tag ||--o| Tag : "canonical (self-reference)"
    Event ||--o{ EventAttendee : "has many"
    
    %% Cross-database references (logical only, not enforced by FK)
    AppUser ||--o{ Place : "owns (OwnerUserId)"
    AppUser ||--o{ Review : "writes (UserId)"
    AppUser ||--o{ CheckIn : "creates (UserId)"
    AppUser ||--o{ Event : "creates (CreatedById)"
    AppUser ||--o{ EventAttendee : "attends (UserId)"
```

## Database Separation Notes

### AuthDbContext (Identity & Social)
- **Purpose**: Authentication, authorization, and social relationships
- **File**: `Data/Auth/AuthDbContext.cs`
- **Connection**: `AuthConnection` in appsettings.json
- **Tables**:
  - AspNetUsers (AppUser) - Extended Identity user
  - AspNetRoles, AspNetUserRoles, AspNetUserClaims, etc. (Identity tables)
  - Friendships - Bidirectional social connections
- **Indexes**:
  - Unique index on AppUser.UserName

### AppDbContext (Domain)
- **Purpose**: Core application domain (places, activities, events, reviews)
- **File**: `Data/App/AppDbContext.cs`
- **Connection**: `AppConnection` in appsettings.json
- **Indexes**:
  - `(Latitude, Longitude)` on Place - Geospatial bounding box queries
  - Unique `Name` on ActivityKind
  - Unique `(PlaceId, Name)` on PlaceActivity
  - `(PlaceActivityId, UserId)` on Review
  - Unique `Name` on Tag
  - `(PlaceActivityId, CreatedAt)` on CheckIn - Time-based queries
- **Seed Data**:
  - 8 predefined ActivityKinds (Soccer, Climbing, Tennis, Hiking, Running, Photography, Coffee, Gym)

## Cascade Delete Behaviors

| Relationship | Delete Behavior | Reason |
|--------------|-----------------|--------|
| Friendship → User/Friend | **Restrict** | Prevent accidental user deletion with active friendships |
| PlaceActivity → Place | **Cascade** | Remove activities when place is deleted |
| PlaceActivity → ActivityKind | **Restrict** | Prevent deletion of kinds in use |
| Review → PlaceActivity | **Cascade** | Remove reviews when activity is deleted |
| CheckIn → PlaceActivity | **Cascade** | Remove check-ins when activity is deleted |
| ReviewTag → Review | **Cascade** | Remove tags when review is deleted |
| ReviewTag → Tag | **Cascade** | Clean up join table |
| EventAttendee → Event | **Cascade** | Remove attendees when event is deleted |

## Cross-Database References

The application uses **string-based foreign keys** to reference AppUser from the App database:
- `Place.OwnerUserId` → AppUser.Id
- `Review.UserId` → AppUser.Id (denormalized with UserName)
- `CheckIn.UserId` → AppUser.Id
- `Event.CreatedById` → AppUser.Id
- `EventAttendee.UserId` → AppUser.Id

These are **not enforced by database foreign key constraints** due to the separation of contexts, but are validated at the application layer.
