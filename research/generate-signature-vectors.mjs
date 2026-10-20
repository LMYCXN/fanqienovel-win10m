// Offline fixtures from the pinned upstream implementation; no network access.
import { registerHooks } from 'node:module';
import { existsSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
registerHooks({ resolve(specifier, context, next) {
  if (context.parentURL && specifier.startsWith('.')) {
    const base = new URL(specifier, context.parentURL);
    for (const suffix of ['', '.ts', '/index.ts']) {
      const candidate = new URL(base.href + suffix);
      if (candidate.pathname.endsWith('.ts') && existsSync(fileURLToPath(candidate))) return next(candidate.href, context);
    }
  }
  return next(specifier, context);
}});
const actualCrypto = globalThis.crypto;
Object.defineProperty(globalThis, 'crypto', { value: {
  subtle: actualCrypto.subtle,
  getRandomValues(array) {
    if (array instanceof Uint32Array) array[0] = 0xfedcba98;
    else array.set([0x12, 0x34, 0x56, 0x78]);
    return array;
  },
}});
const { default: sign } = await import('./fanqie-assistant/src/crypto/sign.ts');
const queries = ['', 'device_id=1234567890123456&version_name=7.0.1.32&query=%E9%83%A8%E6%97%8F%E8%8D%A3%E5%85%89', 'device_id=a+b%2Bc&version_name=1%2B2&empty=&long=' + 'x'.repeat(180)];
const rows = [];
for (const query of queries) {
  const h = await sign(query, '', 1792396800);
  rows.push([query, h['x-argus'], h['x-ladon']].join('\t'));
}
writeFileSync(new URL('../tests/signature-vectors.tsv', import.meta.url), rows.join('\n') + '\n');
console.log('Generated 3 deterministic upstream GET signature vectors.');

const { encrypt: encryptTT } = await import('./fanqie-assistant/src/crypto/ttencrypt.ts');
const { gzip } = await import('./fanqie-assistant/src/utils/compress.ts');
const encode = value => Buffer.from(value).toString('base64');
const sample = new TextEncoder().encode('{"magic_tag":"synthetic registration fixture"}');
const compressed = await gzip(sample.buffer);
const random = new Uint8Array(32); random.set([0x12,0x34,0x56,0x78]);
const iv = Uint8Array.from({ length: 16 }, (_, i) => i);
const shared = Uint8Array.from(atob('rCXGfd2POMGzeiNIgo4iLg=='), c => c.charCodeAt(0));
const encrypt = async (data, key) => {
  const imported = await actualCrypto.subtle.importKey('raw', key, 'AES-CBC', false, ['encrypt']);
  return Buffer.concat([iv, Buffer.from(await actualCrypto.subtle.encrypt({ name:'AES-CBC', iv }, imported, data))]);
};
const id = new Uint8Array(16);
new DataView(id.buffer).setBigUint64(0, 1234567890123456789n, true);
const localKey = Uint8Array.from({ length: 16 }, (_, i) => 0xf0 + i);
const html = '<html><body><p>灯塔 &amp; 海岸<span>。</span></p><p>下一行<br />仍在这里。</p></body></html>';
const gzippedHtml = await gzip(html);
const postQuery = 'device_id=1234567890123456789&aid=1967';
const { hash } = await import('./fanqie-assistant/src/crypto/index.ts');
const stub = await hash.md5(compressed);
const postHeaders = await sign(postQuery, stub, 1792396800);
const cryptoRows = [
  ['TT', encode(compressed), encode(random), encode(await encryptTT(sample.buffer))],
  ['DEVICE', '1234567890123456789', encode(iv), encode(await encrypt(id, shared))],
  ['KEY', encode(await encrypt(localKey, shared)), encode(localKey)],
  ['CHAPTER', encode(await encrypt(gzippedHtml, localKey)), encode(localKey), encode(Buffer.from(html))],
  ['POST', postQuery, encode(compressed), postHeaders['x-argus'], postHeaders['x-ladon'], stub],
];
writeFileSync(new URL('../tests/content-vectors.tsv', import.meta.url), cryptoRows.map(row => row.join('\t')).join('\n') + '\n');
console.log('Generated synthetic TT, device/key/chapter AES and POST signature fixtures.');
