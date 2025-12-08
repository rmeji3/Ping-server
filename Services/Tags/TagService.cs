using Conquest.Data.App;
using Conquest.Dtos.Tags;
using Conquest.Models.Reviews;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Conquest.Services.Tags;

public class TagService(AppDbContext db, ILogger<TagService> logger) : ITagService
{
    public async Task<IEnumerable<TagDto>> GetPopularTagsAsync(int count)
    {
        // Count usage in ReviewTags
        var tags = await db.Tags
            .AsNoTracking()
            .Where(t => !t.IsBanned && t.IsApproved) // Only show safe tags publicly? Or maybe just not banned?
            // Let's assume popular list should only show approved/safe tags
            .Select(t => new
            {
                Tag = t,
                Count = t.ReviewTags.Count
            })
            .OrderByDescending(x => x.Count)
            .Take(count)
            .ToListAsync();

        return tags.Select(x => new TagDto(
            x.Tag.Id,
            x.Tag.Name,
            x.Count,
            x.Tag.IsApproved,
            x.Tag.IsBanned
        ));
    }

    public async Task<IEnumerable<TagDto>> SearchTagsAsync(string query, int count)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<TagDto>();

        var q = query.Trim().ToLowerInvariant();

        var tags = await db.Tags
            .AsNoTracking()
            .Where(t => t.Name.Contains(q) && !t.IsBanned) // Don't suggest banned tags
            .OrderBy(t => t.Name.Length) // Exact/shorter matches first
            .Take(count)
            .Select(t => new TagDto(
                t.Id,
                t.Name,
                t.ReviewTags.Count, // Include count for context
                t.IsApproved,
                t.IsBanned
            ))
            .ToListAsync();

        return tags;
    }

    public async Task ApproveTagAsync(int id)
    {
        var tag = await db.Tags.FindAsync(id);
        if (tag == null) throw new KeyNotFoundException("Tag not found");

        tag.IsApproved = true;
        tag.IsBanned = false; // mutually exclusive usually
        await db.SaveChangesAsync();
        logger.LogInformation("Tag {TagId} approved", id);
    }

    public async Task BanTagAsync(int id)
    {
        var tag = await db.Tags.FindAsync(id);
        if (tag == null) throw new KeyNotFoundException("Tag not found");

        tag.IsBanned = true;
        tag.IsApproved = false;
        await db.SaveChangesAsync();
        logger.LogInformation("Tag {TagId} banned", id);
    }

    public async Task MergeTagAsync(int sourceId, int targetId)
    {
        if (sourceId == targetId) return;

        var sourceTag = await db.Tags.Include(t => t.ReviewTags).FirstOrDefaultAsync(t => t.Id == sourceId);
        var targetTag = await db.Tags.FindAsync(targetId); // Just check existence

        if (sourceTag == null || targetTag == null)
            throw new KeyNotFoundException("One or both tags not found");

        // Strategy: Re-parent ReviewTags
        // 1. Identify which reviews have the source tag
        // 2. For each review, check if it ALREADY has the target tag
        // 3. If yes -> remove source ReviewTag (avoid duplicate key)
        // 4. If no -> change source ReviewTag.TagId to targetId

        // We need to fetch ReviewTags for source
        var sourceReviewTags = sourceTag.ReviewTags.ToList();
        
        // We also need to know which reviews already have the target tag to avoid duplicates
        // (ReviewId, TagId) is PK
        var reviewIdsWithSource = sourceReviewTags.Select(rt => rt.ReviewId).ToList();
        
        var existingTargetReviewTags = await db.ReviewTags
            .Where(rt => rt.TagId == targetId && reviewIdsWithSource.Contains(rt.ReviewId))
            .Select(rt => rt.ReviewId)
            //.ToHashSetAsync(); // ToHashSetAsync not always available in older EF, use specific
            .ToListAsync();
            
        var reviewsAlreadyHavingTarget = new HashSet<int>(existingTargetReviewTags);

        foreach (var rt in sourceReviewTags)
        {
            if (reviewsAlreadyHavingTarget.Contains(rt.ReviewId))
            {
                // This review already has the target tag, so just remove the source tag entry
                db.ReviewTags.Remove(rt);
            }
            else
            {
                // Move this entry to the target tag
                // Since PK is (ReviewId, TagId), we can't just update TagId if we are tracking it? 
                // Actually EF might complain if we mutate part of PK.
                // Best to Remove and Add new.
                db.ReviewTags.Remove(rt);
                db.ReviewTags.Add(new ReviewTag
                {
                    ReviewId = rt.ReviewId,
                    TagId = targetId
                });
            }
        }

        // Delete the source tag
        db.Tags.Remove(sourceTag);

        await db.SaveChangesAsync();
        logger.LogInformation("Merged tag {SourceId} into {TargetId}", sourceId, targetId);
    }
}
