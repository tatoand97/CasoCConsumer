# Caso C API - Orquestacion multi-agente por HTTP (.NET 8, Responses API)

API ASP.NET Core .NET 8 que expone el Caso C como servicio HTTP y mantiene la orquestacion en codigo del backend.

## Que hace

- Reutiliza un `OrderAgent` existente mediante `OrderAgentId`.
- Crea o reconcilia `PolicyAgent` al iniciar la aplicacion.
- Crea o reconcilia `PlannerAgent` al iniciar la aplicacion.
- Orquesta por request el flujo `OrderAgent -> PolicyAgent -> PlannerAgent`.
- Usa Responses API con polling hasta estado terminal.
- Devuelve al cliente solo la respuesta final del `PlannerAgent`.

## Arquitectura

```text
HTTP Client
  |
CasoC API (.NET 8 Web API)
  |
App Orchestrator in code
  |-- OrderAgent
  |-- PolicyAgent
  '-- PlannerAgent
```

La API no usa Foundry Workflows, no introduce `ManagerAgent`, no usa tools tipo `agent` y no mueve la orquestacion fuera del backend.

## Bootstrap y runtime

Al arrancar la API:

- carga y valida configuracion
- valida acceso al proyecto Foundry
- valida `OrderAgentId`
- reconcilia `PolicyAgent`
- reconcilia `PlannerAgent`
- guarda en memoria los ids, nombres y versiones resueltas

En cada request:

- recibe el prompt del usuario
- ejecuta `OrderAgent -> PolicyAgent -> PlannerAgent`
- devuelve la respuesta final HTTP

## Configuracion

Configura `appsettings.json`:

```json
{
  "CasoC": {
    "AzureOpenAiEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureOpenAiDeployment": "<deployment-name>",
    "OrderAgentId": "<existing-order-agent-id>",
    "ResponsesTimeoutSeconds": 60,
    "ResponsesMaxBackoffSeconds": 8
  }
}
```

Opcionalmente puedes agregar `OrderAgentVersion`. Si no se define, la API usa la version mas reciente.

Validaciones aplicadas al arranque:

- `AzureOpenAiEndpoint` debe ser HTTPS
- `AzureOpenAiEndpoint` debe contener `/api/projects/`
- `OrderAgentId` es obligatorio
- `ResponsesTimeoutSeconds` debe ser mayor que `0`
- `ResponsesMaxBackoffSeconds` debe ser mayor que `0`

## Como correrla

```powershell
dotnet restore
dotnet run
```

En desarrollo, `launchSettings.json` expone:

- `https://localhost:7230`
- `http://localhost:5088`

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

### `GET /api/casoc/health`

Response `200`:

```json
{
  "status": "ok",
  "orderAgent": {
    "id": "OrderAgent:3",
    "name": "OrderAgent",
    "version": "3"
  },
  "policyAgent": {
    "id": "policy-agent-casec:2",
    "name": "policy-agent-casec",
    "version": "2"
  },
  "plannerAgent": {
    "id": "planner-agent-casec-composer:1",
    "name": "planner-agent-casec-composer",
    "version": "1"
  }
}
```

### `POST /api/casoc/ask/debug`

Devuelve contexto intermedio validado para depuracion:

```json
{
  "orderContext": "{ ... }",
  "policyContext": "{ ... }",
  "finalAnswer": "La orden ...",
  "traceId": "0HNTK3U4J2R5A:00000002"
}
```

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

- bootstrap started/completed
- order agent validated
- policy agent reconciled
- planner agent reconciled
- request received
- orchestration completed
- orchestration failed

## Notas

- Se reutilizan `CasoCSettings`, `AgentReconciler`, `AgentRunner`, `PolicyAgentFactory` y `PlannerAgentFactory`.
- `PolicyAgent` y `PlannerAgent` se reconcilian una sola vez al arranque.
- La aplicacion sigue orquestando el Caso C desde codigo, igual que en el repo original.
