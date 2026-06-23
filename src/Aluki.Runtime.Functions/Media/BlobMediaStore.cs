using Aluki.Runtime.Capture.Media;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace Aluki.Runtime.Functions.Media;

/// <summary>
/// Stores media binaries in Azure Blob Storage. Uses the <c>Blob:ConnectionString</c>
/// configuration value (Key Vault-backed app setting <c>Blob__ConnectionString</c>).
/// </summary>
public sealed class BlobMediaStore : IMediaBlobStore
{
    private const string ContainerName = "whatsapp-media";
    private readonly string _connectionString;

    public BlobMediaStore(IConfiguration configuration)
    {
        _connectionString = configuration["Blob:ConnectionString"]
            ?? configuration["BlobConnectionString"]
            ?? throw new InvalidOperationException("Blob connection string is not configured (Blob:ConnectionString).");
    }

    public async Task<string> UploadAsync(
        string blobPath,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var container = new BlobContainerClient(_connectionString, ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(blobPath);
        using var stream = new MemoryStream(content);
        await blob.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);

        return blob.Uri.ToString();
    }
}
