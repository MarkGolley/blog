# Observability Demo Pack

This folder captures the operational maturity narrative for `markgolley.dev` + `AislePilot`.

## Documents

- [Telemetry Architecture](./telemetry-architecture.md)
- [Debugging Walkthrough](./debugging-walkthrough.md)
- [Demo Content Pack](./demo-content-pack.md)
- [Screenshots Checklist](./screenshots/README.md)

## Scope

The implementation targets:

- OpenTelemetry instrumentation
- structured logs with correlation IDs and trace IDs
- metrics for reliability, performance, and AI usage/cost
- an end-to-end telemetry pipeline via OTel Collector -> LGTM stack
- reproducible failure diagnosis workflow suitable for interviews
