# Video Notları

Playtest / oyun kayıtlarını (OBS mp4/mkv) izlerken **zaman damgalı not** almak için küçük bir Windows uygulaması.
Bug, değişiklik isteği, serbest not, bip (küfür) ve kesim işaretleri; isteğe bağlı ekran görüntüsü ve
videoda bir alanı kırmızı çerçeveyle işaretleme. Notlar otomatik kaydedilir, tek tuşla Markdown rapor çıkar.

## Kurulum

1. **VLC media player (64 bit)** kurulu olmalı: <https://www.videolan.org/vlc/>.
   Uygulama VLC'nin oynatıcı motorunu kullanır, bu yüzden OBS'in H.264/HEVC mp4/mkv kayıtlarını sorunsuz oynatır.
2. [Releases](../../releases) sayfasından `VideoNotlari.exe` dosyasını indir ve çalıştır.
   - Windows **.NET 10 Desktop Runtime** isterse açılan penceredeki linkten kur
     (ya da <https://dotnet.microsoft.com/download/dotnet/10.0> → *.NET Desktop Runtime*, x64).
   - SmartScreen "Windows bilgisayarınızı korudu" derse: *Ek bilgi* → *Yine de çalıştır* (exe imzasız).

## Kullanım

Videoyu aç: `Ctrl+O`, **Aç** butonu, videoyu pencereye sürükle-bırak ya da exe'ye video dosyasını ver.

**Not alma**
- Bug / değişiklik: videoyu durdur (`Boşluk`), kutuya yaz, **BUG** (`B`) ya da **DEĞİŞİKLİK** (`D`).
  Yazı kutusundayken: `Enter` = NOT, `Ctrl+B` = BUG, `Ctrl+D` = DEĞİŞİKLİK, `Esc` = kutudan çık.
- Hızlı işaretler: `K` = BİP (küfür), `1` = Kesim BAŞLA, `2` = Kesim BİTİR, `N` = NOT.
- **SS ekle** açıkken BUG / DEĞİŞİKLİK / NOT o anki kareyi PNG olarak kaydeder.
- **Alan işaretleme:** videonun üstünde fareyle sürükle → video durur, alan kırmızı çerçeveyle görünür;
  notu yaz ve türünü seç. Ekran görüntüsü tam kare + kırmızı çerçeve olarak kaydedilir. `Esc` = iptal.
- **Seçilene SS ekle** (`S`): listede seçili nota o anki kareyi ekler.

**Liste** (yalnız açık videonun notları; zaman çubuğunda renkli işaretler)
- Çift tık / `G`: o ana git (2 sn önce) · `F2`: düzenle · `Del`: seçileni sil · `Ctrl+Z`: son notu sil · sağ tık: SS'i aç

**Oynatıcı**
- `Boşluk` oynat/durdur · `←` `→` 3 sn (`Shift` ile 10 sn) · `,` `.` bir kare geri/ileri
- `-` `+` hız (0.25x–3x) · `↑` `↓` ya da videoda fare tekerleği: ses
- `Page Up` / `Page Down`: aynı klasördeki önceki / sonraki video
- `F` / `F11` / videoda çift tık: tam ekran (`Esc` çıkar) · `Tab`: not panelini aç/kapat
- Uygulamadaki **?** butonu tüm kısayolları gösterir.

## Dosyalar

Her değişiklikte otomatik kaydedilir:

| Yer | İçerik |
|-----|--------|
| `Masaüstü\Video Notlari\notlar\<video adı>.txt` | Her videonun notları |
| `Masaüstü\video_notlari.txt` | Tüm notlar tek dosyada |
| `Masaüstü\Video Notlari\ss\` | Ekran görüntüleri (PNG) |
| `Masaüstü\Video Notlari\rapor.md` | **Raporu kaydet** (`Ctrl+S`): videoya ve türe göre, önce buglar ve değişiklikler, SS'ler bağlantılı |

Not dosyaları sekmeyle ayrılmış metindir: `saniye  zaman  tür  not  video  ss`.

## Kaynaktan derleme

.NET 10 SDK ile:

```
dotnet publish -c Release -o yayin
```

Çıktı: `yayin\VideoNotlari.exe` (framework-dependent, tek dosya).
Test modu: `VideoNotlari.exe --selftest <log dosyası> <video>` (veri klasörü için `VIDEONOTLARI_DATA` ortam değişkeni;
test fareyi kısa süre hareket ettirir).
