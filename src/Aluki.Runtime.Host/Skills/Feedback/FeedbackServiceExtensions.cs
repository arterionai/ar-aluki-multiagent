using Aluki.Runtime.Abstractions.Skills.Feedback;
using Aluki.Runtime.Capture.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aluki.Runtime.Host.Skills.Feedback;

public static class FeedbackServiceExtensions
{
    public static IServiceCollection AddFeedbackCapture(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<NpgsqlConnectionFactory>();
        services.AddScoped<FeedbackRepository>();
        services.AddScoped<IFeedbackRepository>(sp => sp.GetRequiredService<FeedbackRepository>());
        services.AddScoped<FeedbackCaptureService>();
        return services;
    }
}
