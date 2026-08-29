using System;
using System.Diagnostics;
using System.Globalization;

// ===================================================================
// TEKNOFEST 2026 RESMİ HAKEM YER İSTASYONU PAKETLEYİCİ
// EK-7 DOKÜMANINA GÖRE — 78 BYTE
// ===================================================================

namespace LaviraSON
{
    public static class HakemPaketleyici
    {
        // Paket sayacı — 0'dan 255'e kadar döngüsel
        private static byte paketSayaci = 0;

        // YENİ MİMARİ: Ana gövde ve görev yükü ayrı diziler.
        public static byte[] PaketOlustur(string[] ana, string[] gorev)
        {
            byte[] paket = new byte[78];

            // Güvenlik: Eğer diziler henüz dolmadıysa/boş gelirse program çökmesin
            if (ana == null) ana = new string[0];
            if (gorev == null) gorev = new string[0];

            // -------------------------------------------------------
            // HEADER — sabit, değişmez (Byte 1-4)
            // -------------------------------------------------------
            paket[0] = 0xFF;
            paket[1] = 0xFF;
            paket[2] = 0x54;
            paket[3] = 0x52;

            try
            {
                // -------------------------------------------------------
                // Byte 5 [4] — TAKIM ID (Yarışmada verilecek)
                // -------------------------------------------------------
                paket[4] = 0;

                // -------------------------------------------------------
                // Byte 6 [5] — PAKET SAYACI
                // -----------------------------------------------------
                paket[5] = paketSayaci;
                paketSayaci = (paketSayaci == 255) ? (byte)0 : (byte)(paketSayaci + 1);

                // =======================================================
                // ANA GÖVDE VERİLERİ (ana[] dizisinden okunuyor)
                // =======================================================

                // Byte 7-10 [6-9] — İRTİFA (barometrik) -> ana[1]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 1)), 0, paket, 6, 4);

                // Byte 11-14 [10-13] — ROKET GPS İRTİFA -> ana[13]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 13)), 0, paket, 10, 4);

                // Byte 15-18 [14-17] — ROKET ENLEM -> ana[5]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 5)), 0, paket, 14, 4);

                // Byte 19-22 [18-21] — ROKET BOYLAM -> ana[6]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 6)), 0, paket, 18, 4);

                // =======================================================
                // GÖREV YÜKÜ VERİLERİ (gorev[] dizisinden okunuyor)
                // Gömülücülerin arayüze uygun gönderdiği orijinal indeksler
                // =======================================================

                // Byte 23-26 [22-25] — GÖREV YÜKÜ GPS İRTİFA -> gorev[1]
                Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 22, 4);

                // Byte 27-30 [26-29] — GÖREV YÜKÜ ENLEM -> gorev[6]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(gorev, 6)), 0, paket, 26, 4);

                // Byte 31-34 [30-33] — GÖREV YÜKÜ BOYLAM -> gorev[7]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(gorev, 7)), 0, paket, 30, 4);

                // =======================================================
                // KADEME BİLGİLERİ (Sadece Zorlu Görev - Bizde 0x00)
                // =======================================================
                Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 34, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 38, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 42, 4);

                // =======================================================
                // JİROSKOP & İVME (ana[] dizisinden okunuyor)
                // =======================================================

                // Byte 47-50 [46-49] — JİROSKOP X (Pitch) -> ana[7]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 7)), 0, paket, 46, 4);

                // Byte 51-54 [50-53] — JİROSKOP Y (Roll) -> ana[8]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 8)), 0, paket, 50, 4);

                // Byte 55-58 [54-57] — JİROSKOP Z (Yaw) -> ana[9]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 9)), 0, paket, 54, 4);

                // Byte 59-62 [58-61] — İVME X -> ana[20]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 20)), 0, paket, 58, 4);

                // Byte 63-66 [62-65] — İVME Y -> ana[21]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 21)), 0, paket, 62, 4);

                // Byte 67-70 [66-69] — İVME Z -> ana[22]
                Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 22)), 0, paket, 66, 4);

                // Byte 71-74 [70-73] — AÇI (0x00)
                Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 70, 4);

                // -------------------------------------------------------
                // Byte 75 [74] — DURUM (UINT8) -> ana[0]
                // -------------------------------------------------------
                paket[74] = UcusDurumunuHakemeCevir(ParseInt(ana, 0));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Hakem Paket Parse Hatası: " + ex.Message);
            }

            // -------------------------------------------------------
            // Byte 76 [75] — CRC / CHECKSUM (UINT8)
            // -------------------------------------------------------
            int checkSum = 0;
            for (int i = 4; i <= 74; i++)
                checkSum += paket[i];
            paket[75] = (byte)(checkSum % 256);

            // -------------------------------------------------------
            // Byte 77-78 [76-77] — FOOTER
            // -------------------------------------------------------
            paket[76] = 0x0D;
            paket[77] = 0x0A;

            return paket;
        }

        // ===================================================================
        // UÇUŞ DURUM DÖNÜŞÜMÜ
        // ===================================================================
        private static byte UcusDurumunuHakemeCevir(int ucusDurumu)
        {
            switch (ucusDurumu)
            {
                case 0:
                case 1:
                case 2:
                    return 1;
                case 3:
                    return 2;
                case 4:
                case 5:
                case 6:
                    return 4;
                default:
                    return 1;
            }
        }

        // ===================================================================
        // PARSE YARDIMCILARI
        // ===================================================================
        private static int ParseInt(string[] arr, int index)
        {
            if (arr != null && index >= 0 && index < arr.Length &&
                !string.IsNullOrWhiteSpace(arr[index]) &&
                int.TryParse(arr[index].Trim(), out int result))
                return result;
            return 0;
        }   

        private static float ParseFloat(string[] arr, int index)
        {
            if (arr != null && index >= 0 && index < arr.Length &&
                !string.IsNullOrWhiteSpace(arr[index]) &&
                float.TryParse(arr[index].Trim().Replace(',', '.'),
                               NumberStyles.Any,
                               CultureInfo.InvariantCulture,
                               out float result))
                return float.IsNaN(result) ? 0f : result;
            return 0f;
        }
    }
}