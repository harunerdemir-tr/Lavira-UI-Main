using System;
using System.IO;
using System.Diagnostics;
using NAudio.Wave;
using System.Collections.Generic;

namespace LaviraSON
{
    public class DurumSesiYoneticisi : IDisposable
    {
        private WaveOutEvent outputDevice;
        private AudioFileReader audioFile;
        private int sonDurumKodu = -1;
        private string sesKlasoru;
        private Dictionary<int, string> sesDosyalari;
        private readonly object kilit = new object();

        public DurumSesiYoneticisi()
        {
            sesKlasoru = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sesler");

            // STM32 gömülü durum kodlarıyla birebir eşleşen ses dosyaları:
            sesDosyalari = new Dictionary<int, string>
            {
                { 0, "Bağlantı Kuruldu .mp3" },
                { 1, "Yükseliyor.mp3" },
                // 2 (Motor Yanma Sonu) ve 3 (Süzülme) için ses çalınmaz
                { 4, "Birinci ayrılma gerç.mp3" },
                { 5, "ikinci ayrılma gerçe.mp3" },
                { 6, "İniş yapıldı Görev B.mp3" } 
            };
        }

        public void DurumGuncelle(int durumKodu)
        {
            if (durumKodu == sonDurumKodu)
                return; // Durum değişmediyse oynatma

            sonDurumKodu = durumKodu;

            if (sesDosyalari.TryGetValue(durumKodu, out string dosyaAdi))
            {
                string tamYol = Path.Combine(sesKlasoru, dosyaAdi);
                SesOynat(tamYol);
            }
        }

        private void SesOynat(string dosyaYolu)
        {
            try
            {
                if (!File.Exists(dosyaYolu))
                {
                    Debug.WriteLine($"Ses dosyası bulunamadı: {dosyaYolu}");
                    return;
                }

                SesiDurdur();

                lock (kilit)
                {
                    outputDevice = new WaveOutEvent();
                    audioFile = new AudioFileReader(dosyaYolu);
                    outputDevice.Init(audioFile);
                    outputDevice.Play();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ses oynatılırken hata oluştu: {ex.Message}");
                SesiDurdur();
            }
        }

        private void SesiDurdur()
        {
            try
            {
                lock (kilit)
                {
                    if (outputDevice != null)
                    {
                        outputDevice.Stop();
                        outputDevice.Dispose();
                        outputDevice = null;
                    }
                    if (audioFile != null)
                    {
                        audioFile.Dispose();
                        audioFile = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SesiDurdur hatası: {ex.Message}");
            }
        }

        public void Dispose()
        {
            SesiDurdur();
        }
    }
}
