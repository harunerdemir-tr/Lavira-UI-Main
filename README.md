# 🚀 LaviraSON - TEKNOFEST Yer İstasyonu (Ground Station)

**LaviraSON**, TEKNOFEST 2026 Roket Yarışması kurallarına (EK-7) tam uyumlu olarak C# (Windows Forms) ile geliştirilmiş profesyonel bir yer istasyonu ve telemetri arayüzüdür. Ana Uçuş Kontrol Bilgisayarı (UKB) ve Görev Yükü'nden gelen verileri eşzamanlı olarak işler, Unity tabanlı 3B simülasyon ile görselleştirir ve resmi hakem sistemine aktarır.

## ✨ Temel Özellikler

*   **⚡ Asenkron Veri İşleme (Producer-Consumer Mimarisi):** Seri portlardan akan veriler, UI (Arayüz) thread'ini dondurmamak için `BlockingCollection` ve Task'lar üzerinden işlenir. Çoklu iş parçacığı (Multi-threading) mimarisi sayesinde veri kayıpları ve kilitlenmeler (Race Condition) önlenmiştir.
*   **📡 Çift Kanallı Telemetri:** Ana UKB ve Görev Yükü (Payload) telemetrileri eşzamanlı olarak farklı portlardan okunur ve tek bir Merkezi Durum (Single Source of Truth) objesinde birleştirilir.
*   **🎮 Unity 3B Simülasyon Entegrasyonu:** Roketin *Pitch, Roll, Yaw* açıları ve *Ayrılma Durum Kodları* UDP protokolü üzerinden yerel ağda (`127.0.0.1:5555`) çalışan Unity simülasyonuna aktarılır. Win32 API kullanılarak Unity penceresi doğrudan Windows Forms arayüzüne gömülür.
*   **🗺️ GMap.NET ile Canlı GPS Takibi:** Harita üzerinde roketin anlık konumu (10 Hz yenileme limitiyle) takip edilir. Uçuş öncesi rampa konumu sabitlenerek anlık uçuş mesafesi hesaplanır. Gelişmiş filtreleme ile bozuk/eksik GPS paketleri (örn. 0.00 veya <4 ondalık hassasiyet) haritaya yansıtılmadan reddedilir.
*   **📈 Dinamik Grafikler:** İrtifa, Hız, İvme, Basınç ve Sıcaklık verileri gerçek zamanlı olarak `System.Windows.Forms.DataVisualization.Charting` üzerinden çizdirilir.
*   **⚖️ EK-7 Hakem Entegrasyonu:** Sisteme gelen veriler, yarışma standartlarına uygun 78 byte'lık paketler halinde (Checksum eklenerek) 19200 baud rate ile hakem yer istasyonuna yönlendirilir.
*   **💾 Güvenli Loglama:** Gelen tüm telemetri verileri, RFC 4180 standartlarına (çift tırnak sarmalaması) uygun olarak milisaniye damgasıyla yerel CSV dosyalarına yedeklenir.

## 🛠️ Kullanılan Teknolojiler ve Kütüphaneler

*   **Dil:** C# (.NET Framework)
*   **Harita Sağlayıcı:** `GMap.NET.WindowsForms` (Google Satellite Map Cache)
*   **Görselleştirme Entegrasyonu:** Unity 3D (C#), UDP Sockets
*   **Haberleşme:** `System.IO.Ports.SerialPort`
*   **Eşzamanlılık (Concurrency):** `System.Collections.Concurrent`, `System.Threading.Tasks`

## 🧠 Veri Mimarisi ve Paket Yapısı

Sistem, gelen virgül ayracıyla bölünmüş (CSV) string formatını kullanır.
Ana gövde için beklenen paket formatı (Örnek 27 Alan):
`DURUM, ANA_IRT, ANA_HIZ, ANA_SIC, ANA_NEM, GPS_EN, GPS_BOY, PITCH, ROLL, YAW, BASINC_MS, BASINC_BMP, BASINC_TOPLAM, ..., IVME_X, IVME_Y, IVME_Z, FILTRE_X, FILTRE_Y, FILTRE_Z, PAKET_NO`

Hatalı veya eksik veri gelen durumlarda paketler reddedilir ve stabilizasyon sağlanır.

## 👨‍💻 Geliştirici
*   **Harun Yahya Erdemir** - *Bilgisayar Mühendisi-Kocaeli Üniversitesi*

---
*Bu proje, TEKNOFEST Roket Yarışması isterleri doğrultusunda geliştirilmiştir.*# Lavira-UI

