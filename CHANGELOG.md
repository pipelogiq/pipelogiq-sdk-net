# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Worker no longer stops on startup in `QueueProvisioningMode.AssertOnly` when required RabbitMQ queues are not created yet; it now enters retry loop and reconnects once queues appear.
- Worker runtime now keeps running after connection/configuration failures and retries reconnection every 10 seconds instead of stopping.
- Worker now reports `ready` when RabbitMQ connection is up even if only part of StageNext queues are subscribed, and heartbeat metadata now includes active/total/missing StageNext queue counts.

## [0.1.0] preview - 2026-02-21

### Added

- Initial public preview of Pipelogiq .NET SDK.
- Basic runner and stage execution support.
- OpenTelemetry-compatible tracing guidance.
- Logging integration.

> This is an early preview release. APIs may change.

[Unreleased]: https://github.com/pipelogiq/pipelogiq-sdk-net/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/pipelogiq/pipelogiq-sdk-net/releases/tag/v0.1.0
