using System.Text;
using Grpc.Core;
using Shared.Protos;
using Grpc.Net.Client;
using System.Net.Http.Json;

namespace FileAnalysisService.Services;

public class FileAnalysisService : FileAnalysis.FileAnalysisBase
{
    private readonly GrpcChannel _fileStoringChannel;
    private readonly ILogger<FileAnalysisService> _logger;
    private readonly string _wordCloudApiUrl;

    public FileAnalysisService(IConfiguration configuration, ILogger<FileAnalysisService> logger)
    {
        var fileStoringUrl = configuration["FileStoringService:Url"] ?? "https://localhost:7001";
        _fileStoringChannel = GrpcChannel.ForAddress(fileStoringUrl);
        _wordCloudApiUrl = configuration["WordCloudApi:Url"] ?? "https://quickchart.io/wordcloud";
        _logger = logger;
    }

    public override async Task<AnalyzeFileResponse> AnalyzeFile(AnalyzeFileRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"Starting analysis for file {request.FileId}");

            var client = new FileStoring.FileStoringClient(_fileStoringChannel);
            var fileResponse = await client.GetFileAsync(new GetFileRequest { FileId = request.FileId });

            var content = fileResponse.Content.ToByteArray();
            var text = Encoding.UTF8.GetString(content);

            var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var characterCount = text.Length;

            // Получаем все файлы для сравнения
            var allFilesResponse = await client.GetAllFilesAsync(new GetAllFilesRequest());
            var similarFiles = new List<string>();

            // Получаем шинглы текущего файла
            var currentShingles = GetShingles(text);

            // Сравниваем с каждым файлом
            foreach (var file in allFilesResponse.Files)
            {
                if (file.FileId == request.FileId) continue; // Пропускаем текущий файл

                var otherFileResponse = await client.GetFileAsync(new GetFileRequest { FileId = file.FileId });
                var otherText = Encoding.UTF8.GetString(otherFileResponse.Content.ToByteArray());
                var otherShingles = GetShingles(otherText);

                var similarity = CalculateJaccardSimilarity(currentShingles, otherShingles);
                if (similarity > 0.3) // Порог схожести 30%
                {
                    similarFiles.Add(file.FileId);
                }
            }

            _logger.LogInformation($"Analysis completed for file {request.FileId}");
            return new AnalyzeFileResponse
            {
                FileId = request.FileId,
                WordCount = wordCount,
                CharacterCount = characterCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file {FileId}", request.FileId);
            throw new RpcException(new Status(StatusCode.Internal, $"Error analyzing file: {ex.Message}"));
        }
    }

    public override async Task<GetWordCloudResponse> GetWordCloud(GetWordCloudRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"Generating word cloud for file {request.FileId}");

            var client = new FileStoring.FileStoringClient(_fileStoringChannel);
            var fileResponse = await client.GetFileAsync(new GetFileRequest { FileId = request.FileId });

            var content = fileResponse.Content.ToByteArray();
            var text = Encoding.UTF8.GetString(content);

            var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .GroupBy(w => w.ToLower())
                .Select(g => new { Word = g.Key, Count = g.Count() })
                .OrderByDescending(w => w.Count)
                .Take(100)
                .ToDictionary(w => w.Word, w => w.Count);

            var wordCloudData = new
            {
                text = words,
                width = 800,
                height = 400,
                colors = new[] { "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728", "#9467bd" }
            };

            using var httpClient = new HttpClient();
            var response = await httpClient.PostAsJsonAsync(_wordCloudApiUrl, wordCloudData);
            response.EnsureSuccessStatusCode();

            var imageBytes = await response.Content.ReadAsByteArrayAsync();

            _logger.LogInformation($"Word cloud generated successfully for file {request.FileId}");
            return new GetWordCloudResponse
            {
                FileId = request.FileId,
                WordCloudImage = Google.Protobuf.ByteString.CopyFrom(imageBytes)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating word cloud for file {FileId}", request.FileId);
            throw new RpcException(new Status(StatusCode.Internal, $"Error generating word cloud: {ex.Message}"));
        }
    }

    private HashSet<string> GetShingles(string text)
    {
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLower())
            .Where(w => w.Length > 3) // Игнорируем короткие слова
            .ToList();

        var shingles = new HashSet<string>();
        for (int i = 0; i < words.Count - 2; i++)
        {
            shingles.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
        }

        return shingles;
    }

    private double CalculateJaccardSimilarity(HashSet<string> set1, HashSet<string> set2)
    {
        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }
} 