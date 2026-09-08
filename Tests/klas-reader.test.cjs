const { readFileSync } = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const script = readFileSync(require('node:path').join(__dirname, '../Platform/KlasRead.js'), 'utf8');
async function run(options = {}) {
  const calls = [];
  const response = await vm.runInNewContext(script, {
    location: { origin: 'https://klas.kw.ac.kr', pathname: options.login ? '/usr/cmn/login/LoginForm.do' : '/std/cmn/frame/Frame.do' },
    appModule: { atnlcSbjectList: options.empty ? [] : [{ subj: 'course-1', subjNm: '과목', yearhakgi: '2026,2' }] },
    AbortController, AbortSignal,
    setTimeout: (callback, delay) => { if (delay === 250) callback(); return 1; }, clearTimeout: () => {},
    fetch: async (path, request) => {
      calls.push({ path, request });
      return { ok: true, json: async () => options.badResponse && calls.length === 2 ? { message: '로그인 만료' } : [] };
    }
  });
  return { result: JSON.parse(response), calls };
}
(async () => {
  const normal = await run();
  assert.equal(normal.result.ok, true);
  assert.equal(normal.calls.length, 2);
  assert.equal(normal.calls[0].request.credentials, 'same-origin');
  assert.equal(JSON.parse(normal.calls[0].request.body).selectYearhakgi, '2026,2');
  assert.equal(normal.result.data[0].name, '과목');
  const partial = await run({ badResponse: true });
  assert.equal(partial.result.ok, false);
  assert.equal(partial.result.data, undefined);
  const login = await run({ login: true });
  assert.equal(login.result.ok, false);
  assert.equal(login.calls.length, 0);
  const empty = await run({ empty: true });
  assert.equal(empty.result.ok, false);
  assert.equal(empty.calls.length, 0);
  console.log('PASS: KLAS 조회 요청·부분 실패·로그인 필요·수강 과목 로딩 실패 (4개 시나리오)');
})().catch(error => { console.error(error); process.exitCode = 1; });
