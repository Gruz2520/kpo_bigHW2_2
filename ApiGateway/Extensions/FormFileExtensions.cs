using Microsoft.AspNetCore.Http;

namespace ApiGateway.Extensions;

public static class FormFileExtensions
{
    public static async Task<byte[]> GetBytesAsync(this IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }
} 