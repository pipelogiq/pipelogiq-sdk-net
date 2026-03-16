# Examples

## `MinimalWorker`

Smallest possible worker setup with a single handler.

## `WorkerAndPipelineHost`

Structured example with:

- worker mode (registers multiple handlers)
- optional Telegram AI channel in worker mode (message -> AI pipeline -> message)
- pipeline submission mode (builds and sends a demo pipeline)
- separated folders (`Configuration`, `Handlers`, `HostedServices`, `Models`, `Services`)
