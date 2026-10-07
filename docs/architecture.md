# Архитектура

## Общая картина

```
браузер в LAN или через VPN
   │  https://home.vladpolt.com
   ▼
nginx (на хосте home)
   ├── /          → файлы из /var/www/home            (frontend, собранный Vite)
   └── /api/      → http://127.0.0.1:5000             (backend, Kestrel; консоль — WebSocket)
                        │
                        ├── /etc/vm/*.conf               настройки VM: читает и пишет
                        ├── systemctl show/start/stop    состояние VM и действия, через polkit
                        ├── /run/vm-<имя>/vnc.sock       экран VM для консоли
                        ├── /proc, /sys                  нагрузка хоста и каждой VM, температура, мосты
                        └── journalctl -o json           журнал vm@<имя>.service

vm@<имя>.service (root)
   └── /usr/local/sbin/vm-run <имя>   читает /etc/vm/<имя>.conf, проверяет, создаёт tap-«провода»
          └── exec qemu-system-x86_64  сокеты в /run/vm-<имя>/: qmp, qga, serial, vnc
```

- **Фронт** — статическое React-приложение. Данные берёт только из `/api/...`, раз в несколько секунд
  перезапрашивает (`usePoll`). Консоль — noVNC поверх WebSocket.
- **Бэкенд** — один self-contained бинарник `deploy/app/home-backend` (.NET внутри). Отдаёт только JSON
  (и WebSocket консоли), слушает только loopback, работает от пользователя `home-backend` без root,
  в песочнице systemd.
- **VM запускает не бэкенд, а systemd**: `vm@<имя>.service` → `vm-run` → QEMU. Бэкенд только просит systemd
  (start/stop/restart/kill) и правит `.conf`. Если бэкенд упадёт или его остановить, VM этого не заметят.
- **Сборка** делается на компьютере разработчика (`build.ps1`), результат коммитится в `deploy/`.
  Хост ничего не собирает: `update.sh` делает `git pull` и раскладывает файлы.

## Права: кто что может

Бэкенду нужно управлять VM, но не быть root. Поэтому права выданы точечно:

| Что | Как разрешено | Чего нельзя |
|---|---|---|
| start / stop / restart / kill VM | polkit, `deploy/vm/50-home-backend.rules`: только `vm@<имя>.service` и только эти четыре действия | любые другие юниты, enable/disable, правка юнитов |
| удалить VM вместе с диском | тот же polkit: только `start` для `vm-disk-remove@<имя>.service`. Скрипт от root удаляет том `<группа>/<имя>`, только тонкий в пуле `data` и только у остановленной VM | удалить любой другой том |
| кнопка Update | тот же polkit: только `restart` для `home-update.service`. Юнит от root запускает `update.sh` | выбрать, что запускать: команда зашита в юнит, бэкенд может только перезапустить юнит |
| ISO-образы | папка `/var/lib/home-backend/iso` принадлежит бэкенду; он сам скачивает туда файлы по ссылке | подсунуть VM файл хоста: `vm-run` открывает ISO сам и проверяет открытый файл (см. ниже) |
| настройки VM | `/etc/vm` принадлежит `root:home-backend` с правами 0775, в юните `ReadWritePaths=/etc/vm` | писать куда-то ещё: `ProtectSystem=strict` |
| консоль | `vnc.sock` после старта VM получает группу `home-backend` и права 0660 (`ExecStartPost` в `vm@.service`) | `qmp.sock`, `qga.sock`, `console.sock`: 0600, только root. QMP умеет почти всё, вплоть до чтения файлов хоста |
| журнал VM | группа `systemd-journal` | — |

**`.conf` пишет непривилегированный бэкенд, а читает root.** Поэтому `vm-run` файл не исполняет
(не `source`), а разбирает построчно и проверяет каждое значение по тем же правилам, что и бэкенд
(`VmConfigFile.cs`): имя, число ядер и памяти, формат MAC и моста, не больше 8 карт. А диск обязан быть
тонким томом LVM в пуле `data`. Так даже взломанный бэкенд не подсунет VM корневой раздел хоста.
Путь к диску бэкенд при изменении настроек не трогает. У новой VM диск всегда `/dev/<группа>/<имя>`, и создаёт
его не бэкенд, а `vm-run` при первом запуске, причём только том с именем VM (`DISK_SIZE` ГиБ, тонкий, в пуле).

**ISO лежат в папке бэкенда, а читает их root.** Поэтому `vm-run` не передаёт QEMU путь, а открывает файл сам
(дескриптор 3, QEMU читает его через `/proc/self/fd/3`) и проверяет уже открытый файл: обычный, лежит ровно
в `/var/lib/home-backend/iso`, принадлежит `home-backend`. Симлинк на `/dev/home/root`, жёсткая ссылка на файл root
или подмена файла между проверкой и открытием не проходят.

