using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignService _campaignService;

    public CampaignsController(ICampaignService campaignService)
    {
        _campaignService = campaignService;
    }

    [HttpGet]
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
    public async Task<ActionResult<ApiResponse<CampaignDetailResponse>>> GetById(int id)
    {
        var data = await _campaignService.GetByIdAsync(id);
        return Ok(new ApiResponse<CampaignDetailResponse> { Success = true, Data = data });
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Create([FromBody] CreateCampaignRequest request)
    {
        var data = await _campaignService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<CampaignResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Update(int id, [FromBody] CreateCampaignRequest request)
    {
        var data = await _campaignService.UpdateAsync(id, request);
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign updated successfully." });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _campaignService.DeleteAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Campaign deleted successfully." });
    }

    [HttpGet("exists")]
    public async Task<ActionResult<ApiResponse<bool>>> CheckNameExists([FromQuery] string name, [FromQuery] int? excludeId = null)
    {
        var exists = await _campaignService.CheckNameExistsAsync(name, excludeId);
        return Ok(new ApiResponse<bool> { Success = true, Data = exists });
    }

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Cancel(int id)
    {
        var data = await _campaignService.CancelAsync(id);
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign cancelled successfully." });
    }

    [HttpPost("{id}/pause")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Pause(int id)
    {
        var data = await _campaignService.PauseAsync(id);
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign paused successfully." });
    }

    [HttpPost("{id}/resume")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> Resume(int id)
    {
        var data = await _campaignService.ResumeAsync(id);
        return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign resumed successfully." });
    }

    [HttpGet("{id}/recipients")]
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
    public IActionResult GetCsvSample()
    {
        var csvContent = "firstname,lastname,phone,email,country\nSample Data,Sample Data,+15551234567,66d824de53e6b@example.com,Sample Data\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);
        return File(bytes, "text/csv", "campaigns_sample.csv");
    }

    [HttpPost("csv-validate")]
    [Consumes("multipart/form-data")]
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

        var headers = SplitCsvRow(lines[0]).Select(h => h.ToLower().Trim()).ToList();

        int phoneIdx = headers.FindIndex(h => h == "phone" || h == "phoneno" || h == "phone number" || h == "telephone");
        int firstNameIdx = headers.FindIndex(h => h == "firstname" || h == "first name" || h == "name");

        if (phoneIdx == -1 || firstNameIdx == -1)
        {
            System.IO.File.Delete(filePath);
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "cannot upload wrong format csv file" });
        }

        var phoneRegex = new System.Text.RegularExpressions.Regex(@"^\+[1-9]\d{6,14}$");
        int totalRecords = 0;
        int validCount = 0;
        int invalidCount = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            totalRecords++;
            var fields = SplitCsvRow(line);
            if (fields.Count <= Math.Max(phoneIdx, firstNameIdx))
            {
                invalidCount++;
                continue;
            }

            var phoneVal = fields[phoneIdx].Trim();
            var cleanedPhone = phoneVal.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
            if (!cleanedPhone.StartsWith("+"))
            {
                cleanedPhone = "+" + cleanedPhone;
            }

            var firstName = fields[firstNameIdx].Trim();

            if (phoneRegex.IsMatch(cleanedPhone) && firstName.Length >= 2)
            {
                validCount++;
            }
            else
            {
                invalidCount++;
            }
        }

        if (validCount == 0)
        {
            System.IO.File.Delete(filePath);
            return BadRequest(new ApiResponse<CsvValidationResponse> { Success = false, Message = "cannot upload wrong format csv file" });
        }

        var requestScheme = Request.Scheme;
        var requestHost = Request.Host.Value;
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
                InvalidCount = invalidCount
            },
            Message = "CSV uploaded successfully"
        });
    }

    [HttpPost("csv-create")]
    public async Task<ActionResult<ApiResponse<CampaignResponse>>> CreateCsvCampaign([FromBody] CreateCsvCampaignRequest request)
    {
        try
        {
            var data = await _campaignService.CreateCsvCampaignAsync(request);
            return Ok(new ApiResponse<CampaignResponse> { Success = true, Data = data, Message = "Campaign created from CSV successfully." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiResponse<CampaignResponse> { Success = false, Message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiResponse<CampaignResponse> { Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<CampaignResponse> { Success = false, Message = ex.Message });
        }
    }

    private static List<string> SplitCsvRow(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var currentField = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField.ToString().Trim(' ', '"'));
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        result.Add(currentField.ToString().Trim(' ', '"'));
        return result;
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
}
