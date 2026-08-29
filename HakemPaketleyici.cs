using System;
using System.Diagnostics;
using System.Globalization;

// ===================================================================
// DİKKAT|| BU HAKEM PAKETLEYİCİSİ GEÇEN SENENİN EKLERİNE UYGUN HAZIRLANMIŞTIR.
// DİKKAT|| ANCAK BU SENENİN YARIŞMASINDA BÖYLE BİR DURUM SÖZ KONUSU DEĞİLDİR.
// DİKKAT|| BU YÜZDEN BU SCRİPT DURCAK ANCAK GÜNCELLEME YAPILIP KULLANILMAYACAKTIR.
// ===================================================================

namespace LaviraSON
{
    public static class HakemPaketleyici
    {
        private static byte paketSayaci = 0;
        public static byte[] PaketOlustur(string[] ana, string[] gorev)
        {
            byte[] paket = new byte[78];
            if (ana == null) ana = new string[0];
            if (gorev == null) gorev = new string[0];
            paket[0] = 0xFF;
            paket[1] = 0xFF;
            paket[2] = 0x54;
            paket[3] = 0x52;
            // -------------------------------------------------------
            // Byte 5 [4] — TAKIM ID (Yarışmada verilecek)
            // -------------------------------------------------------
            paket[4] = 0;
            paket[5] = paketSayaci;
            paketSayaci = (paketSayaci == 255) ? (byte)0 : (byte)(paketSayaci + 1);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 1)), 0, paket, 6, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 13)), 0, paket, 10, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 5)), 0, paket, 14, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 6)), 0, paket, 18, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 22, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(gorev, 6)), 0, paket, 26, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(gorev, 7)), 0, paket, 30, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 34, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 38, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 42, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 7)), 0, paket, 46, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 8)), 0, paket, 50, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 9)), 0, paket, 54, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 20)), 0, paket, 58, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 21)), 0, paket, 62, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(ParseFloat(ana, 22)), 0, paket, 66, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, paket, 70, 4);
            paket[74] = UcusDurumunuHakemeCevir(ParseInt(ana, 0));
            int checkSum = 0;
            for (int i = 4; i <= 74; i++)
                checkSum += paket[i];
            paket[75] = (byte)(checkSum % 256);
            paket[76] = 0x0D;
            paket[77] = 0x0A;

            return paket;
        }
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