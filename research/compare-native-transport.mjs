// Compare the same anonymous search over Node transport and the pinned signer.
import { registerHooks } from 'node:module';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
registerHooks({ resolve(specifier, context, next) {
  if (context.parentURL && specifier.startsWith('.')) {
    for (const suffix of ['', '.ts', '/index.ts']) {
      const candidate = new URL(new URL(specifier, context.parentURL).href + suffix);
      if (candidate.pathname.endsWith('.ts') && existsSync(fileURLToPath(candidate))) return next(candidate.href, context);
    }
  }
  return next(specifier, context);
}});
const { signRequest } = await import('./fanqie-assistant/src/crypto/sign.ts');
const request = JSON.parse(readFileSync(process.argv[2], 'utf8'));
for (const [name, headers] of [['native-signature', request.headers], ['upstream-signature', await signRequest(request.url)]]) {
  const response = await fetch(request.url, { headers, redirect: 'error', credentials: 'omit', signal: AbortSignal.timeout(15000) });
  const body = await response.text();
  let code;
  try { code = JSON.parse(body).code; } catch {}
  console.log(JSON.stringify({ name, status: response.status, bytes: Buffer.byteLength(body), code }));
}
