// Node 24+. Default mode is GET only. --free-content registers one anonymous
// device and one content key. No mode activates membership or replaces devices.
import { registerHooks } from 'node:module';
import { existsSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (context.parentURL && specifier.startsWith('.')) {
      const base = new URL(specifier, context.parentURL);
      for (const suffix of ['', '.ts', '/index.ts']) {
        const candidate = new URL(base.href + suffix);
        if (candidate.pathname.endsWith('.ts') && existsSync(fileURLToPath(candidate))) {
          return nextResolve(candidate.href, context);
        }
      }
    }
    return nextResolve(specifier, context);
  },
});

globalThis.unsafeWindow = { fetch: globalThis.fetch, XMLHttpRequest: undefined };
const { signRequest } = await import('./fanqie-assistant/src/crypto/sign.ts');
const { defaultConfig } = await import('./fanqie-assistant/src/config.ts');
const { decryptChapter } = await import('./fanqie-assistant/src/crypto/content.ts');

const report = {
  checkedAt: new Date().toISOString(),
  node: process.version,
  sourceCommit: 'b62ec5dbe6149a93bca249949549c805ac7da424',
  policy: 'GET only; no cookies, registration, premium activation, device replacement, or retries; response bodies omitted',
  offline: {},
  probes: [],
};
const bookId = '7069948840148732967';
const query = extra => new URLSearchParams({
  iid: defaultConfig.install_id, device_id: defaultConfig.device_id,
  ac: 'wifi', channel: '43536163a', aid: '1967', app_name: 'novelapp',
  version_code: '70132', version_name: '7.0.1.32', device_platform: 'android',
  os: 'android', ssmix: 'a', os_version: '10', device_type: 'P30',
  device_brand: 'realme', update_version_code: '70132',
  manifest_version_code: '70132', pv_player: '70132', ...extra,
}).toString();

const started = performance.now();
const signed = await signRequest('https://fanqienovel.com/reading/bookapi/search/tab/v?' + query({ query: '部族荣光' }));
report.offline.signature = {
  generated: true, elapsedMs: Math.round(performance.now() - started),
  headerLengths: Object.fromEntries(Object.entries(signed).map(([k, v]) => [k, v.length])),
  meaning: 'Local generation only; does not establish server acceptance',
};
const keyBytes = crypto.getRandomValues(new Uint8Array(16));
const iv = crypto.getRandomValues(new Uint8Array(16));
const key = await crypto.subtle.importKey('raw', keyBytes, 'AES-CBC', false, ['encrypt']);
const plaintext = '<p>本地合成测试：Lumia 阅读器</p>';
const encrypted = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-CBC', iv }, key, new TextEncoder().encode(plaintext)));
const payload = new Uint8Array(16 + encrypted.length);
payload.set(iv); payload.set(encrypted, 16);
report.offline.syntheticContent = {
  passed: await decryptChapter(Buffer.from(payload).toString('base64'), {}, { key_info: { key: keyBytes.buffer } }) === plaintext,
  meaning: 'Synthetic fixture with a freshly generated local key; no service content or service key used',
};

async function probe(name, url, signedRequest = false) {
  const entry = { name, endpoint: new URL(url).origin + new URL(url).pathname, signed: signedRequest };
  const start = performance.now();
  let json;
  try {
    const headers = signedRequest ? await signRequest(url) : {};
    if (new URL(url).hostname === 'reading.snssdk.com') headers['User-Agent'] = 'com.dragon.read';
    const response = await fetch(url, { headers, credentials: 'omit', redirect: 'error', signal: AbortSignal.timeout(8000) });
    const body = await response.text();
    Object.assign(entry, { httpStatus: response.status, bytes: Buffer.byteLength(body), contentType: response.headers.get('content-type') });
    try { json = JSON.parse(body); } catch { /* Record non-JSON without storing the body. */ }
    if (json) {
      entry.code = json.code;
      entry.message = String(json.message ?? json.msg ?? '').slice(0, 120);
      entry.topKeys = Object.keys(json);
      if (json.data && typeof json.data === 'object') entry.dataKeys = Object.keys(json.data).slice(0, 35);
      if (json.search_tabs) entry.searchCells = json.search_tabs.map(t => ({ tabType: t.tab_type, cells: t.data?.length ?? 0 }));
      if (json.data?.allItemIds) entry.catalogChapters = json.data.allItemIds.length;
      if (json.data?.item_data_list) entry.catalogChapters = json.data.item_data_list.length;
      if (json.data?.content !== undefined) {
        entry.contentCharacters = String(json.data.content).length;
        entry.contentInvalid = json.data.content === 'Invalid';
        entry.keyVersionPresent = json.data.key_version !== undefined;
      }
    }
  } catch (error) {
    entry.error = error.message;
    entry.causeCode = error.cause?.code;
  }
  entry.elapsedMs = Math.round(performance.now() - start);
  report.probes.push(entry);
  console.log(JSON.stringify(entry));
  await delay(1000);
  return json;
}

