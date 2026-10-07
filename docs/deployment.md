# Выкладка на хост «home»

На хосте лежит sparse checkout репозитория в `/opt/home-ve`, в нём только папка `deploy/`:

```
deploy/
  app/home-backend        бэкенд: один self-contained бинарник linux-x64
  www/                    собранный фронт; install.sh копирует его в /var/www/home
  home-backend.service    systemd-юнит бэкенда
  config.example.json     образец /etc/home-backend/config.json
  vm/                     vm-run, qmp, vm@.service, vm-autostart(.service), правило polkit
  nginx/home.conf         сайт nginx
  install.sh              установка и любое обновление (идемпотентный)
  update.sh               git pull + install.sh
```

## Первая установка

Всё на хосте под root (`ssh root@192.168.178.2`).

### 1. Ключ для чтения репозитория

Репо приватный, хосту нужен свой read-only deploy key (ключ роутера к этому репо доступа не даёт):

```bash
ssh-keygen -t ed25519 -f /root/.ssh/home_deploy -N "" -C "home deploy"
cat /root/.ssh/home_deploy.pub
```

Публичный ключ: GitHub → home-ve → Settings → Deploy keys → Add (без «Allow write access»). Потом:

```bash
cat >> /root/.ssh/config <<'CFG'
Host github.com
    IdentityFile /root/.ssh/home_deploy
CFG
```

### 2. Клонировать только `deploy/`

```bash
apt-get install -y git
git clone --filter=blob:none --sparse git@github.com:poltavetsvolodymyr/home-ve.git /opt/home-ve
cd /opt/home-ve && git sparse-checkout set deploy
```

### 3. Установить

```bash
bash /opt/home-ve/deploy/install.sh
```

.NET на хост не ставится. Что делает скрипт:

- ставит недостающее из `qemu-system-x86`, `socat`, `dbus`, `polkitd`. Через D-Bus `systemctl` от обычного пользователя разговаривает с systemd, а в минимальном Debian его может не быть;
- создаёт системного пользователя `home-backend` (без shell и home);
- создаёт `/etc/home-backend/config.json` из образца (права 0640) и спрашивает пароль для морды;
- **переводит старые `.conf`** на новый формат: `NETS="br-lan=… br-wan=…"` → строки `NET=br-lan …`,
  и если `vm@<имя>` был включён (`enable`), пишет `AUTOSTART=yes` и выключает этот `enable`. Теперь при
  загрузке VM запускает `vm-autostart.service`. Старый файл остаётся рядом как `<имя>.conf.bak`;
- ставит `vm-run`, `qmp`, `vm-autostart` в `/usr/local/sbin`, юниты в `/etc/systemd/system`, правило polkit
  в `/etc/polkit-1/rules.d`; `/etc/vm` получает группу `home-backend` и права 0775;
- ставит юнит бэкенда, включает и перезапускает его;
- целиком заменяет `/var/www/home` свежим фронтом.

Работающие VM скрипт не трогает. Новый `vm-run` (и с ним консоль в браузере) VM получит при следующем
перезапуске. Для роутера это ~30 секунд без интернета, так что выбери удобный момент:

```bash
systemctl restart vm@router
ls -l /run/vm-router/      # vnc.sock: srw-rw---- root home-backend; остальные сокеты только root
```

### 4. Имя `home.vladpolt.com`

В Cloudflare → vladpolt.com → DNS → Add record, так же, как для `router`:

| Type | Name | IPv4 | Proxy |
|---|---|---|---|
| A | `home` | `192.168.178.2` | DNS only (серое облако) |

Снаружи по этому адресу ничего нет (это адрес в домашней сети), а дома и через VPN он ведёт на хост.
`rebind-domain-ok=/vladpolt.com/` в dnsmasq роутера уже разрешает такие ответы. Проверка с хоста:
`getent hosts home.vladpolt.com` → `192.168.178.2`.

### 5. Сертификат

Хост получает свой wildcard-сертификат, тем же acme.sh и тем же способом, что роутер. Он не зависит
от роутера: морда хоста нужна именно тогда, когда с роутером что-то не так.

