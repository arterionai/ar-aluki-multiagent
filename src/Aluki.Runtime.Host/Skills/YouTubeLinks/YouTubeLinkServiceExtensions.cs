using Aluki.Runtime.Abstractions.Skills.YouTubeLinks;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Host.Skills.YouTubeLinks;

public static class YouTubeLinkServiceExtensions
{
    public static IServiceCollection AddYouTubeLinkCapture(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();

        services.AddScoped<YouTubeLinkRepository>();
        services.AddScoped<IYouTubeLinkRepository>(
            sp => sp.GetRequiredService<YouTubeLinkRepository>());

        services.AddSingleton<IPrimaryYouTubeMetadataProvider,
            LoggingPrimaryYouTubeMetadataProvider>();
        services.AddSingleton<ISecondaryYouTubeMetadataProvider,
            LoggingSecondaryYouTubeMetadataProvider>();
        services.AddSingleton<IYouTubeClassificationProvider,
            StubYouTubeClassificationProvider>();

        services.AddScoped<YouTubeLinkCaptureService>();

        return services;
    }
}
