﻿using AlertHub.Api.Cultures;
using AlertHub.Api.Extensions;
using AlertHub.Api.Models;
using AlertHub.Data;
using AlertHub.Data.DTOs;
using AlertHub.Data.Entities;
using AlertHub.Data.Entities.Enums;
using FluentValidation;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace AlertHub.Api.Controllers;
[Route("api/[controller]")]
[ApiController]
public class DangerReportController : ControllerBase
{
    private readonly ILogger<ApplicationDbContext> _logger;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _config;
    private readonly IValidator<CreateDangerReportDTO> _reportValidator;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;

    public DangerReportController(ILogger<ApplicationDbContext> logger, ApplicationDbContext dbContext, IConfiguration config,
        IValidator<CreateDangerReportDTO> reportValidator, IWebHostEnvironment environment, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _dbContext = dbContext;
        _config = config;
        _reportValidator = reportValidator;
        _environment = environment;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost]
    public async Task<ActionResult> Post([FromForm]CreateDangerReportDTO dangerReportDTO, CancellationToken cancellationToken)
    {
        try
        {
            var validationResult = await _reportValidator.ValidateAsync(dangerReportDTO, cancellationToken);

            if (validationResult.IsValid == false)
            {
                var errorsByProperty = new Dictionary<string, string>();
                validationResult.Errors.ForEach(validationFailure =>
                    errorsByProperty.Add(validationFailure.PropertyName, validationFailure.ErrorMessage));
                return BadRequest(new { errors = errorsByProperty });
            }

            string imageName;
            Task uploadTask;

            if (dangerReportDTO.ImageFile is not null)
            {
                var imageExtension = dangerReportDTO.ImageFile.ContentType.Split("/")[1];
                imageName = $"{Guid.NewGuid().ToString()}.{imageExtension}";
                uploadTask = Task.Run(async () =>
                {
                    await UploadImage(imageName, dangerReportDTO.ImageFile, cancellationToken);
                }, cancellationToken);
            }
            else
            {
                imageName = "no-image.png";
                uploadTask = Task.CompletedTask;
            }

            var newDangerReport = await CreateDangerReportFromDTO(dangerReportDTO, imageName);

            // Begin a transaction and only commit it only if no exceptions
            // are thrown. Otherwise roll the changes back
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            await _dbContext.DangerReports.AddAsync(newDangerReport, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _dbContext.ActiveDangerReports.AddAsync(new ActiveDangerReport
            {
                DangerReportId = newDangerReport.Id,
            }, cancellationToken);

            // Fire and run (and hodl)
            BackgroundJob.Enqueue(() =>
                CreateCoordinatesInformation(newDangerReport.Location.X, newDangerReport.Location.Y, 
                                                      newDangerReport.Id, cancellationToken)
            );

            await _dbContext.SaveChangesAsync(cancellationToken);

            await uploadTask;

            await _dbContext.Database.CommitTransactionAsync(cancellationToken);
            _logger.LogInformation("Successfully created danger report by user: {userid}", newDangerReport.UserId);

            return Ok(new {DangerReportId = newDangerReport.Id});
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception was thrown while trying to create a danger report");

            if (_dbContext.Database.CurrentTransaction != null)
            {
                await _dbContext.Database.RollbackTransactionAsync(CancellationToken.None);
            }

            return BadRequest();
        }
    }

        private async Task UploadImage(string imageName, IFormFile imageFile, CancellationToken cancellationToken)
    {
        var imagesPath = Path.Combine(_environment.WebRootPath, "UploadDangerReportImages");
        if (Directory.Exists(imagesPath) == false) {
            Directory.CreateDirectory(imagesPath);
        }

        await using FileStream fileStream = System.IO.File.Create(Path.Combine(imagesPath, imageName));
        await imageFile.CopyToAsync(fileStream, cancellationToken);
        await fileStream.FlushAsync(CancellationToken.None);
        _logger.LogInformation("Successfully uploaded image {imageName}", imageName);
    }

    private Task<DangerReport> CreateDangerReportFromDTO(CreateDangerReportDTO reportDTO, string imageName)
    {
        var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

        var disasterType = Enum.Parse<DisasterType>(reportDTO.DisasterType);
        var longitude = double.Parse(reportDTO.Longitude);
        var latitude = double.Parse(reportDTO.Latitude);
        var location = geometryFactory.CreatePoint(new Coordinate(longitude, latitude))!;
        var nowUtc = DateTime.UtcNow;

        var newDangerReport = new DangerReport
        {
            DisasterType = disasterType,
            Location = location,
            CreatedAt = nowUtc,
            ImageName = imageName,
            Description = reportDTO.Description,
            Status = ReportStatus.Pending,
            Culture = reportDTO.Culture,
            UserId = reportDTO.UserId
        };

        return Task.FromResult(newDangerReport);
    }

    [NonAction]
    public async Task CreateCoordinatesInformation(double longitude, double latitude, int dangerReportId, CancellationToken cancellationToken)
    {
        var chromeUserAgent = 
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36";
        var firefoxUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:110.0) Gecko/20100101 Firefox/110.0";

        var greekResponse = await FetchCoordinatesInfo(longitude, latitude, "el-GR", chromeUserAgent, cancellationToken);
        await AddCoordinatesInformationToDb(greekResponse, dangerReportId, cancellationToken);
        var englishResponse = await FetchCoordinatesInfo(longitude, latitude, "en-US", firefoxUserAgent, cancellationToken);
        await AddCoordinatesInformationToDb(englishResponse, dangerReportId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AddCoordinatesInformationToDb(NominatimResponse nominatimResponse, int dangerReportId, CancellationToken cancellationToken)
    {
        await _dbContext.CoordinatesInformation.AddAsync(new CoordinatesInformation
        {
            Country = nominatimResponse.Country,
            Municipality = nominatimResponse.Municipality,
            Culture = nominatimResponse.Culture,
            DangerReportId = dangerReportId
        }, cancellationToken);
    }

    private async Task<NominatimResponse> FetchCoordinatesInfo(double longitude, double latitude, string culture, 
        string userAgent, CancellationToken cancellationToken)
    {
        // Get only en from en-US, el from el-GR, etc
        var language = culture.Split("-")[0];

        var nominatimUrl = _config.GetValue<string>("NominatimUrl")!
            .Replace("{longitude}", longitude.ToString(CultureInfo.GetCultureInfo("en-US")))
            .Replace("{latitude}", latitude.ToString(CultureInfo.GetCultureInfo("en-US")))
            .Replace("{language}", language);

        _logger.LogDebug("The url is: {url}", nominatimUrl);

        var httpClient = _httpClientFactory.CreateClient();

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var response = await httpClient.GetAsync(nominatimUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP Error: {response.StatusCode}");
        }

        // Get the country and municipality from the fetch result
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var jsonDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var country = jsonDocument.RootElement.GetProperty("address").GetProperty("country").GetString();
        var municipality = jsonDocument.RootElement.GetProperty("address").GetProperty("municipality").GetString();

        return new NominatimResponse { Country = country, Municipality = municipality, Culture = culture };
    }

    [HttpGet]
    public async Task<ActionResult> Get(int dangerReportId, string culture)
    {
        try
        {
            string rootPath = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var imagesPath = $@"{rootPath}/UploadDangerReportImages";

            var report = await _dbContext.DangerReports
                .Select(dr => new DangerReportDTO
                {
                    Id = dr.Id,
                    DisasterType = dr.DisasterType.ToString(),
                    Longitude = dr.Location.X,
                    Latitude = dr.Location.Y,
                    CreatedAt = dr.CreatedAt,
                    ImageUrl = dr.ImageName != null ? 
                        $@"{imagesPath}/{dr.ImageName}" : 
                        null,
                    Description = dr.Description,
                    Status = dr.Status.ToString(),
                    Culture = dr.Culture,
                    Country = dr.CoordinatesInformation
                        .GetCountryByCulture(culture, dr.Id, dr.CoordinatesInformation),
                    Municipality = dr.CoordinatesInformation
                        .GetMunicipalityByCulture(culture, dr.Id, dr.CoordinatesInformation),
                    UserId = dr.UserId,
                })
                .FirstOrDefaultAsync(dr => dr.Id.Equals(dangerReportId));

            if (report == null)
            {
                _logger.LogWarning("The danger report with id: {dangerReportId} was not found", dangerReportId);
                return BadRequest("Wrong id");
            }

            report.DisasterType = DisasterConverter.TranslateDisaster(
                Enum.Parse<DisasterType>(report.DisasterType), culture);

            report.Status = StatusConverter.TranslateStatus(
                Enum.Parse<ReportStatus>(report.Status), culture);

            return Ok(report);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while trying to get danger report with id: {dangerReportId}", dangerReportId);
            return BadRequest();
        }
    }

    [HttpGet("GetActiveReportsByTimeDescending")]
    public async Task<ActionResult<List<DangerReportDTO>>> GetActiveReportsByTimeDescending(int pageNumber, int itemsPerPage, string culture)
    {
        try
        {
            string rootPath = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var imagesPath = $@"{rootPath}/UploadDangerReportImages";

            var paginatedReports = await _dbContext.ActiveDangerReports
                .AsNoTracking()
                .OrderByDescending(activeReport => activeReport.DangerReport.CreatedAt)
                .Paginate(pageNumber, itemsPerPage)
                .Select(activeReport => new DangerReportDTO
                {
                    Id = activeReport.DangerReportId,
                    DisasterType = activeReport.DangerReport.DisasterType.ToString(),
                    Longitude = activeReport.DangerReport.Location.X,
                    Latitude = activeReport.DangerReport.Location.Y,
                    CreatedAt = activeReport.DangerReport.CreatedAt,
                    ImageUrl = activeReport.DangerReport.ImageName != null ? 
                        $@"{imagesPath}/{activeReport.DangerReport.ImageName}" : 
                        null,
                    Description = activeReport.DangerReport.Description,
                    Status = activeReport.DangerReport.Status.ToString(),
                    Culture = activeReport.DangerReport.Culture,
                    Country = activeReport.DangerReport.CoordinatesInformation
                        .GetCountryByCulture(culture, activeReport.DangerReportId, activeReport.DangerReport.CoordinatesInformation),
                    Municipality = activeReport.DangerReport.CoordinatesInformation
                        .GetMunicipalityByCulture(culture, activeReport.DangerReportId, activeReport.DangerReport.CoordinatesInformation),
                    UserId = activeReport.DangerReport.UserId,
                })
                .ToListAsync();

            if (culture.ToLower().Equals("en-us"))
            {
                return Ok(paginatedReports);
            }

            foreach (var report in paginatedReports)
            {
                report.DisasterType = DisasterConverter.TranslateDisaster(
                    Enum.Parse<DisasterType>(report.DisasterType), culture);

                report.Status = StatusConverter.TranslateStatus(
                    Enum.Parse<ReportStatus>(report.Status), culture);
            }

            return Ok(paginatedReports);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while fetching paginated " +
                                 "danger reports sorted by time descending");
            return BadRequest();
        }
    }

    [Authorize(Roles = "Civil_Protection")]
    [HttpGet("GetDisastersByMunicipalityAndImportance")]
    public async Task<ActionResult> GetDisastersByMunicipalityAndImportance(int pageNumber, int itemsPerPage, string culture)
    {
        try
        {
            var reportsQuery = _dbContext.CoordinatesInformation
                .AsNoTracking()
                .Where(ci => ci.DangerReport.ActiveDangerReport.DangerReportId.Equals(ci.DangerReportId) &&
                             ci.Culture.Equals(culture))
                .GroupBy(ci => new
                {
                    ci.Country,
                    ci.Municipality,
                    ci.DangerReport.DisasterType
                });

            var count = await reportsQuery.CountAsync();
            var totalNumberOfPages = (int)Math.Ceiling((double)count / itemsPerPage);

            var paginatedReports = await reportsQuery
                .Select(group => new 
                { 
                    DisasterType = culture.ToLower().Equals("en-us") 
                        ? group.Key.DisasterType.ToString()
                        : DisasterConverter.TranslateDisaster(group.Key.DisasterType, culture), 
                    DisasterTypeIndex = (int)group.Key.DisasterType,
                    Country = group.Key.Country, 
                    Municipality = group.Key.Municipality, 
                    Importance = group.Count()

                })
                .OrderByDescending(group => group.Importance)
                .Paginate(pageNumber, itemsPerPage)
                .ToListAsync();

            return Ok(new {TotalPages = totalNumberOfPages, DangerReports = paginatedReports});
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while trying to fetch reports by importance");
            return BadRequest();
        }
    }

    [Authorize(Roles = "Civil_Protection")]
    [HttpGet("GetActiveReportsByDisasterAndMunicipality")]
    public async Task<ActionResult> GetActiveReportsByDisasterAndMunicipality(int disasterIndex, string municipality, string culture)
    {
        try
        {
            string rootPath = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
            var imagesPath = $@"{rootPath}/UploadDangerReportImages";

            var reports = await _dbContext.ActiveDangerReports
                .AsNoTracking()
                .Where(activeReport => 
                    (int)activeReport.DangerReport.DisasterType == disasterIndex &&
                    activeReport.DangerReport.CoordinatesInformation.Any(ci => ci.Municipality.Equals(municipality)))
                .Select(activeReport => new CivilProtectionDangerReportDTO
                {
                    Id = activeReport.DangerReportId,
                    DisasterType = culture.ToLower().Equals("en-us")
                        ? activeReport.DangerReport.DisasterType.ToString()
                        : DisasterConverter.TranslateDisaster(activeReport.DangerReport.DisasterType, culture), 
                    Longitude = activeReport.DangerReport.Location.X,
                    Latitude = activeReport.DangerReport.Location.Y,
                    CreatedAt = activeReport.DangerReport.CreatedAt,
                    ImageUrl = activeReport.DangerReport.ImageName != null ? 
                        $@"{imagesPath}/{activeReport.DangerReport.ImageName}" : 
                        null,
                    Description = activeReport.DangerReport.Description,
                })
                .ToListAsync();

            return Ok(reports);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while trying to fetch reports by disaster and municipality");
            return BadRequest();
        }
    }

    [Authorize(Roles = "Civil_Protection")]
    [HttpGet("GetRejectedDangerReportsByTimeDescending")]
    public async Task<ActionResult> GetRejectedDangerReportsByTimeDescending(int pageNumber, int itemsPerPage, string culture)
    {
        try
        {
            var reportsQuery = _dbContext.ArchivedDangerReports
                .AsNoTracking()
                .Where(adr => adr.DangerReport.Status == ReportStatus.Rejected)
                .OrderByDescending(activeReport => activeReport.DangerReport.CreatedAt);

            var count = await reportsQuery.CountAsync();
            var totalNumberOfPages = (int)Math.Ceiling((double)count / itemsPerPage);

            var paginatedReports = await reportsQuery
                .AsNoTracking()
                .Paginate(pageNumber, itemsPerPage)
                .Select(activeReport => new ArchivedDangerReportDTO()
                {
                    Id = activeReport.DangerReportId,
                    DisasterType = activeReport.DangerReport.DisasterType.ToString(),
                    Longitude = activeReport.DangerReport.Location.X,
                    Latitude = activeReport.DangerReport.Location.Y,
                    CreatedAt = activeReport.DangerReport.CreatedAt,
                    Country = activeReport.DangerReport.CoordinatesInformation
                        .GetCountryByCulture(culture, activeReport.DangerReportId, activeReport.DangerReport.CoordinatesInformation),
                    Municipality = activeReport.DangerReport.CoordinatesInformation
                        .GetMunicipalityByCulture(culture, activeReport.DangerReportId, activeReport.DangerReport.CoordinatesInformation),
                })
                .ToListAsync();

            if (culture.ToLower().Equals("en-us"))
            {
                return Ok(new {TotalPages = totalNumberOfPages, DangerReports = paginatedReports});
            }

            foreach (var report in paginatedReports)
            {
                report.DisasterType = DisasterConverter.TranslateDisaster(
                    Enum.Parse<DisasterType>(report.DisasterType), culture);
            }

            return Ok(new {TotalPages = totalNumberOfPages, DangerReports = paginatedReports});
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while fetching paginated " +
                                 "rejected archived danger reports sorted by time descending");
            return BadRequest();
        }
    }

