using System.Text;
using Grpc.Core;
using Shared.Protos;
using Grpc.Net.Client;
using System.Net.Http.Json;
using System.Linq;
using System.Security.Cryptography;

namespace FileAnalysisService.Services;

public class FileAnalysisGrpcService : FileAnalysis.FileAnalysisBase
{
    private readonly GrpcChannel _fileStoringChannel;
    private readonly ILogger<FileAnalysisGrpcService> _logger;
    private readonly string _wordCloudApiUrl;

    public FileAnalysisGrpcService(IConfiguration configuration, ILogger<FileAnalysisGrpcService> logger)
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
            _logger.LogInformation($"Current file content: {text}");

            // Подсчитываем частоту слов
            var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.ToLower())
                .Where(w => w.Length > 2)
                .GroupBy(w => w)
                .Select(g => new WordFrequency
                {
                    Word = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(w => w.Count)
                .Take(10)
                .ToList();

            var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var characterCount = text.Length;

            // Получаем все файлы для сравнения
            var allFilesResponse = await client.GetAllFilesAsync(new GetAllFilesRequest());
            _logger.LogInformation($"Found {allFilesResponse.Files.Count} files in total");
            
            var similarFiles = new List<SimilarFile>();

            // Сравниваем с каждым файлом
            foreach (var file in allFilesResponse.Files)
            {
                if (file.FileId == request.FileId)
                {
                    _logger.LogInformation($"Skipping current file {file.FileId}");
                    continue;
                }

                _logger.LogInformation($"Comparing with file {file.FileId} ({file.FileName})");
                
                var otherFileResponse = await client.GetFileAsync(new GetFileRequest { FileId = file.FileId });
                var otherText = Encoding.UTF8.GetString(otherFileResponse.Content.ToByteArray());
                _logger.LogInformation($"Other file content: {otherText}");

                // Простое сравнение: считаем количество общих слов
                var currentWords = text.ToLower().Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
                var otherWords = otherText.ToLower().Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

                var commonWords = currentWords.Intersect(otherWords).Count();
                var totalWords = currentWords.Union(otherWords).Count();

                var similarity = totalWords > 0 ? (double)commonWords / totalWords : 0;
                _logger.LogInformation($"Similarity with file {file.FileId}: {similarity:P2}");

                if (similarity > 0.1) // Порог схожести 10%
                {
                    _logger.LogInformation($"Found similar file: {file.FileId} with similarity {similarity:P2}");
                    similarFiles.Add(new SimilarFile
                    {
                        FileId = file.FileId,
                        FileName = file.FileName,
                        SimilarityPercentage = Math.Round(similarity * 100, 2)
                    });
                }
            }

            // Сортируем по убыванию схожести
            similarFiles = similarFiles.OrderByDescending(f => f.SimilarityPercentage).ToList();
            _logger.LogInformation($"Found {similarFiles.Count} similar files");

            return new AnalyzeFileResponse
            {
                FileId = request.FileId,
                FileName = fileResponse.FileName,
                FileHash = fileResponse.FileHash,
                WordCount = wordCount,
                CharacterCount = characterCount,
                FrequentWords = { words },
                SimilarFiles = { similarFiles }
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
        // Предварительная обработка текста
        var processedText = text.ToLower()
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Replace(".", " ")
            .Replace(",", " ")
            .Replace("!", " ")
            .Replace("?", " ")
            .Replace(";", " ")
            .Replace(":", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace("[", " ")
            .Replace("]", " ")
            .Replace("{", " ")
            .Replace("}", " ")
            .Replace("\"", " ")
            .Replace("'", " ")
            .Replace("  ", " "); // Удаляем двойные пробелы

        // Разбиваем на слова и фильтруем
        var words = processedText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2) // Минимальная длина слова
            .Where(w => !IsStopWord(w)) // Удаляем стоп-слова
            .ToList();

        var shingles = new HashSet<string>();
        
        // Используем 2-граммы, 3-граммы и 4-граммы для лучшего сравнения
        for (int i = 0; i < words.Count - 1; i++)
        {
            shingles.Add($"{words[i]} {words[i + 1]}"); // 2-граммы
        }
        
        for (int i = 0; i < words.Count - 2; i++)
        {
            shingles.Add($"{words[i]} {words[i + 1]} {words[i + 2]}"); // 3-граммы
        }

        for (int i = 0; i < words.Count - 3; i++)
        {
            shingles.Add($"{words[i]} {words[i + 1]} {words[i + 2]} {words[i + 3]}"); // 4-граммы
        }

        return shingles;
    }

    private bool IsStopWord(string word)
    {
        // Список стоп-слов
        var stopWords = new HashSet<string>
        {
            "the", "be", "to", "of", "and", "a", "in", "that", "have", "i",
            "it", "for", "not", "on", "with", "he", "as", "you", "do", "at",
            "this", "but", "his", "by", "from", "they", "we", "say", "her", "she",
            "or", "an", "will", "my", "one", "all", "would", "there", "their", "what",
            "so", "up", "out", "if", "about", "who", "get", "which", "go", "me",
            "when", "make", "can", "like", "time", "no", "just", "him", "know", "take",
            "people", "into", "year", "your", "good", "some", "could", "them", "see", "other",
            "than", "then", "now", "look", "only", "come", "its", "over", "think", "also",
            "back", "after", "use", "two", "how", "our", "work", "first", "well", "way",
            "even", "new", "want", "because", "any", "these", "give", "day", "most", "us",
            "и", "в", "во", "не", "что", "он", "на", "я", "с", "со", "как", "а", "то", "все", "она",
            "так", "его", "но", "да", "ты", "к", "у", "же", "вы", "за", "бы", "по", "только", "ее",
            "мне", "было", "вот", "от", "меня", "еще", "нет", "о", "из", "ему", "теперь", "когда",
            "даже", "ну", "вдруг", "ли", "если", "уже", "или", "ни", "быть", "был", "него", "до",
            "вас", "нибудь", "опять", "уж", "вам", "ведь", "там", "потом", "себя", "ничего", "ей",
            "может", "они", "тут", "где", "есть", "надо", "ней", "для", "мы", "тебя", "их", "чем",
            "была", "сам", "чтоб", "без", "будто", "чего", "раз", "тоже", "себе", "под", "будет",
            "ж", "тогда", "кто", "этот", "того", "потому", "этого", "какой", "совсем", "ним", "здесь",
            "этом", "один", "почти", "мой", "тем", "чтобы", "нее", "сейчас", "были", "куда", "зачем",
            "всех", "никогда", "можно", "при", "наконец", "два", "об", "другой", "хоть", "после",
            "над", "больше", "тот", "через", "эти", "нас", "про", "всего", "них", "какая", "много",
            "разве", "три", "эту", "моя", "впрочем", "хорошо", "свою", "этой", "перед", "иногда",
            "лучше", "чуть", "том", "нельзя", "такой", "им", "более", "всегда", "конечно", "всю"
        };

        return stopWords.Contains(word.ToLower());
    }

    private double CalculateJaccardSimilarity(HashSet<string> set1, HashSet<string> set2)
    {
        if (set1.Count == 0 && set2.Count == 0) return 1.0; // Оба множества пустые
        if (set1.Count == 0 || set2.Count == 0) return 0.0; // Одно из множеств пустое

        var intersection = set1.Intersect(set2).Count();
        var union = set1.Union(set2).Count();
        
        return (double)intersection / union;
    }
} 