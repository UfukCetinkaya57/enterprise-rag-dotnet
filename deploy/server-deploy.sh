#!/usr/bin/env bash
# ============================================================================
# rag.ufukcetinkaya.com — SUNUCUDA çalıştırılacak deploy scripti.
# Ubuntu + Nginx (üzerinde BAŞKA CANLI SİTELER var). Bu script onlara DOKUNMAZ:
#   - Yalnızca YENİ bir sites-available dosyası + symlink ekler.
#   - Her nginx reload öncesi 'nginx -t'; BAŞARISIZ olursa symlink'i SİLİP DURUR.
#   - Yalnızca bu projenin compose dosyasını kullanır; -v (veri silme) YOK.
#
# Kullanım (repo kökünde, sunucuda):
#   sudo DOMAIN=rag.ufukcetinkaya.com EMAIL=you@example.com bash deploy/server-deploy.sh preflight
#   sudo ... bash deploy/server-deploy.sh app        # container up + local health
#   sudo ... bash deploy/server-deploy.sh nginx-http # 80-only conf + nginx -t + reload
#   sudo ... bash deploy/server-deploy.sh cert        # certbot certonly --webroot
#   sudo ... bash deploy/server-deploy.sh nginx-tls   # tam conf (80+443) + nginx -t + reload
#   sudo ... bash deploy/server-deploy.sh smoke       # dışarıdan smoke test
# Aşamaları TEK TEK çalıştır; her aşamanın çıktısını kontrol et.
# ============================================================================
set -euo pipefail

DOMAIN="${DOMAIN:-rag.ufukcetinkaya.com}"
EMAIL="${EMAIL:-}"
API_PORT="${API_PORT:-8092}"

# Docker Compose v2 (docker compose) yoksa v1 (docker-compose) kullan — sunucuya göre otomatik.
if docker compose version >/dev/null 2>&1; then
  DC="docker compose"
elif command -v docker-compose >/dev/null 2>&1; then
  DC="docker-compose"
else
  echo "DUR: Docker Compose bulunamadı (ne 'docker compose' ne 'docker-compose')." >&2
  exit 1
fi
COMPOSE="$DC -f docker-compose.prod.yml --env-file .env.prod"
AVAIL="/etc/nginx/sites-available/${DOMAIN}"
LINK="/etc/nginx/sites-enabled/${DOMAIN}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

log(){ printf '\n\033[1;36m== %s\033[0m\n' "$*"; }
die(){ printf '\n\033[1;31mDUR: %s\033[0m\n' "$*" >&2; exit 1; }

# nginx -t; başarısızsa symlink'i geri al ve DUR (mevcut siteler etkilenmesin).
nginx_test_or_rollback(){
  if nginx -t; then
    systemctl reload nginx
    echo "nginx reload OK"
  else
    echo "nginx -t BAŞARISIZ — symlink kaldırılıyor, reload YAPILMIYOR."
    rm -f "$LINK"
    die "nginx config geçersiz. Mevcut siteler etkilenmedi. Çıktıyı bana getir."
  fi
}

case "${1:-}" in

preflight)
  log "1a) DNS: $DOMAIN → sunucu public IP?"
  dig +short "$DOMAIN" || true
  echo "(Yukarıda sunucunun public IP'si çıkmalı. Boşsa DNS yayılmamış → DUR.)"

  log "1b) Port $API_PORT boş mu? (çıktı boşsa boş demektir)"
  ss -tlnp | grep ":$API_PORT " || echo "$API_PORT boş ✓"

  log "1c) Mevcut nginx config sağlam mı? (bizim işimiz değil ama üzerine ekleyeceğiz)"
  nginx -t || die "Mevcut nginx config ZATEN bozuk. Bizim değişikliğimiz değil; önce bunu çöz."

  log "1d) Çalışan container'lar / port çakışması"
  docker ps --format 'table {{.Names}}\t{{.Ports}}\t{{.Status}}'
  echo "(Yukarıda $API_PORT'u kullanan başka container OLMAMALI.)"
  ;;

