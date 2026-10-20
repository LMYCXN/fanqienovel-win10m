// Read-only title/author search comparison using the pinned upstream GET signer.
import { registerHooks } from 'node:module';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
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
const baseQuery = { iid:'2187355326270644', device_id:'2187355326004404', ac:'wifi', channel:'43536163a', aid:'1967', app_name:'novelapp', version_code:'70132', version_name:'7.0.1.32', device_platform:'android', os:'android', ssmix:'a', os_version:'10', device_type:'P30', device_brand:'realme', update_version_code:'70132', manifest_version_code:'70132', pv_player:'70132' };
const cases = process.argv.includes('--all')
  ? ['https://fanqienovel.com', 'https://reading.snssdk.com'].flatMap(host => ['1','3'].flatMap(tab => ['神通者','天蚕土豆'].map(query => ({host,tab,query}))))
  : ['神通者','天蚕土豆'].flatMap(query => [{host:'https://fanqienovel.com',tab:'1',query},{host:'https://reading.snssdk.com',tab:'3',query}]);
const results = [];
for (const test of cases) {
  const url = test.host + '/reading/bookapi/search/tab/v?' + new URLSearchParams({...baseQuery, query:test.query, passback:'0', selected_items:'', tab_type:test.tab});
  const response = await fetch(url, { headers:{...await signRequest(url),'User-Agent':'com.dragon.read'}, credentials:'omit', redirect:'error', signal:AbortSignal.timeout(20000) });
  const body = await response.text();
  const result = {...test, status:response.status, bytes:Buffer.byteLength(body)};
  try {
    const json = JSON.parse(body);
    result.code = json.code;
    result.tabs = (json.search_tabs ?? []).map(tab => ({ type:tab.tab_type, title:tab.title, next:tab.passback, more:tab.has_more, cells:(tab.data ?? []).map(cell => ({ type:cell.show_type, keys:Object.keys(cell), books:(cell.book_data ?? []).map(book => ({id:book.book_id,title:book.book_name,author:book.author})), nested:(cell.cell_data ?? []).map(child => ({type:child.show_type,books:(child.book_data ?? []).map(book => ({id:book.book_id,title:book.book_name,author:book.author}))})) })) }));
    const directory = new URL('../artifacts/search-regression/', import.meta.url);
    mkdirSync(directory, {recursive:true});
    writeFileSync(new URL(`${test.host.includes('fanqienovel')?'web':'app'}-${test.tab}-${test.query}.json`, directory), body);
  } catch (error) { result.error = error.message; }
  results.push(result);
  console.log(JSON.stringify(result));
}
writeFileSync(new URL('../artifacts/search-regression/summary.json', import.meta.url), JSON.stringify(results,null,2));
