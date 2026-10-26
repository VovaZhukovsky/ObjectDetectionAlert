# ObjectDetectionApi

.NET 8 Web API, который по кадру с RTSP-камеры определяет состояние миски с кормом (`enough`, `low`, `blocked`, `no_bowl`) с помощью YOLO11 classify ONNX модели. Запускается на удалённом сервере, получает кадр напрямую с камеры через ffmpeg через WireGuard туннель, отправляет уведомления в Telegram.

## Схема инфраструктуры

```
Камера (<camera-ip>)
        │ RTSP
        ▼
Роутер OpenWrt (10.10.0.2)
        │ WireGuard tunnel (UDP 51820)
        ▼
Сервер <server-ip> (10.10.0.1)
        │
 ObjectDetectionApi
        │
   Telegram Bot
```

---

## Роутер (OpenWrt)

### 1. Установить WireGuard

```sh
opkg update
opkg install wireguard-tools kmod-wireguard luci-proto-wireguard
```

### 2. Сгенерировать ключи

```sh
wg genkey | tee /etc/wireguard/private.key | wg pubkey > /etc/wireguard/public.key
```

### 3. Настроить интерфейс через UCI

```sh
uci set network.wg0=interface
uci set network.wg0.proto=wireguard
uci set network.wg0.private_key="<router_private_key>"
uci set network.wg0.addresses="10.10.0.2/24"

uci add network wireguard_wg0
uci set network.@wireguard_wg0[-1].name="vps"
uci set network.@wireguard_wg0[-1].public_key="<server_public_key>"
uci set network.@wireguard_wg0[-1].endpoint_host="<server-ip>"
uci set network.@wireguard_wg0[-1].endpoint_port="51820"
uci set network.@wireguard_wg0[-1].allowed_ips="10.10.0.1/32"
uci set network.@wireguard_wg0[-1].persistent_keepalive="25"

uci commit network
/etc/init.d/network restart
```

### 4. Настроить firewall

В LuCI → Network → Firewall:
- Зона `wg`: Input/Output — accept, Masquerading — включить
- Traffic Rules: добавить правило `wg → lan` только для `<camera-ip>` (блокирует доступ к остальной домашней сети)

---

## Сервер (<server-ip>)

### 1. Установить зависимости

```sh
sudo apt install dotnet-sdk-8.0 ffmpeg libfontconfig1 wireguard
```

### 2. Настроить WireGuard

```sh
wg genkey | tee /etc/wireguard/server_private.key | wg pubkey > /etc/wireguard/server_public.key
```

`/etc/wireguard/wg0.conf`:
```ini
[Interface]
PrivateKey = <server_private_key>
Address = 10.10.0.1/24
ListenPort = 51820

[Peer]
PublicKey = <router_public_key>
AllowedIPs = 10.10.0.2/32, <camera-ip>/32
```

```sh
systemctl enable --now wg-quick@wg0
# открыть порт
iptables -A INPUT -p udp --dport 51820 -j ACCEPT
iptables-save > /etc/iptables/rules.v4
# маршрут к камере
ip route add <camera-ip>/32 via 10.10.0.2
```

### 3. Деплой ObjectDetectionApi

Деплой происходит автоматически при пуше в `main` через GitHub Actions (`.github/workflows/deploy.yml`).
Workflow копирует `deploy.sh` на сервер и запускает его от `github_actions_user`. Скрипт делает `git pull` в `~github_actions_user/objectdetection/src`,
собирает Release во временную папку `~github_actions_user/objectdetection/build`, останавливает сервис, копирует сборку в `/opt/objectdetection` и запускает сервис.
systemd-юнит скрипт не трогает — он настраивается на сервере руками.
Если сервис не поднялся, скрипт печатает последние строки журнала и завершается с ошибкой.

**Первоначальная настройка сервера (один раз)**

Пользователь `github_actions_user` и его SSH-ключ — те же, что для `income_expense_bot`.
На сервере должны быть `git`, `ffmpeg` и .NET SDK 8 в `PATH` этого пользователя.

