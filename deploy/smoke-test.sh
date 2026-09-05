#!/usr/bin/env bash
# Deploy sonrası smoke test. Prod zırhının çalıştığını doğrular.
#
# Kullanım:
#   ./deploy/smoke-test.sh                       # https://rag.ufukcetinkaya.com
#   BASE=http://127.0.0.1:8092 ./deploy/smoke-test.sh   # Nginx öncesi doğrudan API
#
# Not: Kota testi gerçek istek harcar. QUOTA_TEST=0 ile atlanabilir.

set -u
BASE="${BASE:-https://rag.ufukcetinkaya.com}"
QUOTA_TEST="${QUOTA_TEST:-1}"
PASS=0; FAIL=0

# Geçici dosyaları script dizininde tut (curl/mktemp yol uyumu her ortamda tutarlı olsun).
TMPD="$(cd "$(dirname "$0")" && pwd)/.smoke-tmp"
mkdir -p "$TMPD"
trap 'rm -rf "$TMPD"' EXIT

check() { # açıklama  beklenen  gerçek
  if [ "$2" = "$3" ]; then echo "  PASS: $1 ($3)"; PASS=$((PASS+1));
  else echo "  FAIL: $1 (beklenen $2, gelen $3)"; FAIL=$((FAIL+1)); fi
}

echo "== Smoke test: $BASE =="

# 1) Health 200
code=$(curl -sk -o /dev/null -w "%{http_code}" "$BASE/health")
check "health 200" "200" "$code"

# 2) Seed dokümana soru → 200 + cevap + kaynak
body=$(curl -sk -X POST "$BASE/api/chat" -H "Content-Type: application/json" \
       -d '{"question":"Yillik izin kac gun?"}')
echo "$body" | grep -q '"answer"'  && a=ok || a=yok
echo "$body" | grep -q '"sources"' && s=ok || s=yok
check "chat cevap alanı" "ok" "$a"
check "chat kaynak alanı" "ok" "$s"

# 3) Diagnostics kapalı → 404
code=$(curl -sk -o /dev/null -w "%{http_code}" "$BASE/api/diagnostics/eval?question=x")
check "diagnostics 404" "404" "$code"

# 4) Sahte PDF (yanlış magic byte) → 400
tmp="$TMPD/fake.pdf"; printf 'BuBirPdfDegil' > "$tmp"
code=$(curl -sk -o /dev/null -w "%{http_code}" -X POST "$BASE/api/documents" \
       -F "file=@$tmp;type=application/pdf")
check "sahte PDF 400" "400" "$code"

# 5) Kota aşımı → 429 (upload kotası en düşük olan; art arda dene)
if [ "$QUOTA_TEST" = "1" ]; then
  tmp="$TMPD/mini.pdf"; printf '%%PDF-1.4 minimal' > "$tmp"
  last=000
  for i in $(seq 1 6); do
    last=$(curl -sk -o /dev/null -w "%{http_code}" -X POST "$BASE/api/documents" \
           -F "file=@$tmp;type=application/pdf")
  done
  check "upload kota aşımı 429" "429" "$last"
else
  echo "  SKIP: kota testi (QUOTA_TEST=0)"
fi

echo "== Sonuç: $PASS geçti, $FAIL başarısız =="
[ "$FAIL" -eq 0 ]
