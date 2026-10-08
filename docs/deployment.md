# Выкладка на хост «home»

На хосте лежит sparse checkout репозитория в `/opt/home-ve`, в нём только папка `deploy/`:

```
deploy/
  app/home-backend        бэкенд: один self-contained бинарник linux-x64
  www/                    собранный фронт; install.sh копирует его в /var/www/home
  home-backend.service    systemd-юнит бэкенда
  config.example.json     образец /etc/home-backend/config.json
  vm/                     vm-run, qmp, vm@.service, vm-autostart(.service), правило polkit
  nginx/home.conf         сайт nginx (install.sh ставит его сам)
  install.sh              установка и любое обновление (идемпотентный)
  update.sh               git pull + install.sh
```

## Первая установка

Хост: Debian 13 (trixie), LVM с тонким пулом `data` для дисков VM. Всё на хосте под root.

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

- ставит недостающее из `qemu-system-x86`, `socat`, `lvm2`, `dbus`, `polkitd`, `zstd`, `curl`, `ca-certificates`,
  `nginx`, `openssl`. Через D-Bus `systemctl` от обычного пользователя разговаривает с systemd, а в минимальном
  Debian его может не быть;
- создаёт системного пользователя `home-backend` (без shell и home);
- создаёт `/etc/home-backend/config.json` из образца (права 0640) и спрашивает пароль для морды;
- **находит, куда класть диски VM**: группу LVM, в которой есть тонкий пул `data`, и пишет её в `DiskGroup`
  (только если там ещё пусто). Пула нет — скрипт подскажет, как его сделать, например
  `lvcreate --type thin-pool -l 90%FREE -n data <группа>`; пока его нет и `install.sh` не запущен снова,
  морда не создаёт VM;
- **переводит старые `.conf`** на новый формат: `NETS="br-lan=… br-wan=…"` → строки `NET=br-lan …`,
  и если `vm@<имя>` был включён (`enable`), пишет `AUTOSTART=yes` и выключает этот `enable`. Теперь при
  загрузке VM запускает `vm-autostart.service`. Старый файл остаётся рядом как `<имя>.conf.bak`;
- ставит `vm-run`, `qmp`, `vm-autostart` в `/usr/local/sbin`, юниты в `/etc/systemd/system`, правило polkit
  в `/etc/polkit-1/rules.d`; `/etc/vm` получает группу `home-backend` и права 0775;
- ставит юнит бэкенда, включает и перезапускает его;
- целиком заменяет `/var/www/home` свежим фронтом;
- **nginx**: сайт `/etc/nginx/sites-available/home` берётся из `deploy/nginx/home.conf` при каждом запуске
  (предыдущий остаётся рядом как `home.bak`, и если `nginx -t` новый не принимает, возвращается он). Какой
  сертификат — написано в `/etc/nginx/home-ve/tls.conf`: его скрипт создаёт один раз и дальше не трогает.
  Сначала там самоподписанный сертификат на имя и адреса хоста (`/etc/nginx/home-ve/selfsigned.*`, на 825 дней;
  за 30 дней до конца скрипт делает новый). Если сайт уже был настроен руками, `tls.conf` получает его сертификат.

Готово: морда открывается по `https://<адрес хоста>/`. С самоподписанным сертификатом браузер один раз
предупредит (соединение всё равно шифруется). Без предупреждения — свой домен и сертификат, шаг 4.

Работающие VM скрипт не трогает. Новый `vm-run` (и с ним консоль в браузере) VM получит при следующем
перезапуске. Если в VM роутер, это ~30 секунд без интернета, так что выбери удобный момент:

```bash
systemctl restart vm@router
ls -l /run/vm-router/      # vnc.sock: srw-rw---- root home-backend; остальные сокеты только root
```

### 4. Свой домен и сертификат (по желанию)

Так сделано у автора: домен в Cloudflare, сертификат Let's Encrypt через acme.sh с проверкой через DNS, поэтому
хост не должен быть виден из интернета. Ниже `example.com`, `home.example.com` и `192.168.1.2` — подставь свои.

**Имя.** Cloudflare → домен → DNS → Add record:

