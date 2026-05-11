# ObjectDetectionApi

.NET 8 Web API для обнаружения объектов на RTSP-потоке с помощью YOLOv11 ONNX модели. Запускается на удалённом сервере, получает видео напрямую с камеры через WireGuard туннель, отправляет уведомления в Telegram.

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

### 3. Собрать и задеплоить ObjectDetectionApi

```sh
# локально
dotnet publish ObjectDetectionApi/ObjectDetectionApi -c Release -o /home/<user>/objectdetection-publish
scp -P <ssh-port> -r /home/<user>/objectdetection-publish <user>@<server-ip>:/home/<user>/
```

### 4. Создать systemd сервис

```sh
sudo tee /etc/systemd/system/objectdetection.service << 'EOF'
[Unit]
Description=Object Detection API
After=network.target

[Service]
Type=simple
User=<user>
WorkingDirectory=/home/<user>/objectdetection-publish
ExecStart=/home/<user>/objectdetection-publish/ObjectDetectionApi
Restart=always
RestartSec=10
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now objectdetection
```

---

## appsettings.json на сервере

```json
{
  "Onnx": {
    "ModelPath": "/home/<user>/objectdetection-publish/best.onnx",
    "ObjectsForSearch": ["dry_cat_food"],
    "Confidence": 0.5,
    "Iou": 0.45
  },
  "Video": {
    "Input": "rtsp://<camera-user>:<camera-password>@<camera-ip>:554/ch=1&subtype=0",
    "NeedOutput": false,
    "OutputPath": "video_output",
    "MaxFiles": 30
  },
  "TelegramBot": {
    "Token": "...",
    "ChatId": "..."
  },
  "Worker": {
    "IntervalSeconds": 60
  }
}
```

`NeedOutput: true` — сохранять аннотированные MP4 в `OutputPath`, ротация до `MaxFiles` файлов.  
`Confidence` и `Iou` — поднять, если модель ловит фантомные объекты.

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
tail -f /home/<user>/objectdetection-publish/logs/log-$(date +%Y%m%d).txt
```

## Проверка туннеля

```sh
wg show                  # handshake должен быть свежим
ping 10.10.0.2           # роутер
ping <camera-ip>         # камера
```

## API

```
GET /object/exist   — есть ли объекты из ObjectsForSearch на видео
GET /object/count   — количество по каждому классу
```