```bash
apt-get install -y --no-install-recommends nginx curl openssl ca-certificates
git clone --depth 1 https://github.com/acmesh-official/acme.sh.git /tmp/acme.sh
cd /tmp/acme.sh
./acme.sh --install --nocron --noprofile --home /opt/acme.sh --config-home /etc/acme.sh --accountemail твой@email
cd / && rm -rf /tmp/acme.sh
chmod 700 /etc/acme.sh
```

Токен и Zone ID берём из `/etc/ddns.conf` роутера (`cat` там) и вставляем на хосте. root на роутер по ssh
с паролем не пускают (`PermitRootLogin prohibit-password`, так и оставляем), поэтому через буфер обмена:

```bash
read -rsp 'CF token: ' CF_TOKEN; echo
read -rp 'CF zone id: ' CF_ZONE
CF_Token=$CF_TOKEN CF_Zone_ID=$CF_ZONE /opt/acme.sh/acme.sh --config-home /etc/acme.sh \
  --issue --server letsencrypt --dns dns_cf -d vladpolt.com -d '*.vladpolt.com'
unset CF_TOKEN CF_ZONE
```

- `read -s` не показывает ввод, и вставленное через `read` не попадает в историю команд.
- Вставлять только значение, без `CF_TOKEN=` и кавычек.
- acme.sh запоминает токен в `/etc/acme.sh/account.conf` (только root) для продлений, как на роутере.

Положить для nginx и продлевать раз в сутки:

```bash
install -d -m 700 /etc/nginx/tls
/opt/acme.sh/acme.sh --config-home /etc/acme.sh --install-cert -d vladpolt.com \
  --key-file /etc/nginx/tls/vladpolt.com.key \
  --fullchain-file /etc/nginx/tls/vladpolt.com.crt \
  --reloadcmd 'systemctl reload nginx'
cat > /etc/systemd/system/acme-renew.service <<'EOF'
[Unit]
Description=Renew certificates with acme.sh when due
After=network-online.target

[Service]
Type=oneshot
ExecStart=/opt/acme.sh/acme.sh --cron --config-home /etc/acme.sh
EOF
cat > /etc/systemd/system/acme-renew.timer <<'EOF'
[Unit]
Description=Daily certificate renewal check

[Timer]
OnCalendar=daily
RandomizedDelaySec=1h
Persistent=true

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload && systemctl enable --now acme-renew.timer
```

Те же таймер и служба, что на роутере. Куда класть новый сертификат и что перезагружать, `--cron` берёт
из `/etc/acme.sh/vladpolt.com_ecc/vladpolt.com.conf` (`Le_RealKeyPath`, `Le_RealFullChainPath`,
`Le_ReloadCmd`): их туда записал `--install-cert`.

### 6. nginx

```bash
install -m 0644 /opt/home-ve/deploy/nginx/home.conf /etc/nginx/sites-available/home
ln -sf /etc/nginx/sites-available/home /etc/nginx/sites-enabled/home
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx
```

Открыть `https://home.vladpolt.com`. Что в `home.conf`, помимо того же, что у роутера:

- `map $http_upgrade $connection_upgrade` и `location ~ ^/api/vms/[^/]+/console$` — консоль работает
  через WebSocket. nginx по умолчанию разговаривает с бэкендом по HTTP/1.0 и выбрасывает заголовки
  `Upgrade`/`Connection`, а здесь передаёт их, и соединение «переключается» в WebSocket.
- `proxy_read_timeout 1h` — без этого nginx закроет консоль через 60 секунд тишины на экране.
- Регулярный `location ~` важнее обычного префиксного `/api/`, поэтому консоль попадает именно в него.

Сайт nginx `install.sh` не трогает: это настройка один раз, правится руками.

## Обновление

После `build.ps1`, commit и push на компьютере:

```bash
/opt/home-ve/deploy/update.sh
```

`update.sh` делает `git pull --ff-only` и, если что-то пришло, запускает свежий `install.sh`.

То же самое из морды: шестерёнка в шапке → **Update now** → подтверждение. Кнопка запускает
`home-update.service`: это `update.sh` от root отдельной службой, поэтому обновление доживает до конца,
хотя `install.sh` по ходу перезапускает бэкенд. Страница показывает статус и вывод скрипта, а после
успешного обновления предлагает перезагрузить себя (мог прийти новый фронт). Из консоли:
`systemctl start home-update` и `journalctl -u home-update -n 50`.

