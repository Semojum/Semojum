# Figma / 실제 앱 화면 비교

2026-10-28 후속 실행본(app-pass28, 동봉 엔진 v3.4.41). 왼쪽 Figma, 오른쪽 실제 WPF 캡처를 같은 픽셀 크기로 배치했다. 59개 설계 중 55개 대응 화면이 있다(이번에 인식 불가 문자 안내가 추가됐다). 캡처는 `--smoke-test`에서 창을 96 DPI로 배치해 Figma 1x PNG와 1:1로 비교한다(디스플레이 배율 150%에서도 동일). 메뉴는 실제 팝업 6개를 각각 캡처한 뒤 합성했고, 크레딧 안내는 전체 앱 캡처에서 해당 부분을 잘랐다. 메모·답글·꼬리말 관리·점역 양식·알림은 Figma 예시 데이터를 같은 컴포넌트에 넣어 캡처했고, 메인 편집기는 Figma와 같은 선택·캐럿 상태로 캡처했다.

문서 본문·선택·계정·사용량 등은 테스트 데이터다. 전체 크기가 같은 52개도 픽셀 일치를 뜻하지 않는다. 범위 저장·페이지 설정의 높이 증가는 사용자 승인 예외다. 이번에 수정한 화면과 남은 점은 [후속 기록](../figma-alignment-2026-10-28-followup.md)과 [작업 목록](../remaining-work.md)을 참고한다.

