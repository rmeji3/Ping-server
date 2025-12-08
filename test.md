2. Implement Tags System

**Status**: Database schema complete, no controller or moderation workflow

Models include `Tag`, `ReviewTag`, `IsBanned`, `IsApproved`, `CanonicalTagId` but no endpoints or workflow.

**Recommended endpoints**:

```
GET  /api/tags/popular                    - Get popular tags
GET  /api/tags/search?q=                  - Search tags
POST /api/admin/tags/{id}/approve         - Approve tag (admin)
POST /api/admin/tags/{id}/ban             - Ban tag (admin)
POST /api/admin/tags/{id}/merge/{canonicalId} - Merge duplicate tags
```

**Benefits**:

* Better review discovery
* Trending activities insight
* Content moderation capabilities
* User-generated categorization

***