| Type | Name | IPv4 | Proxy |
|---|---|---|---|
| A | `home` | `192.168.1.2` | DNS only (серое облако) |

Снаружи по этому адресу ничего нет (это адрес в домашней сети), а дома и через VPN он ведёт на хост. Если
DNS-сервер роутера защищён от DNS rebinding (dnsmasq со `stop-dns-rebind`), разреши свой домен:
`rebind-domain-ok=/example.com/`. Проверка с хоста: `getent hosts home.example.com` → `192.168.1.2`.

**Сертификат.** Нужен API-токен Cloudflare с правом Zone → DNS → Edit на этот домен и Zone ID (страница домена,
справа внизу).

```bash
git clone --depth 1 https://github.com/acmesh-official/acme.sh.git /tmp/acme.sh
cd /tmp/acme.sh
./acme.sh --install --nocron --noprofile --home /opt/acme.sh --config-home /etc/acme.sh --accountemail твой@email
cd / && rm -rf /tmp/acme.sh
chmod 700 /etc/acme.sh
read -rsp 'CF token: ' CF_TOKEN; echo
read -rp 'CF zone id: ' CF_ZONE
CF_Token=$CF_TOKEN CF_Zone_ID=$CF_ZONE /opt/acme.sh/acme.sh --config-home /etc/acme.sh \
  --issue --server letsencrypt --dns dns_cf -d example.com -d '*.example.com'
unset CF_TOKEN CF_ZONE
```

- `read -s` не показывает ввод, и вставленное через `read` не попадает в историю команд.
- Вставлять только значение, без `CF_TOKEN=` и кавычек.
- acme.sh запоминает токен в `/etc/acme.sh/account.conf` (только root) для продлений.

Положить для nginx, сказать nginx, что брать его, и продлевать раз в сутки:

```bash
install -d -m 700 /etc/nginx/tls
/opt/acme.sh/acme.sh --config-home /etc/acme.sh --install-cert -d example.com \
  --key-file /etc/nginx/tls/example.com.key \
  --fullchain-file /etc/nginx/tls/example.com.crt \
  --reloadcmd 'systemctl reload nginx'
cat > /etc/nginx/home-ve/tls.conf <<'EOF'
ssl_certificate     /etc/nginx/tls/example.com.crt;
ssl_certificate_key /etc/nginx/tls/example.com.key;
EOF
nginx -t && systemctl reload nginx
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

Куда класть новый сертификат и что перезагружать, `--cron` берёт из `/etc/acme.sh/example.com_ecc/example.com.conf`
(`Le_RealKeyPath`, `Le_RealFullChainPath`, `Le_ReloadCmd`): их туда записал `--install-cert`.

Открыть `https://home.example.com`.

### Что в `home.conf`

- `map $http_upgrade $connection_upgrade` и `location ~ ^/api/vms/[^/]+/console$` — консоль работает
  через WebSocket. nginx по умолчанию разговаривает с бэкендом по HTTP/1.0 и выбрасывает заголовки
  `Upgrade`/`Connection`, а здесь передаёт их, и соединение «переключается» в WebSocket.