Кнопка появится после первого обновления руками: юнит `home-update.service` и правило polkit для него
ставит `install.sh`.
Если изменился `vm-run` или `vm@.service`, VM подхватят их при следующем перезапуске.

## Откат

```bash
cd /opt/home-ve
git log --oneline -5             # найти предыдущую версию
git reset --hard <хеш>           # вернуться к ней
bash deploy/install.sh           # применить
```

Вернуться на последнюю версию: `/opt/home-ve/deploy/update.sh`.

## Безопасность

- Снаружи доступен только nginx. Kestrel слушает `127.0.0.1:5000`.
- Запросы не из `AllowedNetworks` (LAN `192.168.178.0/24` и VPN `10.8.0.0/24`) обрываются без ответа.
- Вход по паролю, хеш PBKDF2 в `/etc/home-backend/config.json`. Cookie сессии живёт 7 дней и `Secure`
  (nginx передаёт `X-Forwarded-Proto`). Попыток входа не больше 10 за 5 минут с одного IP.
- Бэкенд без root. Что именно ему разрешено (polkit, `/etc/vm`, сокет консоли), см.
  [architecture.md](architecture.md#права-кто-что-может).
- `vm-run` работает от root и не доверяет `.conf`: разбирает, а не исполняет, и пускает в качестве диска
  только тонкие тома пула `data`.
- Консоль — это клавиатура и экран VM. Кто вошёл в морду, тот может залогиниться в VM, если знает её пароль,
  или перезагрузить её в single-user. Пароль морды должен быть не слабее root-пароля роутера.

## Настройки

Файл `/etc/home-backend/config.json` (образец: `deploy/config.example.json`). После правки нужен
`systemctl restart home-backend`.

| Ключ | По умолчанию | |
|---|---|---|
| `Urls` | `http://127.0.0.1:5000` | где слушает Kestrel; nginx проксирует сюда `/api/` |
| `AllowedNetworks` | `192.168.178.0/24`, `10.8.0.0/24`, `127.0.0.0/8` | кого пускать |
| `VmConfigDir` | `/etc/vm` | где `.conf` (vm-run всегда читает `/etc/vm`) |
| `VmRuntimeDir` | `/run` | где папки `vm-<имя>` с сокетами |
| `DataDir` | `/var/lib/home-backend` | ключи cookie |
| `PasswordHash` | — | задаётся командой ниже |

Сменить пароль:

```bash
/opt/home-ve/deploy/app/home-backend set-password /etc/home-backend/config.json && systemctl restart home-backend
```

## VM руками, без морды

```bash
systemctl status vm@router            # состояние
systemctl restart vm@router           # перезапуск: выключение через кнопку питания, потом старт
qmp router system_powerdown           # «нажать кнопку питания»
journalctl -u vm@router -n 50         # почему не стартует: vm-run пишет, какая строка .conf не так
nano /etc/vm/router.conf              # правка; применится при следующем старте
```

## Если что-то не так

```bash
systemctl status home-backend                       # запущена ли служба
journalctl -u home-backend -n 50                    # её лог
curl -i http://127.0.0.1:5000/api/auth/me           # бэкенд напрямую: 401 = жив и ждёт входа
curl -i https://home.vladpolt.com/api/auth/me       # через nginx: тоже 401; 404 или 502 = конфиг nginx
pkcheck --action-id org.freedesktop.systemd1.manage-units --process $(systemctl show -p MainPID --value home-backend) \
  --detail unit vm@router.service --detail verb restart   # разрешает ли polkit бэкенду перезапуск
```

- `pkcheck` или кнопки: «Could not connect: No such file or directory» → не работает системная шина D-Bus: `apt-get install dbus && systemctl start dbus polkit`.
- Кнопки дают «Interactive authentication required» → нет правила polkit или не стоит `polkitd`:
  `ls /etc/polkit-1/rules.d/`, `systemctl status polkit`.
- Консоль: «Failed to connect» и 502 → VM запущена старым `vm-run` без VNC-сокета: `systemctl restart vm@<имя>`.
- Сохранение настроек: «Permission denied» → у `/etc/vm` не та группа: `bash /opt/home-ve/deploy/install.sh`.
