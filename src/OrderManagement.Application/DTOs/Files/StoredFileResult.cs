namespace OrderManagement.Application.DTOs.Files;

public sealed record StoredFileResult
{
    public required string PublicUrl { get; init; }

    public required string StoredFileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }
}