using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Storage;
using WhatsAppCampaignApi.Services.Email;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;
    private readonly IDashboardCacheService _dashboardCacheService;
    private readonly IFileStorage _storage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CampaignsController> _logger;

    public CampaignsController(
        ICampaignService campaignService,
        IDashboardCacheService dashboardCacheService,
        IFileStorage storage,
        IConfiguration configuration,
        ILogger<CampaignsController> logger)
    {
        _campaignService = campaignService;
        _dashboardCacheService = dashboardCacheService;
        _storage = storage;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>The public origin links are built on: App:PublicBaseUrl, else this request's.</summary>
    private string PublicOrigin =>
        _configuration["App:PublicBaseUrl"]?.TrimEnd('/') is { Length: > 0 } configured
            ? configured
            : $"{Request.Scheme}://{Request.Host.Value}";

    [HttpGet]
    [RequiresPermission("Campaign.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CampaignResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] string? channel = null,
        [FromQuery] string? template = null,
        [FromQuery] string? relationType = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true)
    {
        // Filtering, sorting and paging all happen in the database: the list used to download
        // every campaign and do all three in the browser.
        var request = new PagedRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        var data = await _campaignService.GetAllAsync(request, new CampaignListFilter
        {
            Status = status,
            Channel = channel,
            Template = template,
            RelationType = relationType,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo
        });
        return Ok(new ApiResponse<PagedResponse<CampaignResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    [RequiresPermission("Campaign.View")]
    public async Task<ActionResult<ApiResponse<CampaignDetailResponse>>> GetById(int id)
    {
        var data = await _campaignService.GetByIdAsync(id);
        return Ok(new ApiResponse<CampaignDetailResponse> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("Campaign.Create")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Create([FromBody] CreateCampaignRequest request)
    {
        var data = await _campaignService.CreateAsync(request);
        _dashboardCacheService.InvalidateCache();
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<CampaignResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    [RequiresPermission("Campaign.Edit")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Update(int id, [FromBody] CreateCampaignRequest request)
    {
        var data = await _campaignService.UpdateAsync(id, request);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign updated successfully." });
    }

    [HttpDelete("{id}")]
    [RequiresPermission("Campaign.Delete")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _campaignService.DeleteAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse { Success = true, Message = "Campaign deleted successfully." });
    }

    [HttpGet("exists")]
    [RequiresPermission("Campaign.View")]
    public async Task<ActionResult<ApiResponse<bool>>> CheckNameExists([FromQuery] string name, [FromQuery] int? excludeId = null)
    {
        var exists = await _campaignService.CheckNameExistsAsync(name, excludeId);
        return Ok(new ApiResponse<bool> { Success = true, Data = exists });
    }

    [HttpPost("{id}/cancel")]
    [RequiresPermission("Campaign.Send")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Cancel(int id)
    {
        var data = await _campaignService.CancelAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign cancelled successfully." });
    }

    /// <summary>Maker-checker: how many campaigns are waiting for approval (for the list badge).</summary>
    [HttpGet("pending-approval/count")]
    [RequiresPermission("Campaign.View")]
    public async Task<ActionResult<ApiResponse<int>>> PendingApprovalCount()
    {
        var count = await _campaignService.GetPendingApprovalCountAsync();
        return Ok(new ApiResponse<int> { Success = true, Data = count });
    }

    /// <summary>Pre-flight check: sender DNS (SPF, DKIM, DMARC, MX) and content. Nothing is sent.</summary>
    [HttpPost("precheck")]
    [RequiresPermission("Campaign.Create", "Campaign.Edit")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PrecheckItem>>>> Precheck(
        [FromServices] IDeliverabilityService deliverability, [FromBody] PrecheckRequest request, CancellationToken ct)
    {
        var items = await deliverability.PrecheckAsync(request, ct);
        return Ok(new ApiResponse<IReadOnlyList<PrecheckItem>> { Success = true, Data = items });
    }

    /// <summary>Sends the rendered email (sample values) to up to five proof addresses.</summary>
    [HttpPost("proof")]
    [RequiresPermission("Campaign.Create", "Campaign.Edit")]
    public async Task<ActionResult<ApiResponse<int>>> Proof(
        [FromServices] IDeliverabilityService deliverability, [FromBody] ProofSendRequest request, CancellationToken ct)
    {
        var sent = await deliverability.SendProofAsync(request, ct);
        return Ok(new ApiResponse<int> { Success = sent > 0, Data = sent, Message = $"Proof sent to {sent} address(es)." });
    }

    /// <summary>Email campaigns: clicks per link, total and unique.</summary>
    [HttpGet("{id}/links")]
    [RequiresPermission("Campaign.View")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CampaignLinkClicks>>>> Links(int id, CancellationToken ct)
    {
        var data = await _campaignService.GetLinkReportAsync(id, ct);
        return Ok(new ApiResponse<IReadOnlyList<CampaignLinkClicks>> { Success = true, Data = data });
    }

    /// <summary>A/B tests: pick the winner now (optionally a specific variant) and send it to everyone held back.</summary>
    [HttpPost("{id}/ab-test/decide")]
    [RequiresPermission("Campaign.Send")]
    public async Task<ActionResult<ApiResponse<int?>>> DecideAbTest(int id, [FromBody] AbDecisionRequest? request)
    {
        var winner = await _campaignService.DecideAbTestAsync(id, request?.VariantId);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<int?> { Success = true, Data = winner, Message = "Winner chosen and released." });
    }

    [HttpPost("{id}/retry")]
    [RequiresPermission("Campaign.Retry")]
    public async Task<ActionResult<ApiResponse<CampaignRetryResponse>>> RetryFailed(int id)
    {
        var data = await _campaignService.RetryFailedAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<CampaignRetryResponse>
        {
            Success = true,
            Data = data,
            Message = $"Retrying {data.RecipientCount} failed recipient(s)."
        });
    }

    [HttpPost("{id}/approve")]
    [RequiresPermission("Campaign.Approve")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Approve(int id, [FromBody] CampaignApprovalDecisionRequest? request)
    {
        var data = await _campaignService.ApproveAsync(id, request?.Comment);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign approved." });
    }

    [HttpPost("{id}/reject")]
    [RequiresPermission("Campaign.Approve")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Reject(int id, [FromBody] CampaignApprovalDecisionRequest? request)
    {
        var data = await _campaignService.RejectAsync(id, request?.Comment);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign rejected." });
    }

    [HttpPost("{id}/pause")]
    [RequiresPermission("Campaign.Send")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Pause(int id)
    {
        var data = await _campaignService.PauseAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign paused successfully." });
    }

    [HttpPost("{id}/resume")]
    [RequiresPermission("Campaign.Send")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Resume(int id)
    {
        var data = await _campaignService.ResumeAsync(id);
        _dashboardCacheService.InvalidateCache();
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign resumed successfully." });
    }

    [HttpGet("{id}/recipients")]
    [RequiresPermission("Campaign.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CampaignRecipientResponse>>>> GetRecipients(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? state = null,
        [FromQuery] string? search = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, Search = search };
        var data = await _campaignService.GetRecipientsAsync(id, request, state);
        return Ok(new ApiResponse<PagedResponse<CampaignRecipientResponse>> { Success = true, Data = data });
    }

    /// <summary>Queued vs executed recipient counts, for the detail page's tabs.</summary>
    [HttpGet("{id}/recipients/counts")]
    [RequiresPermission("Campaign.View")]
    public async Task<IActionResult> GetRecipientCounts(int id)
    {
        var (queued, executed) = await _campaignService.GetRecipientCountsAsync(id);
        return Ok(new ApiResponse<object> { Success = true, Data = new { queued, executed } });
    }

    /// <summary>The campaign's full execution log as CSV, streamed.</summary>
    [HttpGet("{id}/recipients/export")]
    [RequiresPermission("Campaign.View")]
    public async Task ExportRecipients(int id, CancellationToken ct)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"campaign-{id}-recipients-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv\"";
        await _campaignService.ExportRecipientsCsvAsync(id, Response.Body, ct);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequiresPermission("Campaign.Create", "Campaign.Edit", "BulkCampaign.Create")]
    [RequestSizeLimit(30L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 30L * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<UploadResponse>>> UploadFile(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ApiResponse<UploadResponse> { Success = false, Message = "No file uploaded." });
        }

        // Stored through the storage gatekeeper: only media types on the allow-list, verified by
        // content rather than by the name the client chose, under a generated file name. This
        // endpoint used to accept anything — an .html or .svg upload was then served from our own
        // origin, which is stored cross-site scripting against every signed-in user.
        StoredFile stored;
        try
        {
            await using var content = file.OpenReadStream();
            stored = await _storage.SavePublicMediaAsync(content, file.FileName, file.ContentType, "campaigns", ct);
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new ApiResponse<UploadResponse> { Success = false, Message = ex.Message });
        }

        var fileUrl = $"{PublicOrigin}{stored.RelativeUrl}";

        return Ok(new ApiResponse<UploadResponse>
        {
            Success = true,
            Data = new UploadResponse
            {
                Url = fileUrl,
                FileName = file.FileName
            },
            Message = "File uploaded successfully."
        });
    }

    /// <summary>
    /// A sample CSV showing the columns the importer accepts.
    ///
    /// Several rows rather than one: a single row does not show that the file is meant to hold
    /// many, and a sample with a quoted comma in it is the fastest way to answer "what if my data
    /// contains a comma" without anyone reading documentation.
    /// </summary>
    [HttpGet("csv-sample")]
    [RequiresPermission("BulkCampaign.View")]
    public IActionResult GetCsvSample()
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("firstname,lastname,phone,email,country");
        csv.AppendLine("Aditya,Sharma,+919812345670,aditya.sharma@example.com,India");
        csv.AppendLine("Priya,Nair,+919812345671,priya.nair@example.com,India");
        csv.AppendLine("John,Miller,+15551234567,john.miller@example.com,United States");
        csv.AppendLine("\"Fernandes, Ana\",Costa,+5511998765432,ana.costa@example.com,Brazil");

        // The BOM is deliberate: without it Excel opens a UTF-8 CSV as the local ANSI codepage and
        // mangles every non-ASCII name in the file.
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv.ToString()))
            .ToArray();

        return File(bytes, "text/csv", "bulk_campaign_sample.csv");
    }

    /// <summary>
    /// Reads an uploaded CSV and reports how many rows are usable, without creating anything.
    ///
    /// <para>
    /// Streams the file a record at a time through <see cref="BulkCampaignCsv"/> rather than
    /// loading it with <c>ReadAllLines</c>, so memory is flat regardless of row count, and quoted
    /// fields containing commas or newlines parse correctly instead of silently becoming broken
    /// rows.
    /// </para>
    /// <para>
    /// Every judgement here — which columns count, what a valid phone is, what counts as a name —
    /// comes from the same helper the create path uses, so the preview cannot promise a different
    /// number of recipients than the campaign ends up with.
    /// </para>
    /// </summary>
    [HttpPost("csv-validate")]
    [Consumes("multipart/form-data")]
    [RequiresPermission("BulkCampaign.Create")]
    [RequestSizeLimit(CsvUploadLimits.MaxBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = CsvUploadLimits.MaxBytes)]
    public async Task<ActionResult<ApiResponse<CsvValidationResponse>>> ValidateCsv(
        IFormFile file,
        CancellationToken cancellationToken,
        // Form field rather than a route or query value: the file and the channel it is being
        // validated for arrive in the same multipart body. Absent means WhatsApp, so every
        // caller written before the email channel existed still validates exactly as before.
        [FromForm] string? channel = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "No file uploaded." });
        }

        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "Only .csv files can be uploaded. Export your sheet as CSV and try again." });
        }

        // Customer lists are personal data: stored outside the web root, never served. They used
        // to sit in wwwroot/uploads/csv, where anyone with the URL could download them.
        string privateKey;
        await using (var content = file.OpenReadStream())
        {
            privateKey = await _storage.SavePrivateAsync(content, file.FileName, "csv", cancellationToken);
        }

        var filePath = _storage.ResolvePrivatePath(privateKey)!;
        var uniqueFileName = Path.GetFileName(filePath);

        if (!Enum.TryParse<MessageChannel>(channel, true, out var parsedChannel))
        {
            parsedChannel = MessageChannel.WhatsApp;
        }

        int totalRecords = 0, validCount = 0, invalidCount = 0;
        var errors = new List<CsvRowError>();
        CsvColumnMap? map = null;

        try
        {
            await using var readStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

            await foreach (var (rowNumber, fields) in BulkCampaignCsv.ReadRecordsAsync(readStream, cancellationToken))
            {
                if (map == null)
                {
                    map = BulkCampaignCsv.MapColumns(fields, parsedChannel);
                    if (map == null)
                    {
                        System.IO.File.Delete(filePath);
                        return BadRequest(new ApiResponse<CsvValidationResponse>
                        {
                            Success = false,
                            Message = parsedChannel == MessageChannel.Email
                                ? "An email campaign's file needs a phone column, a name column and an email column. Download the sample file to see the expected format."
                                : "The file needs a phone column and a name column. Download the sample file to see the expected format."
                        });
                    }

                    continue;
                }

                totalRecords++;

                if (BulkCampaignCsv.TryReadRow(fields, rowNumber, map, out _, out var rowErrors, parsedChannel))
                {
                    validCount++;
                }
                else
                {
                    invalidCount++;
                    // The count stays exact; only the list is capped, so a wholly malformed file
                    // cannot produce a response bigger than the upload that caused it.
                    if (errors.Count < BulkCampaignCsv.MaxReportedErrors)
                    {
                        errors.AddRange(rowErrors.Take(BulkCampaignCsv.MaxReportedErrors - errors.Count));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read uploaded CSV {FileName}", file.FileName);
            System.IO.File.Delete(filePath);
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "The file could not be read as CSV. Download the sample file to see the expected format." });
        }

        if (map == null || totalRecords == 0)
        {
            System.IO.File.Delete(filePath);
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "The file has a header row but no data rows." });
        }

        // Valid rows proceed even when some rows in the same file are invalid — invalid rows are
        // reported (not silently dropped) rather than rejecting the whole upload, matching standard
        // bulk-import UX. When there are zero valid rows the uploaded file itself is discarded
        // (nothing usable to create a campaign from), but the response still succeeds (HTTP 200)
        // with the row-level error list, instead of an opaque 400 that says nothing about why.
        if (validCount == 0)
        {
            System.IO.File.Delete(filePath);
            return Ok(new ApiResponse<CsvValidationResponse>
            {
                Success = true,
                Data = new CsvValidationResponse
                {
                    FileUrl = string.Empty,
                    FileName = file.FileName,
                    TotalRecords = totalRecords,
                    ValidCount = 0,
                    InvalidCount = invalidCount,
                    Errors = errors
                },
                Message = "No valid records found in the CSV file. See the row errors below."
            });
        }

        var fileUrl = $"{Request.Scheme}://{Request.Host.Value}/uploads/csv/{uniqueFileName}";

        return Ok(new ApiResponse<CsvValidationResponse>
        {
            Success = true,
            Data = new CsvValidationResponse
            {
                FileUrl = fileUrl,
                FileName = file.FileName,
                TotalRecords = totalRecords,
                ValidCount = validCount,
                InvalidCount = invalidCount,
                Errors = errors
            },
            Message = invalidCount == 0
                ? $"{validCount:N0} record(s) ready to send."
                : $"{validCount:N0} of {totalRecords:N0} record(s) are ready to send; {invalidCount:N0} row(s) will be skipped."
        });
    }

    [HttpPost("csv-create")]
    [RequiresPermission("BulkCampaign.Create")]
    public async Task<ActionResult<ApiResponse<CsvCampaignCreateResponse>>> CreateCsvCampaign([FromBody] CreateCsvCampaignRequest request)
    {
        try
        {
            var data = await _campaignService.CreateCsvCampaignAsync(request);
            return Ok(new ApiResponse<CsvCampaignCreateResponse> { Success = true, Data = data, Message = "Campaign created from CSV successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<CsvCampaignCreateResponse> { Success = false, Message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiResponse<CsvCampaignCreateResponse> { Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create campaign from CSV. Inner exception: {InnerException}", ex.InnerException?.Message);
            return StatusCode(500, new ApiResponse<CsvCampaignCreateResponse> { Success = false, Message = "Failed to create campaign. Please check your CSV data and try again." });
        }
    }

}

public class UploadResponse
{
    public string Url { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public class CsvValidationResponse
{
    public string FileUrl { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int ValidCount { get; set; }
    public int InvalidCount { get; set; }
    public List<CsvRowError> Errors { get; set; } = new();
}
