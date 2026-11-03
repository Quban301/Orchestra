using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orchestra.Infrastructure.Scheduling;
using Orchestra.Infrastructure.Tasks;

namespace Orchestra.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrchestraInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Orchestra")
                 ?? throw new InvalidOperationException("ConnectionStrings:Orchestra is not configured.");

        services.AddDbContext<OrchestraDbContext>(o => o.UseNpgsql(cs));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITaskStore, EfTaskStore>();
        return services;
    }

    /// <summary>Runs the coordinator loop in this process (enable on API/coordinator nodes only).</summary>
    public static IServiceCollection AddOrchestraScheduler(this IServiceCollection services)
        => services.AddHostedService<SchedulerLoop>();
}
