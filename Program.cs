using Azure.AI.Projects;
using Azure.Identity;
using CasoC;
using CasoC.Models;
using CasoC.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<CasoCSettings>()
    .Bind(builder.Configuration.GetSection(CasoCSettings.SectionName))
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.AzureOpenAiEndpoint),
        "The configuration key 'CasoC:AzureOpenAiEndpoint' is required.")
    .Validate(
        settings => Uri.TryCreate(settings.AzureOpenAiEndpoint, UriKind.Absolute, out Uri? endpointUri) &&
                    endpointUri.Scheme == Uri.UriSchemeHttps,
        "The configuration key 'CasoC:AzureOpenAiEndpoint' must be a valid HTTPS endpoint.")
    .Validate(
        settings => settings.AzureOpenAiEndpoint!.Contains("/api/projects/", StringComparison.OrdinalIgnoreCase),
        "The configuration key 'CasoC:AzureOpenAiEndpoint' must be a Foundry project endpoint containing '/api/projects/'.")
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.AzureOpenAiDeployment),
        "The configuration key 'CasoC:AzureOpenAiDeployment' is required.")
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.OrderAgentId),
        "The configuration key 'CasoC:OrderAgentId' is required.")
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.PolicyAgentId),
        "The configuration key 'CasoC:PolicyAgentId' is required.")
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.PlannerAgentId),
        "The configuration key 'CasoC:PlannerAgentId' is required.")
    .Validate(
        settings => settings.ResponsesTimeoutSeconds > 0,
        "The configuration key 'CasoC:ResponsesTimeoutSeconds' must be a positive integer.")
    .Validate(
        settings => settings.ResponsesMaxBackoffSeconds > 0,
        "The configuration key 'CasoC:ResponsesMaxBackoffSeconds' must be a positive integer.")
    .ValidateOnStart();

builder.Services.AddProblemDetails();
builder.Services.AddSingleton<AzureCliCredential>();
builder.Services.AddSingleton(sp =>
{
    CasoCSettings settings = sp.GetRequiredService<IOptions<CasoCSettings>>().Value;
    return new AIProjectClient(
        new Uri(settings.AzureOpenAiEndpoint!, UriKind.Absolute),
        sp.GetRequiredService<AzureCliCredential>());
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<AIProjectClient>().OpenAI);
builder.Services.AddSingleton(sp =>
{
    CasoCSettings settings = sp.GetRequiredService<IOptions<CasoCSettings>>().Value;
    return new AgentRunner(TimeSpan.FromSeconds(settings.ResponsesMaxBackoffSeconds));
});
builder.Services.AddSingleton<CasoCBootstrapState>();
builder.Services.AddSingleton<CasoCBootstrapper>();
builder.Services.AddSingleton<CasoCOrchestrator>();
builder.Services.AddHostedService<CasoCBootstrapHostedService>();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        Exception? exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        ILogger logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CasoC.Api");

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

RouteGroupBuilder casoCApi = app.MapGroup("/api/casoc");

casoCApi.MapPost("/ask", async (
    AskRequest request,
    HttpContext httpContext,
    CasoCOrchestrator orchestrator,
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

    logger.LogInformation("Request received. Path: {Path}", httpContext.Request.Path);

    try
    {
        CasoCOrchestrationResult result = await orchestrator.RunAsync(
            request.Prompt.Trim(),
            cancellationToken);

        logger.LogInformation("Orchestration completed. Path: {Path}", httpContext.Request.Path);
        return Results.Ok(new AskResponse(result.FinalAnswer, traceId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Orchestration failed. Path: {Path}", httpContext.Request.Path);
        throw;
    }
});

casoCApi.MapPost("/ask/debug", async (
    AskRequest request,
    HttpContext httpContext,
    CasoCOrchestrator orchestrator,
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

    logger.LogInformation("Request received. Path: {Path}", httpContext.Request.Path);

    try
    {
        CasoCOrchestrationResult result = await orchestrator.RunAsync(
            request.Prompt.Trim(),
            cancellationToken);

        logger.LogInformation("Orchestration completed. Path: {Path}", httpContext.Request.Path);
        return Results.Ok(new AskDebugResponse(
            result.OrderContext,
            result.PolicyContext,
            result.FinalAnswer,
            traceId));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Orchestration failed. Path: {Path}", httpContext.Request.Path);
        throw;
    }
});

casoCApi.MapGet("/health", (CasoCBootstrapState bootstrapState) =>
{
    CasoCBootstrapSnapshot snapshot = bootstrapState.GetRequiredSnapshot();

    return Results.Ok(new HealthResponse(
        "ok",
        new AgentInfoResponse(
            snapshot.OrderAgent.Id,
            snapshot.OrderAgent.Name,
            snapshot.OrderAgent.Version,
            snapshot.OrderAgent.ValidationStatus),
        new AgentInfoResponse(
            snapshot.PolicyAgent.Id,
            snapshot.PolicyAgent.Name,
            snapshot.PolicyAgent.Version,
            snapshot.PolicyAgent.ValidationStatus),
        new AgentInfoResponse(
            snapshot.PlannerAgent.Id,
            snapshot.PlannerAgent.Name,
            snapshot.PlannerAgent.Version,
            snapshot.PlannerAgent.ValidationStatus)));
});

app.Run();
