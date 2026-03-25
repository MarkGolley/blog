# Cloudflare Path Proxy For AislePilot

Goal: keep public URL as `https://markgolley.dev/projects/aisle-pilot` while AislePilot runs on a separate Cloud Run service.

## Why this path

- No new domain purchase required.
- Very low cost on Cloudflare Free for low traffic.

References:

- Workers pricing (Free includes 100,000 requests/day): https://developers.cloudflare.com/workers/platform/pricing/
- Workers limits and daily request behavior: https://developers.cloudflare.com/workers/platform/limits/
- Workers routes setup: https://developers.cloudflare.com/workers/configuration/routing/routes/

## 1) Create Worker

1. In Cloudflare dashboard, go to **Workers & Pages**.
2. Create a new Worker (for example `aislepilot-proxy`).
3. Replace script with contents of `Deployment/cloudflare/aislepilot-proxy-worker.js`.
4. Add environment variable:
   - Name: `AISLEPILOT_ORIGIN`
   - Value: your Cloud Run AislePilot URL, e.g. `https://myblog-aislepilot-zy2liwdipq-nw.a.run.app`
5. Deploy Worker.

## 2) Add route

Add route pattern:

`markgolley.dev/projects/aisle-pilot*`

Attach it to your `aislepilot-proxy` Worker.

Notes:

- Route patterns are case-sensitive for path.
- Your `markgolley.dev` DNS record must stay proxied (orange cloud), otherwise Workers will not run.

## 3) Point blog CTA to main domain path

Deploy blog service so CTA remains on `markgolley.dev`:

```powershell
.\Deployment\deploy-blog.ps1 `
  -EnvironmentName Production `
  -PublicBaseUrl "https://markgolley.dev" `
  -AislePilotPublicBaseUrl "https://markgolley.dev"
```

## 4) Verify

```powershell
curl.exe -i "https://markgolley.dev/projects/aisle-pilot"
```

Expect `200` (or a redirect then `200`).

Then open:

- `https://markgolley.dev/projects`
- Click **Try AislePilot**
- URL should stay on `markgolley.dev/projects/aisle-pilot`
