<img src="karenot.png" width="96" align="right" alt="KareNot logosu">

# KareNot

Oyun test kayıtlarını (OBS mp4/mkv) izlerken **sorunlu ya da değişmesi gereken yerleri zaman damgasıyla işaretleyip**
bunları ekip arkadaşlarına, geliştiricilere, AI agent'larına ve video editörüne düzgün bir şekilde iletmek için küçük bir
Windows uygulaması. KILLSTREAM gibi indie oyunların playtest kayıtları için yazıldı. Mac'te IINA için de bir eklentisi var
([iina/](iina/README.md)).

- **Oyun testi:** bug, değişiklik isteği ve serbest not. İstersen ekran görüntüsü ya da kısa video klibi eklenir, videoda bir
  alan kırmızı çerçeveyle işaretlenir. Her notun durumu (Açık / Düzeltildi / Tekrar test) ve önemi (Düşük / Orta / Kritik) olur.
- **Kurgu:** bip (küfür) ve kesim aralıkları. Editöre ya da editör olarak kullandığın bir AI agent'ına makinenin okuyabileceği
  JSON ve okunur bir liste olarak gider. İstersen uygulama bipli ve kesilmiş videoyu kendisi de çıkarır.
- **Paylaşım:** Markdown + HTML rapor, agent ve ekip için görev listesi, tek dosyalık bulgu paketi (ZIP), CSV,
  DaVinci Resolve işaretleri.

## Kurulum

