# Screenshot Checklist

Capture and store screenshots in this folder after running the local stack.

Recommended files:

1. `01-operational-overview.png`
   - include request rate, error rate, latency stats
2. `02-failure-logs.png`
   - logs panel with visible `TraceId` and error detail
3. `03-tempo-trace-failure-span.png`
   - trace waterfall with failing AI span selected
4. `04-ai-observability.png`
   - AI failure rate, duration, token throughput, and cost panels
5. `05-background-jobs-impact.png`
   - background failure or queue-depth panel during incident run

For consistency in blog/demo content:

- use same time range across screenshots (for example `Last 30 minutes`)
- keep the same service filter
- include timestamp in panel tooltip where relevant
