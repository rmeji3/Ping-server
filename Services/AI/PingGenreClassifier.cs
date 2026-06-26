using Ping.Data.App;
using Ping.Services.Background;
using Ping.Services.Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ping.Services.AI;

public class PingGenreClassifier(
    AppDbContext db,
    IPingNameService pingNameService,
    ISemanticService semanticService,
    ILogger<PingGenreClassifier> logger) : IPingGenreClassifier
{
    /// <summary>
    /// Priority-ordered mapping from Google Place types to Ping genre names.
    /// Evaluated top-to-bottom; first match wins.
    /// </summary>
    private static readonly (string googleType, string genreName)[] GoogleTypeMap =
    [
        // Nightlife
        ("night_club",        "Nightlife"),
        ("bar",               "Nightlife"),
        // Cafe
        ("cafe",              "Cafe"),
        ("bakery",            "Cafe"),
        // Food
        ("restaurant",        "Food"),
        ("food",              "Food"),
        ("meal_takeaway",     "Food"),
        ("meal_delivery",     "Food"),
        // Wellness / Health
        ("gym",               "Wellness"),
        ("spa",               "Wellness"),
        ("beauty_salon",      "Wellness"),
        ("hair_care",         "Wellness"),
        ("hospital",          "Wellness"),
        ("doctor",            "Wellness"),
        ("pharmacy",          "Wellness"),
        ("dentist",           "Wellness"),
        ("physiotherapist",   "Wellness"),
        // Outdoors
        ("park",              "Outdoors"),
        ("campground",        "Outdoors"),
        ("rv_park",           "Outdoors"),
        ("natural_feature",   "Outdoors"),
        // Sports
        ("stadium",           "Sports"),
        ("sports_complex",    "Sports"),
        ("bowling_alley",     "Sports"),
        ("golf_course",       "Sports"),
        ("ski_resort",        "Sports"),
        // Art
        ("museum",            "Art"),
        ("art_gallery",       "Art"),
        // Entertainment
        ("movie_theater",     "Entertainment"),
        ("amusement_park",    "Entertainment"),
        ("casino",            "Entertainment"),
        ("zoo",               "Entertainment"),
        ("aquarium",          "Entertainment"),
        ("performing_arts_theater", "Entertainment"),
        // Shopping
        ("shopping_mall",     "Shopping"),
        ("supermarket",       "Shopping"),
        ("grocery_or_supermarket", "Shopping"),
        ("store",             "Shopping"),
        ("convenience_store", "Shopping"),
        // Fashion
        ("clothing_store",    "Fashion"),
        ("shoe_store",        "Fashion"),
        ("jewelry_store",     "Fashion"),
        // Automotive
        ("car_dealer",        "Automotive"),
        ("car_repair",        "Automotive"),
        ("car_wash",          "Automotive"),
        ("gas_station",       "Automotive"),
        // Education
        ("school",            "Education"),
        ("secondary_school",  "Education"),
        ("university",        "Education"),
        ("library",           "Education"),
        // Travel
        ("lodging",           "Travel"),
        ("airport",           "Travel"),
        ("train_station",     "Travel"),
        ("bus_station",       "Travel"),
        ("subway_station",    "Travel"),
        ("tourist_attraction","Travel"),
        // Parking
        ("parking",           "Parking"),
        // Pets
        ("pet_store",         "Pets"),
        ("veterinary_care",   "Pets"),
        // Tech
        ("electronics_store", "Tech"),
        // Music
        ("music_store",       "Music"),
    ];

    public async Task ClassifyAsync(PingGenreJob job, CancellationToken ct = default)
    {
        // Reload the ping to confirm it still exists and has no genre.
        var ping = await db.Pings
            .Include(p => p.PingActivities)
            .FirstOrDefaultAsync(p => p.Id == job.PingId && !p.IsDeleted, ct);

        if (ping is null)
        {
            logger.LogWarning("[GenreClassifier] Ping {PingId} not found or deleted. Skipping.", job.PingId);
            return;
        }

        if (ping.PingGenreId.HasValue)
        {
            logger.LogDebug("[GenreClassifier] Ping {PingId} already has genre {GenreId}. Skipping.", job.PingId, ping.PingGenreId);
            return;
        }

        // Load all genres once — they are static seed data (23 entries).
        var allGenres = await db.PingGenres.AsNoTracking().ToListAsync(ct);
        var genreByName = allGenres.ToDictionary(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase);

        // Check if there is an activity done
        var latestActivity = ping.PingActivities
            .OrderByDescending(a => a.CreatedUtc)
            .FirstOrDefault();
        var activityName = latestActivity?.Name;

        // If there is an activity, we prioritize it and bypass Tier 1 Google Place types.
        if (string.IsNullOrWhiteSpace(activityName) && !string.IsNullOrWhiteSpace(job.GooglePlaceId))
        {
            // ── Tier 1: deterministic mapping from Google Place types ─────────────
            logger.LogInformation("[GenreClassifier] Ping {PingId}: trying Tier 1 (Google Place types) for place {PlaceId}.", job.PingId, job.GooglePlaceId);

            var placeTypes = await pingNameService.GetGooglePlaceTypesAsync(job.GooglePlaceId);
            if (placeTypes.Count > 0)
            {
                logger.LogInformation("[GenreClassifier] Ping {PingId}: Google returned types [{Types}].", job.PingId, string.Join(", ", placeTypes));

                var typeSet = new HashSet<string>(placeTypes, StringComparer.OrdinalIgnoreCase);
                foreach (var (googleType, genreName) in GoogleTypeMap)
                {
                    if (typeSet.Contains(googleType) && genreByName.TryGetValue(genreName, out var genreId))
                    {
                        logger.LogInformation("[GenreClassifier] Ping {PingId}: Tier 1 match — type '{GoogleType}' → genre '{Genre}'.", job.PingId, googleType, genreName);
                        await PersistGenreAsync(ping, genreId, ct);
                        return;
                    }
                }

                logger.LogInformation("[GenreClassifier] Ping {PingId}: Tier 1 found no mapping for types [{Types}]. Falling through to Tier 2.", job.PingId, string.Join(", ", placeTypes));
            }
            else
            {
                logger.LogInformation("[GenreClassifier] Ping {PingId}: Tier 1 — no types returned from Google. Falling through to Tier 2.", job.PingId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(activityName))
        {
            logger.LogInformation("[GenreClassifier] Ping {PingId}: Activity '{Activity}' present — bypassing Tier 1.", job.PingId, activityName);
        }
        else
        {
            logger.LogInformation("[GenreClassifier] Ping {PingId}: no GooglePlaceId and no Activity — skipping Tier 1.", job.PingId);
        }

        // ── Tier 2: Semantic Kernel classification ───────────────────────────
        logger.LogInformation("[GenreClassifier] Ping {PingId}: trying Tier 2 (SK) for name \"{Name}\" and activity \"{Activity}\".", job.PingId, job.PingName, activityName);

        var genreNames = allGenres.Select(g => g.Name).ToList();
        var suggestedName = await semanticService.ClassifyGenreAsync(job.PingName, activityName, genreNames);

        if (!string.IsNullOrWhiteSpace(suggestedName) && genreByName.TryGetValue(suggestedName, out var skGenreId))
        {
            logger.LogInformation("[GenreClassifier] Ping {PingId}: Tier 2 SK → genre '{Genre}'.", job.PingId, suggestedName);
            await PersistGenreAsync(ping, skGenreId, ct);
        }
        else
        {
            logger.LogWarning("[GenreClassifier] Ping {PingId}: Tier 2 SK returned unrecognised genre '{Suggested}'. Leaving unclassified.", job.PingId, suggestedName);
        }
    }

    private async Task PersistGenreAsync(Models.Pings.Ping ping, int genreId, CancellationToken ct)
    {
        ping.PingGenreId = genreId;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("[GenreClassifier] Ping {PingId}: genre set to {GenreId}.", ping.Id, genreId);
    }
}