## Формат `/etc/vm/<имя>.conf`

```
# VM "router", run by vm@router.service. After editing: systemctl restart vm@router
CPUS=2
MEMORY=2048
DISK=/dev/home/router
NET=br-lan BC:24:11:14:4D:CD
NET=br-wan BC:24:11:C7:E4:7B
AUTOSTART=yes
```

| Ключ | Значение | Правило |
|---|---|---|
| `CPUS` | число виртуальных ядер | 1–64 |
| `MEMORY` | память, МиБ | 128–262144 |
| `DISK` | `/dev/<группа>/<том>` | тонкий том в пуле `data` (проверяет `vm-run`) |
| `NET` | `<мост> <MAC>`, по строке на карту, в порядке слотов | мост существует, MAC не multicast и не повторяется, до 8 карт |
| `AUTOSTART` | `yes` / `no` | `vm-autostart.service` при загрузке запускает VM с `yes` |
| `DISK_SIZE` | ГиБ, необязательно | если тома ещё нет, `vm-run` создаёт его такого размера (только том с именем VM) |
| `CDROM` | имя файла `.iso`, необязательно | ISO из `/var/lib/home-backend/iso` в CD-приводе; грузится, если диск пустой |

Пустые строки и строки с `#` пропускаются. Бэкенд переписывает файл целиком (временный файл + rename),
так что свои комментарии в нём не живут.

Изменения железа применяются при следующем старте VM, как в Proxmox. Карта N внутри гостя — это слот N:
пока MAC тот же, гость видит «ту же» карту (`.link`-файлы роутера привязывают имена `ens18`/`ens19` к MAC).

## Бэкенд: `backend/HomeBackend`

```
Program.cs                 CLI-команда (set-password) или веб-сервер
Hosting/
  HomeBackendServices.cs   AddHomeBackend(): конфиг + регистрация всех фич
  HomeBackendPipeline.cs   UseHomeBackend(): middleware по порядку + все эндпоинты
Configuration/
  HomeBackendOptions.cs    секция HomeBackend из /etc/home-backend/config.json, значения по умолчанию
Security/
  PasswordHash.cs          PBKDF2-хеш пароля
  NetworkAllowlist.cs      запросы не из AllowedNetworks обрываются без ответа
  SecurityHeaders.cs       nosniff, DENY, no-referrer, CSP
Api/
  ApiJsonContext.cs        список всех типов, которые API (де)сериализует
  ErrorResponse.cs         тело ответа 400: {"error": "..."}
  HostCommandFailure.cs    сбой systemctl/journalctl → 502 Bad Gateway
Features/                  см. ниже
Infrastructure/
  ProcessRunner.cs         запуск программ без shell, с таймаутом
  LinuxFiles.cs            чтение однострочных файлов /proc и /sys
  DataSources.cs           AddDataSource<>(): Linux-реализация или мок
  MockClock.cs             общее фейковое время загрузки для моков
Cli/                       home-backend hash-password / set-password
```

### Фичи

| Фича | Страница | Эндпоинты | Откуда данные (Linux / мок) |
|---|---|---|---|
| `Auth` | вход | `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` | хеш из конфига |
| `Host` | Host | `GET /api/host` | собирает из SystemStatus |
| `SystemStatus` | (на Host) | — | `LinuxSystemSource`: /proc, /etc; `CpuMonitor`; `CpuTemperatureMonitor` + `HwmonCpuTemperatureSource` (k10temp/coretemp из /sys/class/hwmon) |
| `Vms` | VMs, страница VM, New VM | `POST /api/vms` (создать), `DELETE /api/vms/{имя}[?disk=true]` (удалить), `GET /api/vms`, `GET /api/vms/{имя}`, `POST /api/vms/{имя}/{start\|shutdown\|reboot\|poweroff}`, `PUT /api/vms/{имя}/config`, `GET /api/vms/{имя}/logs`, `GET /api/vms/{имя}/console` (WebSocket), `GET /api/bridges` | `LinuxVmHost`: /etc/vm, systemctl, /proc/&lt;pid&gt;, /sys/class/net; `MockVmHost` |
| `Isos` | ISO images | `GET /api/isos`, `POST /api/isos` (скачать по ссылке), `DELETE /api/isos/{имя}` (удалить или отменить загрузку) | `IsoStore`: папка `IsoDir`, загрузки в фоне через `.<имя>.part` |
| `Logs` | (вкладка Logs у VM) | — | `JournalctlSource`: `journalctl -o json -u vm@<имя>.service` |
| `Update` | Settings (шестерёнка в шапке) | `GET /api/host/update`, `POST /api/host/update` | `SystemctlUpdateRunner`: `systemctl restart/show home-update.service` + его журнал; `MockUpdateRunner` |

