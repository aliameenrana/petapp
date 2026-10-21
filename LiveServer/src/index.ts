// PetApp live service: the catalog the app polls, the packages it downloads,
// and the publish API MiniGameStudio uploads to.
//
//   GET  /catalog.json              games (catalog/games.json) merged with the feed (catalog/feed.json)
//   GET  /games/<sha256>.zip        content-addressed, immutable package
//   PUT  /api/packages/<sha256>     upload one package (auth)
//   POST /api/publish               make uploaded packages the next version of a game (auth)

interface Env {
  PACKAGES: R2Bucket;
  PUBLISH_TOKEN?: string;
}

interface PlatformPackage {
  platform: string;
  url: string;
  sha256: string;
  size: number;
}

interface CatalogGame {
  id: string;
  title: string;
  version: number;
  sdk: number;
  packages: PlatformPackage[];
}

interface Registry {
  games: CatalogGame[];
}

const REGISTRY_KEY = "catalog/games.json";
const FEED_KEY = "catalog/feed.json";
const PLATFORMS = new Set(["ios", "osx"]);
const GAME_ID = /^[a-z0-9][a-z0-9-]{0,63}$/;
const SHA256 = /^[0-9a-f]{64}$/;
const PACKAGE_PATH = /^\/games\/([0-9a-f]{64})\.zip$/;
const UPLOAD_PATH = /^\/api\/packages\/([0-9a-f]{64})$/;
const MAX_PACKAGE_BYTES = 90 * 1024 * 1024;

export default {
  async fetch(request, env): Promise<Response> {
    const path = new URL(request.url).pathname;
    try {
      if (request.method === "GET" && path === "/catalog.json") return await catalog(env);

      const download = PACKAGE_PATH.exec(path);
      if (request.method === "GET" && download) return await getPackage(env, download[1]);

      if (path.startsWith("/api/")) {
        if (!env.PUBLISH_TOKEN) return json({ error: "publishing is not configured: set the PUBLISH_TOKEN secret" }, 503);
        if (!(await authorized(request, env.PUBLISH_TOKEN))) return json({ error: "invalid publish token" }, 401);

        const upload = UPLOAD_PATH.exec(path);
        if (request.method === "PUT" && upload) return await putPackage(request, env, upload[1]);
        if (request.method === "POST" && path === "/api/publish") return await publish(request, env);
      }
      return json({ error: "not found" }, 404);
    } catch (e) {
      console.error(e);
      return json({ error: "internal error" }, 500);
    }
  },
} satisfies ExportedHandler<Env>;

async function catalog(env: Env): Promise<Response> {
  const [registry, feed] = await Promise.all([
    readJson<Registry>(env, REGISTRY_KEY, { games: [] }),
    readJson<{ feed: unknown[] }>(env, FEED_KEY, { feed: [] }),
  ]);
  return json({ games: registry.value.games, feed: feed.value.feed });
}

async function getPackage(env: Env, sha256: string): Promise<Response> {
  const object = await env.PACKAGES.get(packageKey(sha256));
  if (!object) return json({ error: "package not found" }, 404);
  return new Response(object.body, {
    headers: {
      "Content-Type": "application/zip",
      "Content-Length": String(object.size),
      ETag: object.httpEtag,
      // Safe to cache forever: the URL is the content's hash.
      "Cache-Control": "public, max-age=31536000, immutable",
    },
  });
}

async function putPackage(request: Request, env: Env, sha256: string): Promise<Response> {
  const declared = Number(request.headers.get("Content-Length") ?? "0");
  if (!declared || declared > MAX_PACKAGE_BYTES) return json({ error: `package must be 1..${MAX_PACKAGE_BYTES} bytes` }, 413);

  const key = packageKey(sha256);
  const existing = await env.PACKAGES.head(key);
  if (existing) return json({ sha256, size: existing.size });

  const body = await request.arrayBuffer();
  try {
    // R2 rejects the write if the bytes don't hash to the name, so a package URL can't lie about its content.
    await env.PACKAGES.put(key, body, { sha256, httpMetadata: { contentType: "application/zip" } });
  } catch (e) {
    return json({ error: `package rejected: ${e instanceof Error ? e.message : String(e)}` }, 400);
  }
  return json({ sha256, size: body.byteLength }, 201);
}

