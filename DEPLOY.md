# Auto Deploy on VPS

## Что уже настроено в проекте

- Один `docker-compose.yml` для VPS (без `production` постфикса)
- GitHub Actions: `.github/workflows/deploy-vps.yml`
- Скрипт деплоя на сервере: `scripts/deploy_vps.sh`
- Nginx конфиг-шаблон: `deploy/nginx/cryptonet.conf`

## 1. Что нужно сделать на GitHub

В репозитории добавь Secrets:

- `VPS_HOST` - IP или домен сервера
- `VPS_USER` - пользователь SSH
- `VPS_SSH_KEY` - приватный SSH ключ (весь текст)
- `VPS_PORT` - порт SSH (обычно `22`)
- `VPS_PATH` - путь проекта на сервере, например `/opt/cryptonet`
- `PROD_ENV_FILE` - полный текст `.env` для сервера

Пример `PROD_ENV_FILE`:

```env
GROQ_API_KEY=gsk_xxx
MYSQL_ROOT_PASSWORD=strong_root_password
MYSQL_DATABASE=cryptonet
MYSQL_USER=cryptonet_user
MYSQL_PASSWORD=strong_user_password
```

## 2. Что сделать на VPS (один раз)

1) Установить Docker и Docker Compose plugin.

2) Установить Nginx и Certbot:

```bash
sudo apt update
sudo apt install -y nginx certbot python3-certbot-nginx
```

3) Скопировать `deploy/nginx/cryptonet.conf` в `/etc/nginx/sites-available/cryptonet.conf`,
заменить `your-domain.com` на твой домен, включить сайт:

```bash
sudo ln -sf /etc/nginx/sites-available/cryptonet.conf /etc/nginx/sites-enabled/cryptonet.conf
sudo nginx -t
sudo systemctl reload nginx
```

4) Выпустить SSL:

```bash
sudo certbot --nginx -d your-domain.com -d www.your-domain.com
```

5) Открыть в firewall только `22`, `80`, `443`.

## 3. Как работает автодеплой

При пуше в `main` workflow:

1. Копирует репозиторий на VPS
2. Создает `.env` из секрета `PROD_ENV_FILE`
3. Запускает `docker compose up -d --build`

## 4. Ручные команды проверки на VPS

```bash
cd /opt/cryptonet
docker compose ps
docker compose logs -f web
docker compose logs -f ai
docker compose logs -f mysql
```