- `proxy_read_timeout 1h` — без этого nginx закроет консоль через 60 секунд тишины на экране.
- Регулярный `location ~` важнее обычного префиксного `/api/`, поэтому консоль попадает именно в него.
- Свои правки сайта `install.sh` затрёт при следующем обновлении; сертификат — только в `tls.conf`.

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
`systemctl restart home-update` и `journalctl -u home-update -n 50`.

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
- Запросы не из `AllowedNetworks` (по умолчанию частные сети `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16` и сам
  хост) обрываются без ответа. Сузить до своих сетей: `AllowedNetworks` в `config.json`.
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
| `AllowedNetworks` | `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `127.0.0.0/8`, `::1/128` | кого пускать |
| `DiskGroup` | — (находит `install.sh`) | группа LVM с тонким пулом `data`: диск новой VM — `/dev/<группа>/<имя>` |
| `VmConfigDir` | `/etc/vm` | где `.conf` (vm-run всегда читает `/etc/vm`) |
| `VmRuntimeDir` | `/run` | где папки `vm-<имя>` с сокетами |
| `DataDir` | `/var/lib/home-backend` | ключи cookie |
| `PasswordHash` | — | задаётся командой ниже |

Сменить пароль:

```bash
/opt/home-ve/deploy/app/home-backend set-password /etc/home-backend/config.json && systemctl restart home-backend
```

## Новая VM

1. **ISO images → Download an ISO**: ссылка на установочный образ, например netinst Debian. Хост скачивает его
   сам в `/var/lib/home-backend/iso`, телефон можно закрыть.
2. **New VM**: имя, ядра, память, размер диска, ISO в CD-приводе, сетевые карты. С галкой «Start now» VM сразу
   стартует, и открывается консоль с установщиком.
3. При первом старте `vm-run` создаёт диск `/dev/<DiskGroup>/<имя>` (тонкий том в пуле `data`): место занимается
   по мере записи. Пустой диск не грузится, и BIOS загружает CD. После установки грузится уже диск, так что ISO
   можно оставить, а можно убрать в Settings → CD drive.

VM грузится через BIOS (SeaBIOS), как роутер. Установщики это умеют, но систему ставить нужно в режиме BIOS/MBR.

**Удаление**: VM → Settings → Delete VM, только остановленную. Без галки удаляется только `.conf`, диск остаётся,
и новая VM с тем же именем подхватит его как есть. С галкой удаляется и диск, для этого нужно ввести имя VM.

## Бэкапы

Куда: отдельный том `/dev/home/backups` (50G, ext4), смонтирован в `/var/backups/vm` (строка в `/etc/fstab`
с `nofail`). Пока он не смонтирован, `install.sh` не включает таймер, а `vm-backup` отказывается писать,
чтобы не забить корневой раздел.

Как (`deploy/vm/vm-backup`, юнит `vm-backup@<имя>`):

1. если VM работает и в ней есть `qemu-guest-agent`, файловые системы гостя на мгновение замораживаются (fsfreeze);
   без агента копия как после выдернутого шнура, журналируемые ФС это переживают;
2. тонкий снапшот диска: мгновенно и без места;
3. гость размораживается и работает дальше, а снапшот сжимается `zstd` в
   `/var/backups/vm/<имя>/<имя>-<ГГГГММДД-ЧЧММСС>.img.zst` (рядом `.conf` — настройки VM на тот момент, `.info` — размер диска);
4. снапшот удаляется; остаются самые свежие бэкапы за каждый из 7 последних дней и за каждую из 4 недель до них.

Когда: каждую ночь около 3:30 (`vm-backup-all.timer`, ±30 минут) для всех VM без `BACKUP=no`, и кнопкой
VM → Backups → Back up now. Галка «Back up every night» в настройках VM включает и выключает ночной бэкап.

Восстановление: VM → Backups → Restore, только у остановленной VM, с вводом имени. Диск перезаписывается
бэкапом, а то, что было на нём до этого, остаётся снапшотом `home/<имя>-undo` до следующего восстановления.
Пока идёт восстановление, VM не стартует.

```bash
systemctl list-timers vm-backup-all              # когда следующий ночной запуск
systemctl restart vm-backup@router               # бэкап руками
journalctl -u vm-backup@router -n 30             # как прошёл
ls -lh /var/backups/vm/router/                   # что лежит
systemctl start vm-restore@router:20261008-033512   # восстановить (VM должна быть остановлена)
lvconvert --merge home/router-undo               # откатить восстановление (VM остановлена)
```

## Выгрузка наружу (Cloudflare R2)

Вторая копия бэкапов вне дома: `/var/backups/vm` целиком и архив настроек хоста (`/etc`, `/root/.ssh`,
`/var/lib/home-backend` без ISO) уходят в бакет R2 **зашифрованными на хосте** (rclone crypt: шифруются и данные,
и имена файлов). Делает это `vm-offsite` (`deploy/offsite`), юнит `vm-offsite.service`: каждую ночь после бэкапов VM
и кнопкой Settings → Offsite backup → **Upload now**.

В облаке:
- `vm/` — зеркало `/var/backups/vm` (то, что удалила локальная ротация, удаляется и здесь, но сначала…);
- `trash/<время>/` — …переезжает сюда и лежит ещё 30 дней.

Предохранители (`/etc/vm-offsite/offsite.conf`, образец `offsite.conf.example` рядом): не больше 5 ГБ за запуск,
ничего не грузится, если локально или в облаке больше 60 ГиБ, не больше 20 удалений за запуск, скорость 20 Мбит/с.
Плюс квота на роутере: не больше 100 ГиБ в месяц с хоста в Cloudflare (nftables, правится руками на роутере).
rclone ходит строго по IPv4 (`--bind 0.0.0.0`), чтобы квота роутера его видела.

### Настройка (один раз)

`install.sh` ставит rclone с rclone.org, если его нет или он старше 1.65: rclone 1.60 из Debian 13 с R2 не работает
(ошибка `501 Not Implemented` на каждом файле).

1. **Cloudflare → R2 → Create bucket**: имя, например, `home-backups`, Location: **Europe (EU)**, класс Standard.
2. **R2 → Manage API tokens → Create API token**: Object Read & Write, только этот бакет. Запиши Access Key ID,
   Secret Access Key и endpoint `https://<account-id>.r2.cloudflarestorage.com` (для EU-бакета —
   `https://<account-id>.eu.r2.cloudflarestorage.com`).