Внутри `Vms`:

- **`VmConfigFile`** — формат `.conf`: разбор, запись, проверка. Те же правила, что в `vm-run`.
- **`SystemctlVmUnits`** — какие команды `systemctl` за каким действием и разбор `systemctl show`:

  | Действие | Команда | Что происходит |
  |---|---|---|
  | Start | `start vm@<имя>` | запуск |
  | Shut down | `stop --no-block` | `ExecStop` «нажимает кнопку питания» через QMP и ждёт гостя до 150 с |
  | Reboot | `restart --no-block` | то же выключение, потом запуск. Так подхватываются новые настройки |
  | Power off | `kill --signal=TERM`, затем `stop --no-block` | выдернуть шнур: на SIGTERM QEMU сразу бросает гостя и выходит с кодом 0, поэтому VM становится Stopped, а не Failed. `stop` — страховка, если QEMU не отреагирует |

- **`VmMonitor`** (`BackgroundService`) раз в 2 секунды перечитывает конфиги, состояние юнитов и
  для запущенных VM `/proc/<pid>/stat` и `statm` процесса QEMU. CPU % считается от ядер самой VM
  (100 % = все её vCPU заняты), память — RSS процесса QEMU. Эндпоинты отдают последний снимок,
  а после действия или сохранения бэкенд обновляет его сразу.
- **`VmConsole`** — мост WebSocket ⇄ unix-сокет `vnc.sock`. Байты VNC идут как есть (подпротокол `binary`),
  протокол разбирает noVNC в браузере. VM не запущена → 404, сокета нет → 502. Каждое направление
  закрывается само: закрылась VM — браузер получает нормальное закрытие, закрыл браузер — сокет к VM закрывается.

### Фоновые замеры

CPU — это скорость, нужны два замера с паузой. Поэтому `CpuMonitor` и `VmMonitor` читают счётчики каждые
2 секунды, а `CpuTemperatureMonitor` — раз в 5 секунд. Эндпоинты просто отдают последнее значение.

### Путь запроса

Порядок задан в `Hosting/HomeBackendPipeline.cs`:

1. **ForwardedHeaders** — берёт IP клиента из `X-Forwarded-For`, который ставит nginx. Заголовку верит
   только от loopback.
2. **NetworkAllowlist** — если IP не из `AllowedNetworks` (LAN и VPN), соединение обрывается без ответа.
3. **SecurityHeaders**.
4. **RateLimiter** — для входа: не больше 10 попыток за 5 минут с одного IP.
5. **Authentication** — cookie `home-backend`, живёт 7 дней, продлевается при использовании.
6. **Authorization** — всё под `/api`, кроме login и logout, требует входа. Без сессии ответ 401.
7. **WebSockets** — для консоли; та же cookie, тот же allowlist.
8. **Эндпоинт**. Если systemctl или journalctl упали, ответ 502 с текстом ошибки.

### Trimming: что нельзя делать

Бинарник публикуется trimmed (`Properties/PublishProfiles/Home.pubxml`), поэтому рефлексии быть не должно:

- JSON только через source generation: новый тип запроса или ответа нужно добавить в `Api/ApiJsonContext.cs`;
- эндпоинты собирает Request Delegate Generator, конфиг — binding generator;
- анализаторы trimming включены в обычной сборке, так что `dotnet build` сразу покажет предупреждение.
  Сборка должна быть без предупреждений.

Подвох minimal API: метод-обработчик с единственным параметром `HttpContext` попадает в перегрузку
`RequestDelegate`, и его результат молча выбрасывается (так был бы сломан logout). Для таких случаев
используйте лямбду (см. `AuthFeature`); компилятор предупреждает об этом (ASP0016).

## Фронт: `frontend/src`

