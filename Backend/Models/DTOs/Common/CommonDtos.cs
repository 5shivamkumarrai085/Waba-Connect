namespace WhatsAppCampaignApi.Models.DTOs.Common;

public class PagedRequest
{
    /// <summary>
    /// Largest page any list endpoint returns. A client asking for more gets this many and pages
    /// on: an unbounded page size is a single request that can load a whole table into memory.
    /// </summary>
    public const int MaxPageSize = 200;

    private int _page = 1;
    private int _pageSize = 20;

    /// <summary>1-based. Zero or negative is treated as the first page (it used to produce a
    /// negative OFFSET and a 500).</summary>
    public int Page
    {
        get => _page;
        set => _page = Math.Max(1, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; }
}

/// <summary>Server-side filters for the campaigns list.</summary>
public class CampaignListFilter
{
    public string? Status { get; set; }
    public string? Channel { get; set; }
    /// <summary>Exact template name (WhatsApp or email).</summary>
    public string? Template { get; set; }
    /// <summary>One relation type; matches campaigns that include it.</summary>
    public string? RelationType { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public class PagedResponse<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
}
