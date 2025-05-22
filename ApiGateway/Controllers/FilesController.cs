using System.Text;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc;
using Shared.Protos;
using Microsoft.Extensions.Options;
using ApiGateway.Models;
using System.Text.Json;
using System.Net.Http.Json;
using ApiGateway.Extensions;

namespace ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly GrpcChannel _fileStoringChannel;
    private readonly GrpcChannel _fileAnalysisChannel;
    private readonly ILogger<FilesController> _logger;
    private readonly HttpClient _httpClient;
    private readonly ServiceUrls _serviceUrls;

    public FilesController(IConfiguration configuration, ILogger<FilesController> logger, HttpClient httpClient, IOptions<ServiceUrls> serviceUrls)
    {
        var fileStoringUrl = configuration["FileStoringService:Url"] ?? "https://localhost:7001";
        var fileAnalysisUrl = configuration["FileAnalysisService:Url"] ?? "https://localhost:7002";
        _fileStoringChannel = GrpcChannel.ForAddress(fileStoringUrl);
        _fileAnalysisChannel = GrpcChannel.ForAddress(fileAnalysisUrl);
        _logger = logger;
        _httpClient = httpClient;
        _serviceUrls = serviceUrls.Value;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            _logger.LogInformation($"Attempting to upload file {file.FileName}");
            
            var client = new FileStoring.FileStoringClient(_fileStoringChannel);
            var request = new UploadFileRequest
            {
                FileName = file.FileName,
                Content = Google.Protobuf.ByteString.CopyFrom(await file.GetBytesAsync())
            };
            
            var response = await client.UploadFileAsync(request);
            
            _logger.LogInformation($"File uploaded successfully with ID: {response.FileId}");
            return Ok(new { FileId = response.FileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file");
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("analysis/{fileId}")]
    public async Task<IActionResult> GetAnalysis(string fileId)
    {
        try
        {
            _logger.LogInformation($"Attempting to get analysis for file {fileId}");
            
            var client = new FileAnalysis.FileAnalysisClient(_fileAnalysisChannel);
            var response = await client.AnalyzeFileAsync(new AnalyzeFileRequest { FileId = fileId });

            _logger.LogInformation($"Successfully got analysis for file {fileId}");
            return Ok(new
            {
                FileId = response.FileId,
                FileName = response.FileName,
                FileHash = response.FileHash,
                WordCount = response.WordCount,
                CharacterCount = response.CharacterCount,
                FrequentWords = response.FrequentWords.Select(w => new
                {
                    Word = w.Word,
                    Count = w.Count
                }),
                SimilarFiles = response.SimilarFiles.Select(f => new
                {
                    FileId = f.FileId,
                    SimilarityPercentage = f.SimilarityPercentage
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analysis for file {FileId}", fileId);
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("{fileId}")]
    public async Task<IActionResult> GetFile(string fileId)
    {
        try
        {
            var client = new FileStoring.FileStoringClient(_fileStoringChannel);
            var response = await client.GetFileAsync(new GetFileRequest { FileId = fileId });

            return File(response.Content.ToByteArray(), "text/plain", response.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file {FileId}", fileId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{fileId}/wordcloud")]
    public async Task<IActionResult> GetWordCloud(string fileId)
    {
        try
        {
            var client = new FileAnalysis.FileAnalysisClient(_fileAnalysisChannel);
            var response = await client.GetWordCloudAsync(new GetWordCloudRequest { FileId = fileId });

            return File(response.WordCloudImage.ToByteArray(), "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting word cloud for file {FileId}", fileId);
            return StatusCode(500, "Internal server error");
        }
    }
} 