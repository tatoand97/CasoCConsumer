using Azure.AI.Projects.OpenAI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CasoC.Services;

internal sealed class CasoCOrchestrator
{
    private const string SupportedOrderStatusList = "Created, Confirmed, Packed, Shipped, Delivered, Cancelled, Unknown, NotFound";

    private const string OrderAgentPromptTemplate =
        """
        Recupera solo datos estructurados de la orden solicitada. Usa tu herramienta MCP configurada si aplica.
        Devuelve un unico objeto JSON y nada mas.
        No uses markdown.
        No agregues explicaciones ni texto fuera del JSON.
        Campos requeridos: "id", "status", "requiresAction".
        Campo opcional: "reason".
        Valores validos para "status": "Created", "Confirmed", "Packed", "Shipped", "Delivered", "Cancelled", "Unknown", "NotFound".
        Si no encuentras la orden, devuelve:
        {{"id":"<id solicitado>","status":"NotFound","requiresAction":false,"reason":"Order not found"}}
        Si no puedes clasificar el estado con certeza, usa "Unknown" y explica el motivo en "reason".

        Solicitud del usuario:
        {0}
        """;

    private const string PolicyAgentPromptTemplate =
        """
        Evalua esta orden validada y devuelve la evaluacion de politica en el formato requerido.
        Devuelve solo JSON valido sin markdown ni texto adicional.
        Usa exactamente este formato:
        {{"requiresAction": true, "message": "explicacion breve"}}

        Orden validada:
        {0}
        """;

