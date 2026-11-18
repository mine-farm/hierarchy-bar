# Hierarchy Object Pins

Unity Hierarchy 검색창 아래에 두 개의 빠른 선택 행을 추가하는 Editor 전용 UPM 패키지입니다.

- `World`, `Player` 태그 오브젝트 빠른 선택
- 선택한 씬 오브젝트를 사용자별 즐겨찾기로 등록
- 핀 클릭 시 선택 및 Ping
- 핀 더블 클릭 시 Scene View에서 프레임
- 핀 우클릭 메뉴에서 선택, 프레임, 제거
- 저장하지 않은 씬의 임시 핀과 씬 저장 후 자동 승격 지원

## 요구 사항

- Unity 6000.3 이상
- `World` 태그는 프로젝트 Tag Manager에 등록되어 있어야 합니다.
- `Player`는 Unity 기본 태그를 사용합니다.

이 패키지는 Hierarchy 검색창 아래에 행을 삽입하기 위해 Unity Editor 내부 TreeView 구현을 리플렉션으로 사용합니다. Unity Editor 버전을 변경한 뒤에는 동작을 다시 확인해야 합니다.

## 설치

로컬 체크아웃을 사용할 때 Package Manager에서 다음 경로를 추가합니다.

```text
file:../../hierarchy-bar
```

Git 저장소에서 설치할 때 Package Manager의 **Add package from git URL**에 다음 URL을 사용합니다.

```text
https://github.com/mine-farm/hierarchy-bar.git#v1.0.0
```

## 사용법

1. 활성 씬에서 오브젝트를 선택합니다.
2. Hierarchy의 `☆` 버튼을 눌러 즐겨찾기에 등록합니다.
3. 등록된 버튼을 누르면 해당 오브젝트가 선택됩니다.
4. `World` 또는 `Player` 버튼을 누르면 활성 씬에서 해당 태그를 가진 첫 오브젝트를 선택합니다.

핀 데이터는 각 프로젝트의 `UserSettings/HierarchyObjectPins.asset`에 사용자별로 저장됩니다.
