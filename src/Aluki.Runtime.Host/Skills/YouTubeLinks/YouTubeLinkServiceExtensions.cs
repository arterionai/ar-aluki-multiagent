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

        services.AddHttpClient("youtube-data-api")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(4));
        services.AddHttpClient("youtube-oembed")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(4));

        services.AddSingleton<IPrimaryYouTubeMetadataProvider,
            YouTubeDataApiMetadataProvider>();
        services.AddSingleton<ISecondaryYouTubeMetadataProvider,
            OEmbedYouTubeMetadataProvider>();
        services.AddSingleton<IYouTubeClassificationProvider,
            FoundryYouTubeClassificationProvider>();

        services.AddScoped<YouTubeLinkCaptureService>();

        return services;
    }
}