    private const string PlannerPromptTemplate =
        """
        Genera la respuesta final para el usuario usando solo este contexto validado.

        Solicitud original:
        {0}

        Datos de la orden:
        {1}

        Evaluacion de politica:
        {2}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Dictionary<string, string> SupportedOrderStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Created"] = "Created",
        ["Confirmed"] = "Confirmed",
        ["Packed"] = "Packed",
        ["Shipped"] = "Shipped",
        ["Delivered"] = "Delivered",
        ["Cancelled"] = "Cancelled",
        ["Unknown"] = "Unknown",
        ["NotFound"] = "NotFound",
    };

    private readonly ProjectOpenAIClient _openAiClient;
    private readonly AgentRunner _runner;
    private readonly CasoCBootstrapState _bootstrapState;

    internal CasoCOrchestrator(
        ProjectOpenAIClient openAiClient,
        AgentRunner runner,
        CasoCBootstrapState bootstrapState)
    {
        _openAiClient = openAiClient;
        _runner = runner;
        _bootstrapState = bootstrapState;
    }

    internal async Task<CasoCOrchestrationResult> RunAsync(
        string userRequest,
        CancellationToken cancellationToken)
    {
        CasoCBootstrapSnapshot snapshot = _bootstrapState.GetRequiredSnapshot();

        string orderAgentResponse = await RunOrderAgentAsync(
            snapshot,
            userRequest,
            cancellationToken);

        ValidatedOrderContext orderContext = ParseAndValidateOrderContext(orderAgentResponse);
        string validatedOrderJson = SerializeValidatedJson(orderContext);

        string policyAgentResponse = await RunPolicyAgentAsync(
            snapshot,
            validatedOrderJson,
            cancellationToken);

        ValidatedPolicyAssessment policyAssessment = ParseAndValidatePolicyAssessment(policyAgentResponse);
        string validatedPolicyJson = SerializeValidatedJson(policyAssessment);

        string finalText = await RunPlannerAgentAsync(
            snapshot,
            userRequest,
            validatedOrderJson,
            validatedPolicyJson,
            cancellationToken);

        return new CasoCOrchestrationResult(
            validatedOrderJson,
            validatedPolicyJson,
            finalText);
    }

    private async Task<string> RunOrderAgentAsync(
        CasoCBootstrapSnapshot snapshot,
        string userRequest,
        CancellationToken cancellationToken)
    {
        return await _runner.RunPromptAsync(
            _openAiClient,
            snapshot.OrderAgent.Name,
            string.Format(OrderAgentPromptTemplate, userRequest),
            snapshot.ResponseTimeout,
            cancellationToken);
    }

    private static ValidatedOrderContext ParseAndValidateOrderContext(string responseText)
    {
        OrderAgentResponseDto payload = DeserializeJsonObject<OrderAgentResponseDto>(responseText, "OrderAgent");

        if (string.IsNullOrWhiteSpace(payload.Id))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'id' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (string.IsNullOrWhiteSpace(payload.Status))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'status' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (payload.RequiresAction is null)
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON is missing required field 'requiresAction'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        string normalizedStatus = NormalizeOrderStatus(payload.Status);

        return new ValidatedOrderContext
        {
            Id = payload.Id.Trim(),
            Status = normalizedStatus,
            RequiresAction = payload.RequiresAction.Value,
            Reason = string.IsNullOrWhiteSpace(payload.Reason) ? null : payload.Reason.Trim(),
        };
    }

    private async Task<string> RunPolicyAgentAsync(
        CasoCBootstrapSnapshot snapshot,
        string validatedOrderJson,
        CancellationToken cancellationToken)
    {
        return await _runner.RunPromptAsync(
            _openAiClient,
            snapshot.PolicyAgent.Name,
            string.Format(PolicyAgentPromptTemplate, validatedOrderJson),
            snapshot.ResponseTimeout,
            cancellationToken);
    }

    private static ValidatedPolicyAssessment ParseAndValidatePolicyAssessment(string responseText)
    {
        PolicyAgentResponseDto payload = DeserializeJsonObject<PolicyAgentResponseDto>(responseText, "PolicyAgent");

        if (payload.RequiresAction is null)
        {
            throw new InvalidOperationException(
                $"PolicyAgent JSON is missing required field 'requiresAction'. Raw response: {BuildResponseSnippet(responseText)}");
        }

        if (string.IsNullOrWhiteSpace(payload.Message))
        {
            throw new InvalidOperationException(
                $"PolicyAgent JSON is missing required field 'message' or it is empty. Raw response: {BuildResponseSnippet(responseText)}");
        }

        return new ValidatedPolicyAssessment
        {
            RequiresAction = payload.RequiresAction.Value,
            Message = payload.Message.Trim(),
        };
    }

    private async Task<string> RunPlannerAgentAsync(
        CasoCBootstrapSnapshot snapshot,
        string userRequest,
        string validatedOrderJson,
        string validatedPolicyJson,
        CancellationToken cancellationToken)
    {
        return await _runner.RunPromptAsync(
            _openAiClient,
            snapshot.PlannerAgent.Name,
            string.Format(PlannerPromptTemplate, userRequest, validatedOrderJson, validatedPolicyJson),
            snapshot.ResponseTimeout,
            cancellationToken);
    }

    private static T DeserializeJsonObject<T>(string responseText, string agentLabel)
        where T : class
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"{agentLabel} returned invalid JSON. Expected a single JSON object. Raw response: {BuildResponseSnippet(responseText)}");
            }

            T? payload = JsonSerializer.Deserialize<T>(document.RootElement.GetRawText(), JsonOptions);
            return payload ?? throw new InvalidOperationException(
                $"{agentLabel} returned an empty JSON payload. Raw response: {BuildResponseSnippet(responseText)}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"{agentLabel} returned invalid JSON. Expected a single JSON object. Raw response: {BuildResponseSnippet(responseText)}",
                ex);
        }
    }

    private static string NormalizeOrderStatus(string status)
    {
        string candidate = status.Trim();
        if (!SupportedOrderStatuses.TryGetValue(candidate, out string? normalized))
        {
            throw new InvalidOperationException(
                $"OrderAgent JSON field 'status' has unsupported value '{candidate}'. Supported values: {SupportedOrderStatusList}.");
        }

        return normalized;
    }

    private static string SerializeValidatedJson<T>(T payload)
    {
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildResponseSnippet(string responseText)
    {
        const int MaxLength = 240;
        string condensed = responseText
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (condensed.Length <= MaxLength)
        {
            return condensed;
        }

        return $"{condensed[..MaxLength]}...";
    }

    private sealed class OrderAgentResponseDto
    {
        public string? Id { get; init; }

        public string? Status { get; init; }

        public bool? RequiresAction { get; init; }

        public string? Reason { get; init; }
    }

    private sealed class ValidatedOrderContext
    {
        public required string Id { get; init; }

        public required string Status { get; init; }

        public required bool RequiresAction { get; init; }

        public string? Reason { get; init; }
    }

    private sealed class PolicyAgentResponseDto
    {
        public bool? RequiresAction { get; init; }

        public string? Message { get; init; }
    }

    private sealed class ValidatedPolicyAssessment
    {
        public required bool RequiresAction { get; init; }

        public required string Message { get; init; }
    }
}

internal sealed record CasoCOrchestrationResult(
    string OrderContext,
    string PolicyContext,
    string FinalAnswer);
