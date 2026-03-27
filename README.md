# CasoCConsumer API

Consumidor HTTP ASP.NET Core en `.NET 10` para el Caso C A2A real.

Este repo es solo el runtime de consumo. El backend valida e invoca unicamente a `PlannerAgent`. La delegacion hacia `OrderAgent` y `PolicyAgent` ocurre dentro de `PlannerAgent` por A2A. `CasoCConsumer` no hace fan-out backend, no reconcilia agentes y no crea infraestructura.

## Arquitectura objetivo

```text
Cliente HTTP
  |
  v
CasoCConsumer
  |
  v
PlannerAgent
  |- A2A -> OrderAgent
  `- A2A -> PolicyAgent
```

## Prerequisite

Antes de levantar este repo:

- tener instalado el SDK de `.NET 10`
- ejecutar el repo bootstrap `CasoC`
- tener creado o reconciliado `PlannerAgent`
- asegurar que `PlannerAgent` tenga sus conexiones A2A validas

El bootstrap del caso y la reconciliacion de agentes viven en `CasoC`, no en este runtime.

## Configuracion

`appsettings.json` debe usar solo esta configuracion de runtime:

```json
{
  "CasoCConsumer": {
    "AzureOpenAiEndpoint": "https://<resource>.services.ai.azure.com/api/projects/<project>",
    "AzureOpenAiDeployment": "<deployment-name>",
    "PlannerAgentId": "<planner-agent-version-id>",
    "ResponsesTimeoutSeconds": 60,
    "ResponsesMaxBackoffSeconds": 8
  }
}
```

Notas:

- `AzureOpenAiEndpoint` debe ser HTTPS y contener `/api/projects/`
- `AzureOpenAiDeployment` se mantiene como parte de la configuracion del runtime
- `PlannerAgentId` debe resolver a una version explicita del planner, ya sea como version id exacto o como `<agent-name>:<version>`
- `ResponsesTimeoutSeconds` y `ResponsesMaxBackoffSeconds` deben ser mayores que `0`

## Que hace

- valida configuracion
- valida endpoint Foundry y acceso al proyecto
- valida externamente `PlannerAgentId`
- guarda en memoria `id`, `name` y `version` del planner
- expone `POST /api/casoc/ask`, `POST /api/casoc/ask/debug` y `GET /api/casoc/health`
- envia el prompt solo a `PlannerAgent`
- devuelve la respuesta final del planner al cliente

## Out of scope

- direct calls to `OrderAgent`
- direct calls to `PolicyAgent`
- workflow orchestration
- agent reconciliation
- Foundry infrastructure creation

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
  "plannerAnswer": "La orden ORD-000001 ...",
  "traceId": "0HNTK3U4J2R5A:00000002"
}
```

### `GET /api/casoc/health`

Response `200`:

```json
{
  "status": "ok",
  "plannerAgent": {
    "id": "planner-agent-casec-composer:1",
    "name": "planner-agent-casec-composer",
    "version": "1"
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
curl -k -X POST https://localhost:7230/api/casoc/ask/debug `
  -H "Content-Type: application/json" `
  -d "{\"prompt\":\"Dame el estado de la orden ORD-000001 y dime si requiere accion.\"}"
```

```powershell
curl -k https://localhost:7230/api/casoc/health
```

## Logging

La API registra como minimo:

- startup validation started
- foundry project access validated
- planner agent validated
- startup validation completed
- planneragent request received
- planneragent request completed
- planneragent request failed
