export default {
  async fetch(request, env) {
    const incomingUrl = new URL(request.url);

    // Safety: this Worker should only proxy AislePilot paths.
    if (!incomingUrl.pathname.startsWith("/projects/aisle-pilot")) {
      return new Response("Not found", { status: 404 });
    }

    const rawOrigin = (env.AISLEPILOT_ORIGIN || "").trim();
    if (!rawOrigin) {
      return new Response("AISLEPILOT_ORIGIN is not configured.", { status: 500 });
    }

    let origin;
    try {
      origin = new URL(rawOrigin);
    } catch {
      return new Response("AISLEPILOT_ORIGIN is invalid.", { status: 500 });
    }

    const upstreamUrl = new URL(incomingUrl.pathname + incomingUrl.search, origin.toString());
    const upstreamRequest = new Request(upstreamUrl.toString(), request);

    // Keep this as a pure passthrough proxy.
    return fetch(upstreamRequest);
  }
};