if (process.argv.includes('--free-content')) {
  // Isolate a fresh device from the public sample ID. Invoke only the two
  // protocol setup functions, never ensureDevice/registerFullDevice.
  const allowed = new Set([
    'https://i.snssdk.com/service/2/device_register/',
    'https://reading.snssdk.com/reading/crypt/registerkey',
  ]);
  globalThis.GM_xmlhttpRequest = options => {
    const endpoint = new URL(options.url);
    if (options.method !== 'POST' || !allowed.has(endpoint.origin + endpoint.pathname)) {
      throw new Error('Protocol setup endpoint not allowed by this probe');
    }
    const controller = new AbortController();
    fetch(options.url, {
      method: 'POST', headers: options.headers, body: options.data,
      credentials: 'omit', redirect: 'error', signal: AbortSignal.any([controller.signal, AbortSignal.timeout(12000)]),
    }).then(async response => {
      const responseText = await response.text();
      report.probes.push({
        name: endpoint.pathname.includes('device_register') ? 'register-one-anonymous-device' : 'register-content-key',
        endpoint: endpoint.origin + endpoint.pathname, method: 'POST',
        httpStatus: response.status, bytes: Buffer.byteLength(responseText),
      });
      options.onload({ status: response.status, statusText: response.statusText, responseText });
    }).catch(error => options.onerror({ status: 0, statusText: error.message }));
    return { abort: () => controller.abort() };
  };
  report.policy = 'One anonymous device and one key registration; one free chapter; no cookies, premium activation, device replacement, or retries; keys and content omitted';
  const { registerDevice, registerKey } = await import('./fanqie-assistant/src/api/device.ts');
  const { default: config } = await import('./fanqie-assistant/src/config.ts');
  const originalLog = console.log;
  try {
    // Upstream setup functions log raw keys. Suppress those logs entirely.
    console.log = () => {};
    const device = await registerDevice();
    const keyInfo = await registerKey(device);
    config.currentConfig = { ...device, key_info: keyInfo };
    report.deviceRegistered = true;
    report.keyRegistered = Boolean(keyInfo.key);
    const catalogResponse = await fetch('https://fanqienovel.com/api/reader/directory/detail?bookId=' + bookId, { credentials: 'omit', signal: AbortSignal.timeout(8000) });
    const catalog = await catalogResponse.json();
    const first = catalog?.data?.chapterListWithVolume?.flat().find(c => c.needPay === 0 && c.isChapterLock === false && !c.isPaidPublication && !c.isPaidStory);
    if (!first) throw new Error('No confirmed free and unlocked sample chapter');
    report.sampleChapterId = String(first.itemId);
    const url = 'https://reading.snssdk.com/reading/reader/full/v?' + query({
      iid: device.install_id, device_id: device.device_id, device_type: device.device_type,
      item_id: String(first.itemId), req_type: '1',
    });
    const j = await probe('fresh-device-free-chapter', url, true);
    if (j?.code !== 0 || !j?.data?.content || j.data.content === 'Invalid') throw new Error('Free chapter not available');
    const plain = await decryptChapter(j.data.content, j.data, config.currentConfig);
    const text = typeof plain === 'string' ? plain : JSON.stringify(plain);
    report.freeChapter = {
      decrypted: Boolean(text), plaintextType: typeof plain,
      plaintextCharacters: text?.length ?? 0,
      containsHanCharacters: /[\u3400-\u9fff]/u.test(text ?? ''),
      htmlParagraphs: text?.match(/<p(?:\s|>)/gi)?.length ?? 0,
      advertisedParagraphs: j.data.paragraphs_num,
      advertisedFreeParagraphs: j.data.free_para_nums,
      advertisedWordCount: j.data.novel_data?.chapter_word_number,
      cryptStatus: j.data.crypt_status,
      compressStatus: j.data.compress_status,
      keyVersionMatches: Number(j.data.key_version) === Number(keyInfo.keyver),
    };
  } catch (error) {
    report.freeContentError = error.message;
  } finally {
    console.log = originalLog;
  }
} else if (!process.argv.includes('--offline')) {
  await probe('web-control', 'https://fanqienovel.com/');
  const catalog = await probe('web-catalog', 'https://fanqienovel.com/api/reader/directory/detail?bookId=' + bookId);
  await probe('signed-web-search', 'https://fanqienovel.com/reading/bookapi/search/tab/v?' + query({ query: '部族荣光', passback: '0', selected_items: '', tab_type: '1' }), true);
  await probe('signed-app-detail', 'https://reading.snssdk.com/reading/bookapi/detail/v?' + query({ book_id: bookId }), true);
  await probe('signed-app-catalog', 'https://reading.snssdk.com/reading/bookapi/directory/all_items/v?' + query({ book_id: bookId }), true);
  const chapters = catalog?.data?.chapterListWithVolume?.flat() ?? [];
  const first = chapters.find(c => c.needPay === 0 && c.isChapterLock === false && !c.isPaidPublication && !c.isPaidStory);
  if (first) {
    report.sampleChapterId = String(first.itemId);
    await probe('web-free-chapter', 'https://fanqienovel.com/api/reader/full?itemId=' + first.itemId);
    await probe('signed-app-free-chapter', 'https://reading.snssdk.com/reading/reader/full/v?' + query({ item_id: String(first.itemId), req_type: '1' }), true);
  } else {
    report.chapterProbeSkipped = 'No chapter confirmed unlocked and unpaid by the returned web catalog';
  }
}

const filename = process.argv.includes('--free-content') ? 'fanqie-free-content-results.json' : process.argv.includes('--offline') ? 'fanqie-offline-results.json' : 'fanqie-probe-results.json';
writeFileSync(new URL(filename, import.meta.url), JSON.stringify(report, null, 2) + '\n');
console.log(JSON.stringify({ ...report, saved: filename }));
