# KareNot — IINA eklentisi (macOS)

Windows'taki KareNot'un Mac karşılığı. IINA'da video izlerken zaman damgalı not alırsın
(BUG, DEĞİŞİKLİK, NOT, BİP, Kesim BAŞLA / BİTİR). Notlar Windows uygulamasıyla **aynı dosyalara, aynı biçimde**
yazılır; Mac'te yazılan notlar Windows'ta, Windows'takiler Mac'te açılır.

> Bu eklenti Windows bilgisayarda yazıldı ve **henüz gerçek bir Mac'te / IINA'da denenmedi**. Dosya biçimi kısmı
> otomatik testle Windows uygulamasının kurallarına göre doğrulandı; IINA tarafı yalnızca IINA'nın belgelerine göre
> yazıldı. Aşağıdaki "Denenmemiş noktalar" bölümüne bak.

## Kurulum

Gerekenler: IINA'nın eklenti destekleyen bir sürümü (1.4 ve sonrası; yeni sürüm önerilir).

### Yol 1: Klasörü bağla (önerilen, güncellemesi kolay)

Repo Mac'te bir yerde olsun (ör. `~/KareNot`). Terminal'de:

```sh
ln -s ~/KareNot/iina/KareNot.iinaplugin \
  ~/Library/Application\ Support/com.colliderli.iina/plugins/KareNot.iinaplugin-dev
```

Sonra IINA'yı kapatıp yeniden aç. Bağlantının adı mutlaka `.iinaplugin-dev` ile bitmeli (IINA belgelerindeki kural).
`plugins` klasörü yoksa önce `mkdir -p ~/Library/Application\ Support/com.colliderli.iina/plugins` ile oluştur.
IINA'nın `iina-plugin` komut satırı aracı varsa aynı işi `iina-plugin link ~/KareNot/iina/KareNot.iinaplugin` yapar.

Kodu değiştirince (ya da `git pull` sonrası) IINA'yı yeniden başlatman yeterli.

### Yol 2: Paket dosyası

`iina-plugin pack ~/KareNot/iina/KareNot.iinaplugin` bir `.iinaplgz` dosyası üretir; IINA'da
Ayarlar → Eklentiler (Plugins) üzerinden kurulur.

### İlk açılışta

- IINA eklentinin istediği izinleri gösterir: **dosya sistemi** (notları ve SS'leri Masaüstü'ne yazmak, `mkdir` / `mv`
  çalıştırmak için), **video üstü katman** (alan işaretleme) ve **ekran mesajı (OSD)**.
- macOS ilk yazmada "IINA, Masaüstü klasörüne erişmek istiyor" diye sorabilir: **İzin Ver**. Yanlışlıkla reddedersen:
  Sistem Ayarları → Gizlilik ve Güvenlik → Dosyalar ve Klasörler → IINA → Masaüstü.
- Panel: IINA'nın kenar çubuğunda **KareNot** sekmesi (kenar çubuğunu IINA'nın kendi düğmesiyle ya da Eklentiler
  menüsündeki "KareNot panelini göster" ile açarsın).

## Dosyalar (Windows ile aynı)

| Dosya | Ne |
|---|---|
| `~/Desktop/Video Notlari/notlar/<video adı>.txt` | Her videonun notları (esas kaynak) |
| `~/Desktop/video_notlari.txt` | Bütün notlar tek dosyada (her değişiklikte yeniden yazılır) |
| `~/Desktop/Video Notlari/ss/*.png` | Ekran görüntüleri |
| `~/Desktop/Video Notlari/rapor.md` | Rapor (menüden ya da paneldeki "Raporu kaydet") |
| `~/Desktop/Video Notlari/inceleme.json` | Kaldığın yer, "buraya kadar inceledim" |

Biçim birebir aynı: UTF-8 (BOM yok), `\n` satır sonu, sekmeyle ayrılmış sütunlar, sondaki boş sütunlar yazılmaz,
saniyeler `0.00`, zaman `hh:mm:ss.s` (onda bir aşağı yuvarlanır). Video adı geçersiz dosya adı karakteri içeriyorsa
Windows'taki gibi `_` ile değiştirilir; böylece iki uygulama aynı `notlar/*.txt` dosyasını kullanır.
Dosyalar önce `.tmp` olarak yazılıp yerine taşınır. Başka bir IINA penceresinin ya da Windows uygulamasının
(eşitlenen bir Masaüstü varsa) diğer videolara yazdığı notlar, kaydetmeden önce diskten yeniden okunur, ezilmez.

