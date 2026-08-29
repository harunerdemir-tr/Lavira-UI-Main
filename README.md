# 🚀 LaviraSON - TEKNOFEST Yer İstasyonu (Ground Station)

**LaviraSON**, TEKNOFEST 2026 Roket Yarışması kurallarına (EK-7) tam uyumlu olarak C# (Windows Forms) ile geliştirilmiş profesyonel bir yer istasyonu ve telemetri arayüzüdür. Ana Uçuş Kontrol Bilgisayarı (UKB) ve Görev Yükü'nden gelen ham ikili (binary) paketleri eşzamanlı olarak işler, Unity tabanlı 3B simülasyon ile görselleştirir, güvenli CSV formatında loglar ve resmi hakem sistemine aktarır.

## ✨ Temel Özellikler

* **⚡ Asenkron Veri İşleme (Producer-Consumer Mimarisi):** Seri portlardan akan ham baytlar, UI (Arayüz) thread'ini dondurmamak için `BlockingCollection<byte[]>` ve arka plan `Task` iş parçacıkları üzerinden işlenir. Çoklu iş parçacığı mimarisi sayesinde veri kayıpları ve kilitlenmeler (*Race Condition*) önlenmiştir.
* **📡 Çift Kanallı Eşzamanlı Telemetri:** Ana UKB ve Görev Yükü (Payload) telemetrileri farklı COM portlarından bağımsız olarak okunur ve thread-safe tek bir **Merkezi Durum (*Single Source of Truth*)** nesnesinde birleştirilir.
* **🎮 Unity 3B Simülasyon Entegrasyonu:** Roketin *Pitch, Roll, Yaw* açıları ve Uçuş Durum Kodları UDP protokolü üzerinden yerel ağda (`127.0.0.1:5555`) çalışan Unity simülasyonuna aktarılır. Win32 API (`SetParent`) kullanılarak Unity penceresi doğrudan Windows Forms arayüzüne gömülür.
* **🗺️ GMap.NET ile Canlı GPS Takibi:** Harita üzerinde roketin anlık konumu (10 Hz yenileme limitiyle) Google Uydu Haritası üzerinde takip edilir. Uçuş öncesi rampa konumu sabitlenerek anlık yer mesafesi hesaplanır. Geçersiz koordinatlar (`0.00` veya sınır dışı değerler) gelişmiş filtreleme ile haritaya yansıtılmadan elenir.
* **🔊 Sesli Durum İkaz Sistemi (NAudio):** Uçuş aşamaları (Ayrılma, Tepe Noktası, İniş vb.) `NAudio` kütüphanesi kullanılarak sesli ikaz ve anonslarla pilota bildirilir.
* **📈 Dinamik Grafikler:** İrtifa, Hız, İvme ($X, Y, Z$), Basınç ve Sıcaklık verileri gerçek zamanlı olarak `System.Windows.Forms.DataVisualization.Charting` üzerinden yumuşatılmış animasyonlarla çizdirilir.
* **⚖️ EK-7 Hakem Entegrasyonu:** Birleştirilen telemetri verileri, yarışma standartlarına uygun 78 byte'lık paketler halinde (Checksum eklenerek) 19200 baud rate ile hakem yer istasyonuna yönlendirilir.
* **💾 Güvenli ve Tamponlu Loglama (RFC 4180):** Gelen her telemetri paketi milisaniye zaman damgasıyla yerel CSV dosyalarına yedeklenir. Disk I/O darboğazını önlemek için periyodik `Flush()` mekanizması ve RFC 4180 (çift tırnak sarmalama) standardı kullanılır.

## 🛠️ Kullanılan Teknolojiler ve Kütüphaneler

* **Dil & Platform:** C# (.NET Framework) / Windows Forms
* **Harita Sağlayıcı:** `GMap.NET.WindowsForms` (Google Satellite Map Cache)
* **Görselleştirme Entegrasyonu:** Unity 3D (C#), UDP Sockets, Win32 API
* **Ses Motoru:** `NAudio` (Durum bildirim sesleri)
* **Haberleşme:** `System.IO.Ports.SerialPort`
* **Eşzamanlılık (Concurrency):** `System.Collections.Concurrent`, `System.Threading.Tasks`, `Interlocked`

## 🧠 Veri Mimarisi ve Paket Protokolü

Sistem, metin/string parçalama yerine donanım seviyesinde **saf ikili (Binary / Byte Array)** haberleşme protokolü kullanır:

### 1. Ana Gövde (UKB) Paketi — 61 Byte
* **Başlık (Header):** `0xAB` (1 Byte)
* **Veri Alanı:** Durum Kodu (1 Byte), İrtifa, Hız, Sıcaklık, GPS Enlem/Boylam, Pitch, Roll, Yaw, Basınçlar ve İvmeler olmak üzere **Big-Endian Float** (4'er Byte) alanları içerir.
* **Doğrulama (Checksum):** İlk 58 baytın toplamı (Byte 58).
* **Bitiş (Tail):** `0x0D, 0x0A` (`\r\n`) (Byte 59-60).

### 2. Görev Yükü (Payload) Paketi — 35 Byte
* **Başlık (Header):** `0xAA, 0x55` (2 Byte)
* **Veri Alanı:** Ham İvme ($X, Y, Z$), Filtreli İvme ($X, Y, Z$), GPS Enlem ve Boylam verileri olmak üzere **Little-Endian Float** (4'er Byte) alanları içerir.
* **Doğrulama (Checksum):** 2 ile 33. baytlar arasındaki **XOR** kontrolü (Byte 34).

> *Hatalı başlık, eksik bayt veya Checksum/XOR uyuşmazlığı tespit edilen paketler işleme alınmadan kuyruktan atılır.*

## 💾 Loglama Mekanizması

* **Dosya Formatı:** Uygulama açılışında `Logs/roket_log_yyyyMMdd_HHmmss.csv` dosyası otomatik oluşturulur.
* **Format Standardı:** Tüm veriler RFC 4180 standartlarına uygun olarak çift tırnak (`"..."`) ile sarmalanır; özel karakter ve ayraç bozulmaları önlenir.
* **Performans Optimizasyonu:** UI ve okuma thread'lerinin disk I/O nedeniyle kilitlenmemesi için veriler tamponlanır ve 1 saniyelik zamanlayıcı (`Timer`) ile asenkron olarak diske boşaltılır (*Flush*).

## 👨‍💻 Geliştirici

* **Harun Yahya Erdemir** - *Bilgisayar Mühendisi - Kocaeli Üniversitesi*