    [Authorize(Roles = "Civil_Protection")]
    [HttpGet("GetApprovedDangerReportsByTimeDescending")]
    public async Task<ActionResult> GetApprovedDangerReportsByTimeDescending(int pageNumber, int itemsPerPage, string culture)
    {
        try
        {
            var reportsQuery = _dbContext.ArchivedDangerReports
                .AsNoTracking()
                .Where(adr => adr.DangerReport.Status == ReportStatus.Approved)
                .OrderByDescending(activeReport => activeReport.DangerReport.CreatedAt);

            var count = await reportsQuery.CountAsync();
            var totalNumberOfPages = (int)Math.Ceiling((double)count / itemsPerPage);

            var paginatedReports = await reportsQuery
                .AsNoTracking()
                .Paginate(pageNumber, itemsPerPage)
                .Select(activeReport => new ArchivedDangerReportDTO()
                {
                    Id = activeReport.DangerReportId,
                    DisasterType = activeReport.DangerReport.DisasterType.ToString(),
                    Longitude = activeReport.DangerReport.Location.X,
                    Latitude = activeReport.DangerReport.Location.Y,
                    CreatedAt = activeReport.DangerReport.CreatedAt,
                    Country = activeReport.DangerReport.CoordinatesInformation
                        .GetCountryByCulture(culture, activeReport.DangerReportId, activeReport.DangerReport.CoordinatesInformation),
                    Municipality = activeReport.DangerReport.CoordinatesInformation
                        .GetMunicipalityByCulture(culture, activeReport.DangerReportId, activeReport.DangerReport.CoordinatesInformation),
                })
                .ToListAsync();

            if (culture.ToLower().Equals("en-us"))
            {
                return Ok(new {TotalPages = totalNumberOfPages, DangerReports = paginatedReports});
            }

            foreach (var report in paginatedReports)
            {
                report.DisasterType = DisasterConverter.TranslateDisaster(
                    Enum.Parse<DisasterType>(report.DisasterType), culture);
            }

            return Ok(new {TotalPages = totalNumberOfPages, DangerReports = paginatedReports});
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while fetching paginated " +
                                 "approved archived danger reports sorted by time descending");
            return BadRequest();
        }
    }

    [Authorize(Roles = "Civil_Protection")]
    [HttpPost("RejectDangerReportsByDisasterAndMunicipality")]
    public async Task<IActionResult> RejectDangerReportsByDisasterAndMunicipality(int disasterIndex, string municipality, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var activeReports = await _dbContext.ActiveDangerReports
                .Where(activeReport =>
                    (int)activeReport.DangerReport.DisasterType == disasterIndex &&
                    activeReport.DangerReport.CoordinatesInformation.Any(ci => ci.Municipality.Equals(municipality)))
                .Include(adr => adr.DangerReport)
                .ToListAsync(cancellationToken);

            foreach (var activeReport in activeReports)
            {
                activeReport.DangerReport.Status = ReportStatus.Rejected;
                await _dbContext.ArchivedDangerReports.AddAsync(new ArchivedDangerReport
                {
                    DangerReportId = activeReport.DangerReportId,
                }, cancellationToken);
                _dbContext.ActiveDangerReports.Remove(activeReport);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _dbContext.Database.CommitTransactionAsync(cancellationToken);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while trying to reject reports of {disaster} in municipality {municipality}",
                Enum.GetName(typeof(DisasterType), disasterIndex), municipality);
            await _dbContext.Database.RollbackTransactionAsync(cancellationToken);
            return BadRequest();
        }
    }

    [Authorize(Roles = "Civil_Protection")]
    [HttpPost("ApproveDangerReportsByDisasterAndMunicipality")]
    public async Task<IActionResult> ApproveDangerReportsByDisasterAndMunicipality(int disasterIndex, string municipality, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var activeReports = await _dbContext.ActiveDangerReports
                .Where(activeReport =>
                    (int)activeReport.DangerReport.DisasterType == disasterIndex &&
                    activeReport.DangerReport.CoordinatesInformation.Any(ci => ci.Municipality.Equals(municipality)))
                .Include(adr => adr.DangerReport)
                .ToListAsync(cancellationToken);

            foreach (var activeReport in activeReports)
            {
                activeReport.DangerReport.Status = ReportStatus.Approved;
                await _dbContext.ArchivedDangerReports.AddAsync(new ArchivedDangerReport
                {
                    DangerReportId = activeReport.DangerReportId,
                }, cancellationToken);
                _dbContext.ActiveDangerReports.Remove(activeReport);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _dbContext.Database.CommitTransactionAsync(cancellationToken);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while trying to reject reports of {disaster} in municipality {municipality}",
                Enum.GetName(typeof(DisasterType), disasterIndex), municipality);
            await _dbContext.Database.RollbackTransactionAsync(CancellationToken.None);
            return BadRequest();
        }
    }
}
