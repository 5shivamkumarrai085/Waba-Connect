using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;
    private readonly IDashboardCacheService _dashboardCacheService;
    private readonly ILogger<CampaignsController> _logger;

    public CampaignsController(ICampaignService campaignService, IDashboardCacheService dashboardCacheService, ILogger<CampaignsController> logger)
    {
        _campaignService = campaignService;
        _dashboardCacheService = dashboardCacheService;
        _logger = logger;
    }

    [HttpGet]
    [RequiresPermission("Campaign.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<CampaignResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, Search = search };
        var data = await _campaignService.GetAllAsync(request, status);
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
        [FromQuery] int pageSize = 20)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize };
        var data = await _campaignService.GetRecipientsAsync(id, request);
        return Ok(new ApiResponse<PagedResponse<CampaignRecipientResponse>> { Success = true, Data = data });
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequiresPermission("Campaign.Create", "Campaign.Edit", "BulkCampaign.Create")]
    public async Task<ActionResult<ApiResponse<UploadResponse>>> UploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ApiResponse<UploadResponse> { Success = false, Message = "No file uploaded." });
        }

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        var requestScheme = Request.Scheme;
        var requestHost = Request.Host.Value;
        var fileUrl = $"{requestScheme}://{requestHost}/uploads/{uniqueFileName}";

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

    [HttpGet("csv-sample")]
    [RequiresPermission("BulkCampaign.View")]
    public IActionResult GetCsvSample()
    {
        var csvContent = "firstname,lastname,phone,email,country\nSample Data,Sample Data,+15551234567,66d824de53e6b@example.com,Sample Data\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
        return File(bytes, "text/csv", "campaigns_sample.csv");
    }

    [HttpPost("csv-validate")]
    [Consumes("multipart/form-data")]
    [RequiresPermission("BulkCampaign.Create")]
    public async Task<ActionResult<ApiResponse<CsvValidationResponse>>> ValidateCsv(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "No file uploaded." });
        }

        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "cannot upload wrong format csv file" });
        }

        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "csv");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        var lines = await System.IO.File.ReadAllLinesAsync(filePath);
        if (lines.Length < 2)
        {
            System.IO.File.Delete(filePath);
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "cannot upload wrong format csv file" });
        }

        var headers = CsvHelper.SplitCsvRow(lines[0]).Select(h => h.ToLower().Trim()).ToList();

        int phoneIdx = headers.FindIndex(h => h == "phone" || h == "phoneno" || h == "phone number" || h == "telephone");
        int firstNameIdx = headers.FindIndex(h => h == "firstname" || h == "first name" || h == "name");

        if (phoneIdx == -1 || firstNameIdx == -1)
        {
            System.IO.File.Delete(filePath);
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "cannot upload wrong format csv file" });
        }

        int totalRecords = 0;
        int validCount = 0;
        int invalidCount = 0;
        var errors = new List<CsvRowError>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            totalRecords++;
            var rowNumber = i + 1; // 1-based, matches what a user sees opening the file in a spreadsheet (row 1 = header)
            var fields = CsvHelper.SplitCsvRow(line);
            if (fields.Count <= Math.Max(phoneIdx, firstNameIdx))
            {
                invalidCount++;
                errors.Add(new CsvRowError { RowNumber = rowNumber, Column = null, Value = line, Reason = "Row has fewer columns than the header row." });
                continue;
            }

            var phoneVal = fields[phoneIdx].Trim();
            // PhoneNumberHelper is the single normaliser for the whole app. The inline version
            // that used to live here stripped only spaces, dashes and brackets, so a phone column
            // Excel had typed as a number ("919143000000.0") failed every row of an otherwise
            // good file — and said only that the format was invalid.
            var phoneValid = PhoneNumberHelper.TryNormalize(phoneVal, out _, out var phoneFailure);

            var firstName = fields[firstNameIdx].Trim();
            var nameValid = firstName.Length >= 2;

            if (phoneValid && nameValid)
            {
                validCount++;
            }
            else
            {
                invalidCount++;
                if (!phoneValid)
                {
                    errors.Add(new CsvRowError { RowNumber = rowNumber, Column = "phone", Value = phoneVal, Reason = phoneFailure! });
                }
                if (!nameValid)
                {
                    errors.Add(new CsvRowError { RowNumber = rowNumber, Column = "firstname", Value = firstName, Reason = "Name must be at least 2 characters long." });
                }
            }
        }

        var requestScheme = Request.Scheme;
        var requestHost = Request.Host.Value;

        // Valid rows proceed even when some rows in the same file are invalid — invalid
        // rows are reported (not silently dropped) rather than rejecting the whole upload,
        // matching standard bulk-import UX. When there are zero valid rows the uploaded
        // file itself is discarded (nothing usable to create a campaign from), but the
        // response still succeeds (HTTP 200) with the full row-level error list, instead
        // of the previous opaque 400 that gave no indication of what was wrong.
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

        var fileUrl = $"{requestScheme}://{requestHost}/uploads/csv/{uniqueFileName}";

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
            Message = "CSV uploaded successfully"
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
