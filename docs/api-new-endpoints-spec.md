# Спецификация: новые endpoint'ы Pipelogiq API

Версия: `v1`  
Дата: `2026-03-10`  
Статус: `Draft for implementation`

## 1) Цель

Добавить в Pipelogiq API endpoint'ы для:

1. Динамического добавления стадий в уже созданный pipeline.
2. Возобновления стадии, ожидающей внешнее подтверждение (approval).

Эти endpoint'ы требуются для AI-оркестрации и confirmation flow.

## 2) Совместимость

Новые endpoint'ы должны быть совместимы с текущим `.NET SDK`:

- `POST /pipelines/{pipelineId}/stages` -> `AppendStagesResponse`
- `POST /stages/{stageId}/resume` -> без тела ответа (`204`) или эквивалентный `200`

SDK DTO, с которыми нужно сохранять совместимость:

- `AppendStagesRequest { Stages: List<StageInfo> }`
- `AppendStagesResponse { Stages: List<StageDto> }`
- `ResumeStageRequest { Approved: bool, RejectionReason?: string }`

## 3) Endpoint: POST /pipelines/{pipelineId}/stages

### Назначение

Добавляет одну или несколько стадий к существующему pipeline во время исполнения.

### Request

- Path params:
  - `pipelineId: int` (`> 0`)
- Body: `AppendStagesRequest`

Пример:

```json
{
  "Stages": [
    {
      "StageName": "agent.tool",
      "StageHandlerName": "AgentToolHandler",
      "Input": {
        "ToolName": "getOrder",
        "Args": {
          "id": "42"
        }
      },
      "Options": {
        "RunNextIfFailed": false,
        "MaxRetries": 0
      },
      "IsEvent": false
    }
  ]
}
```

### Response

- `200 OK`
- Body: `AppendStagesResponse`

Пример:

```json
{
  "Stages": [
    {
      "Id": 9021,
      "PipelineId": 1307,
      "Name": "agent.tool",
      "StageHandlerName": "AgentToolHandler",
      "Status": "pending",
      "CreatedAt": "2026-03-10T10:14:05Z",
      "Input": "{\"ToolName\":\"getOrder\",\"Args\":{\"id\":\"42\"}}",
      "NextStageId": null,
      "IsSkipped": false,
      "IsEvent": false,
      "RunNextIfCurrentFailed": false
    }
  ]
}
```

### Валидация

1. `pipelineId` валиден и существует.
2. `Stages` не пустой массив.
3. Для каждой стадии:
  - `StageName` обязателен.
  - `StageHandlerName` обязателен.
4. Если `Options` переданы:
  - `RetryInterval >= 0`
  - `MaxRetries >= 0`
  - `TimeOut > 0` (если задан)

### Ошибки

- `400 Bad Request`:
  - невалидный `pipelineId`
  - пустой `Stages`
  - невалидные поля стадии
- `404 Not Found`:
  - pipeline не найден
- `409 Conflict`:
  - pipeline в финальном статусе (`completed/failed/cancelled`) и append запрещен

## 4) Endpoint: POST /stages/{stageId}/resume

### Назначение

Возобновляет выполнение стадии, находящейся в ожидании внешнего решения.

### Request

- Path params:
  - `stageId: int` (`> 0`)
- Body: `ResumeStageRequest`

Примеры:

```json
{
  "Approved": true
}
```

```json
{
  "Approved": false,
  "RejectionReason": "User rejected payment mutation"
}
```

### Response

- Предпочтительно `204 No Content`
- Допустимо `200 OK` с сервисным телом (если это ваш существующий стиль)

### Валидация

1. `stageId` валиден и существует.
2. Стадия находится в состоянии ожидания (`waiting_for_approval` или эквивалент).
3. Если `Approved = false`, поле `RejectionReason` обязательно и не пустое.

### Идемпотентность

Повторный identical `resume` на уже обработанной стадии:

- либо возвращает `204`/`200` без повторного побочного эффекта,
- либо `409` при конфликте решения.

Рекомендуемое правило:

- если решение совпадает с уже примененным -> идемпотентный успех;
- если решение отличается -> `409 Conflict`.

### Ошибки

- `400 Bad Request`:
  - невалидный `stageId`
  - `Approved=false` без `RejectionReason`
- `404 Not Found`:
  - stage не найден
- `409 Conflict`:
  - stage не в ожидаемом статусе
  - конфликтующее повторное решение

## 5) Error Contract

Использовать единый формат ошибок API (если уже есть корпоративный формат, придерживаться его).
Если формат не стандартизован, рекомендуется `application/problem+json`:

```json
{
  "type": "https://api.pipelogiq.dev/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "Stages must not be empty",
  "traceId": "00-....",
  "errors": {
    "Stages": ["At least one stage is required."]
  }
}
```

## 6) Поведение и транзакционность

1. `append stages` выполняется атомарно в рамках одной транзакции.
2. Порядок стадий в БД соответствует порядку в `Stages`.
3. `resume stage` использует compare-and-set по текущему статусу стадии.
4. Защита от гонок обязательна (row lock / optimistic concurrency).

## 7) Безопасность и аудит

1. Применяется текущая auth-схема API (без изменений).
2. Логировать audit-события:
  - append вызов (pipelineId, count, actor, timestamp)
  - resume решение (stageId, approved, actor, timestamp)
3. Не логировать чувствительные данные из `Input`, context и секретов.

## 8) Наблюдаемость

Метрики:

- `api_append_stages_total` (labels: status_code)
- `api_stage_resume_total` (labels: approved, status_code)
- `api_append_stages_duration_ms`
- `api_stage_resume_duration_ms`

Трассировка:

- span на каждый endpoint
- привязка к pipeline/stage id в attributes

## 9) OpenAPI

Обязательно добавить оба endpoint'а в OpenAPI/Swagger:

1. Описать schemas:
  - `AppendStagesRequest`
  - `AppendStagesResponse`
  - `ResumeStageRequest`
  - Error schema
2. Добавить примеры request/response.
3. Зафиксировать статусы `200/204/400/404/409`.

## 10) План тестирования

### Unit tests

1. Валидация request DTO.
2. Бизнес-правила по статусам pipeline/stage.
3. Идемпотентность resume.

### Integration tests

1. Happy path append нескольких стадий.
2. Append в финальный pipeline -> `409`.
3. Resume approved -> стадия уходит из waiting state.
4. Resume rejected -> pipeline получает rejection decision.
5. Повторный identical resume -> идемпотентный успех.
6. Повторный conflicting resume -> `409`.

## 11) Критерии приемки

1. SDK методы `AppendAgentStagesAsync` и `ResumeStageApprovalAsync` работают без изменений.
2. Контракты endpoint'ов соответствуют этой спецификации.
3. Все новые unit/integration тесты проходят.
4. Нет регрессий существующих endpoint'ов.
5. OpenAPI документация обновлена.
