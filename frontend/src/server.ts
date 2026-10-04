import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

/** Where the .NET API runs. The browser only talks to this server, which forwards /api. */
const apiUrl = (process.env['API_URL'] ?? 'http://localhost:5080').replace(/\/$/, '');

/**
 * On Cloud Run the API is private and only accepts calls from this service's identity. Its ID
 * token goes in X-Serverless-Authorization, so the user's Firebase token keeps the Authorization
 * header. Unset locally, where the API is reached directly.
 */
const apiAudience = process.env['API_AUDIENCE'];
let serviceToken: { value: string; expiresAt: number } | undefined;

async function getServiceToken(): Promise<string | undefined> {
  if (!apiAudience) return undefined;
  if (serviceToken && serviceToken.expiresAt > Date.now()) return serviceToken.value;
  const res = await fetch(
    'http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity?audience=' +
      encodeURIComponent(apiAudience),
    { headers: { 'Metadata-Flavor': 'Google' } },
  );
  if (!res.ok) throw new Error(`Metadata server returned ${res.status}`);
  // Google ID tokens last an hour; refresh well before that.
  serviceToken = { value: await res.text(), expiresAt: Date.now() + 45 * 60 * 1000 };
  return serviceToken.value;
}

/**
 * Settings the browser needs at runtime, so one build works in every environment.
 * Firebase's web config is public: it identifies the project, it does not grant access.
 */
app.get('/app-config.json', (_req, res) => {
  const projectId = process.env['FIREBASE_PROJECT_ID'] ?? '';
  res.set('Cache-Control', 'no-store');
  res.json({
    firebase: {
      apiKey: process.env['FIREBASE_API_KEY'] ?? '',
      authDomain: process.env['FIREBASE_AUTH_DOMAIN'] || `${projectId}.firebaseapp.com`,
      projectId,
    },
    // Local development only: point the browser at the Firebase Auth emulator, e.g. http://localhost:9099.
    authEmulator: process.env['FIREBASE_AUTH_EMULATOR_URL'] ?? '',
  });
});

/**
 * Forward /api to the backend on the same origin, so the browser needs no CORS and the
 * API address stays a server setting.
 */
app.use('/api', express.raw({ type: '*/*', limit: '1mb' }), async (req, res) => {
  try {
    const headers = new Headers();
    for (const name of ['authorization', 'content-type', 'accept', 'accept-language']) {
      const value = req.headers[name];
      if (typeof value === 'string') headers.set(name, value);
    }
    const token = await getServiceToken();
    if (token) headers.set('x-serverless-authorization', `Bearer ${token}`);
    const hasBody = !['GET', 'HEAD'].includes(req.method) && Buffer.isBuffer(req.body);
    const upstream = await fetch(apiUrl + req.originalUrl, {
      method: req.method,
      headers,
      body: hasBody ? new Uint8Array(req.body) : undefined,
    });
    res.status(upstream.status);
    for (const name of ['content-type', 'location', 'www-authenticate']) {
      const value = upstream.headers.get(name);
      if (value) res.set(name, value);
    }
    res.send(Buffer.from(await upstream.arrayBuffer()));
  } catch {
    res.status(502).json({ title: 'The API is not reachable.', status: 502 });
  }
});

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) => (response ? writeResponseToNodeResponse(response, res) : next()))
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
