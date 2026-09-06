# AislePilot modern image format decision

Date: 2026-08-22

## Decision

Keep content-versioned JPEG as the canonical generated meal image and compatibility fallback. Do not add application-side WebP or AVIF transcoding yet. Introduce modern formats through an image CDN or edge transformer when that delivery layer is available, using content negotiation while retaining the original JPEG URL.

This is a deliberate defer decision, not a decision against modern formats. The delivery change should proceed when it can reduce transferred bytes without adding a second AI request, native image-processing dependencies, or synchronous work to the generation path.

## Current evidence

- OpenAI currently returns a compressed 1024×1024 JPEG at quality/compression 70 in one background request.
- The bundled catalogue contains 24 JPEG files totalling 2,711,314 bytes, approximately 113 KB per image. These are selected before the older PNG copies.
- The 22 legacy PNG copies total 35,069,969 bytes. Removing obsolete duplicates belongs to the separate retention and cleanup item.
- Generated filenames are content-versioned, so a negotiated derivative can inherit immutable caching while the JPEG remains recoverable from disk and Firestore.
- Browser markup already has explicit dimensions, responsive display hints, and JPEG fallback URLs.

## Options evaluated

### Encode WebP/AVIF in the application

Rejected for now. A cross-platform encoder would add package and runtime weight to every deployment. The evaluated MIT-licensed SkiaSharp package is roughly 8.7 MB before platform-native assets and provides WebP but not a verified AVIF path. ImageSharp provides built-in WebP support with a smaller managed package, but its split licence requires commercial-eligibility review before adoption. Either option adds image decode/encode CPU and failure handling to the application.

### Request a second format from OpenAI

Rejected. The image-generation request selects one output format. Producing a JPEG fallback and a WebP original this way would require a second paid request and would increase background completion time and failure surface.

### Pre-generate repository WebP files only

Rejected as incomplete. It would optimize bundled meals but not newly generated meals, and it would add another set of binary assets without a lifecycle policy.

### Negotiate derivatives at the CDN/edge

Preferred. Store and recover one canonical JPEG, derive WebP or AVIF outside the request and generation paths, and let supporting clients receive the smaller representation. Keep the JPEG response for clients without modern-format support.

## Delivery requirements

Before enabling negotiated formats:

1. Put `/images/aislepilot-meals/` behind an image transformer or CDN that can derive WebP and AVIF from the canonical JPEG.
2. Key transformed variants by the full content-versioned source filename, requested width, and output format.
3. Negotiate using `Accept`; include `Vary: Accept` when one URL can return different formats, or use distinct derivative URLs.
4. Preserve the original JPEG and its one-year immutable caching policy.
5. Never transcode synchronously inside an interactive AislePilot request.
6. Warm common derivatives asynchronously after image generation or on the first edge miss.
7. Validate content type, dimensions, visual quality, cache headers, and fallback behavior in production-like smoke tests.

## Rollout gate

Enable WebP first only if representative generated images show at least 15% median transfer reduction versus the current JPEG at equivalent acceptable visual quality, with no regression in p95 plan visibility or image-placeholder duration. Evaluate AVIF separately; enable it only when its additional byte saving justifies its higher encoding cost. Roll back by disabling format negotiation—the canonical JPEG URLs remain unchanged.

