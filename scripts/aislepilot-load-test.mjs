import { performance } from "node:perf_hooks";

const baseUrl = (process.env.AISLEPILOT_LOAD_BASE_URL ?? "http://127.0.0.1:5098").replace(/\/$/, "");
const virtualUsers = Number.parseInt(process.env.AISLEPILOT_LOAD_VUS ?? "8", 10);
const warmRequests = Number.parseInt(process.env.AISLEPILOT_LOAD_WARM_REQUESTS ?? "32", 10);
const warmP95LimitMs = Number.parseFloat(process.env.AISLEPILOT_LOAD_WARM_P95_MS ?? "2000");
const coldP95LimitMs = Number.parseFloat(process.env.AISLEPILOT_LOAD_COLD_P95_MS ?? "5000");

const percentile = (values, rank) => {
  const ordered = [...values].sort((left, right) => left - right);
  return ordered[Math.max(0, Math.ceil((rank / 100) * ordered.length) - 1)];
};

const createSession = async index => {
  const response = await fetch(`${baseUrl}/projects/aisle-pilot`, {
    headers: {
      "user-agent": `AislePilot-LoadTest/${index}`,
      "x-forwarded-proto": "https"
    }
  });
  if (!response.ok) throw new Error(`Setup GET failed with HTTP ${response.status}.`);
  const html = await response.text();
  const token = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/i)?.[1]
    ?? html.match(/value="([^"]+)"[^>]*name="__RequestVerificationToken"/i)?.[1];
  if (!token) throw new Error("Setup GET did not return an antiforgery token.");
  const setCookies = typeof response.headers.getSetCookie === "function"
    ? response.headers.getSetCookie()
    : [response.headers.get("set-cookie")].filter(Boolean);
  const cookie = setCookies.map(value => value.split(";", 1)[0]).join("; ");
  return { index, token, cookie };
};

const postPlan = async session => {
  const form = new URLSearchParams({
    "Request.Supermarket": "Tesco",
    "Request.WeeklyBudget": "70",
    "Request.HouseholdSize": "2",
    "Request.PlanDays": "7",
    "Request.CookDays": "7",
    "Request.MealsPerDay": "3",
    "Request.DietaryModes": "Balanced",
    "Request.PreferQuickMeals": "true",
    "__RequestVerificationToken": session.token
  });
  form.append("Request.SelectedMealTypes", "Breakfast");
  form.append("Request.SelectedMealTypes", "Lunch");
  form.append("Request.SelectedMealTypes", "Dinner");
  const started = performance.now();
  const response = await fetch(`${baseUrl}/projects/aisle-pilot`, {
    method: "POST",
    redirect: "manual",
    headers: {
      "content-type": "application/x-www-form-urlencoded",
      "cookie": session.cookie,
      "user-agent": `AislePilot-LoadTest/${session.index}`,
      "x-forwarded-proto": "https"
    },
    body: form
  });
  const elapsedMs = performance.now() - started;
  const body = await response.text();
  if (response.status !== 200 || body.length < 1000) {
    throw new Error(`Plan POST failed with HTTP ${response.status} and ${body.length} response bytes.`);
  }
  return elapsedMs;
};

if (!Number.isInteger(virtualUsers) || virtualUsers < 1 || virtualUsers > 32) {
  throw new Error("AISLEPILOT_LOAD_VUS must be between 1 and 32.");
}
if (!Number.isInteger(warmRequests) || warmRequests < virtualUsers || warmRequests > 200) {
  throw new Error("AISLEPILOT_LOAD_WARM_REQUESTS must be between the VU count and 200.");
}

const sessions = await Promise.all(Array.from({ length: virtualUsers }, (_, index) => createSession(index + 1)));
const coldDurations = await Promise.all(sessions.map(postPlan));
const warmDurations = [];
for (let offset = 0; offset < warmRequests; offset += virtualUsers) {
  const batch = sessions.slice(0, Math.min(virtualUsers, warmRequests - offset));
  warmDurations.push(...await Promise.all(batch.map(postPlan)));
}

const coldP95 = percentile(coldDurations, 95);
const warmP95 = percentile(warmDurations, 95);
const summary = {
  baseUrl,
  virtualUsers,
  coldSamples: coldDurations.length,
  warmSamples: warmDurations.length,
  coldP50Ms: Math.round(percentile(coldDurations, 50)),
  coldP95Ms: Math.round(coldP95),
  warmP50Ms: Math.round(percentile(warmDurations, 50)),
  warmP95Ms: Math.round(warmP95),
  warmP95LimitMs,
  coldP95LimitMs
};
console.log(JSON.stringify(summary, null, 2));

if (coldP95 >= coldP95LimitMs || warmP95 >= warmP95LimitMs) {
  process.exitCode = 1;
}