| Figma 노드 | 화면 | 참조 / 앱 크기 | 비교 이미지·상태 |
|---|---|---|---|
| [207:2527](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-8) | 마이페이지·사용량 | 1440×1000 / 1440×1000 | [나란히 보기](207-2527.png) |
| [207:2631](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-112) | 메인 편집기 | 1440×1000 / 1440×1000 | [나란히 보기](207-2631.png) |
| [207:2928](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-409) | TXT 편집기 | 1440×1000 / 1440×1000 | [나란히 보기](207-2928.png) |
| [207:3181](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-662) | 상단 메뉴 6종 | 1440×1000 / 1440×1000 | [나란히 보기](207-3181.png) |
| [207:3316](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-797) | 점자 패널·줄번역 | 820×420 / 820×420 | [나란히 보기](207-3316.png) |
| [207:3346](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-827) | 크레딧 소진 안내 띠 | 1440×136 / 1440×136 | [나란히 보기](207-3346.png) |
| [207:3542](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1023) | 분리 점자 창 | 600×230 / 600×230 | [나란히 보기](207-3542.png) |
| [207:3562](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1043) | 태그가 있는 텍스트 패널 | 640×300 / 640×300 | [나란히 보기](207-3562.png) |
| [207:3592](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1073) | 텍스트 상황 메뉴 | 220×154 / 220×154 | [나란히 보기](207-3592.png) |
| [207:3601](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1082) | 메모 입력 | 300×150 / 300×150 | [나란히 보기](207-3601.png) |
| [207:3612](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1093) | 메모·답글 | 340×196 / 340×196 | [나란히 보기](207-3612.png) |
| [207:3634](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1115) | 대체 초안 | 660×270 / 660×270 | [나란히 보기](207-3634.png) |
| [207:3661](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1142) | 열기 | 560×300 / 560×300 | [나란히 보기](207-3661.png) |
| [207:3691](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1172) | 다른 이름으로 저장 | 520×190 / 520×190 | [나란히 보기](207-3691.png) |
| [207:3712](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1193) | 범위 지정 저장 | 520×200 / 520×240 | [나란히 보기](207-3712.png) |
| [207:3739](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1220) | 이동 | 440×250 / 440×250 | [나란히 보기](207-3739.png) |
| [207:3762](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1243) | 찾아 바꾸기 | 440×260 / 440×260 | [나란히 보기](207-3762.png) |
| [207:3793](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1274) | 기본 점역 옵션 | 620×500 / 620×500 | [나란히 보기](207-3793.png) |
| [207:3858](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1339) | 편집용 점자문서 페이지설정 | 480×250 / 480×280 | [나란히 보기](207-3858.png) |
| [207:3893](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1374) | 6점키 설정 | 480×200 / 480×200 | [나란히 보기](207-3893.png) |
| [207:3930](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1411) | 점역 양식 | 460×340 / 460×340 | [나란히 보기](207-3930.png) |
| [207:3966](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1447) | 양식 고르기 | 320×192 / 320×192 | [나란히 보기](207-3966.png) |
| [207:3979](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1460) | 새 양식 저장 | 360×170 / 360×170 | [나란히 보기](207-3979.png) |
| [207:3998](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1479) | 사용자 양식 설정 | 560×520 / 560×520 | [나란히 보기](207-3998.png) |
| [207:4053](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1534) | 사용자 양식 삭제 | 380×170 / 380×170 | [나란히 보기](207-4053.png) |
| [207:4070](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1551) | 꼬리말 입력 | 520×210 / 520×210 | [나란히 보기](207-4070.png) |
| [207:4099](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1580) | 꼬리말 관리 | 560×300 / 560×300 | [나란히 보기](207-4099.png) |
| [207:4143](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1624) | 시각 자료 설명 유형 | 620×470 / 620×470 | [나란히 보기](207-4143.png) |
| [207:4238](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1719) | 수식 편집기 | 980×462 / 980×462 | [나란히 보기](207-4238.png) |
| [207:4310](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1791) | 적용 규정 | 620×440 / 620×440 | [나란히 보기](207-4310.png) |
| [207:4354](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1835) | 점자 규정 검색 | 720×700 / 720×700 | [나란히 보기](207-4354.png) |
| [207:4413](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1894) | 게시판 목록 | 700×420 / 700×420 | [나란히 보기](207-4413.png) |
| [207:4472](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1953) | 게시판 글쓰기 | 700×420 / 700×420 | [나란히 보기](207-4472.png) |
| [207:4499](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-1980) | 게시글 상세 | 700×420 / 700×420 | [나란히 보기](207-4499.png) |
| [207:4537](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2018) | 알림 | 400×230 / 400×230 | [나란히 보기](207-4537.png) |
| [207:4566](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2047) | 운영자 콘솔 | 700×330 / — | 대응 화면 미구현 |
| [207:4622](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2103) | 운영자 답변 등록 | 440×240 / — | 대응 화면 미구현 |
| [207:4641](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2122) | 도움말 | 900×320 / 900×320 | [나란히 보기](207-4641.png) |
| [207:4675](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2156) | 세모점 정보 | 440×230 / 440×230 | [나란히 보기](207-4675.png) |
| [207:4702](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2183) | 작업 파일 없음 | 420×260 / 420×260 | [나란히 보기](207-4702.png) |
| [207:4708](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2189) | 변환 진행 | 420×160 / 420×160 | [나란히 보기](207-4708.png) |
| [207:4717](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2198) | 지원하지 않는 파일 | 440×180 / 440×180 | [나란히 보기](207-4717.png) |
| [207:4732](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2213) | 100MB 초과 | 440×180 / 440×180 | [나란히 보기](207-4732.png) |
| [207:4747](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2228) | 암호 PDF | 440×210 / — | 대응 화면 미구현 |
| [207:4765](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2246) | 부분 변환 실패 | 520×180 / 520×180 | [나란히 보기](207-4765.png) |
| [207:4780](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2261) | 스캔 PDF 확인 | 440×180 / — | 대응 화면 미구현 |
| [207:4795](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2276) | 변환 실패 | 520×190 / 520×190 | [나란히 보기](207-4795.png) |
| [207:4812](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2293) | 연결 끊김 | 520×180 / 520×180 | [나란히 보기](207-4812.png) |
| [207:4827](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2308) | 수동 점자 덮어쓰기 | 520×190 / 520×190 | [나란히 보기](207-4827.png) |
| [207:4844](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2325) | 인식 불가 문자 | 520×180 / 520×180 | [나란히 보기](207-4844.png) |
| [207:4859](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2340) | 엔진 응답 오류 | 440×180 / 440×180 | [나란히 보기](207-4859.png) |
| [207:4874](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2355) | 읽기 전용 파일 | 520×180 / 520×180 | [나란히 보기](207-4874.png) |
| [207:4889](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2370) | 블록 합치기 | 440×180 / 440×180 | [나란히 보기](207-4889.png) |
| [207:4904](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2385) | 로그인 거절 | 440×180 / 440×180 | [나란히 보기](207-4904.png) |
| [207:4919](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2400) | 오프라인 | 520×180 / 520×180 | [나란히 보기](207-4919.png) |
| [207:4934](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2415) | 크레딧 소진 | 560×210 / 560×210 | [나란히 보기](207-4934.png) |
| [207:4951](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2432) | 저장 실패 | 520×190 / 520×190 | [나란히 보기](207-4951.png) |
| [207:4966](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2447) | 미저장 확인 | 440×180 / 440×180 | [나란히 보기](207-4966.png) |
| [207:4983](https://www.figma.com/design/GTexdXykSRobKPUCDjqnxB?node-id=1-2464) | 잘못된 범위 | 440×180 / 440×180 | [나란히 보기](207-4983.png) |

## 검사 로그

- [WPF 159개](checks.txt)
- [코어·동봉 엔진 80/80 passed](core.txt)
- [화면별 반영·남은 차이](screen-status.json)

원격 API 검사는 이번 캡처에서 재실행하지 않았다. 과거 실서버 검사 및 삭제 확인은 QA 기록에 별도로 표시했다.