1. **VLC media player (64 bit)** kurulu olmalı: <https://www.videolan.org/vlc/>. Uygulama VLC'nin oynatıcı motorunu kullanır.
2. [Releases](../../releases) sayfasından `KareNot.exe` dosyasını indir ve çalıştır.
   - Windows **.NET 10 Desktop Runtime** isterse açılan linkten kur (<https://dotnet.microsoft.com/download/dotnet/10.0>, x64).
   - SmartScreen uyarısında: *Ek bilgi* → *Yine de çalıştır* (exe imzasız).
3. İsteğe bağlı: **ffmpeg** (klip, önizleme, bipli/kesilmiş video, ölü an tespiti): `winget install Gyan.FFmpeg`.
   **whisper.cpp** (küfür adayları, tamamen çevrimdışı): `whisper-cli.exe` ve çok dilli bir ggml modeli
   (ör. `ggml-small.bin`). Yolları *Araçlar ▾ → Ayarlar* içinden seçilir.

## Kullanım

Videoyu aç: `Ctrl+O`, **Video aç**, sürükle-bırak ya da exe'ye video dosyasını ver. Açılan video kaldığın yerden devam eder.
Sağ panelde **Oyun testi | Kurgu | Videolar | Kısayollar** sekmeleri var. Bütün kısayollar her sekmede çalışır; eklenen not
kendi sekmesini açar. `F1` kısayol listesini gösterir.

**Oyun testi**
- Videoyu durdur (`Boşluk`), kutuya yaz, türünü seç: **BUG** `B`, **DEĞİŞİKLİK** `D`, **NOT** `N`
  (kutudayken `Enter` = NOT, `Ctrl+B` / `Ctrl+D`). **Hazır metin ▾** sık yazdığın açıklamaları doldurur; yazarken önceki
  notlar önerilir.
- **SS ekle** açıkken o anki kare kaydedilir. **Alan işaretleme:** videoda fareyle sürükle, alan kırmızı çerçeveyle SS'e girer.
- Seçili nota: `S` SS ekle, `C` kısa klip (varsayılan 5 sn öncesi + 5 sn sonrası), `L` döngüde izle, sağ tık / ⋯ menüsünde
  durum, önem, **Görev metni olarak kopyala**.
- Arama ve filtre (`Ctrl+F`): kelime ya da `#kamera` gibi etiketler; "Açık olanlar", "Kritik", "Tekrar test" vb.

**Kurgu**
- `K` bip (basılı tutarsan bıraktığın yere kadar aralık olur), `1` kesim başla, `2` kesim bitir. Kesimler liste ve zaman
  çubuğunda `02:14–02:38 · 24 sn` gibi aralık olarak görünür; eşi olmayan işaret uyarı verir.
- `X` kesilmiş hâli izle (kesimleri atlar). *Araçlar ▾* içinde: **küfür adaylarını bul** (Whisper) ve **ölü anları bul**
  (uzun sessiz / donuk bölümler kesim adayı olur). Adaylar "aday:" yazısıyla eklenir, `Ctrl+Z` hepsini geri alır.

**Gezinme ve inceleme**
- `[` `]` ya da `Ctrl+←` `Ctrl+→`: önceki / sonraki işaret. `I`: buraya kadar inceledim. `Home`: baştan.
- **Videolar** sekmesi klasördeki kayıtları Başlanmadı / Devam ediyor / Tamamlandı olarak gösterir; build ve testçi bilgisi
  (*Oturum bilgisi…*) raporlara yazılır. Zaman çubuğunun üzerine gelince o anın küçük karesi görünür.
- `Ctrl+Z` / `Ctrl+Y`: geri al / yinele (silme, düzenleme, durum dahil).

**Oynatıcı:** `Boşluk` oynat/durdur · `←` `→` 3 sn (`Shift` 10 sn) · `,` `.` kare · `-` `+` hız · `↑` `↓` ses ·
`Page Up/Down` klasördeki önceki/sonraki video · `F` / `F11` tam ekran · `Tab` paneli gizle.

## Paylaşım ve dışa aktarma

| Ne | Nereden | Kime |
|----|---------|------|
| `rapor.md` + `rapor.html` | **Raporu kaydet** (`Ctrl+S`) | Ekip, geliştirici (HTML küçük resimli ve klipli) |
| Görev listesi `agent_gorevleri_<video>.md` | *Dışa aktar ▾* | AI agent'ları ve geliştiriciler: her açık bulgu için başlık, zaman, önem, SS/klip, Beklenen / Gözlenen / Kabul ölçütü iskeleti |
| Bulgu paketi (ZIP) | *Dışa aktar ▾* ya da `Ctrl+P` | Tek dosyada rapor, görev listesi, CSV, kurgu notları, SS ve klipler (ör. "yalnız bu videonun açık bugları") |
| Kurgu notları (JSON + Markdown) | *Dışa aktar ▾* | Editör ya da editör olarak çalışan AI: bip ve kesim aralıkları, notlar |
| DaVinci Resolve işaretleri (EDL) | *Dışa aktar ▾* | *Timeline → Import → Timeline Markers from EDL* |
| Bipli ve kesilmiş video | *Dışa aktar ▾* | Kurgunun hızlı ilk hâli (ffmpeg) |
| CSV | *Dışa aktar ▾* | Excel, Trello, Jira içe aktarma |

Çıktılar `Masaüstü\Video Notlari\cikti\` klasörüne yazılır.

## Dosyalar

| Yer | İçerik |
|-----|--------|
| `Masaüstü\Video Notlari\notlar\<video adı>.txt` | Her videonun notları (esas kaynak) |
| `Masaüstü\video_notlari.txt` | Tüm notlar tek dosyada |
| `Masaüstü\Video Notlari\ss\` | Ekran görüntüleri ve klipler |
| `Masaüstü\Video Notlari\inceleme.json` | Kaldığın yer, inceleme durumu, build / testçi |
| `Masaüstü\Video Notlari\hazir_notlar.txt`, `kufur_listesi.txt` | Düzenlenebilir listeler |

Not dosyaları sekmeyle ayrılmış metindir: `saniye  zaman  tür  not  video  ss  durum  önem  bitiş  klip`
(sondaki boş sütunlar yazılmaz; eski 5–6 sütunlu dosyalar olduğu gibi okunur). IINA eklentisi aynı dosyaları kullanır.

## Kaynaktan derleme

.NET 10 SDK ile:

```
dotnet publish -c Release -o yayin
```

Çıktı: `yayin\KareNot.exe` (framework-dependent, tek dosya).
Test modu: `KareNot.exe --selftest <log dosyası> <video>` (veri klasörü için `VIDEONOTLARI_DATA` ortam değişkeni; aynı
klasörde ikinci bir video olmalı; test fareyi kısa süre hareket ettirir).
