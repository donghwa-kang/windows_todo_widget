(async () => {
  const deadline = new AbortController();
  const timeout = setTimeout(() => deadline.abort(), 75000);
  // 사용자 새로고침 한 번에만 실행한다. 쿠키나 비밀번호는 앱으로 전달하지 않는다.
  try {
    if (location.origin !== 'https://klas.kw.ac.kr' || location.pathname.includes('/login/'))
      throw new Error('KLAS에 로그인한 뒤 현황 가져오기를 눌러 주세요.');
    let subjects;
    for (let attempt = 0; attempt < 60; attempt++) {
      if (typeof appModule !== 'undefined' && Array.isArray(appModule.atnlcSbjectList)) {
        subjects = Array.from(appModule.atnlcSbjectList);
        if (subjects.length > 0) break;
      }
      await new Promise(resolve => setTimeout(resolve, 250));
    }
    // 로딩 중의 빈 배열을 전체 완료로 해석하지 않는다.
    if (!subjects || subjects.length === 0)
      throw new Error('수강 과목을 확인할 수 없습니다. KLAS 홈의 학기 선택과 로딩 상태를 확인해 주세요.');
    if (subjects.length > 100) throw new Error('수강 과목 수가 예상 범위를 벗어났습니다.');
    const output = [];
    async function read(path, subject) {
      const response = await fetch(path, {
        method: 'POST', credentials: 'same-origin', redirect: 'error',
        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
        body: JSON.stringify({ selectSubj: subject.subj, selectYearhakgi: subject.yearhakgi, selectChangeYn: 'Y' }),
        signal: AbortSignal.any([deadline.signal, AbortSignal.timeout(15000)])
      });
      if (!response.ok) throw new Error('KLAS 조회 실패 (' + response.status + '). 잠시 후 다시 시도해 주세요.');
      const data = await response.json();
      if (!Array.isArray(data)) throw new Error('로그인이 만료되었거나 KLAS 응답 형식이 변경되었습니다.');
      return data;
    }
    for (const subject of subjects) {
      if (typeof subject.subj !== 'string' || typeof subject.yearhakgi !== 'string' || typeof subject.subjNm !== 'string')
        throw new Error('수강 과목 형식을 확인할 수 없습니다.');
      // 한 과목씩 읽어 한 번에 많은 요청을 보내지 않는다.
      const lectures = await read('/std/lis/evltn/SelectOnlineCntntsStdList.do', subject);
      const assignments = await read('/std/lis/evltn/TaskStdList.do', subject);
      output.push({ name: subject.subjNm, semester: subject.yearhakgi,
        lectures: lectures.map(x => ({ evltnSe: x.evltnSe, isonoff: x.isonoff, prog: x.prog, endDate: x.endDate })),
        assignments: assignments.map(x => ({ submityn: x.submityn, expiredate: x.expiredate, reexpiredate: x.reexpiredate })) });
    }
    return JSON.stringify({ ok: true, data: output });
  } catch (error) {
    return JSON.stringify({ ok: false, error: error instanceof Error ? error.message : 'KLAS 조회에 실패했습니다.' });
  } finally { clearTimeout(timeout); }
})()
