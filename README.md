# Caso C Consumer API

API ASP.NET Core .NET 8 para consumo puro del Caso C por HTTP.

## Que hace

- Carga configuracion.
- Valida el endpoint de Foundry al iniciar.
- Valida que existan y sean accesibles `OrderAgentId`, `PolicyAgentId` y `PlannerAgentId`.
- Guarda en memoria los ids, nombres y versiones resueltas.
- Orquesta por request el flujo `OrderAgent -> PolicyAgent -> PlannerAgent`.
- Expone `POST /api/casoc/ask`, `POST /api/casoc/ask/debug` y `GET /api/casoc/health`.

## Que no hace

- No crea agentes.
- No reconcilia agentes.
- No crea versiones nuevas.
- No modifica infraestructura de Foundry.
- No introduce Workflows.
- No introduce `ManagerAgent`.
- No usa tools tipo `agent`.

## Arquitectura

```text
HTTP Client
  |
CasoCConsumer (.NET 8 Web API)
  |
CasoCOrchestrator
  |-- OrderAgent
  |-- PolicyAgent
  '-- PlannerAgent
```

La orquestacion sigue en el backend. El repo `CasoCConsumer` solo consume agentes externos ya preparados.

## Prerequisite

El repo `CasoC` o el repo equivalente de bootstrap debe ejecutarse primero para asegurar que `PolicyAgent` y `PlannerAgent` ya existen en Foundry.

Este repo asume que ya existen tres agentes accesibles:

- `OrderAgent`
- `PolicyAgent`
- `PlannerAgent`

## Configuracion

Configura `appsettings.json` con los tres agentes existentes:

```json
{
  "CasoC": {
    "AzureOpenAiEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureOpenAiDeployment": "<deployment-name>",
    "OrderAgentId": "OrderAgent:3",
    "PolicyAgentId": "policy-agent-casec:2",
    "PlannerAgentId": "planner-agent-casec-composer:1",
    "ResponsesTimeoutSeconds": 60,
    "ResponsesMaxBackoffSeconds": 8
  }
}
```

Claves requeridas:

- `AzureOpenAiEndpoint`
- `AzureOpenAiDeployment`
- `OrderAgentId`
- `PolicyAgentId`
- `PlannerAgentId`
- `ResponsesTimeoutSeconds`
- `ResponsesMaxBackoffSeconds`

Validaciones al arranque:

- `AzureOpenAiEndpoint` debe ser HTTPS.
- `AzureOpenAiEndpoint` debe contener `/api/projects/`.
- Los tres `*AgentId` deben estar configurados.
- Los tres `*AgentId` deben existir y ser accesibles en Foundry.
- `ResponsesTimeoutSeconds` debe ser mayor que `0`.
- `ResponsesMaxBackoffSeconds` debe ser mayor que `0`.

## Bootstrap de la API

Al iniciar:

- valida configuracion
- valida acceso al endpoint de Foundry
- valida `OrderAgentId`
- valida `PolicyAgentId`
- valida `PlannerAgentId`
- guarda ids, nombres y versiones resueltas con estado `ExternalValidated`

No hay reconciliacion ni creacion de infraestructura.

## Endpoints

### `POST /api/casoc/ask`

Request:

```json
{
  "prompt": "Dame el estado de la orden ORD-000001 y dime si requiere accion."
}
```

Response `200`:

```json
{
  "answer": "La orden ORD-000001 ...",
  "traceId": "0HNTK3U4J2R5A:00000001"
}
```

### `POST /api/casoc/ask/debug`

Response `200`:

```json
{
  "orderContext": "{ ... }",
  "policyContext": "{ ... }",
  "finalAnswer": "La orden ...",
  "traceId": "0HNTK3U4J2R5A:00000002"
}
```

### `GET /api/casoc/health`

Response `200`:

```json
{
  "status": "ok",
  "orderAgent": {
    "id": "OrderAgent:3",
    "name": "OrderAgent",
    "version": "3",
    "validationStatus": "ExternalValidated"
  },
  "policyAgent": {
    "id": "policy-agent-casec:2",
    "name": "policy-agent-casec",
    "version": "2",
    "validationStatus": "ExternalValidated"
  },
  "plannerAgent": {
    "id": "planner-agent-casec-composer:1",
    "name": "planner-agent-casec-composer",
    "version": "1",
    "validationStatus": "ExternalValidated"
  }
}
```

## Como correrla

```powershell
dotnet restore
dotnet run
```

En desarrollo, `launchSettings.json` expone:

- `https://localhost:7230`
- `http://localhost:5088`

## Ejemplos de curl

```powershell
curl -k -X POST https://localhost:7230/api/casoc/ask `
  -H "Content-Type: application/json" `
  -d "{\"prompt\":\"Dame el estado de la orden ORD-000001 y dime si requiere accion.\"}"
```

```powershell
curl -k https://localhost:7230/api/casoc/health
```

## Logging

La API registra como minimo:

- bootstrap validation started
- bootstrap validation completed
- order agent validated
- policy agent validated
- planner agent validated
- request received
- orchestration completed
- orchestration failed
