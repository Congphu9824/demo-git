using System.Diagnostics;
using System.Text.Json;
using ECommerce.Api.Middleware;
using ECommerce.Application.Common;
using ECommerce.Application.Diagnostics;
using ECommerce.Infrastructure;
using ECommerce.Infrastructure.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

// Configure bootstrap Serilog logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ServiceName", TelemetryConstants.ServiceName)
    .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting ECommerce.Api host initialization...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        var lokiUrl = context.Configuration["Loki:Url"] ?? "http://localhost:3100";

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service_name", TelemetryConstants.ServiceName)
            .Enrich.WithProperty("deployment_environment", context.HostingEnvironment.EnvironmentName)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.GrafanaLoki(
                lokiUrl,
                new[]
                {
                    new LokiLabel { Key = "service_name", Value = TelemetryConstants.ServiceName },
                    new LokiLabel { Key = "deployment_environment", Value = context.HostingEnvironment.EnvironmentName }
                });
    });

    // Add controllers and JSON formatting
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "ECommerce Observability Demo API",
            Version = "v1",
            Description = "Backend Web API for ECommerce Observability Demo with OpenTelemetry, Prometheus, Loki, and Tempo"
        });
    });

    // Add CORS for frontend integration
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    // Add Infrastructure and Application services
    builder.Services.AddInfrastructureServices(builder.Configuration);

    // Add Health Checks
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("API is running"), tags: new[] { "live" })
        .AddDbContextCheck<AppDbContext>("database", tags: new[] { "ready" });

    // Configure OpenTelemetry Resource
    var resourceBuilder = ResourceBuilder.CreateDefault()
        .AddService(serviceName: TelemetryConstants.ServiceName, serviceVersion: TelemetryConstants.ServiceVersion)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = builder.Environment.EnvironmentName,
            ["host.name"] = Environment.MachineName
        });

    var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

    // Configure OpenTelemetry Tracing and Metrics
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resourceBuilder)
                .AddSource(TelemetryConstants.ActivitySourceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.Filter = httpContext =>
                    {
                        // Exclude health check and metrics polling from trace noise if desired
                        var path = httpContext.Request.Path.Value ?? string.Empty;
                        return !path.StartsWith("/metrics") && !path.StartsWith("/health");
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                })
                .AddEntityFrameworkCoreInstrumentation(options =>
                {
                    options.SetDbStatementForText = true;
                });

            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                tracing.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                });
            }
        })
        .WithMetrics(metrics =>
        {
            metrics
                .SetResourceBuilder(resourceBuilder)
                .AddMeter(TelemetryConstants.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter();

            if (!string.IsNullOrEmpty(otlpEndpoint))
            {
                metrics.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                });
            }
        });

    // Serilog pushes structured logs directly to Loki via Serilog.Sinks.Grafana.Loki configured above
    var app = builder.Build();

    // Global custom Exception Handling
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Enable Swagger
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ECommerce API v1");
        c.RoutePrefix = "swagger";
    });

    app.UseCors("AllowAll");

    // OpenTelemetry Prometheus Scrape endpoint
    app.MapPrometheusScrapingEndpoint();

    // Map Health Checks
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var result = JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.TotalMilliseconds
                }),
                totalDuration = report.TotalDuration.TotalMilliseconds
            });
            await context.Response.WriteAsync(result);
        }
    });

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live"),
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = report.Status.ToString() }));
        }
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
            }));
        }
    });

    app.MapControllers();
 
    // Development-only Slow API endpoint for Observability & Root Cause analysis demo
    if (app.Environment.IsDevelopment())
    {
        app.MapGet("/api/debug/slow", async (int? delayMs, ILogger<Program> logger) =>
        {
            var delay = delayMs ?? 2000;
            logger.LogWarning("Debug slow API triggered with artificial delay: {DelayMs}ms", delay);
            using var activity = ObservabilityActivitySource.StartActivity("DebugSlowOperation");
            activity?.SetTag("debug.delay_ms", delay);
            activity?.SetTag("debug.simulated_reason", "Artificial latency injection for Observability Demo");
            await Task.Delay(delay);
            return Results.Ok(new
            {
                status = "Success",
                message = $"Simulated slow response completed after {delay}ms",
                delayMs = delay,
                traceId = Activity.Current?.TraceId.ToString(),
                spanId = Activity.Current?.SpanId.ToString()
            });
        }).WithTags("Debug");
    }

    // Initialize & Seed Database
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            if (db.Database.IsRelational())
            {
                await db.Database.EnsureCreatedAsync();
            }
            await DbInitializer.SeedAsync(db, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during database initialization/seeding");
        }
    }

    Log.Information("ECommerce.Api initialized successfully. Ready to serve requests.");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