async function publish(request: Request, env: Env): Promise<Response> {
  let body: { id?: unknown; title?: unknown; sdk?: unknown; packages?: unknown };
  try {
    body = await request.json();
  } catch {
    return json({ error: "body must be JSON" }, 400);
  }
  const { id, title, sdk, packages } = body;
  if (typeof id !== "string" || !GAME_ID.test(id)) return json({ error: "id must be lowercase letters, digits and dashes" }, 400);
  if (typeof title !== "string" || !title.trim()) return json({ error: "title is required" }, 400);
  if (typeof sdk !== "number" || !Number.isInteger(sdk) || sdk < 1) return json({ error: "sdk must be a positive integer" }, 400);
  if (!Array.isArray(packages) || packages.length === 0) return json({ error: "packages is required" }, 400);

  const resolved: PlatformPackage[] = [];
  const seen = new Set<string>();
  for (const p of packages as { platform?: unknown; sha256?: unknown }[]) {
    if (typeof p.platform !== "string" || !PLATFORMS.has(p.platform) || seen.has(p.platform))
      return json({ error: `platform must be one of ${[...PLATFORMS].join(", ")}, each at most once` }, 400);
    if (typeof p.sha256 !== "string" || !SHA256.test(p.sha256)) return json({ error: `${p.platform}: sha256 must be 64 hex characters` }, 400);
    const stored = await env.PACKAGES.head(packageKey(p.sha256));
    if (!stored) return json({ error: `${p.platform}: upload the package before publishing it` }, 409);
    seen.add(p.platform);
    resolved.push({ platform: p.platform, url: packageKey(p.sha256), sha256: p.sha256, size: stored.size });
  }

  // Optimistic concurrency: only replace the registry this publish read, so two publishes can't both claim a version.
  for (let attempt = 0; attempt < 3; attempt++) {
    const { value: registry, etag } = await readJson<Registry>(env, REGISTRY_KEY, { games: [] });
    const current = registry.games.find((g) => g.id === id);
    const entry: CatalogGame = { id, title: title.trim(), version: (current?.version ?? 0) + 1, sdk, packages: resolved };
    const games = current ? registry.games.map((g) => (g.id === id ? entry : g)) : [...registry.games, entry];
    const written = await env.PACKAGES.put(REGISTRY_KEY, JSON.stringify({ games }, null, 2), {
      onlyIf: etag ? { etagMatches: etag } : undefined,
      httpMetadata: { contentType: "application/json" },
    });
    if (written) return json({ version: entry.version });
  }
  return json({ error: "the catalog changed during publish; try again" }, 409);
}

// Hashing both sides first gives equal-length inputs, so the comparison leaks neither content nor length.
async function authorized(request: Request, token: string): Promise<boolean> {
  const header = request.headers.get("Authorization") ?? "";
  const given = header.startsWith("Bearer ") ? header.slice(7) : "";
  const encoder = new TextEncoder();
  const [a, b] = await Promise.all([
    crypto.subtle.digest("SHA-256", encoder.encode(given)),
    crypto.subtle.digest("SHA-256", encoder.encode(token)),
  ]);
  return crypto.subtle.timingSafeEqual(a, b);
}

async function readJson<T>(env: Env, key: string, fallback: T): Promise<{ value: T; etag?: string }> {
  const object = await env.PACKAGES.get(key);
  if (!object) return { value: fallback };
  return { value: await object.json<T>(), etag: object.etag };
}

function packageKey(sha256: string): string {
  return `games/${sha256}.zip`;
}

function json(data: unknown, status = 200): Response {
  return new Response(JSON.stringify(data), {
    status,
    // The app polls this; a cached answer would hide a new version.
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store" },
  });
}
