using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orchestra.Infrastructure;
using Orchestra.Worker;
using Orchestra.Worker.Handlers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOrchestraInfrastructure(builder.Configuration);
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));

var workerOptions = builder.Configuration.GetSection("Worker").Get<WorkerOptions>() ?? new WorkerOptions();
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(workerOptions.ShutdownGraceSeconds + 10));

// Register your own handlers here.
builder.Services.AddSingleton<ITaskHandler, EchoHandler>();
builder.Services.AddSingleton<ITaskHandler, SleepHandler>();
builder.Services.AddSingleton<ITaskHandler, FlakyHandler>();

builder.Services.AddSingleton<HandlerRegistry>();
builder.Services.AddSingleton<TaskExecutor>();
builder.Services.AddHostedService<WorkerService>();

await builder.Build().RunAsync();
