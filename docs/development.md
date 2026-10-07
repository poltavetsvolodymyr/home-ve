# Разработка

## Что нужно

- .NET 10 SDK
- Node.js 22 LTS (Vite 7 требует Node 20.19+ или 22.12+)
- IDE: `backend/HomeBackend.slnx` открывается в Visual Studio или Rider, `frontend/` — в VS Code
  (подхватит ESLint и Prettier).

## Запуск

Бэкенд с фейковыми данными (`appsettings.Development.json`: мок, порт 5080, пароль `admin`):

```powershell
dotnet run --project backend/HomeBackend --launch-profile "HomeBackend (mock)"
```

Фронт с hot reload, `/api` проксируется на бэкенд выше:

```powershell
cd frontend
npm install
npm run dev          # http://localhost:5173
```

## Проверки

| Что | Команда |
|---|---|
| тесты бэкенда | `dotnet test --solution backend/HomeBackend.slnx` |
| всё по фронту: типы, ESLint, Prettier, тесты | `cd frontend; npm run check` |
| только тесты фронта | `npm test` (или `npm run test:watch`) |
| поправить форматирование | `npm run format` |
| стиль C# | `dotnet format backend/HomeBackend.slnx --verify-no-changes` |

Перед коммитом стоит прогнать обе первые строки. Сборка бэкенда должна быть без предупреждений:
это в том числе предупреждения trimming (см. [architecture.md](architecture.md#trimming-что-нельзя-делать)).

`dotnet test` работает через Microsoft.Testing.Platform (так требует xunit v3 на .NET 10 SDK). Этот режим
включён в `global.json` в корне репозитория.

### Что покрыто тестами

- **Бэкенд, юнит-тесты** (`HomeBackend.Tests/Features`, `Security`): формат `.conf` (разбор, запись,
  все правила проверки), команды systemctl для действий и разбор `systemctl show`, разбор `/proc/<pid>`,
  журнал, /sys/class/hwmon и выбор датчика CPU; хеш пароля; список разрешённых сетей.
- **Бэкенд, интеграционные** (`HomeBackend.Tests/Integration`): настоящее приложение с мок-данными на
  случайном порту loopback. Вход и выход, 401 без сессии, список VM, действия (400 на неизвестное, 404 на
  чужую VM), сохранение настроек с проверкой, журнал VM, обрыв соединения для чужой сети, лимит входа,
  заголовки безопасности. `ConsoleTests` поднимает фейковый VNC-сервер на unix-сокете и гоняет байты
  через настоящий WebSocket в обе стороны; для остановленной VM — 404.
- **Фронт** (`*.test.ts` рядом с кодом): форматирование, плитка температуры, состояния и действия VM,
  проверка формы настроек и случайный MAC, раскладка символов в нажатия клавиш для консоли, уровни журнала.
- **`deploy/vm/vm-run`** автотестов не имеет; при правке прогоните `shellcheck -s sh deploy/vm/*` и
  проверьте вручную на хосте `systemctl restart vm@<имя>` + `journalctl -u vm@<имя> -n 20`.

### Консоль локально

С мок-бэкендом вкладка Console ищет сокет `.data/run/vm-router/vnc.sock` (`HomeBackend:VmRuntimeDir`
в `appsettings.Development.json`). Без него будет 502, это нормально. Чтобы увидеть живой экран на Linux,
достаточно любой QEMU с VNC на этом сокете:

```bash
mkdir -p backend/HomeBackend/.data/run/vm-router
qemu-system-x86_64 -m 128 -display vnc=unix:backend/HomeBackend/.data/run/vm-router/vnc.sock
```

## Соглашения

**Общие:** код и комментарии на английском, документация на русском. Отступы и прочее задаёт
`.editorconfig`. Комментарии объясняют «почему», а не пересказывают код.

**C#:**
- namespace по папке, file-scoped;
- API-модели — `sealed record`, они же JSON (camelCase);
- фича — папка в `Features/` (см. [architecture.md](architecture.md#фичи));
- никакой рефлексии: всё должно переживать trimming.

**TypeScript/React:**
- функциональные компоненты, именованные экспорты, один компонент на файл;
- страница — папка в `src/features/`, общее — в `src/shared/`;
- импорты через `@/`;
- форматирует Prettier (`.prettierrc.json`: без `;`, одинарные кавычки, 120 символов);
- стили — глобальные классы, цвета только из `src/styles/tokens.css`.

## Сборка для хоста

```powershell
.\build.ps1          # Windows
./build.sh           # Linux/macOS, те же шаги
```

Скрипт собирает фронт в `deploy/www`, публикует бэкенд по профилю
`backend/HomeBackend/Properties/PublishProfiles/Home.pubxml` в `deploy/app` (один файл примерно на 16 МБ,
linux-x64, .NET внутри) и добавляет всё в git, включая бит исполнения бинарника. Дальше commit, push
и `update.sh` на хосте, см. [deployment.md](deployment.md).

Каждая сборка кладёт в git новый бинарник. На хосте это не мешает: partial clone
(`--filter=blob:none`) тянет только текущую версию. Но история на GitHub со временем растёт. Если начнёт
мешать, бинарник можно перенести в GitHub Releases или Git LFS.
