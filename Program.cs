using Azure.AI.Projects;
using Azure.Identity;
using CasoCConsumer;
using CasoCConsumer.Models;
using CasoCConsumer.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<CasoCConsumerSettings>()
    .Bind(builder.Configuration.GetSection(CasoCConsumerSettings.SectionName))
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.AzureOpenAiEndpoint),
        "The configuration key 'CasoCConsumer:AzureOpenAiEndpoint' is required.")
    .Validate(
        settings => Uri.TryCreate(settings.AzureOpenAiEndpoint, UriKind.Absolute, out Uri? endpointUri) &&
                    endpointUri.Scheme == Uri.UriSchemeHttps,
        "The configuration key 'CasoCConsumer:AzureOpenAiEndpoint' must be a valid HTTPS endpoint.")
    .Validate(
        settings => settings.AzureOpenAiEndpoint!.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase),
        "The configuration key 'CasoCConsumer:AzureOpenAiEndpoint' must be a Foundry project endpoint containing '/api/projects/'.")
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.AzureOpenAiDeployment),
        "The configuration key 'CasoCConsumer:AzureOpenAiDeployment' is required.")
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.PlannerAgentId),
        "The configuration key 'CasoCConsumer:PlannerAgentId' is required.")
    .Validate(
        settings => settings.ResponsesTimeoutSeconds > 0,
        "The configuration key 'CasoCConsumer:ResponsesTimeoutSeconds' must be a positive integer.")
    .Validate(
        settings => settings.ResponsesMaxBackoffSeconds > 0,
        "The configuration key 'CasoCConsumer:ResponsesMaxBackoffSeconds' must be a positive integer.")
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<AzureCliCredential>();
builder.Services.AddSingleton(sp =>
{
    CasoCConsumerSettings settings = sp.GetRequiredService<IOptions<CasoCConsumerSettings>>().Value;
    return new AIProjectClient(
        new Uri(settings.AzureOpenAiEndpoint!, UriKind.Absolute),
        sp.GetRequiredService<AzureCliCredential>());
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<AIProjectClient>().OpenAI);
builder.Services.AddSingleton(sp =>
{
    CasoCConsumerSettings settings = sp.GetRequiredService<IOptions<CasoCConsumerSettings>>().Value;
    return new AgentRunner(TimeSpan.FromSeconds(settings.ResponsesMaxBackoffSeconds));
});
builder.Services.AddSingleton<CasoCConsumerAgentRegistry>();
builder.Services.AddSingleton<CasoCConsumerStartupValidator>();
builder.Services.AddSingleton<PlannerAgentConsumer>();
builder.Services.AddHostedService<CasoCConsumerStartupValidationHostedService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        Exception? exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        ILogger logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CasoCConsumer.Api");

        if (exception is not null)
        {
            logger.LogError(
                exception,
                "Unhandled exception processing {Method} {Path}. TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);
        }

        await Results.Problem(
            title: "Request processing failed.",
            detail: "The server could not complete the operation. Check server logs with the trace identifier.",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = context.TraceIdentifier,
            }).ExecuteAsync(context);
    });
});

app.UseHttpsRedirection();

RouteGroupBuilder casoCConsumerApi = app.MapGroup("/api/casoc");

casoCConsumerApi.MapPost("/ask", async (
    AskRequest request,
    HttpContext httpContext,
    PlannerAgentConsumer plannerAgentConsumer,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(AskRequest.Prompt)] = ["The prompt field is required."],
        });
    }

    string traceId = httpContext.TraceIdentifier;
    using IDisposable? _ = logger.BeginScope(new Dictionary<string, object?>
    {
        ["TraceId"] = traceId,
    });

    logger.LogInformation("PlannerAgent request received. Path: {Path}", httpContext.Request.Path);

    try
    {
        string plannerAnswer = await plannerAgentConsumer.AskAsync(
            request.Prompt.Trim(),
            cancellationToken);

        logger.LogInformation("PlannerAgent request completed. Path: {Path}", httpContext.Request.Path);
        return Results.Ok(new AskResponse(plannerAnswer, traceId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "PlannerAgent request failed. Path: {Path}", httpContext.Request.Path);
        throw;
    }
});

casoCConsumerApi.MapPost("/ask/debug", async (
    AskRequest request,
    HttpContext httpContext,
    PlannerAgentConsumer plannerAgentConsumer,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(AskRequest.Prompt)] = ["The prompt field is required."],
        });
    }

    string traceId = httpContext.TraceIdentifier;
    using IDisposable? _ = logger.BeginScope(new Dictionary<string, object?>
    {
        ["TraceId"] = traceId,
    });

    logger.LogInformation("PlannerAgent debug request received. Path: {Path}", httpContext.Request.Path);

    try
    {
        string plannerAnswer = await plannerAgentConsumer.AskAsync(
            request.Prompt.Trim(),
            cancellationToken);

        logger.LogInformation("PlannerAgent debug request completed. Path: {Path}", httpContext.Request.Path);
        return Results.Ok(new AskDebugResponse(plannerAnswer, traceId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "PlannerAgent debug request failed. Path: {Path}", httpContext.Request.Path);
        throw;
    }
});

casoCConsumerApi.MapGet("/health", (CasoCConsumerAgentRegistry agentRegistry) =>
{
    CasoCConsumerAgentSnapshot snapshot = agentRegistry.GetRequiredSnapshot();

    return Results.Ok(new HealthResponse(
        "ok",
        new AgentInfoResponse(
            snapshot.PlannerAgent.Id,
            snapshot.PlannerAgent.Name,
            snapshot.PlannerAgent.Version)));
});

app.Run();
