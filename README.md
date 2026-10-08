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

[PolyForm Noncommercial 1.0.0](LICENSE) с дополнительным разрешением ниже. Коротко: дома, для хобби, учёбы,
некоммерческих организаций, а также частному лицу для собственной работы — бесплатно, в том числе менять и
распространять. Фирме, а также любому, кто на этом зарабатывает (продаёт, встраивает в продукт или устройство,
предоставляет как сервис, ставит клиентам за деньги), нужна коммерческая лицензия: напишите автору через
[GitHub](https://github.com/poltavetsvolodymyr).

Юридически действует английский текст:

> **Additional permission.** An individual — including a freelancer or sole proprietor working on their own
> account — may use this software for their own work, commercial or not, free of charge.
>
> A commercial license is needed for any use by or on behalf of a company or other organization, including
> installing it on computers or servers used by its employees, and for selling it, shipping it in a product or
> device, offering it as a hosted service, or running it for paying clients.

Required Notice: Copyright 2026 Volodymyr Poltavets (https://github.com/poltavetsvolodymyr)

Сторонние части сохраняют свои лицензии: noVNC (MPL-2.0, без изменений, из пакета `@novnc/novnc`,
исходники — https://github.com/novnc/noVNC), React (MIT), lucide-react (ISC), .NET (MIT).
