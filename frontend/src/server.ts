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
 * Settings the browser needs at runtime, so one build works in every environment.
 * The Google client id is public: it identifies the app, it does not grant access.
 */
app.get('/app-config.json', (_req, res) => {
  res.set('Cache-Control', 'no-store');
  res.json({ googleClientId: process.env['GOOGLE_CLIENT_ID'] ?? '' });
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