## Kullanım

Panelde üç sekme var: **Oyun testi | Kurgu | Kısayollar**. Eklenen not kendi sekmesini açar.

- **Oyun testi:** yazı kutusu, "SS ekle" kutucuğu (açıksa BUG/DEĞİŞİKLİK/NOT o anki kareyi PNG kaydeder),
  BUG / DEĞİŞİKLİK / NOT düğmeleri ve açık videonun oyun notları (zaman, tür, önem, durum, yazı).
- **Kurgu:** BİP / Kesim BAŞLA / Kesim BİTİR düğmeleri. Kesim başı ve sonu tek satır olarak eşlenir:
  `00:01:30–00:01:42 · 12 sn`; eşi olmayan işaret "⚠ Bitişi yok / Başlangıcı yok" diye görünür.
  Bitişi olan BİP aralık olarak görünür; seçili BİP'te **"Bitiş = şimdi"** ile bitiş eklenir.
- **Liste:** satıra tıkla = o ana git (2 sn önce) ve seç. Seçili satırda: durum (Açık / Düzeltildi / Tekrar test),
  önem (Düşük / Orta / Kritik), **Düzenle** (yazı), **Sil** (iki kez tıkla; eşlenmiş kesim satırı iki işareti de siler).
  Notu silmek SS dosyasını silmez.
- **Alan seç:** video durur, videonun üstünde sürükleyerek kırmızı bir alan çizersin. Sonraki oyun notunun SS'i
  kırmızı çerçeve çizilmiş kare olur (SS kutucuğu kapalı olsa da). Tekrar "Alan" düğmesi ya da Esc: iptal.

### Kısayollar (IINA → Eklentiler menüsündeki KareNot öğeleri)

| Tuş | İş |
|---|---|
| B | BUG |
| D | DEĞİŞİKLİK |
| N | NOT (önce kutuya yazılmalı; boşsa panel açılır ve kutuya geçilir) |
| K | BİP |
| 1 / 2 | Kesim BAŞLA / Kesim BİTİR |
| [ / ] | Açık sekmenin listesinde önceki / sonraki not (2 sn önceye gider) |
| L | Seçili notu döngüye al (−3 sn … +3 sn); duraklatınca ya da dışarı atlayınca biter, tekrar L de kapatır |
| I | Buraya kadar inceledim (şu anki konum `inceleme.json`'a "reviewed" olarak yazılır) |

Yazı kutusundayken: **Enter** = NOT, **Ctrl+B** = BUG, **Ctrl+D** = DEĞİŞİKLİK, **Ctrl+K** = yazıyla BİP,
**Esc** = kutudan çık.

Menüde ayrıca (tuşsuz): Alan işaretle / iptal, Raporu kaydet, Not klasörünü Finder'da göster, KareNot panelini göster.

**Dikkat — IINA'nın bazı varsayılan tuşlarını ezer:** `[` `]` (hız), `1` `2` (kontrast), `l` (A-B döngü),
`i` (istatistik), `d` (deinterlace). Boşluk, oklar, tam ekran vb. temel tuşlara dokunulmadı. Tuşları değiştirmek
istersen `main.js`'in sonundaki `addMenu(...)` satırlarındaki ikinci değeri (mpv tuş adı) değiştir.

### Kaldığın yerden devam

Video açılınca `inceleme.json`'daki konum 5 sn'den büyük ve videonun son 5 sn'sinden önceyse oraya atlanır ve ekranda
"Kaldığın yerden devam" yazar. Konum ~5 sn'de bir ve video kapanınca kaydedilir. IINA'nın kendi "son konumdan devam"
ayarı zaten bir yere atladıysa eklenti ayrıca atlamaz.

### Rapor

`rapor.md`: videoya, sonra türe göre (BUG, DEĞİŞİKLİK, NOT, BİP, kesim başı, kesim sonu) gruplu; her satır
`- **hh:mm:ss.s** — yazı [Kritik] [Düzeltildi]`, SS'ler `![zaman](ss/<dosya>)` bağlantısıyla. Eski VLC notlarının
SS adı önek olabilir; `ss/` içinde önekle eşleşen dosya aranır.

## Windows'tan farkları

- Video oynatma, sarma, hız, ses, tam ekran, kare kare ilerleme tamamen IINA'nın kendisi. Page Up/Down (klasördeki
  sonraki video), Tab (paneli aç/kapat), F1 yok; IINA'nın karşılıklarını kullan.
