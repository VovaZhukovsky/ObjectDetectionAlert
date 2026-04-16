Роутер (OpenWrt)

# 1. Сгенерировать SSH ключ
ssh-keygen -t ed25519 -f ~/.ssh/id_ed25519 -N ""

# 2. Конвертировать в формат dropbear
dropbearconvert openssh dropbear ~/.ssh/id_ed25519 ~/.ssh/id_ed25519.db

# 3. Добавить публичный ключ на сервер
cat ~/.ssh/id_ed25519.pub
# → скопировать в ~/.ssh/authorized_keys на сервере

# 4. Создать init скрипт автозапуска туннеля
cat > /etc/init.d/rtsp-tunnel << 'EOF'     
#!/bin/sh /etc/rc.common
START=99
STOP=10
USE_PROCD=1

start_service() {
procd_open_instance
procd_set_param command autossh -M 0 -N \
-R 8554:192.168.8.167:554 \
-o ServerAliveInterval=30 \
-p 14749 \
-i /home/zhuvla/.ssh/id_ed25519.db \
zhuvla@156.229.27.20                
procd_set_param respawn 3600 5 0    
procd_close_instance                    
}                                                                                                                                                                     
EOF

chmod +x /etc/init.d/rtsp-tunnel                                                                                                                                      
/etc/init.d/rtsp-tunnel enable                                                                                                                                        
/etc/init.d/rtsp-tunnel start
                                              
---                                     
Удалённый сервер (156.229.27.20)

# 1. Установить зависимости
sudo apt install dotnet-sdk-8.0 ffmpeg libfontconfig1

# 2. Установить MediaMTX
Камера использует HEVC и скорее всего UDP для медиаданных — SSH туннель только TCP. Поэтому видео данные не доходят.
Лучшее решение — использовать MediaMTX как прокси на сервере. Он примет RTSP через TCP туннель и переотдаст локально
без него 
wget https://github.com/bluenviron/mediamtx/releases/download/v1.17.1/mediamtx_v1.17.1_linux_amd64.tar.gz                                                             
tar xf mediamtx_v1.17.1_linux_amd64.tar.gz

# 3. Настроить mediamtx.yml
cat > ~/mediamtx.yml << 'EOF'                                                                                                                                         
rtspAddress: :8555

paths:                                     
cam:                                                                                                                                                                
source: rtsp://admin:admin123456@127.0.0.1:8554/ch=1&subtype=0
rtspTransport: tcp                      
EOF

# 4. Создать systemd сервис MediaMTX
sudo tee /etc/systemd/system/mediamtx.service << 'EOF'
[Unit]                                                                                                                                                                
Description=MediaMTX                       
After=network.target

[Service]
User=zhuvla                                                                                                                                                           
ExecStart=/home/zhuvla/mediamtx /home/zhuvla/mediamtx.yml
Restart=always

[Install]
WantedBy=multi-user.target                                                                                                                                            
EOF

sudo systemctl enable --now mediamtx

# 5. Собрать и задеплоить ObjectDetectionApi
cd ~/ObjectDetectionApi                                                                                                                                               
dotnet build

# 6. Создать systemd сервис ObjectDetectionApi
sudo tee /etc/systemd/system/objectdetection.service << 'EOF'                                                                                                         
[Unit]                                      
Description=ObjectDetection API         
After=network.target mediamtx.service

[Service]
User=zhuvla                                                                                                                                                           
WorkingDirectory=/home/zhuvla/ObjectDetectionApi/ObjectDetectionApi
ExecStart=dotnet run --project /home/zhuvla/ObjectDetectionApi/ObjectDetectionApi
Restart=always

[Install]
WantedBy=multi-user.target                                                                                                                                            
EOF

sudo systemctl enable --now objectdetection

  ---
appsettings.json на сервере

{                                       
"Onnx": {
"ModelPath": "/home/zhuvla/best.onnx",                                                                                                                            
"ObjectsForSearch": ["cat", "cat_bowl", "dry_cat_food"],
"Confidence": 0.5,                                                                                                                                                
"Iou": 0.45                            
},
"Video": {                                                                                                                                                          
"Input": "rtsp://127.0.0.1:8555/cam"
},                                                                                                                                                                  
"Worker": {                              
"IntervalSeconds": 60
}
}