app)
  [ -f .env.prod ] || die ".env.prod yok. 'cp .env.prod.example .env.prod' + OPENAI_API_KEY doldur."
  grep -q "sk-your-real-key-here" .env.prod && die ".env.prod hâlâ placeholder key içeriyor."
  log "3a) Container build + up (yalnızca bu projenin compose'u)"
  $COMPOSE up -d --build
  log "3b) Container durumu"
  $COMPOSE ps
  log "3c) API logları (seed ingest + DB)"
  $COMPOSE logs api --tail 40 | grep -iE "seed|listening|error|exception|ingest" || true
  log "3d) Yerel health (127.0.0.1:$API_PORT)"
  sleep 6
  curl -fsS "http://127.0.0.1:$API_PORT/health" && echo " ✓"
  log "3e) Dışarıdan erişim OLMAMALI (public IP:$API_PORT reddedilmeli)"
  PUBIP="$(dig +short "$DOMAIN" | head -1)"
  if [ -n "$PUBIP" ]; then
    if curl -fsS --connect-timeout 4 "http://$PUBIP:$API_PORT/health" >/dev/null 2>&1; then
      die "GÜVENLİK: API dışarıdan $PUBIP:$API_PORT üzerinden ERİŞİLEBİLİR. 127.0.0.1 bind bozuk."
    else
      echo "✓ $PUBIP:$API_PORT dışarıdan erişilemiyor (beklenen)."
    fi
  fi
  ;;

nginx-http)
  log "4a) 80-only conf'u kopyala (ACME için), symlink oluştur"
  mkdir -p /var/www/certbot
  cp deploy/nginx/rag.http-only.conf "$AVAIL"
  ln -sf "$AVAIL" "$LINK"
  log "4b) nginx -t + reload (başarısızsa symlink silinir + DUR)"
  nginx_test_or_rollback
  log "4c) HTTP erişimi (200 hazırlanıyor mesajı)"
  curl -fsS "http://$DOMAIN/" || echo "(DNS/80 henüz erişilemiyor olabilir)"
  ;;

cert)
  [ -n "$EMAIL" ] || die "EMAIL boş. 'sudo EMAIL=you@... ... cert' ile çalıştır."
  log "5) Let's Encrypt sertifikası (webroot; nginx'i durdurmaz)"
  certbot certonly --webroot -w /var/www/certbot -d "$DOMAIN" \
    --agree-tos -m "$EMAIL" --no-eff-email
  echo "Sertifika: /etc/letsencrypt/live/$DOMAIN/"
  ;;

nginx-tls)
  [ -f "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ] || die "Sertifika yok; önce 'cert' aşaması."
  log "5b) Tam conf'u (80→443 yönlendirme + TLS reverse proxy) yerleştir"
  cp deploy/nginx/rag.ufukcetinkaya.com.conf "$AVAIL"
  ln -sf "$AVAIL" "$LINK"
  log "5c) nginx -t + reload (başarısızsa symlink silinir + DUR)"
  nginx_test_or_rollback
  # certbot yenileme sonrası nginx reload hook
  mkdir -p /etc/letsencrypt/renewal-hooks/deploy
  echo 'systemctl reload nginx' > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
  chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
  log "5d) HTTPS + HTTP→HTTPS yönlendirme"
  curl -fsS "https://$DOMAIN/health" && echo " ✓ https health"
  echo -n "HTTP→HTTPS: "; curl -s -o /dev/null -w "%{http_code} → %{redirect_url}\n" "http://$DOMAIN/health"
  ;;

smoke)
  log "6) Smoke test (dışarıdan, TLS)"
  BASE="https://$DOMAIN" bash deploy/smoke-test.sh
  ;;

*)
  echo "Aşama seç: preflight | app | nginx-http | cert | nginx-tls | smoke"
  exit 1
  ;;
esac