3. На хосте, ключи вводишь сам (в истории shell они не останутся):
   ```bash
   rclone config --config /etc/vm-offsite/rclone.conf
   ```
   - `n` → имя **`r2`** → тип `s3` → provider `Cloudflare` → `access_key_id`, `secret_access_key` → endpoint из
     шага 2 → остальное по умолчанию. В advanced config: **`no_check_bucket = true`** (токен на один бакет не
     может создавать бакеты, без этого загрузка падает с AccessDenied).
   - `n` → имя **`offsite`** → тип `crypt` → remote **`r2:home-backups`** → filename_encryption `standard` →
     directory_name_encryption `true` → пароль: `g` (сгенерировать) или свой → второй пароль (salt) тоже.
   - **Оба пароля сразу сохрани вне хоста** (менеджер паролей). Без них бэкапы в облаке не открыть, если хост умрёт.
4. Проверка и первая выгрузка:
   ```bash
   chmod 600 /etc/vm-offsite/rclone.conf
   rclone --config /etc/vm-offsite/rclone.conf lsd r2:home-backups     # бакет виден (пусто — нормально)
   systemctl restart vm-offsite && journalctl -u vm-offsite -f         # или Upload now в вебморде
   ```
   В Cloudflare в бакете будут папки и файлы с нечитаемыми именами: так и должно быть.

### Восстановление из облака

С любой машины с rclone и тем же `rclone.conf` (его можно собрать заново: шаг 3 с теми же паролями):
```bash
rclone --config rclone.conf lsf -R offsite:vm/router                  # что есть
rclone --config rclone.conf copy offsite:vm/router/router-20261008-033512.img.zst /var/backups/vm/router/
```
Файл ложится расшифрованным; дальше Restore в вебморде как обычно (рядом с `.img.zst` скопируй `.conf` и `.info`).
Настройки хоста: `offsite:vm/_host/host-<время>.tar.zst` → `tar --zstd -xf … -C /tmp/restore` и разложить нужное.

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
curl -ik https://127.0.0.1/api/auth/me              # через nginx: тоже 401; 404 или 502 = конфиг nginx
pkcheck --action-id org.freedesktop.systemd1.manage-units --process $(systemctl show -p MainPID --value home-backend) \
  --detail unit vm@router.service --detail verb restart   # разрешает ли polkit бэкенду перезапуск
```

- `pkcheck` или кнопки: «Could not connect: No such file or directory» → не работает системная шина D-Bus: `apt-get install dbus && systemctl start dbus polkit`.
- Кнопки дают «Interactive authentication required» → нет правила polkit или не стоит `polkitd`:
  `ls /etc/polkit-1/rules.d/`, `systemctl status polkit`.
- Консоль: «Failed to connect» и 502 → VM запущена старым `vm-run` без VNC-сокета: `systemctl restart vm@<имя>`.
- Сохранение настроек: «Permission denied» → у `/etc/vm` не та группа: `bash /opt/home-ve/deploy/install.sh`.
