using System.Security.Cryptography;
using FileStoringService.Models;
using Shared.Protos;
using Microsoft.EntityFrameworkCore;
using Grpc.Core;
using FileInfo = Shared.Protos.FileInfo;

namespace FileStoringService.Services;

public class FileStorageService : FileStoring.FileStoringBase
{
    private readonly ILogger<FileStorageService> _logger;
    private readonly FileDbContext _context;
    private readonly string _storagePath;

    public FileStorageService(ILogger<FileStorageService> logger, FileDbContext context, IConfiguration configuration)
    {
        _logger = logger;
        _context = context;
        _storagePath = configuration["FileStorage:Path"] ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files");
        Directory.CreateDirectory(_storagePath);
    }

    public override async Task<UploadFileResponse> UploadFile(UploadFileRequest request, ServerCallContext context)
    {
        try
        {
            _logger.LogInformation($"Starting file upload for {request.FileName}, content length: {request.Content.Length} bytes");

            if (string.IsNullOrEmpty(request.FileName))
            {
                _logger.LogError("File name is null or empty");
                throw new ArgumentException("File name cannot be null or empty");
            }

            if (request.Content == null || request.Content.Length == 0)
            {
                _logger.LogError("File content is null or empty");
                throw new ArgumentException("File content cannot be null or empty");
            }

            var content = request.Content.ToByteArray();
            var fileHash = ComputeHash(content);
            
            // Проверяем, существует ли файл с таким хешем
            var existingFile = await _context.Files.FirstOrDefaultAsync(f => f.FileHash == fileHash);
            if (existingFile != null)
            {
                _logger.LogInformation($"File with hash {fileHash} already exists");
                return new UploadFileResponse
                {
                    FileId = existingFile.Id.ToString(),
                    FileName = existingFile.FileName,
                    Location = existingFile.FilePath
                };
            }

            // Генерируем уникальное имя файла
            var fileId = Guid.NewGuid().ToString();
            var filePath = Path.Combine(_storagePath, fileId);
            
            // Сохраняем файл в файловой системе
            await File.WriteAllBytesAsync(filePath, content);

            var file = new FileEntity
            {
                FileName = request.FileName,
                Content = content,
                UploadDate = DateTime.UtcNow,
                FileHash = fileHash,
                FilePath = filePath
            };

            _logger.LogInformation("Saving file to database...");
            _context.Files.Add(file);
            await _context.SaveChangesAsync();
            _logger.LogInformation($"File saved successfully with ID: {file.Id}");

            return new UploadFileResponse
            {
                FileId = file.Id.ToString(),
                FileName = file.FileName,
                Location = file.FilePath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}. Error details: {ErrorMessage}", request.FileName, ex.ToString());
            throw new RpcException(new Status(StatusCode.Internal, $"Error uploading file: {ex.Message}"));
        }
    }

    public override async Task<GetFileResponse> GetFile(GetFileRequest request, ServerCallContext context)
    {
        try
        {
            var file = await _context.Files.FindAsync(int.Parse(request.FileId));
            if (file == null)
            {
                throw new FileNotFoundException($"File with ID {request.FileId} not found");
            }

            // Проверяем, существует ли файл в файловой системе
            if (!File.Exists(file.FilePath))
            {
                throw new FileNotFoundException($"File not found at path: {file.FilePath}");
            }

            return new GetFileResponse
            {
                FileId = file.Id.ToString(),
                FileName = file.FileName,
                Content = Google.Protobuf.ByteString.CopyFrom(file.Content),
                FileHash = file.FileHash
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file {FileId}", request.FileId);
            throw;
        }
    }

    public override async Task<GetAllFilesResponse> GetAllFiles(GetAllFilesRequest request, ServerCallContext context)
    {
        try
        {
            var files = await _context.Files
                .Select(f => new Shared.Protos.FileInfo
                {
                    FileId = f.Id.ToString(),
                    FileName = f.FileName,
                    UploadDate = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(f.UploadDate.ToUniversalTime())
                })
                .ToListAsync();

            var response = new GetAllFilesResponse();
            response.Files.AddRange(files);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all files");
            throw;
        }
    }

    private string ComputeHash(byte[] content)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(content);
        return Convert.ToBase64String(hashBytes);
    }
} 