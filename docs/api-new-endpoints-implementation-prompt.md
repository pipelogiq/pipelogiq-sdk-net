# Prompt: Добавление новых endpoint'ов в Pipelogiq API

Используй этот prompt для реализации в серверном репозитории Pipelogiq API.

---

Ты senior backend engineer. Реализуй в Pipelogiq API два новых endpoint'а, совместимых с текущим `.NET SDK`:

1. `POST /pipelines/{pipelineId}/stages`
2. `POST /stages/{stageId}/resume`

## Контекст совместимости SDK

- SDK уже отправляет запросы в эти endpoint'ы:
  - `AppendAgentStagesAsync(int pipelineId, AppendStagesRequest request)`
  - `ResumeStageApprovalAsync(int stageId, bool approved, string? rejectionReason)`
- Ожидаемые DTO (по контракту SDK):
  - `AppendStagesRequest`:
    - `Stages: List<StageInfo>`
  - `AppendStagesResponse`:
    - `Stages: List<StageDto>`
  - `ResumeStageRequest`:
    - `Approved: bool`
    - `RejectionReason: string?`

## Что нужно сделать

1. Добавить роуты и контроллеры.
2. Добавить DTO/валидацию request body и route params.
3. Реализовать application/service-логику.
4. Реализовать persistence-операции транзакционно и безопасно к гонкам.
5. Вернуть корректные HTTP статусы и стандартизированные ошибки.
6. Обновить OpenAPI/Swagger.
7. Добавить тесты (unit + integration).
8. Сохранить backward compatibility со старыми endpoint'ами.

## Детальные требования

### `POST /pipelines/{pipelineId}/stages`

- Назначение: динамически добавить стадии в существующий pipeline.
- Валидация:
  - `pipelineId > 0`
  - `Stages` не пустой список
  - у каждой стадии заполнены минимум `StageName`, `StageHandlerName`
  - `Options` валидны по диапазонам (`RetryInterval >= 0`, `MaxRetries >= 0`, `TimeOut > 0` если задан)
- Ошибки:
  - `400` для валидации
  - `404` если pipeline не найден
  - `409` если pipeline завершен/отменен и append невозможен
- Ответ:
  - `200` + `AppendStagesResponse` с назначенными stage ID и актуальными полями стадий.

### `POST /stages/{stageId}/resume`

- Назначение: возобновить стадию, ожидающую внешнее подтверждение.
- Валидация:
  - `stageId > 0`
  - если `Approved = false`, то `RejectionReason` обязателен и не пустой
- Проверки бизнес-логики:
  - stage существует
  - stage действительно в состоянии ожидания approval
  - повторный resume должен быть идемпотентным
- Ошибки:
  - `400` для валидации
  - `404` если stage не найден
  - `409` если stage не в состоянии ожидания или уже resume'нут с несовместимым решением
- Ответ:
  - `204 No Content` (предпочтительно) или `200` с минимальным телом статуса.

## Нефункциональные требования

- Логирование audit-событий:
  - кто/когда добавил стадии
  - кто/когда и с каким решением сделал resume
- Метрики:
  - счетчики вызовов/ошибок
  - latency по endpoint'ам
- Безопасность:
  - использовать текущую auth-схему API без ослаблений
  - не логировать чувствительные данные из `Input`/context

## Тесты (обязательно)

1. Append:
  - happy-path append нескольких стадий
  - 404 pipeline not found
  - 409 append в завершенный pipeline
  - 400 invalid request (empty stages)
2. Resume:
  - happy-path approved/rejected
  - 404 stage not found
  - 409 stage не в waiting state
  - идемпотентный повтор того же запроса
3. Concurrency:
  - два одновременных resume одного stage
  - append при одновременном завершении pipeline

## Acceptance criteria

- SDK-клиентские методы работают без изменений.
- OpenAPI содержит оба endpoint'а и примеры.
- Все новые тесты зеленые.
- Нет регрессий в существующих endpoint'ах.

## Формат результата

1. Краткий change summary.
2. Список измененных файлов.
3. Контракты endpoint'ов (финальная версия).
4. Сводка тестов и покрытых кейсов.

---

Если текущая серверная архитектура диктует другой слой/паттерн (minimal API, controller-based, CQRS), следуй ей, но сохрани контракт и поведение.
