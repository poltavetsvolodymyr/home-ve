# home-ve

Веб-морда для хоста виртуалок «home» (Debian + QEMU/KVM, без Proxmox). VM описаны файлами `/etc/vm/<имя>.conf`
и запускаются шаблонной службой `vm@<имя>.service`.

Версия 1: состояние хоста (CPU, температура, память, диск), список VM, запуск / выключение / перезагрузка /
жёсткое выключение, правка ядер, памяти, сетевых карт и автозапуска, консоль VM в браузере (noVNC) и журнал VM.
Версия 2: создание VM (диск создаётся при первом запуске, установка с ISO через консоль), удаление VM с диском или
без, ISO-образы со скачиванием по ссылке. Дальше: снапшоты и бэкапы (v3).

## Как это устроено

```
браузер ──► nginx на хосте ──┬─► /var/www/home           фронт: статика (React)
                             └─► /api/ → 127.0.0.1:5000  бэкенд: ASP.NET Core, только API
                                              ├─► /etc/vm/*.conf               настройки VM (читает и пишет)
                                              ├─► systemctl … vm@<имя>          через polkit, только эти юниты
                                              ├─► /run/vm-<имя>/vnc.sock        консоль, WebSocket ⇄ VNC
                                              └─► /proc, /sys, journalctl       нагрузка, температура, журнал

vm@<имя>.service ──► vm-run <имя> ──► qemu-system-x86_64     (root; сам проверяет .conf, ничего не исполняет из него)
```

Бэкенд работает без root, наружу не слушает, пускает только LAN и VPN и только после входа по паролю.
Подробнее в [docs/architecture.md](docs/architecture.md).

## Где что лежит

```
backend/
  HomeBackend/              бэкенд (ASP.NET Core 10, minimal API)
    Program.cs              точка входа: CLI-команда или веб-сервер
    Hosting/                что регистрируется и в каком порядке идут middleware
    Features/<Фича>/        Host, Vms, SystemStatus, Logs, Auth: источник данных (Linux и мок), модели, эндпоинты
    Security/ Api/ Cli/ Configuration/ Infrastructure/   общие части
  HomeBackend.Tests/        тесты бэкенда (xunit)
frontend/
  src/app/                  оболочка: шапка, маршруты, тема
  src/features/<страница>/  vms, host, auth: страница, её запросы к API, компоненты и стили
  src/shared/               HTTP-клиент, usePoll, форматирование, UI-кит
  src/styles/               цвета (токены) и базовые стили
deploy/                     то, что тянет хост: бинарник, собранный фронт, юниты, скрипты
  vm/                       vm-run, qmp, vm@.service, автозапуск, правило polkit
  nginx/                    сайт nginx для home.vladpolt.com
docs/                       архитектура, разработка, выкладка
build.ps1 / build.sh        сборка фронта и бэкенда в deploy/
```

## Быстрый старт (Windows)

Нужны .NET 10 SDK и Node.js 22.

```powershell
dotnet run --project backend/HomeBackend --launch-profile "HomeBackend (mock)"   # API на :5080 с фейковыми VM, пароль admin
cd frontend; npm install; npm run dev                                           # http://localhost:5173 с hot reload
```

Тесты и проверки:

```powershell
dotnet test --solution backend/HomeBackend.slnx   # бэкенд
cd frontend; npm run check                        # фронт: типы, ESLint, Prettier, тесты
```

## Выкладка

```powershell
.\build.ps1                                      # фронт -> deploy/www, бэкенд -> deploy/app/home-backend, всё в git
git commit -m "..."; git push
```

На хосте:

```bash
/opt/home-ve/deploy/update.sh
```

## Документация

- [docs/architecture.md](docs/architecture.md): как устроен код, путь запроса, права, формат `.conf`
- [docs/development.md](docs/development.md): запуск, тесты, линтеры, соглашения
- [docs/deployment.md](docs/deployment.md): первая установка на хост, nginx и сертификат, настройки, откат
- [deploy/README.md](deploy/README.md): шпаргалка для хоста (там лежит только `deploy/`)

## Лицензия

Действует текст в [LICENSE](LICENSE): [PolyForm Noncommercial 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0)
и дополнительное разрешение над ним. Ниже только пересказ.

- **Бесплатно:** дома, для хобби, учёбы и исследований; некоммерческим, образовательным и государственным
  организациям; частному лицу (в том числе фрилансеру или ИП) для собственной работы. Можно менять и распространять.
- **Нужна коммерческая лицензия:** любой фирме, которая зарабатывает (в том числе поставить сотрудникам на рабочие
  компьютеры или себе на серверы), и любому, кто зарабатывает на самом проекте: продаёт его, встраивает в продукт или
  устройство, предоставляет как сервис или ставит клиентам за деньги. Это следует из самой PolyForm Noncommercial:
  такое использование коммерческое, и она его не разрешает. Напишите автору через
  [GitHub](https://github.com/poltavetsvolodymyr).

Правки от других людей принимаются только с согласием на [CONTRIBUTING.md](CONTRIBUTING.md) (галочка в
шаблоне pull request): автор правки сохраняет свои права, а проект может распространяться и под коммерческими
лицензиями.

Сторонние части сохраняют свои лицензии: noVNC (MPL-2.0, без изменений, из пакета `@novnc/novnc`,
исходники — https://github.com/novnc/noVNC), React (MIT), lucide-react (ISC), .NET (MIT).