```sh
sudo mkdir -p /opt/objectdetection
sudo chown -R github_actions_user:github_actions_user /opt/objectdetection

# перенести настройки, модель и собранный датасет со старого места
sudo cp /home/<user>/objectdetection-publish/appsettings.json /opt/objectdetection/
sudo cp /home/<user>/objectdetection-publish/best.onnx /opt/objectdetection/
sudo cp -r /home/<user>/objectdetection-publish/dataset_frames /opt/objectdetection/
sudo chown -R github_actions_user:github_actions_user /opt/objectdetection
```

В `appsettings.json` поменять `Onnx:ModelPath` на `/opt/objectdetection/best.onnx`.

В существующем юните (`sudo systemctl edit --full objectdetection`) поменять:

```ini
User=github_actions_user
WorkingDirectory=/opt/objectdetection
ExecStart=/opt/objectdetection/ObjectDetectionApi
```

Затем `sudo systemctl daemon-reload`. Остальное в юните (прокси, `ASPNETCORE_*`) оставить как есть.

Добавить secrets в GitHub-репозиторий:

| Secret | Значение |
|--------|---------|
| `SSH_KEY` | содержимое `~/.ssh/github_actions_key` |
| `SSH_HOST` | IP сервера |
| `SSH_PORT` | SSH порт |
| `SSH_USER` | `github_actions_user` |

После первого успешного деплоя старую папку `/home/<user>/objectdetection-publish` можно удалить.

---

## appsettings.json на сервере

```json
{
  "Onnx": {
    "ModelPath": "/opt/objectdetection/best.onnx"
  },
  "Video": {
    "Input": "rtsp://<camera-user>:<camera-password>@<camera-ip>:554/ch=1&subtype=0",
    "CollectDataset": false,
    "DatasetPath": "dataset_frames",
    "DatasetSaveEvery": 10,
    "StatusSnapshotPath": "status_snapshots"
  },
  "TelegramBot": {
    "Token": "...",
    "ChatId": "..."
  },
  "Worker": {
    "IntervalSeconds": 60
  },
  "FoodAlert": {
    "ConsecutiveMissesToAlert": 3,
    "NoBowlRunsToAlert": 10
  }
}
```

`CollectDataset: true` — сохранять чистые кадры (без рамок) для обучения новой модели в `DatasetPath`, каждый `DatasetSaveEvery`-й кадр. По умолчанию выключено. После изменения нужен перезапуск сервиса.  
`ModelPath` — классификатор, экспортированный Ultralytics (`yolo export format=onnx`). Классы и размер входа читаются из самой модели; если это не классификатор корма, сервис не стартует и пишет в лог, какие классы нашёл.  
`StatusSnapshotPath` — каждый кадр со статусом `low` или `no_bowl` сохраняется в `StatusSnapshotPath/<статус>/`. Файлы не удаляются.  
`ConsecutiveMissesToAlert` — сколько запусков подряд со статусом `low` нужно для тревоги «мало корма» в Telegram.  
`NoBowlRunsToAlert` — сколько запусков подряд со статусом `no_bowl` нужно для отдельного сообщения «миски нет в кадре».  
Статус `blocked` (кот или рука закрывают миску) счётчики не меняет. Каждое сообщение отправляется один раз и сбрасывается, когда миска снова видна. К сообщению прикрепляется кадр, на котором сработала тревога.

---

## Управление сервисами

```sh
# статус
sudo systemctl status objectdetection

# перезапуск после деплоя
sudo systemctl restart objectdetection

# логи
journalctl -u objectdetection -f
# или файловые логи:
tail -f /opt/objectdetection/logs/log-$(date +%Y%m%d).txt
```

## Проверка туннеля

```sh
wg show                  # handshake должен быть свежим
ping 10.10.0.2           # роутер
ping <camera-ip>         # камера
```

## API

```
GET /food/status    — статус миски на свежем кадре: {"status": "low", "confidence": 0.97, "processedAt": "..."}
                      status: enough | low | blocked | no_bowl
```