- Alan işaretleme için önce "Alan seç" (panelde ya da menüde) gerekir; Windows'taki gibi videoda doğrudan sürükleyerek
  başlamaz (yoksa IINA'nın tıklama/sürükleme davranışı bozulurdu).
- Yok: "Seçilene SS ekle [S]", Ctrl+Z ile son notu silme / geri alma, notun türünü değiştirme (sadece yazı düzenlenir),
  sağ tık menüsü, zaman çubuğunda not işaretleri, kısa klip çıkarma, HTML rapor, build/test eden alanları,
  "tamamlandı" işareti. Bu alanlar dosyada varsa (`klip`, `inceleme.json`'daki `done`, `build`, `tester` ve bilinmeyen
  anahtarlar) olduğu gibi korunur.
- Raporda durum/önem etiketleri ve BİP bitişi de yazılır.

## Denenmemiş noktalar / belirsizlikler

Gerçek IINA'da ilk denemede bakılması gerekenler:

1. **Tek harfli kısayollar ve yazı kutusu.** IINA belgelerine göre tuşlar önce menüye gider. Kutuya yazarken `b`, `d`,
   `n`, `k`, `l`, `i`, `1`, `2`, `[`, `]` kısayol olarak yakalanabilir. Bunun için eklenti, kutu odaktayken bu harfleri
   kutuya kendisi yazar. Kutudan çıktığını webview her zaman bildiremeyebilir; kutu boşken bir kısayol not eklemek
   yerine harfi kutuya yazarsa videoya bir kez tıkla.
2. **Dosya yazma:** IINA'nın `file.write` fonksiyonunun UTF-8 BOM'suz yazdığı varsayıldı (belgede kodlama yazmıyor).
   Klasör oluşturma ve yerine taşıma için `/bin/mkdir` ve `/bin/mv` çalıştırılıyor (dosya API'sinde mkdir / taşıma yok).
3. **SS:** mpv'nin `screenshot-to-file <yol> video` komutu kullanılıyor (altyazısız, OSD'siz kare).
4. **Alan çerçevesi:** SS'e kırmızı çerçeve çizmek için önce `ffmpeg` aranır (`/opt/homebrew/bin`, `/usr/local/bin`,
   PATH); yoksa macOS'un kendi `osascript` (JavaScript + AppKit) ile çizilir. İkisi de başarısız olursa SS çerçevesiz
   kalır ve ekranda uyarı çıkar. Alanın video karesindeki yeri mpv'nin `osd-dimensions` değerlerinden hesaplanır;
   IINA'da yakınlaştırma/kaydırma (zoom/pan) açıksa çerçeve kayabilir.
5. **Yazı kutusuna odaklanma** (N boş kutuyla basılınca): webview kutuya odak verir ama macOS klavye odağını IINA
   penceresinden panele geçirmeyebilir; o zaman kutuya bir kez tıkla.
6. **Döngü (L)** mpv'nin A-B döngüsünü (`ab-loop-a/b`) kullanır; IINA'nın kendi A-B döngü göstergesinde de görünebilir.
7. Birden fazla IINA penceresi aynı **aynı videoyu** açıp ikisinde not alırsa son yazan kazanır (farklı videolar sorun değil).

Sorun olursa: IINA'nın log penceresinde / eklenti konsolunda `[KareNot]` ile başlayan satırlara bak
(menü yeri IINA sürümüne göre değişiyor).

## Geliştirme

- `KareNot.iinaplugin/Info.json` — eklenti tanımı (izinler: `show-osd`, `video-overlay`, `file-system`).
- `KareNot.iinaplugin/main.js` — ana betik (her oynatıcı penceresi için ayrı çalışır).
- `KareNot.iinaplugin/lib/format.js` — not dosyası biçimi, rapor; IINA'ya bağlı değil.
- `KareNot.iinaplugin/ui/sidebar.*`, `ui/overlay.*` — panel ve video üstü katman (webview).
- Test: `node iina/test/format_test.js` (biçim kuralları; saniye yuvarlaması .NET'in `0.00` biçimiyle aynı).
