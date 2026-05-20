using Microsoft.Extensions.Logging.Abstractions;
using Tracer.Adapters.Mock.Upload;
using Tracer.Agent.Configuration;
using Tracer.Core.Abstractions;

namespace Tracer.Agent.Upload;

public static class UploadServiceFactory
{
    public static ITelemetryUploadService Create(AgentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.UploadService.Kind switch
        {
            "LocalFileSystem" => new LocalFileSystemUploadService(
                !string.IsNullOrEmpty(config.UploadService.LocalFileSystemRoot)
                    ? config.UploadService.LocalFileSystemRoot
                    : Path.Combine(config.DataRoot, "_upload_staging")),
            _ => throw new InvalidOperationException(
                $"Unknown upload service kind: '{config.UploadService.Kind}'.")
        };
    }
}