```
main.tsx                  тема до первого рендера, глобальные стили, <App/>
app/
  App.tsx                 вход или роутер
  routes.tsx              вкладки: VMs и Host (путь, заголовок, иконка, что рендерить)
  router.tsx              react-router: вкладки + /vms/:имя внутри Layout, неизвестный путь → /vms
  Layout.tsx              шапка + страница текущего пути (<Outlet/>) + нижние вкладки
  AppHeader.tsx           шапка: бренд, вкладки (на широком экране), ссылка на роутер, тема, выход
  NavTabs.tsx, ThemeToggle.tsx, theme.ts
features/
  auth/                   LoginPage, useAuthState, api.ts
  settings/               SettingsPage = UpdateCard: кнопка Update с подтверждением, статус и вывод update.sh
  host/                   HostPage = HostStats (CPU, температура, память, диск, аптайм) + HostCard
  isos/                   IsosPage: скачать ISO по ссылке, ход загрузок, список и удаление
  vms/
    VmsPage.tsx           кнопки New VM и ISO images, карточки VM со статусом и нагрузкой
    NewVmPage.tsx         новая VM (/vms/new): имя, ядра, память, размер диска, ISO, карты; «запустить и открыть консоль»
    VmDelete.tsx          удаление остановленной VM; с диском — только после ввода её имени
    NetsEditor.tsx, CdromField.tsx   общие части форм новой VM и настроек
    VmPage.tsx            одна VM: кнопки действий + вкладки ?tab=summary|console|settings|logs
    VmActions.tsx         Start / Shut down / Reboot / Power off, с подтверждением для прерывающих
    VmSummary.tsx, VmMeters.tsx
    VmSettingsForm.tsx    ядра, память, сетевые карты, автозапуск; после сохранения предлагает перезагрузку
    VmConsole.tsx         noVNC (грузится только при открытии вкладки): масштаб, Ctrl+Alt+Del, полный экран,
                          строка ввода для телефона (буквы уходят нажатиями клавиш)
    VmKeys.tsx            панель клавиш под экраном: Esc, Tab, стрелки (повтор при удержании), Home/End,
                          PgUp/PgDn, F1–F12, залипающие Ctrl/Alt/Shift для следующей клавиши или строки
    VmLogs.tsx            журнал vm@<имя>.service
    settings.ts, vmState.ts, keysyms.ts, logLevel.ts   чистые функции с тестами
shared/                   HTTP-клиент, usePoll, сессия, форматирование, UI-кит
styles/                   tokens.css (цвета тем), base.css, index.css
```

Правила:

- **Страница = папка в `features/`**: компонент страницы, `api.ts` (типы ответа + функции запроса),
  её собственные компоненты, чистые функции с тестами (`*.test.ts` рядом), её CSS.
- **`shared/` не знает о фичах**, фичи не знают об `app/`. Фича может брать типы и запросы другой фичи
 , но не её компоненты.
- Импорты через алиас `@/`: `@/shared/ui`, а не `../../shared/ui`.
- **Маршрутизация** — react-router с настоящими путями (`/vms`, `/vms/router?tab=console`, `/host`). Файл один,
  `index.html`: nginx отдаёт его на любой путь, которого нет на диске (`try_files`, см. deployment.md),
  а роутер выбирает страницу по пути. Шапка при переходе не перерисовывается, меняется только страница.
- **Данные** только через `usePoll`. Он сам разлогинит на 401, а ошибку и последние данные вернёт странице.
  Вернувшись на вкладку, страница сразу показывает прошлые данные и тут же их обновляет.
- **Каркас сразу.** Страница рендерит всю разметку с первого кадра, а на месте ещё не пришедших значений
  стоят `<Skeleton/>` (серые полоски; для таблиц `<SkeletonRows/>`). Пришли данные — полоски заменились
  значениями, раскладка не прыгает. Никаких «Loading…» вместо страницы.
- **CSS** — обычные глобальные классы. Глобальные стили (`styles/index.css`: токены → база → UI-кит)
  грузятся первыми, стили фич подключаются их компонентами и идут после. Поэтому, например, `.login-card`
  может переопределить `padding` у `.card`. Цвета только через переменные из `tokens.css`, тогда тёмная
  тема работает сама.
- **Тема** — атрибут `<html data-theme>`, по умолчанию тёмная, выбор хранится в `localStorage`.

## Рецепты

### Добавить действие над VM

1. Значение в `enum VmAction` (`Features/Vms/VmState.cs`) и команды в `SystemctlVmUnits.ActionCommands`.
2. Если нужен новый глагол systemctl — добавить его в `deploy/vm/50-home-backend.rules`, иначе polkit откажет.
3. Фронт: `features/vms/vmState.ts` (`availableActions`, `actionLabels`, `confirmText`) и тест рядом.

### Добавить ключ в `.conf`

Три места должны совпадать: `VmConfigFile.cs` (разбор, запись, проверка + тест), `deploy/vm/vm-run`
(разбор и проверка, иначе VM не стартует с «unknown key») и таблица выше. Если ключ должна менять
морда — ещё `VmSettingsRequest` в `VmsFeature.cs` и форма `VmSettingsForm.tsx`.

### Добавить настройку бэкенда

Свойство в `Configuration/HomeBackendOptions.cs`; списки по умолчанию задаются в `WithDefaults()`
(массивы из разных источников конфига сливаются по индексу). Затем `deploy/config.example.json` и таблица
в [deployment.md](deployment.md).
