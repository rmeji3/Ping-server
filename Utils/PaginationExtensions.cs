using Ping.Dtos.Common;
using Microsoft.EntityFrameworkCore;

namespace Ping.Utils;

public static class PaginationExtensions
{
    public static async Task<PaginatedResult<T>> ToPaginatedResultAsync<T>(this IQueryable<T> source, PaginationParams p)
    {
        var count = await source.CountAsync();
        var items = await source
            .Skip((p.PageNumber - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync();

        return new PaginatedResult<T>(items, count, p.PageNumber, p.PageSize);
    }

    public static PaginatedResult<T> ToPaginatedResult<T>(this IEnumerable<T> source, PaginationParams p)
    {
        var enumerable = source.ToList();
        var count = enumerable.Count;
        var items = enumerable
            .Skip((p.PageNumber - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToList();

        return new PaginatedResult<T>(items, count, p.PageNumber, p.PageSize);
    }

    public static PaginatedResult<T> ToPaginatedResult<T>(this IEnumerable<T> source, PaginationParams p, int totalCount)
    {
        return new PaginatedResult<T>(source, totalCount, p.PageNumber, p.PageSize);
    }
}

