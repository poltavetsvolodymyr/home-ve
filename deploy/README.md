# deploy

Это всё, что нужно хосту «home». Собирается на компьютере (`build.ps1`) и приезжает сюда через `git pull`.
Подробно: `docs/deployment.md` в репозитории на GitHub (на хосте в sparse checkout его нет).

| | |
|---|---|
| `app/home-backend` | бэкенд, один бинарник, .NET внутри. Слушает `127.0.0.1:5000` |
| `www/` | фронт; `install.sh` копирует его в `/var/www/home`, оттуда отдаёт nginx |
| `home-backend.service` | systemd-юнит бэкенда, ставится в `/etc/systemd/system/` |
| `home-update.service` | `update.sh` от root; его запускает кнопка Update в морде |
| `config.example.json` | образец `/etc/home-backend/config.json` |
| `vm/vm-run` | запускает VM по `/etc/vm/<имя>.conf`; ставится в `/usr/local/sbin/` |
| `vm/vm-disk-remove`, `vm/vm-disk-remove@.service` | удаляет диск остановленной VM (кнопка Delete VM с галкой «и диск») |
| `vm/vm-stop` | выключение VM для `vm@.service`: кнопка питания, 120 с ожидания, потом выдернуть шнур |
| `vm/qmp` | одна команда в управляющий сокет VM: `qmp router system_powerdown` |
| `vm/vm@.service` | шаблон службы: одна VM = `vm@<имя>` |
| `vm/vm-autostart`, `vm/vm-autostart.service` | при загрузке запускает VM с `AUTOSTART=yes` |
| `vm/50-home-backend.rules` | polkit: бэкенду можно start/stop/restart/kill только `vm@*.service` |
| `nginx/home.conf` | сайт nginx (HTTPS, WebSocket для консоли) |
| `install.sh` | первая установка и применение любого обновления; можно запускать сколько угодно раз |
| `update.sh` | `git pull` + `install.sh` |

```bash
/opt/home-ve/deploy/update.sh                                  # обновить
journalctl -u home-backend -n 50                               # лог бэкенда
journalctl -u vm@router -n 50                                  # лог VM (то же, что вкладка Logs)
systemctl restart vm@router                                    # перезапустить VM (новые настройки, новый vm-run)
/opt/home-ve/deploy/app/home-backend set-password /etc/home-backend/config.json && systemctl restart home-backend
cd /opt/home-ve && git reset --hard <хеш> && bash deploy/install.sh   # откат на версию <хеш>
```
